using System.Diagnostics;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

namespace Monica.Platform.Services;

/// <summary>
/// Writes a fixed-shape <c>.kdbx</c> fixture for the artifact memory gate. Nothing in the app
/// calls it: the gate that answers "does the process give the decrypted model back" needs a big
/// database inside the shipped process, and a committed binary blob could not be re-derived,
/// reviewed or sized to fit a runner. The shape is fixed, the bytes are not - the database seed
/// and every protected stream differ per run.
/// </summary>
public static class KeePassSmokeVaultWriter
{
    /// <summary>
    /// Not a secret. The fixture carries the same password in the gate's log and command line.
    /// </summary>
    public const string DefaultPassword = "keepass-smoke-fixture-not-a-secret";

    private const int MaximumWriteAttempts = 5;

    /// <summary>
    /// Every 64th entry carries a 2 KiB attachment so the gate streams protected binaries, not
    /// only protected strings.
    /// </summary>
    private const int AttachmentStride = 64;

    private const int AttachmentBytes = 2_048;

    public static KeePassSmokeVaultInfo Write(string filePath, string password, int entries, int groups)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(groups);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(entries, KeePassVaultLimits.MaximumEntryCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(groups, KeePassVaultLimits.MaximumGroupCount);

        var stopwatch = Stopwatch.StartNew();
        for (var attempt = 0; attempt < MaximumWriteAttempts; attempt++)
        {
            var payload = BuildPayload(entries, groups, password);
            if (Unlocks(payload, password, entries))
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(filePath, payload);
                stopwatch.Stop();
                return new KeePassSmokeVaultInfo(
                    Path.GetFileName(filePath),
                    payload.Length,
                    entries,
                    groups,
                    stopwatch.ElapsedMilliseconds);
            }
        }

        throw new InvalidOperationException(
            $"KeePass smoke fixture could not be written in a readable state after {MaximumWriteAttempts} attempts.");
    }

    private static bool Unlocks(byte[] payload, string password, int expectedEntries)
    {
        // Called with the gate NOT held. The check runs through the service, which takes the gate on
        // another thread and returns the task to this one; holding the gate across that wait would
        // make the writer block on itself and the fixture would never be written.
        try
        {
            using var session = new KeePassVaultService()
                .OpenAsync(payload, "smoke.kdbx", password)
                .GetAwaiter()
                .GetResult();
            return session.EntryCount == expectedEntries;
        }
        catch (KeePassVaultException)
        {
            return false;
        }
    }

    private static byte[] BuildPayload(int entries, int groups, string password)
    {
        // Gated like every other key build and serialize: a fixture that unlocks only by luck would
        // make the runtime gate flaky rather than meaningful.
        lock (KeePassVaultParseGate.Gate)
        {
            var key = new CompositeKey();
            key.AddUserKey(new KcpPassword(password));
            var database = new PwDatabase();
            database.New(IOConnectionInfo.FromPath("smoke.kdbx"), key);
            database.Name = "Smoke Fixture";
            database.RootGroup.Name = "Smoke Root";

            try
            {
                var perGroup = Math.Max(1, (int)Math.Ceiling(entries / (double)groups));
                var placed = 0;
                for (var groupIndex = 0; placed < entries; groupIndex++)
                {
                    var group = new PwGroup(true, true, $"Folder {groupIndex % groups + 1}", PwIcon.Folder);
                    database.RootGroup.AddGroup(group, true);
                    var groupEnd = Math.Min(entries, placed + perGroup);
                    for (; placed < groupEnd; placed++)
                    {
                        group.AddEntry(BuildEntry(placed + 1), true);
                    }
                }

                using var stream = new MemoryStream();
                new KdbxFile(database).Save(stream, database.RootGroup, KdbxFormat.Default, null);
                return stream.ToArray();
            }
            finally
            {
                if (database.IsOpen)
                {
                    database.Close();
                }
            }
        }
    }

    private static PwEntry BuildEntry(int ordinal)
    {
        var entry = new PwEntry(true, true);
        entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, $"Entry {ordinal:D6}"));
        entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, $"user{ordinal}@example.com"));
        entry.Strings.Set(PwDefs.UrlField, new ProtectedString(false, $"https://entry{ordinal}.example.com"));
        entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, $"secret-{ordinal}"));
        entry.Strings.Set(
            "otp",
            new ProtectedString(true, "otpauth://totp/Smoke?secret=JBSWY3DPEHPK3PXPLBKHQZ3PNFYHI2LTHI===="));
        entry.Strings.Set("Reference", new ProtectedString(true, $"ticket-{ordinal:D6}"));
        entry.Strings.Set(PwDefs.NotesField, new ProtectedString(false, new string('n', 96)));
        if (ordinal % AttachmentStride == 0)
        {
            var content = new byte[AttachmentBytes];
            for (var i = 0; i < content.Length; i++)
            {
                content[i] = (byte)((ordinal + i) % 251);
            }

            entry.Binaries.Set($"payload-{ordinal:D6}.bin", new ProtectedBinary(true, content));
        }

        return entry;
    }
}

public sealed record KeePassSmokeVaultInfo(string FileName, long FileBytes, int Entries, int Groups, long WriteMs);
