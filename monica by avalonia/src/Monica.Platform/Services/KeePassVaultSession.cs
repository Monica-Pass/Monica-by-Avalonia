using System.Runtime.CompilerServices;
using KeePassLib;

namespace Monica.Platform.Services;

/// <summary>
/// An unlocked KeePass database. The decoded model stays owned by the session so callers never
/// re-read the file, while entry secrets and attachment bytes are resolved one entry at a time
/// instead of being projected for the whole database up front.
/// </summary>
public sealed partial class KeePassVaultSession : IDisposable
{
    private PwDatabase? _database;
    private PwGroup? _root;
    private PwGroup? _recycleBin;
    private readonly List<KeePassGroupRow> _groups = [];
    private readonly Dictionary<string, PwGroup> _groupsByUuid = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    internal KeePassVaultSession(
        PwDatabase database,
        string fileName,
        string? sourcePath,
        ReadOnlySpan<byte> payload,
        CancellationToken cancellationToken)
    {
        _database = database;
        _root = database.RootGroup ?? throw KeePassVaultFaults.InvalidFile();
        SourceFileName = fileName;
        SourcePath = string.IsNullOrWhiteSpace(sourcePath) ? null : Path.GetFullPath(sourcePath.Trim());
        FormatVersion = KeePassVaultWrite.ReadFormatVersion(payload);
        PayloadSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload));
        RootGroupUuid = _root.Uuid.ToHexString();
        DatabaseId = KeePassVaultText.CreateDatabaseId(_root.Uuid.UuidBytes);
        DatabaseName = string.IsNullOrWhiteSpace(database.Name)
            ? Path.GetFileNameWithoutExtension(fileName)
            : database.Name.Trim();
        RootGroupRow = new KeePassGroupRow(
            KeePassVaultText.NormalizeDisplayText(_root.Name, Path.GetFileNameWithoutExtension(fileName)),
            "",
            RootGroupUuid,
            null,
            _root.Entries.Any());
        Index(_root, cancellationToken);
    }

    public long DatabaseId { get; }

    public string DatabaseName { get; }

    public string SourceFileName { get; }

    public string RootGroupUuid { get; }

    /// <summary>
    /// The root folder as a row. <see cref="Groups"/> leaves it out because it is not a group inside
    /// the database, but a browser has to show it and to know what an expander would reveal.
    /// </summary>
    public KeePassGroupRow RootGroupRow { get; private set; }

    public IReadOnlyList<KeePassGroupRow> Groups => _groups;

    public int GroupCount => _groups.Count;

    public int EntryCount { get; private set; }

    /// <summary>
    /// The folder deleted entries land in, or null while the database has no usable one. The pointer a
    /// file carries is only trusted when it reaches a group that is in the tree and is not the root, so
    /// a stale pointer costs a new bin rather than the entries routed through it.
    /// </summary>
    public string? RecycleBinUuid => _recycleBin?.Uuid.ToHexString();

    /// <summary>
    /// Whether a folder is the recycle bin or sits inside it. Entries in there are kept out of the
    /// recycle path and out of the move targets, which is what a folder of already-deleted things is.
    /// </summary>
    public bool IsInRecycleBin(string? groupUuid)
    {
        var bin = _recycleBin;
        if (bin is null || string.IsNullOrWhiteSpace(groupUuid))
        {
            return false;
        }

        return _groupsByUuid.TryGetValue(groupUuid.Trim(), out var group) && IsInsideRecycleBin(group);
    }

    private bool IsInsideRecycleBin(PwGroup group)
    {
        var bin = _recycleBin;
        return bin is not null && (ReferenceEquals(group, bin) || group.IsContainedIn(bin));
    }

    private PwGroup? ResolveRecycleBinGroup(PwDatabase database, PwGroup root)
    {
        if (!database.RecycleBinEnabled)
        {
            return null;
        }

        var uuid = database.RecycleBinUuid;
        if (uuid is null || IsZeroUuid(uuid))
        {
            return null;
        }

        var wanted = uuid.ToHexString();
        if (string.Equals(wanted, root.Uuid.ToHexString(), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return FindGroupByUuid(root, wanted);
    }

    private static bool IsZeroUuid(PwUuid uuid)
    {
        var bytes = uuid.UuidBytes;
        return bytes is not { Length: > 0 } || bytes.All(value => value == 0);
    }

    private static PwGroup? FindGroupByUuid(PwGroup group, string uuid)
    {
        foreach (var child in group.Groups)
        {
            if (string.Equals(child.Uuid.ToHexString(), uuid, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }

            if (FindGroupByUuid(child, uuid) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    /// <summary>
    /// Streams every entry with its secrets, custom fields and attachment content resolved.
    /// One entry is materialized at a time, so a large database costs one entry, not the file.
    /// </summary>
    public async IAsyncEnumerable<KeePassEntryDetail> ReadDetailsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var root = _root ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await foreach (var detail in ReadGroupDetailsAsync(root, root, "", cancellationToken)
                               .ConfigureAwait(false))
            {
                yield return detail;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Lists one folder's entries without resolving a secret, so selecting a folder costs that
    /// folder rather than the whole file.
    /// </summary>
    public async Task<IReadOnlyList<KeePassEntryRow>> ReadGroupRowsAsync(
        string? groupUuid,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var root = _root ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var group = ResolveGroup(root, groupUuid);
            if (group is null)
            {
                return [];
            }

            var path = KeePassVaultText.GroupPathOf(group, root);
            return group.Entries.Select(entry => CreateRow(entry, path)).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Resolves the secrets, custom fields and attachment bytes of one entry, or null once the
    /// entry is gone from the model.
    /// </summary>
    public async Task<KeePassEntryDetail?> ReadDetailAsync(
        string groupUuid,
        string entryUuid,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var root = _root ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var group = ResolveGroup(root, groupUuid);
            if (group is null)
            {
                return null;
            }

            var path = KeePassVaultText.GroupPathOf(group, root);
            var entry = group.Entries.FirstOrDefault(
                item => string.Equals(item.Uuid.ToHexString(), entryUuid, StringComparison.OrdinalIgnoreCase));
            return entry is null ? null : CreateDetail(entry, path);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var database = _database;
        _database = null;
        _root = null;
        _recycleBin = null;
        _groups.Clear();
        _groupsByUuid.Clear();
        EntryCount = 0;
        if (database is { IsOpen: true })
        {
            database.Close();
        }

        _gate.Dispose();
    }

    private static async IAsyncEnumerable<KeePassEntryDetail> ReadGroupDetailsAsync(
        PwGroup root,
        PwGroup group,
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var entry in group.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return CreateDetail(entry, path);
        }

        foreach (var child in group.Groups)
        {
            await foreach (var detail in ReadGroupDetailsAsync(
                               root,
                               child,
                               KeePassVaultText.GroupPathOf(child, root),
                               cancellationToken)
                               .ConfigureAwait(false))
            {
                yield return detail;
            }
        }
    }

    private void Index(PwGroup root, CancellationToken cancellationToken)
    {
        _recycleBin = _database is { } database ? ResolveRecycleBinGroup(database, root) : null;
        var state = new IndexState();
        IndexGroup(root, root, "", state, cancellationToken);
        EntryCount = state.EntryCount;
    }

    private void IndexGroup(PwGroup root, PwGroup group, string path, IndexState state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _groupsByUuid[group.Uuid.ToHexString()] = group;
        if (!ReferenceEquals(group, root))
        {
            if (_groups.Count >= KeePassVaultLimits.MaximumGroupCount)
            {
                throw KeePassVaultFaults.ResourceLimitExceeded();
            }

            _groups.Add(new KeePassGroupRow(
                KeePassVaultText.NormalizeDisplayText(group.Name, "Untitled group"),
                path,
                group.Uuid.ToHexString(),
                group.ParentGroup?.Uuid.ToHexString(),
                group.Entries.Any(),
                IsInsideRecycleBin(group)));
        }

        foreach (var entry in group.Entries)
        {
            if (state.EntryCount >= KeePassVaultLimits.MaximumEntryCount)
            {
                throw KeePassVaultFaults.ResourceLimitExceeded();
            }

            state.EntryCount++;
            foreach (var binary in entry.Binaries)
            {
                state.TotalAttachmentBytes = KeePassVaultLimits.AddAttachmentBytes(
                    binary.Value.Length,
                    state.TotalAttachmentBytes);
            }
        }

        foreach (var child in group.Groups)
        {
            IndexGroup(root, child, KeePassVaultText.GroupPathOf(child, root), state, cancellationToken);
        }
    }

    private sealed class IndexState
    {
        public int EntryCount { get; set; }

        public long TotalAttachmentBytes { get; set; }
    }

    private static KeePassEntryDetail CreateDetail(PwEntry entry, string groupPath)
    {
        var row = CreateRow(entry, groupPath);
        var customFields = entry.Strings
            .Where(item => KeePassVaultText.IsCustomField(item.Key))
            .Select(item => new KeePassCustomField(item.Key, item.Value.ReadString(), item.Value.IsProtected))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var attachments = entry.Binaries
            .Select(binary => new KeePassAttachmentContent(
                new KeePassAttachmentRow(binary.Key, binary.Key, binary.Value.Length),
                binary.Value.ReadData()))
            .ToArray();
        return new KeePassEntryDetail(
            row,
            entry.Strings.ReadSafe(PwDefs.PasswordField),
            entry.Strings.ReadSafe(PwDefs.NotesField),
            KeePassVaultText.ReadTotp(entry),
            customFields,
            attachments);
    }

    private static KeePassEntryRow CreateRow(PwEntry entry, string groupPath)
    {
        var attachmentRows = entry.Binaries
            .Select(binary => new KeePassAttachmentRow(binary.Key, binary.Key, binary.Value.Length))
            .ToArray();
        return new KeePassEntryRow(
            entry.Uuid.ToHexString(),
            entry.ParentGroup?.Uuid.ToHexString() ?? "",
            groupPath,
            entry.Strings.ReadSafe(PwDefs.TitleField),
            entry.Strings.ReadSafe(PwDefs.UserNameField),
            entry.Strings.ReadSafe(PwDefs.UrlField),
            KeePassVaultText.ToDateTimeOffset(entry.CreationTime),
            KeePassVaultText.ToDateTimeOffset(entry.LastModificationTime),
            attachmentRows);
    }

    private PwGroup? ResolveGroup(PwGroup root, string? groupUuid)
    {
        var uuid = groupUuid?.Trim();
        if (string.IsNullOrEmpty(uuid) || string.Equals(uuid, RootGroupUuid, StringComparison.OrdinalIgnoreCase))
        {
            return root;
        }

        return _groupsByUuid.GetValueOrDefault(uuid);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(KeePassVaultSession));
        }
    }
}
