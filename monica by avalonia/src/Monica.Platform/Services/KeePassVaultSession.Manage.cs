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

    /// <summary>
    /// The name of the folder a database creates for deleted entries. It is the name every other KeePass
    /// client uses, kept in English on purpose: the folder lives inside the file, so a translated name
    /// would be one database per language.
    /// </summary>
    public const string RecycleBinGroupName = "Recycle Bin";

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
    /// mistake to leave to a default. A folder that does go is written into the database's deletion
    /// list together with everything it took, which is the only way a later sync sees them as gone
    /// rather than hidden. Folders have no recycle path here because a KeePass database recycles
    /// entries, not the branches that hold them.
    /// </summary>
    public async Task<KeePassGroupDeleteResult> DeleteGroupAsync(
        string groupUuid,
        bool deleteContents,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var root = _root ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
        var database = _database ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
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

            var uuids = CollectTreeUuids(group);
            group.ParentGroup?.Groups.Remove(group);
            RecordDeletions(database, uuids);
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

            var source = group.ParentGroup;
            group.ParentGroup?.Groups.Remove(group);
            target.AddGroup(group, true, true);
            MarkMoved(group, source);
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

    /// <summary>
    /// Takes an entry out of the tree it shows in. Recycling keeps it inside the database, in the bin
    /// folder, and leaves no deletion record behind because nothing left; a permanent delete detaches
    /// it and records the uuid, so a client that syncs against this file learns the entry is gone
    /// instead of assuming the other side lost it.
    /// </summary>
    public async Task<KeePassEntryDeleteStatus> DeleteEntryAsync(
        string entryUuid,
        KeePassDeleteMode mode,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var root = _root ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
        var database = _database ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entry = FindEntry(root, entryUuid, cancellationToken);
            if (entry?.ParentGroup is not { } source)
            {
                return KeePassEntryDeleteStatus.NotFound;
            }

            if (mode == KeePassDeleteMode.RecycleBin)
            {
                var bin = EnsureRecycleBinGroup(database, root);
                if (!ReferenceEquals(source, bin))
                {
                    Relocate(entry, bin);
                    MarkModified();
                }

                Reindex(cancellationToken);
                return KeePassEntryDeleteStatus.Recycled;
            }

            source.Entries.Remove(entry);
            RecordDeletions(database, [entry.Uuid]);
            MarkModified();
            Reindex(cancellationToken);
            return KeePassEntryDeleteStatus.PermanentlyDeleted;
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

            Relocate(entry, target);
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

    /// <summary>
    /// Re-parents an entry without touching anything else about it. Only the location changes: a move
    /// that also moved the modification time would read back as an edit to the entry, and in a database
    /// two clients sync against that is a conflict nobody had.
    /// </summary>
    private static void Relocate(PwEntry entry, PwGroup target)
    {
        var source = entry.ParentGroup;
        source?.Entries.Remove(entry);
        target.AddEntry(entry, true);
        MarkMoved(entry, source);
    }

    private static void MarkMoved(PwEntry entry, PwGroup? source)
    {
        entry.PreviousParentGroup = source?.Uuid;
        entry.LocationChanged = DateTime.UtcNow;
    }

    private static void MarkMoved(PwGroup group, PwGroup? source)
    {
        group.PreviousParentGroup = source?.Uuid;
        group.LocationChanged = DateTime.UtcNow;
    }

    /// <summary>
    /// The folder deleted entries go to, created under the root when the file has no usable one. A
    /// pointer that is off, zeroed or aimed at a group that is no longer in the tree is ignored rather
    /// than repaired on open, so opening a database never marks it changed.
    /// </summary>
    private PwGroup EnsureRecycleBinGroup(PwDatabase database, PwGroup root)
    {
        if (ResolveRecycleBinGroup(database, root) is { } existing)
        {
            return existing;
        }

        var bin = new PwGroup(true, true, RecycleBinGroupName, PwIcon.TrashBin);
        root.AddGroup(bin, true);
        database.RecycleBinEnabled = true;
        database.RecycleBinUuid = bin.Uuid;
        database.RecycleBinChanged = DateTime.UtcNow;
        return bin;
    }

    /// <summary>
    /// Every uuid a folder delete takes with it: the folder itself, the entries hanging off it and the
    /// same again for each branch below.
    /// </summary>
    private static List<PwUuid> CollectTreeUuids(PwGroup group)
    {
        var uuids = new List<PwUuid> { group.Uuid };
        uuids.AddRange(group.Entries.Select(entry => entry.Uuid));
        foreach (var child in group.Groups)
        {
            uuids.AddRange(CollectTreeUuids(child));
        }

        return uuids;
    }

    /// <summary>
    /// Writes the database's list of what was deleted. A uuid already on the list is moved to the front
    /// with a fresh timestamp instead of being recorded twice, and the records written together share one
    /// time, because they did go away together.
    /// </summary>
    private static void RecordDeletions(PwDatabase database, IReadOnlyList<PwUuid> uuids)
    {
        var fresh = new List<PwUuid>();
        var pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var uuid in uuids)
        {
            if (pending.Add(uuid.ToHexString()))
            {
                fresh.Add(uuid);
            }
        }

        if (fresh.Count == 0)
        {
            return;
        }

        var kept = new List<PwDeletedObject>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in database.DeletedObjects)
        {
            var uuid = record.Uuid.ToHexString();
            if (pending.Contains(uuid) || !seen.Add(uuid))
            {
                continue;
            }

            kept.Add(record);
        }

        database.DeletedObjects.Clear();
        foreach (var record in kept)
        {
            database.DeletedObjects.Add(record);
        }

        var deletionTime = DateTime.UtcNow;
        foreach (var uuid in fresh)
        {
            database.DeletedObjects.Add(new PwDeletedObject(uuid, deletionTime));
        }
    }

    private KeePassGroupRow CreateGroupRow(PwGroup group)
    {
        var root = _root ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
        return new KeePassGroupRow(
            KeePassVaultText.NormalizeDisplayText(group.Name, UntitledGroupName),
            KeePassVaultText.GroupPathOf(group, root),
            group.Uuid.ToHexString(),
            group.ParentGroup?.Uuid.ToHexString(),
            group.Entries.Any(),
            IsInsideRecycleBin(group));
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
