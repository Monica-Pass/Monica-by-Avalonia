using Monica.App.Features.ImportExport;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// Drives this view model to the point where a KeePass entry is open in the edit form, for the
    /// artifact smoke run that captures the screen. The panel is otherwise only reachable through a
    /// native file dialog, which a shipped-binary screenshot cannot answer. Nothing here writes: it
    /// opens a file, browses it, and stops with the form on screen.
    /// </summary>
    internal async Task<KeePassSmokeEditState> SmokeShowKeePassEditorAsync(
        string path,
        string password,
        CancellationToken cancellationToken = default)
    {
        var opened = await SmokeOpenKeePassDatabaseAsync(path, password, cancellationToken);
        var entryRow = opened.EntryRow;
        if (entryRow is null)
        {
            return new KeePassSmokeEditState(
                EditorShown: false,
                DatabaseOpened: opened.DatabaseOpened,
                TreeRows: _keePassTreeRows.Count,
                EntryRows: 0,
                FileBytes: opened.FileBytes);
        }

        await SelectKeePassRowCommand.ExecuteAsync(entryRow);
        await EditKeePassEntryCommand.ExecuteAsync(null);
        return new KeePassSmokeEditState(
            HasKeePassEditor,
            opened.DatabaseOpened,
            _keePassTreeRows.Count,
            _keePassTreeRows.Count(row => row.IsEntryRow),
            opened.FileBytes);
    }

    /// <summary>
    /// What the shot reports when the database never opened: nothing happened, and the only number worth
    /// logging is the one the tree already had.
    /// </summary>
    private static KeePassManageSmokeState NoKeePassManageState(int treeRows, long fileBytes = 0) => new(
        DatabaseOpened: false,
        FolderShown: false,
        DraftShown: false,
        EntryShown: false,
        UnsavedNoticeShown: false,
        BinShown: false,
        EntryInBin: false,
        BinDeleteSplit: false,
        EntryRestored: false,
        RecycleBinEmptied: false,
        TreeRows: treeRows,
        FolderRows: 0,
        EntryRows: 0,
        FileBytes: fileBytes);

    /// <summary>
    /// The same seam for the management surface: create a folder, add an entry into it and recycle that
    /// entry. It exists because the whole set of row commands sits behind that file dialog, and because
    /// the result is the one frame that can show the tree growing and the unsaved-changes notice lighting
    /// up without a save. Like the browse seam it never writes the file.
    /// </summary>
    internal async Task<KeePassManageSmokeState> SmokeShowKeePassEditorManagementAsync(
        string path,
        string password,
        CancellationToken cancellationToken = default)
    {
        const string managedFolder = "Smoke Managed Folder";
        const string managedEntry = "Smoke Managed Entry";
        // This seam opens the file again, and opening refuses to run over unsaved edits. Failing here
        // rather than browsing on into the previous session keeps the shot honest: the rows it shows
        // are the ones this run created, and if they cannot be, the log says the database never opened.
        if (KeePassVaultIsDirty)
        {
            return NoKeePassManageState(_keePassTreeRows.Count);
        }

        var opened = await SmokeOpenKeePassDatabaseAsync(path, password, cancellationToken);
        var fileBytes = opened.FileBytes;
        if (opened.DatabaseOpened is false)
        {
            return NoKeePassManageState(_keePassTreeRows.Count, fileBytes);
        }

        // A folder is created under what the tree currently resolves as "the folder you are looking
        // at", which for a fresh open is the top row - the same answer a click on it would get.
        var parentRow = _keePassTreeRows.FirstOrDefault(row => row.Kind == KeePassTreeRowKind.Folder);
        if (parentRow is not null)
        {
            await SelectKeePassRowCommand.ExecuteAsync(parentRow);
        }

        KeePassFolderName = managedFolder;
        await CreateKeePassFolderCommand.ExecuteAsync(null);
        var folderRow = _keePassTreeRows.FirstOrDefault(row =>
            row.Kind == KeePassTreeRowKind.Folder && row.Label == managedFolder);

        var draftShown = false;
        if (folderRow is not null)
        {
            await SelectKeePassRowCommand.ExecuteAsync(folderRow);
            NewKeePassEntryCommand.Execute(null);
            draftShown = KeePassEditorPublic is { IsDraft: true };
            if (draftShown)
            {
                KeePassEditorPublic!.Title = managedEntry;
                await ApplyKeePassEntryEditCommand.ExecuteAsync(null);
            }
        }

        var entryShown = _keePassTreeRows.Any(row => row.IsEntryRow && row.Label == managedEntry);

        // The split delete is walked end to end on the shipped binary, minus the modal: the confirmation
        // arrives as an immediate yes, because a dialog would sit on the frame this seam photographs.
        var managedRow = _keePassTreeRows.FirstOrDefault(row => row.IsEntryRow && row.Label == managedEntry);
        if (managedRow?.Entry is { } managedEntryRow)
        {
            await SelectKeePassRowCommand.ExecuteAsync(managedRow);
            await DeleteKeePassEntryAsync(KeePassDeleteMode.RecycleBin, _ => Task.FromResult(true));
        }

        var binRow = _keePassTreeRows.FirstOrDefault(row =>
            row.Kind == KeePassTreeRowKind.Folder && row.Group?.IsRecycleBin == true);
        var binnedRow = _keePassTreeRows.FirstOrDefault(row => row.IsEntryRow && row.Label == managedEntry);
        var entryInBin = binRow is not null &&
            binnedRow?.Entry?.GroupUuid == binRow.Group?.Uuid;
        var binUuid = binRow?.Group?.Uuid;
        if (binnedRow is not null)
        {
            await SelectKeePassRowCommand.ExecuteAsync(binnedRow);
        }

        // Read before the row leaves the bin: the split pair is a fact about the menu on a binned row,
        // and the restore below clears the selection by design.
        var binDeleteSplit = binnedRow is not null && KeePassSelectedEntryInRecycleBin;
        // The two ways out of the bin, walked here because a person reaches them by right-clicking a row
        // in the tree, which is the part only the shipped binary has. Both confirmations arrive as an
        // immediate yes, for the reason the delete's does: a modal would sit on the frame this seam
        // photographs.
        var (entryRestored, recycleBinEmptied) = binnedRow is null
            ? (false, false)
            : await SmokeWalkKeePassBinExitsAsync(managedEntry, binUuid);

        return new KeePassManageSmokeState(
            DatabaseOpened: true,
            FolderShown: folderRow is not null,
            DraftShown: draftShown,
            EntryShown: entryShown,
            UnsavedNoticeShown: KeePassVaultIsDirty,
            BinShown: binRow is not null,
            EntryInBin: entryInBin,
            BinDeleteSplit: binDeleteSplit,
            EntryRestored: entryRestored,
            RecycleBinEmptied: recycleBinEmptied,
            TreeRows: _keePassTreeRows.Count,
            FolderRows: _keePassTreeRows.Count(row => row.IsEntryRow is false),
            EntryRows: _keePassTreeRows.Count(row => row.IsEntryRow),
            FileBytes: fileBytes);
    }

    /// <summary>
    /// Opens the file for the search shot and stops before typing. The query goes in through the window's
    /// own search box, because a seam that set the view model property would prove the wrong thing - the
    /// binding is the part only the shipped binary has.
    /// </summary>
    internal async Task<KeePassSearchSmokeState> SmokeOpenKeePassForSearchAsync(
        string path,
        string password,
        CancellationToken cancellationToken = default)
    {
        // The shots that ran before this one leave their edits staged in the session, and opening
        // refuses to run over them. Closing first is what a person does before opening another file,
        // and it is what makes the rows below belong to this run.
        ClearKeePassImportState(cancelActiveOperation: true);
        var opened = await SmokeOpenKeePassDatabaseAsync(path, password, cancellationToken);
        return new KeePassSearchSmokeState(
            opened.DatabaseOpened,
            _keePassTreeRows.Count,
            _keePassTreeRows.Count(row => row.IsEntryRow is false),
            opened.FileBytes);
    }

    /// <summary>
    /// Opens the file for the remembered-list shot and stops there. Which databases this machine has opened
    /// is the tab's own business, so this seam contributes exactly one thing: a database that did open,
    /// which is what puts a row on the list. Everything the frame then claims is read from the painted row
    /// and driven through the painted controls.
    /// </summary>
    internal async Task<bool> SmokeOpenKeePassForRecentAsync(
        string path,
        string password,
        CancellationToken cancellationToken = default)
    {
        ClearKeePassImportState(cancelActiveOperation: true);
        var opened = await SmokeOpenKeePassDatabaseAsync(path, password, cancellationToken);
        return opened.DatabaseOpened;
    }

    /// <summary>
    /// Opens the database and browses until an entry row is visible, which is what both seams need
    /// before they can do anything with a row. The loop rather than a single toggle because a fixture
    /// nests as deep as its author left it.
    /// </summary>
    private async Task<KeePassSmokeOpenState> SmokeOpenKeePassDatabaseAsync(
        string path,
        string password,
        CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(path.Trim());
        var content = await File.ReadAllBytesAsync(full, cancellationToken);
        _keePassPendingFile = new PickedBinaryFile(Path.GetFileName(full), content, full);
        KeePassSelectedFileName = Path.GetFileName(full);
        KeePassImportPassword = password;
        await PreviewKeePassImportCommand.ExecuteAsync(null);

        for (var pass = 0; pass < 64 && !_keePassTreeRows.Any(row => row.IsEntryRow); pass++)
        {
            var closed = _keePassTreeRows.FirstOrDefault(
                row => row.Kind == KeePassTreeRowKind.Folder && !row.IsExpanded);
            if (closed is null)
            {
                break;
            }

            await ToggleKeePassFolderCommand.ExecuteAsync(closed);
        }

        var entryRow = _keePassTreeRows.FirstOrDefault(row => row.IsEntryRow);
        return new KeePassSmokeOpenState(
            HasKeePassImportPreview,
            entryRow,
            content.LongLength);
    }
}

