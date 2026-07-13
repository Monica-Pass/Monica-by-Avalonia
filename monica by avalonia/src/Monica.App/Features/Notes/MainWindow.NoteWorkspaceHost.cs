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
        await NoteTabStripView.CloseTabWithPromptAsync((MainWindowViewModel)DataContext!, e.Tab);

    private void NoteInspectorView_OnLineRequested(object? sender, NoteLineRequestedEventArgs e) =>
        NoteEditorView.JumpToLine(e.LineNumber);

    private void NoteTabStripView_OnTabClosed(object? sender, NoteTabClosedEventArgs e) =>
        NoteEditorView.RemoveHistory(e.Tab);
}
