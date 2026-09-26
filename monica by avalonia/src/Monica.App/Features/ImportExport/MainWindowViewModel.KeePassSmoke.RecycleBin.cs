using Monica.Platform.Services;

namespace Monica.App.ViewModels;

/// <summary>
/// The recycle bin's two exits, walked on the opened session for the artifact shot. A row in that folder
/// has exactly one move that is not destruction, and the folder itself is the only place a whole set may
/// be thrown away, so the seam asks for both and reports what the tree was left holding. The names it
/// looks rows up by are its own fixtures; nothing here reads or logs an entry's contents.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// Restores the selected row out of the bin, then recycles it again and empties the folder. The
    /// restored entry has to be under a folder that still exists and the emptied bin has to be gone with
    /// everything in it - a build that only moves the row between two folders it should not touch fails
    /// here rather than logging a flag nobody reads.
    /// </summary>
    private async Task<(bool EntryRestored, bool RecycleBinEmptied)> SmokeWalkKeePassBinExitsAsync(
        string managedEntry,
        string? binUuid)
    {
        if (binUuid is null)
        {
            return (false, false);
        }

        await RestoreKeePassEntryCommand.ExecuteAsync(null);
        var outsideBin = !_keePassTreeRows.Any(row =>
            row.IsEntryRow &&
            row.Label == managedEntry &&
            string.Equals(row.Entry?.GroupUuid, binUuid, StringComparison.OrdinalIgnoreCase));
        var entryRestored = outsideBin &&
            _keePassTreeRows.Any(row => row.IsEntryRow && row.Label == managedEntry);
        if (!entryRestored)
        {
            return (false, false);
        }

        var restoredRow = _keePassTreeRows.First(row => row.IsEntryRow && row.Label == managedEntry);
        await SelectKeePassRowCommand.ExecuteAsync(restoredRow);
        await DeleteKeePassEntryAsync(KeePassDeleteMode.RecycleBin, _ => Task.FromResult(true));
        await EmptyKeePassRecycleBinAsync(_ => Task.FromResult(true));
        var recycleBinEmptied = !_keePassTreeRows.Any(row => row.IsEntryRow && row.Label == managedEntry) &&
            !_keePassTreeRows.Any(row =>
                row.IsEntryRow is false &&
                string.Equals(row.Group?.Uuid, binUuid, StringComparison.OrdinalIgnoreCase));
        return (true, recycleBinEmptied);
    }
}
