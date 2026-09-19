using Avalonia.Controls;
using Monica.App.ViewModels;

namespace Monica.App.Features.Vault;

// The release smoke run has to reach the page the same way a user does, so these checks go through
// the tree: pick the row the preset should offer and read back whatever editor it mounted.
public partial class VaultWorkspaceView
{
    internal Control? MountedSurface => VaultSurfaceHost.Content as Control;

    internal bool SelectFirstEntry(MainWindowViewModel viewModel, Func<VaultTreeEntryRow, bool> matches)
    {
        var row = viewModel.VaultTreeRows.OfType<VaultTreeEntryRow>().FirstOrDefault(matches);
        if (row is null)
        {
            return false;
        }

        viewModel.SelectedVaultRow = row;
        return true;
    }

    internal async Task<bool> RunNoteEditorSmokeChecksAsync(MainWindowViewModel viewModel)
    {
        viewModel.SelectSectionCommand.Execute("Notes");
        if (!SelectFirstEntry(viewModel, row => row.Kind == VaultEntryKind.Note) ||
            NoteSurface is not { } editor)
        {
            return false;
        }

        return await editor.RunSmokeChecksAsync(viewModel);
    }

    internal async Task RunNoteEditorKeyboardSmokeChecksAsync(
        MainWindowViewModel viewModel,
        Action<string, bool, string> check)
    {
        viewModel.SelectSectionCommand.Execute("Notes");
        SelectFirstEntry(viewModel, row => row.Kind == VaultEntryKind.Note);
        if (NoteSurface is not { } editor)
        {
            check("note-editor-surface-mounted", false, "the library tree has no note row to open");
            return;
        }

        await editor.RunKeyboardSmokeChecksAsync(check);
    }
}