/// <summary>
/// Counts and flags only. The entry that was opened is named by nothing here, so a smoke log cannot
/// carry a title or a secret out of the database.
/// </summary>
internal sealed record KeePassSmokeEditState(
    bool EditorShown,
    bool DatabaseOpened,
    int TreeRows,
    int EntryRows,
    long FileBytes);

/// <summary>
/// Flags and counts for the same reason: the names this routine types are its own fixtures, and the
/// ones it reads from the file stay in the image. The bin flags say the delete landed in the database's
/// own recycle bin, that the tree offered the split pair on the way in, and that both ways out worked -
/// they carry no entry title.
/// </summary>
internal sealed record KeePassManageSmokeState(
    bool DatabaseOpened,
    bool FolderShown,
    bool DraftShown,
    bool EntryShown,
    bool UnsavedNoticeShown,
    bool BinShown,
    bool EntryInBin,
    bool BinDeleteSplit,
    bool EntryRestored,
    bool RecycleBinEmptied,
    int TreeRows,
    int FolderRows,
    int EntryRows,
    long FileBytes);

/// <summary>
/// A live row, so a seam can act on it, plus the two facts a log may carry.
/// </summary>
internal sealed record KeePassSmokeOpenState(
    bool DatabaseOpened,
    KeePassTreeRow? EntryRow,
    long FileBytes);

/// <summary>
/// The shape of the tree before anything is typed, as counts only. A search shot is worthless unless the
/// hierarchy it replaced was on screen first, and these two numbers are what say it was.
/// </summary>
internal sealed record KeePassSearchSmokeState(
    bool DatabaseOpened,
    int TreeRows,
    int FolderRows,
    long FileBytes);
