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
        if (entryRow is null)
        {
            return new KeePassSmokeEditState(
                EditorShown: false,
                DatabaseOpened: HasKeePassImportPreview,
                TreeRows: _keePassTreeRows.Count,
                EntryRows: 0,
                FileBytes: content.LongLength);
        }

        await SelectKeePassRowCommand.ExecuteAsync(entryRow);
        await EditKeePassEntryCommand.ExecuteAsync(null);
        return new KeePassSmokeEditState(
            HasKeePassEditor,
            HasKeePassImportPreview,
            _keePassTreeRows.Count,
            _keePassTreeRows.Count(row => row.IsEntryRow),
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
