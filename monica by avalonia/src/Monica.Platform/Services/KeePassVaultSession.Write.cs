using System.Security.Cryptography;
using KeePassLib;
using KeePassLib.Security;

namespace Monica.Platform.Services;

/// <summary>
/// The editing and saving half of an unlocked database. Everything here mutates the same decoded
/// model the read side walks, so an edit is visible to a browser immediately and only reaches the
/// disk once a payload has been written and proven to unlock again.
/// </summary>
public sealed partial class KeePassVaultSession
{
    public string? SourcePath { get; }

    /// <summary>
    /// The KDBX version the file was opened at. Saving pins it again, because the underlying writer
    /// otherwise rewrites a 4.1 database as 4.0.
    /// </summary>
    public uint? FormatVersion { get; }

    public bool IsDirty { get; private set; }

    /// <summary>
    /// SHA-256 of the bytes this session was opened from, or of the last payload it wrote. It is
    /// what lets an in-place save notice the file moved on without it.
    /// </summary>
    public string PayloadSha256 { get; private set; }

    public async Task<KeePassEntryDetail?> UpdateEntryAsync(
        KeePassEntryEdit edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ThrowIfDisposed();
        var root = _root ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entry = FindEntry(root, edit.EntryUuid, cancellationToken);
            if (entry is null)
            {
                return null;
            }

            ApplyEdit(entry, edit, keepHistory: true);
            IsDirty = true;
            return CreateDetail(entry, KeePassVaultText.GroupPathOf(entry.ParentGroup ?? root, root));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Serializes the current model into a payload that has already been re-opened and compared
    /// against it. Nothing has been written anywhere yet, so a caller can publish the bytes itself.
    /// </summary>
    public async Task<byte[]> ExportAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var database = _database ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(
                () => KeePassVaultWrite.BuildVerifiedPayload(database, database.MasterKey, FormatVersion),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Saves in place. Refuses to overwrite a file whose bytes differ from the ones this session was
    /// opened from, because the alternative is discarding changes made elsewhere without a word.
    /// </summary>
    public Task<KeePassSaveResult> SaveAsync(CancellationToken cancellationToken = default)
    {
        var source = SourcePath;
        if (string.IsNullOrWhiteSpace(source))
        {
            throw KeePassVaultFaults.NoSourceFile();
        }

        return SaveCoreAsync(source, checkForExternalChange: true, cancellationToken);
    }

    public Task<KeePassSaveResult> SaveToAsync(string path, CancellationToken cancellationToken = default)
    {
        var target = KeePassVaultWrite.NormalizePath(path);
        var inPlace = string.Equals(
            target,
            SourcePath,
            StringComparison.OrdinalIgnoreCase);
        return SaveCoreAsync(target, checkForExternalChange: inPlace, cancellationToken);
    }

    private async Task<KeePassSaveResult> SaveCoreAsync(
        string target,
        bool checkForExternalChange,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (checkForExternalChange && !string.Equals(HashFileIfExists(target), PayloadSha256, StringComparison.Ordinal))
            {
                throw KeePassVaultFaults.ConcurrentChange();
            }

            var database = _database ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
            var payload = KeePassVaultWrite.BuildVerifiedPayload(database, database.MasterKey, FormatVersion);
            await KeePassVaultWrite.WriteAtomicAsync(target, payload, cancellationToken).ConfigureAwait(false);
            PayloadSha256 = Convert.ToHexString(SHA256.HashData(payload));
            IsDirty = false;
            database.Modified = false;
            EntryCount = KeePassVaultWrite.CountEntries(database.RootGroup);
            return new KeePassSaveResult(target, payload.Length, PayloadSha256, EntryCount);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <param name="keepHistory">
    /// A created entry has no earlier shape to remember, so its first fill must not snapshot itself.
    /// </param>
    private void ApplyEdit(PwEntry entry, KeePassEntryEdit edit, bool keepHistory)
    {
        if (keepHistory)
        {
            AddHistorySnapshot(entry);
        }

        entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, edit.Title.Trim()));
        entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, edit.UserName.Trim()));
        entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, edit.Password));
        entry.Strings.Set(PwDefs.UrlField, new ProtectedString(false, edit.Url.Trim()));
        entry.Strings.Set(PwDefs.NotesField, new ProtectedString(false, edit.Notes));

        var totp = edit.AuthenticatorKey.Trim();
        if (totp.Length == 0)
        {
            foreach (var name in KeePassVaultText.TotpFieldNames)
            {
                entry.Strings.Remove(name);
            }
        }
        else
        {
            entry.Strings.Set(KeePassVaultText.TotpFieldNames[0], new ProtectedString(false, totp));
        }

        // An edit carries the whole custom field set, so anything it does not mention is a field the
        // user removed rather than one the editor never looked at.
        foreach (var name in entry.Strings.GetKeys()
                     .Where(name => KeePassVaultText.IsCustomField(name))
                     .ToArray())
        {
            entry.Strings.Remove(name);
        }

        foreach (var field in edit.CustomFields)
        {
            var name = KeePassVaultText.NormalizeFieldName(field.Name);
            if (name.Length == 0 || !KeePassVaultText.IsCustomField(name))
            {
                continue;
            }

            entry.Strings.Set(name, new ProtectedString(field.IsProtected, field.Value));
        }

        entry.LastModificationTime = DateTime.UtcNow;
        var database = _database;
        if (database is not null)
        {
            database.Modified = true;
        }
    }

    /// <summary>
    /// Keeps the pre-edit shape of the entry, the way a KeePass client is expected to. The snapshot
    /// drops its own history, otherwise every edit would carry forward the whole previous chain, and
    /// the entry's list is pruned to the database's own policy straight after it grew by one.
    /// </summary>
    private void AddHistorySnapshot(PwEntry entry)
    {
        var snapshot = entry.CloneDeep();
        snapshot.History.Clear();
        entry.History.Add(snapshot);
        MaintainHistory(entry);
    }

    private static PwEntry? FindEntry(PwGroup group, string entryUuid, CancellationToken cancellationToken)
    {
        var wanted = entryUuid.Trim();
        if (wanted.Length == 0)
        {
            return null;
        }

        foreach (var entry in group.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(entry.Uuid.ToHexString(), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        foreach (var child in group.Groups)
        {
            var found = FindEntry(child, wanted, cancellationToken);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static string? HashFileIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
