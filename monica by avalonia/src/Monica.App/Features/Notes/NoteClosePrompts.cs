using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Monica.App.ViewModels;

namespace Monica.App.Features.Notes;

/// The unsaved-note prompt belongs to the note model, not to whichever page happens to draw a tab
/// strip, so the notes workspace and the library surface ask the same question through this.
public static class NoteClosePrompts
{
    public static Task<FAContentDialogResult> ShowUnsavedTabAsync(
        Window owner,
        MainWindowViewModel viewModel,
        NoteEditorTab tab)
    {
        var title = string.IsNullOrWhiteSpace(tab.Title) ? viewModel.L.Get("Untitled") : tab.Title.Trim();
        return ShowAsync(
            owner,
            viewModel,
            "SaveNoteChangesTitle",
            viewModel.L.Format("UnsavedNoteMessageFormat", title),
            "Save");
    }

    public static Task<FAContentDialogResult> ShowUnsavedTabsAsync(
        Window owner,
        MainWindowViewModel viewModel,
        int dirtyCount) =>
        ShowAsync(
            owner,
            viewModel,
            "SaveUnsavedNotesTitle",
            viewModel.L.Format("UnsavedNotesMessageFormat", dirtyCount),
            "SaveAllNotes");

    private static Task<FAContentDialogResult> ShowAsync(
        Window owner,
        MainWindowViewModel viewModel,
        string titleKey,
        string message,
        string primaryLabel)
    {
        var dialog = new FAContentDialog
        {
            Title = viewModel.L.Get(titleKey),
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                MaxWidth = 440
            },
            PrimaryButtonText = viewModel.L.Get(primaryLabel),
            SecondaryButtonText = viewModel.L.Get("Discard"),
            CloseButtonText = viewModel.L.Get("Cancel"),
            DefaultButton = FAContentDialogButton.Primary
        };

        return dialog.ShowAsync(owner);
    }
}
