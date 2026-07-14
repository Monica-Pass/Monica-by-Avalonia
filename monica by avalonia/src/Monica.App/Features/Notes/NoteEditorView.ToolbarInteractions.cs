using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Monica.App.Features.Notes;
using Monica.App.ViewModels;

namespace Monica.App.Features.Notes;

public partial class NoteEditorView
{
    private async void NoteEditorToolbarView_OnActionRequested(object? sender, NoteEditorActionRequestedEventArgs e)
    {
        switch (e.Action)
        {
            case "undo":
                UndoNoteEditor();
                break;
            case "redo":
                RedoNoteEditor();
                break;
            case "find":
                ShowNoteFindPanel(replaceMode: false);
                break;
            case "replace":
                ShowNoteFindPanel(replaceMode: true);
                break;
            case "image":
                await InsertNoteImageAsync();
                break;
            default:
                ApplyMarkdownAction(e.Action);
                break;
        }

        UpdateNoteEditorStatus();
    }

    private async Task InsertNoteImageAsync()
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var markdown = await viewModel.PickNoteImageMarkdownAsync();
        if (!string.IsNullOrWhiteSpace(markdown))
        {
            InsertMarkdownBlock(markdown);
        }
    }

}
