using System.Text;
using KeePassLib;

namespace Monica.Platform.Services;

/// <summary>
/// The database's own policy about how much of an entry's past to keep, and the place that policy is
/// enforced. Enforcing it is the reason this half exists: a limit is only worth what the writer
/// honours, and the two numbers the file carries for "no cap" and "no history" were previously read
/// here as the same thing - both meant "drop the snapshot" - so a database that said it remembered
/// versions without limit had its versions deleted by the first edit.
/// </summary>
public sealed partial class KeePassVaultSession
{
    /// <summary>
    /// What every snapshot is charged on top of its fields when the size cap is measured. The Android
    /// client uses the same flat figure, so the two sides prune an entry's past at about the same size
    /// rather than at two numbers that look the same until a file gets large.
    /// </summary>
    private const long HistoryEntryOverheadBytes = 128;

    /// <summary>
    /// The policy as the file holds it. Reading is separate from applying because the browser shows
    /// these numbers before a person decides to change any of them.
    /// </summary>
    public async Task<KeePassHistoryPolicy> ReadHistoryPolicyAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var database = _database ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return PolicyOf(database);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Writes a new policy into the open database and marks it dirty. What is already remembered stays
    /// remembered: the caps apply to the next snapshot, the way every other client does it, so
    /// tightening a limit cannot delete a version a person was about to go back to.
    /// </summary>
    public async Task<KeePassHistoryPolicy> ApplyHistoryPolicyAsync(
        KeePassHistoryPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(policy.MaxItems, -1);
        ArgumentOutOfRangeException.ThrowIfLessThan(policy.MaxSizeBytes, -1L);

        ThrowIfDisposed();
        var database = _database ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            database.HistoryMaxItems = policy.MaxItems;
            database.HistoryMaxSize = policy.MaxSizeBytes;
            database.MaintenanceHistoryDays = policy.MaintenanceDays;
            MarkModified();
            return PolicyOf(database);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static KeePassHistoryPolicy PolicyOf(PwDatabase database) =>
        new(database.HistoryMaxItems, database.HistoryMaxSize, database.MaintenanceHistoryDays);

    /// <summary>
    /// Prunes one entry's remembered shapes down to the policy in the order the Android client uses:
    /// the age window first, then the count, then the size, dropping the oldest survivor each time a
    /// cap is still over. A negative count or size means that cap does not apply at all.
    /// </summary>
    private void MaintainHistory(PwEntry entry)
    {
        var database = _database;
        if (database is null)
        {
            return;
        }

        var versions = entry.History.ToList();
        if (versions.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var drop = new bool[versions.Count];
        var kept = 0;
        for (var index = 0; index < versions.Count; index++)
        {
            // A file does not say which timezone its timestamps were written in, so the age is measured
            // from the same reading the browser uses rather than from whatever Kind the value carries.
            var modified = KeePassVaultText.ToDateTimeOffset(versions[index].LastModificationTime);
            drop[index] = now - modified.UtcDateTime >= TimeSpan.FromDays(database.MaintenanceHistoryDays);
            if (!drop[index])
            {
                kept++;
            }
        }

        // Both caps drop the oldest survivor of the stage before them, and the index set is walked
        // rather than the list sliced because the versions are not guaranteed to be in date order.
        var maximumItems = database.HistoryMaxItems;
        while (maximumItems >= 0 && kept > maximumItems)
        {
            var oldest = OldestKept(versions, drop);
            if (oldest < 0)
            {
                break;
            }

            drop[oldest] = true;
            kept--;
        }

        var maximumBytes = database.HistoryMaxSize;
        while (maximumBytes >= 0 && kept > 0 && SizeOfKept(versions, drop) > maximumBytes)
        {
            var oldest = OldestKept(versions, drop);
            if (oldest < 0)
            {
                break;
            }

            drop[oldest] = true;
            kept--;
        }

        // Removed from the back so every lower index still points at the same version as it shrinks.
        for (var index = versions.Count - 1; index >= 0; index--)
        {
            if (drop[index])
            {
                entry.History.RemoveAt((uint)index);
            }
        }
    }

    private static int OldestKept(List<PwEntry> versions, bool[] dropped)
    {
        var oldest = -1;
        DateTime? oldestTime = null;
        for (var index = 0; index < versions.Count; index++)
        {
            if (dropped[index])
            {
                continue;
            }

            var modified = KeePassVaultText.ToDateTimeOffset(versions[index].LastModificationTime).UtcDateTime;
            if (oldestTime is null || modified < oldestTime)
            {
                oldest = index;
                oldestTime = modified;
            }
        }

        return oldest;
    }

    private static long SizeOfKept(List<PwEntry> versions, bool[] dropped)
    {
        var total = 0L;
        for (var index = 0; index < versions.Count; index++)
        {
            if (!dropped[index])
            {
                total += SizeOf(versions[index]);
            }
        }

        return total;
    }

    /// <summary>
    /// What one version costs the file. Only lengths are read here: the number is what the cap is
    /// compared against, and pulling a secret out of a protected field to measure it would put that
    /// secret in a place no reason was needed for.
    /// </summary>
    private static long SizeOf(PwEntry entry)
    {
        var size = HistoryEntryOverheadBytes;
        foreach (var field in entry.Strings)
        {
            size += Utf8Length(field.Key) + field.Value.Length;
        }

        foreach (var binary in entry.Binaries)
        {
            size += Utf8Length(binary.Key) + binary.Value.Length;
        }

        foreach (var tag in entry.Tags)
        {
            size += Utf8Length(tag);
        }

        foreach (var item in entry.CustomData)
        {
            size += Utf8Length(item.Key) + Utf8Length(item.Value);
        }

        return size;
    }

    private static int Utf8Length(string? value) =>
        string.IsNullOrEmpty(value) ? 0 : Encoding.UTF8.GetByteCount(value);
}
