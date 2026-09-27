using KeePassLib;
using KeePassLib.Security;

namespace Monica.Platform.Services;

/// <summary>
/// The shapes an entry used to have. Every edit the session applies pushes a snapshot onto the
/// entry's own history list, so this half only reads that list and hands one of its shapes back
/// through the same funnel a form edit uses - a restore is an edit whose values came from the
/// database instead of from a person.
/// </summary>
public sealed partial class KeePassVaultSession
{
    /// <summary>
    /// The remembered versions of one entry, oldest first. The index in each row is its position in
    /// the database's history list, which is what <see cref="RestoreHistoryAsync" /> takes.
    /// </summary>
    public async Task<IReadOnlyList<KeePassHistoryVersion>> ReadHistoryAsync(
        string entryUuid,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var root = _root ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entry = FindEntry(root, entryUuid, cancellationToken);
            if (entry is null)
            {
                return [];
            }

            var liveUuid = entry.Uuid.ToHexString();
            var versions = new List<KeePassHistoryVersion>((int)entry.History.UCount);
            for (var index = 0; index < entry.History.UCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                versions.Add(CreateHistoryVersion(index, entry.History.GetAt((uint)index), liveUuid));
            }

            return versions;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Reverts one entry to a remembered version, or returns null when the entry is gone or that
    /// version is no longer there - a history list is capped, so a version can age out between the
    /// moment it was listed and the moment it was clicked.
    /// </summary>
    public async Task<KeePassEntryDetail?> RestoreHistoryAsync(
        string entryUuid,
        int index,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var root = _root ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entry = FindEntry(root, entryUuid, cancellationToken);
            if (entry is null || index < 0 || index >= (int)entry.History.UCount)
            {
                return null;
            }

            var historical = entry.History.GetAt((uint)index);
            ApplyEdit(entry, CreateEdit(entry.Uuid.ToHexString(), historical), keepHistory: true);

            // The rest of the entry is what the version said it was, attachments included; the funnel
            // does not carry them, so they follow here, deep-cloned so the history item keeps its own.
            entry.Binaries = historical.Binaries.CloneDeep();
            IsDirty = true;
            return CreateDetail(entry, KeePassVaultText.GroupPathOf(entry.ParentGroup ?? root, root));
        }
        finally
        {
            _gate.Release();
        }
    }

    private static KeePassHistoryVersion CreateHistoryVersion(
        int index,
        PwEntry historical,
        string entryUuid) =>
        new(
            index,
            entryUuid,
            historical.Strings.ReadSafe(PwDefs.TitleField),
            historical.Strings.ReadSafe(PwDefs.UserNameField),
            historical.Strings.ReadSafe(PwDefs.UrlField),
            KeePassVaultText.ToDateTimeOffset(historical.CreationTime),
            KeePassVaultText.ToDateTimeOffset(historical.LastModificationTime),
            historical.Strings.GetKeys().Count(name => KeePassVaultText.IsCustomField(name)),
            (int)historical.Binaries.UCount);

    /// <summary>
    /// Reads a whole version out of the database as an edit, custom fields and TOTP included - the
    /// funnel replaces those sets wholesale, so leaving anything out of this read would delete it
    /// from the entry the moment the restore landed.
    /// </summary>
    private static KeePassEntryEdit CreateEdit(string entryUuid, PwEntry historical) =>
        new(
            entryUuid,
            historical.Strings.ReadSafe(PwDefs.TitleField),
            historical.Strings.ReadSafe(PwDefs.UserNameField),
            historical.Strings.ReadSafe(PwDefs.PasswordField),
            historical.Strings.ReadSafe(PwDefs.UrlField),
            historical.Strings.ReadSafe(PwDefs.NotesField),
            KeePassVaultText.ReadTotp(historical),
            historical.Strings
                .Where(item => KeePassVaultText.IsCustomField(item.Key))
                .Select(item => new KeePassCustomField(item.Key, item.Value.ReadString(), item.Value.IsProtected))
                .ToArray());
}
