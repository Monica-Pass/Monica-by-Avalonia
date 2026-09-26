using KeePassLib;

namespace Monica.Platform.Services;

/// <summary>
/// The way out of the recycle bin. A .kdbx keeps deleted entries in a folder inside the database, so a
/// client that can put something in there has to offer a way back out - otherwise the only person who
/// can undo a mis-click is whoever happens to own another KeePass program.
/// </summary>
public sealed partial class KeePassVaultSession
{
    /// <summary>
    /// How many entries the bin holds, folders inside it included. The number a person reads before
    /// agreeing to destroy everything has to come from the database rather than from the rows currently
    /// on screen, because a collapsed folder is not an empty one.
    /// </summary>
    public int RecycleBinEntryCount =>
        _recycleBin is { } bin ? (int)bin.GetEntriesCount(true) : 0;

    /// <summary>
    /// Takes an entry back out of the bin. An entry carries the uuid of the folder it was moved out of,
    /// so a recycle this session made goes back where it came from even after a save and a reopen - at
    /// KDBX 4.1, which is the only version that writes that pointer down; a 3.1 or 4.0 file has nowhere
    /// to put it. The root is the fall-back for the three cases where the pointer cannot be honoured:
    /// nothing was recorded, the folder is no longer in the file, or the pointer names the bin itself,
    /// which is where an entry must never be sent back to. Whatever the destination, the entry itself is
    /// untouched: same uuid, same fields, same history.
    /// </summary>
    public async Task<KeePassEntryDetail?> RestoreEntryAsync(
        string entryUuid,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var root = _root ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entry = FindEntry(root, entryUuid, cancellationToken);
            if (entry?.ParentGroup is not { } bin || !IsInRecycleBin(bin.Uuid.ToHexString()))
            {
                return null;
            }

            var target = ResolveRestoreTarget(root, entry);
            Relocate(entry, target);
            // The entry is no longer somewhere it was moved away from; leaving the pointer set would
            // make a second restore land it back in the bin it just left.
            entry.PreviousParentGroup = PwUuid.Zero;
            MarkModified();
            Reindex(cancellationToken);
            return CreateDetail(entry, KeePassVaultText.GroupPathOf(target, root));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Empties the bin: everything inside goes permanently, the bin folder itself goes with it, and
    /// every uuid that left is written to the database's deletion list. That list is what tells a
    /// client syncing against this file that the entry is gone rather than merely hidden, so an empty
    /// bin that records nothing would bring the whole set back on the next pull.
    /// </summary>
    public async Task<int> EmptyRecycleBinAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var root = _root ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
        var database = _database ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var bin = ResolveRecycleBinGroup(database, root);
            if (bin is null)
            {
                return 0;
            }

            var entryCount = (int)bin.GetEntriesCount(true);
            var gone = CollectTreeUuids(bin);
            bin.ParentGroup?.Groups.Remove(bin);
            RecordDeletions(database, gone);
            database.RecycleBinUuid = PwUuid.Zero;
            database.RecycleBinChanged = DateTime.UtcNow;
            MarkModified();
            Reindex(cancellationToken);
            return entryCount;
        }
        finally
        {
            _gate.Release();
        }
    }

    private PwGroup ResolveRestoreTarget(PwGroup root, PwEntry entry)
    {
        var wanted = entry.PreviousParentGroup?.ToHexString();
        if (string.IsNullOrWhiteSpace(wanted))
        {
            return root;
        }

        var previous = ResolveGroup(root, wanted);
        return previous is null || IsInsideRecycleBin(previous) ? root : previous;
    }
}
