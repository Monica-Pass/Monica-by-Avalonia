namespace Monica.App.ViewModels;

/// <summary>
/// The seam the artifact run uses to photograph the version list, which no section name reaches: the
/// pane only exists inside a browsed <c>.kdbx</c>, behind a native file dialog. Like the other KeePass
/// seams it stops at the staged change - the file keeps the bytes it was opened with.
/// </summary>
public sealed partial class MainWindowViewModel
{
    internal async Task<KeePassHistorySmokeState> SmokeShowKeePassHistoryAsync(
        string path,
        string password,
        CancellationToken cancellationToken = default)
    {
        // The frames before this one leave their edits staged in the session, and opening refuses to run
        // over them. Closing first is what a person does before opening another file, and it is what makes
        // the version list below belong to a database this run opened.
        ClearKeePassImportState(cancelActiveOperation: true);
        var opened = await SmokeOpenKeePassDatabaseAsync(path, password, cancellationToken);
        if (!opened.DatabaseOpened || opened.EntryRow is not { } entryRow)
        {
            return new KeePassHistorySmokeState(
                opened.DatabaseOpened,
                false,
                0,
                0,
                _keePassTreeRows.Count,
                opened.FileBytes,
                "");
        }

        var originalTitle = entryRow.Entry!.Title;
        await SelectKeePassRowCommand.ExecuteAsync(entryRow);
        await EditKeePassEntryCommand.ExecuteAsync(null);
        if (!HasKeePassEditor)
        {
            return new KeePassHistorySmokeState(
                true, false, 0, 0, _keePassTreeRows.Count, opened.FileBytes, originalTitle);
        }

        KeePassEditorPublic!.Title = "Smoke Renamed Entry";
        await ApplyKeePassEntryEditCommand.ExecuteAsync(null);
        var staged = KeePassVaultIsDirty;

        // The edit republishes the tree, so this is the moment a person wants the versions: the panel
        // still answers the entry it was showing, and the list has to follow it rather than the selection.
        await ShowKeePassHistoryCommand.ExecuteAsync(null);
        return new KeePassHistorySmokeState(
            true,
            staged,
            KeePassHistoryVersions.Count,
            _keePassTreeRows.Count(row => row.IsEntryRow),
            _keePassTreeRows.Count,
            opened.FileBytes,
            originalTitle);
    }
}

/// <summary>
/// What the version frame reports. The original title travels along so the frame can tell whether the
/// revert landed back on it; it is compared, never logged, and no secret is in this record at all.
/// </summary>
internal sealed record KeePassHistorySmokeState(
    bool DatabaseOpened,
    bool EditStaged,
    int VersionCount,
    int EntryRows,
    int TreeRows,
    long FileBytes,
    string OriginalTitle);
