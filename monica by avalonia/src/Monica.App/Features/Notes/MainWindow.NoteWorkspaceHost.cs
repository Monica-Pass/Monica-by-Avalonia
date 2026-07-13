using Avalonia.Controls;
using Avalonia.Interactivity;
using Monica.App.Features.Notes;
using Monica.App.ViewModels;

namespace Monica.App;

public partial class MainWindow
{
    private void NoteWorkspaceGrid_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.NoteWorkspaceViewportWidth = e.NewSize.Width;
        }
    }

    private async void NoteEditorView_OnCloseRequested(object? sender, NoteEditorCloseRequestedEventArgs e) =>
        await CloseNoteTabWithPromptAsync((MainWindowViewModel)DataContext!, e.Tab);

    private void NoteInspectorView_OnLineRequested(object? sender, NoteLineRequestedEventArgs e) =>
        NoteEditorView.JumpToLine(e.LineNumber);

    private void SetNoteEditModeMenuItem_OnClick(object? sender, RoutedEventArgs e) =>
        NoteEditorView.SetMode("edit");

    private void SetNotePreviewModeMenuItem_OnClick(object? sender, RoutedEventArgs e) =>
        NoteEditorView.SetMode("preview");

    private void SetNoteSplitModeMenuItem_OnClick(object? sender, RoutedEventArgs e) =>
        NoteEditorView.SetMode("split");
}
