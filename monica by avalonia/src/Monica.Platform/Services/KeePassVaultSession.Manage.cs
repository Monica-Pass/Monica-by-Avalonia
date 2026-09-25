using KeePassLib;

namespace Monica.Platform.Services;

/// <summary>
/// The structural half of an unlocked database: folders and entries are created, renamed, moved and
/// deleted here. Every operation mutates the decoded model the browser walks, marks the session dirty
/// and re-indexes, so the tree and the counts describe the database as it now is rather than as it was
/// when the file was opened. Nothing reaches disk until a save.
/// </summary>
public sealed partial class KeePassVaultSession
{
    private const string UntitledGroupName = "Untitled group";

    public async Task<KeePassGroupRow?> CreateGroupAsync(
        string parentGroupUuid,
        string name,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var root = _root ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var parent = ResolveGroup(root, parentGroupUuid);
            if (parent is null)
            {
                return null;
            }

            var group = new PwGroup(true, true, GroupName(name), PwIcon.Folder);
            parent.AddGroup(group, true);
            MarkModified();
            Reindex(cancellationToken);
            return CreateGroupRow(group);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<KeePassGroupRow?> RenameGroupAsync(
        string groupUuid,
        string name,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var root = _root ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var group = ResolveGroup(root, groupUuid);
            var trimmed = name?.Trim() ?? "";
            if (group is null || ReferenceEquals(group, root) || trimmed.Length == 0)
            {
                return null;
            }

            group.Name = GroupName(trimmed);
            group.Touch(true);
            MarkModified();
            Reindex(cancellationToken);
            return CreateGroupRow(group);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Detaches a folder. A folder that still holds entries or sub-folders is reported back instead of
    /// being emptied, because deleting passwords by clicking the folder that holds them is not a
    /// mistake to leave to a default.
    /// </summary>
    public async Task<KeePassGroupDeleteResult> DeleteGroupAsync(
        string groupUuid,
        bool deleteContents,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var root = _root ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var group = ResolveGroup(root, groupUuid);
            if (group is null || ReferenceEquals(group, root))
            {
                return new KeePassGroupDeleteResult(KeePassGroupDeleteStatus.NotFound, 0, 0);
            }

            var entryCount = (int)group.GetEntriesCount(true);
            var groupCount = (int)group.GetGroups(true).UCount;
            if (!deleteContents && (entryCount > 0 || groupCount > 0))
            {
                return new KeePassGroupDeleteResult(KeePassGroupDeleteStatus.NotEmpty, entryCount, groupCount);
            }

            group.ParentGroup?.Groups.Remove(group);
            MarkModified();
            Reindex(cancellationToken);
            return new KeePassGroupDeleteResult(KeePassGroupDeleteStatus.Deleted, entryCount, groupCount);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Reparents a folder. A folder dropped on itself, on its current parent, or on one of its own
    /// descendants would cut it out of the tree it lands in, so all three are refused.
    /// </summary>
    public async Task<bool> MoveGroupAsync(
        string groupUuid,
        string targetParentUuid,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var root = _root ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var group = ResolveGroup(root, groupUuid);
            var target = ResolveGroup(root, targetParentUuid);
            if (group is null || target is null
                || ReferenceEquals(group, root)
                || ReferenceEquals(group, target)
                || ReferenceEquals(group.ParentGroup, target)
                || target.IsContainedIn(group))
            {
                return false;
            }

            group.ParentGroup?.Groups.Remove(group);
            target.AddGroup(group, true, true);
            group.Touch(true);
            MarkModified();
            Reindex(cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Adds an entry to one folder and fills it from a draft the editor never committed to the file.
    /// Creating on apply rather than on click means a new entry the user abandons leaves nothing
    /// behind in the database.
    /// </summary>
    public async Task<KeePassEntryDetail?> CreateEntryAsync(
        string groupUuid,
        KeePassEntryEdit draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
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

            var entry = new PwEntry(true, true);
            group.AddEntry(entry, true);
            entry.SetCreatedNow();
            ApplyEdit(entry, draft, keepHistory: false);
            MarkModified();
            Reindex(cancellationToken);
            return CreateDetail(entry, KeePassVaultText.GroupPathOf(group, root));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteEntryAsync(string entryUuid, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var root = _root ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entry = FindEntry(root, entryUuid, cancellationToken);
            if (entry?.ParentGroup is null)
            {
                return false;
            }

            entry.ParentGroup.Entries.Remove(entry);
            MarkModified();
            Reindex(cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Moves an entry into a different folder. A drop on the folder the entry already sits in is
    /// refused rather than reported as a change, so the tree never announces a move that did nothing.
    /// </summary>
    public async Task<KeePassEntryDetail?> MoveEntryAsync(
        string entryUuid,
        string targetGroupUuid,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var root = _root ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entry = FindEntry(root, entryUuid, cancellationToken);
            var target = ResolveGroup(root, targetGroupUuid);
            if (entry?.ParentGroup is null || target is null || ReferenceEquals(entry.ParentGroup, target))
            {
                return null;
            }

            entry.ParentGroup.Entries.Remove(entry);
            target.AddEntry(entry, true);
            entry.Touch(true);
            MarkModified();
            Reindex(cancellationToken);
            return CreateDetail(entry, KeePassVaultText.GroupPathOf(target, root));
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string GroupName(string name) =>
        KeePassVaultText.NormalizeDisplayText(name, UntitledGroupName);

    private KeePassGroupRow CreateGroupRow(PwGroup group)
    {
        var root = _root ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
        return new KeePassGroupRow(
            KeePassVaultText.NormalizeDisplayText(group.Name, UntitledGroupName),
            KeePassVaultText.GroupPathOf(group, root),
            group.Uuid.ToHexString(),
            group.ParentGroup?.Uuid.ToHexString(),
            group.Entries.Any());
    }

    private void MarkModified()
    {
        IsDirty = true;
        if (_database is not null)
        {
            _database.Modified = true;
        }
    }

    private void Reindex(CancellationToken cancellationToken)
    {
        var root = _root ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
        _groups.Clear();
        _groupsByUuid.Clear();
        Index(root, cancellationToken);
        RootGroupRow = RootGroupRow with { HasEntries = root.Entries.Any() };
    }
}
