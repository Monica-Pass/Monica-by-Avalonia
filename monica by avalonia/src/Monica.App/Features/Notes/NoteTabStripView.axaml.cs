using Avalonia.Controls;
using Monica.App.ViewModels;

namespace Monica.App.Features.Notes;

public sealed class NoteTabClosedEventArgs(NoteEditorTab tab) : EventArgs
{
    public NoteEditorTab Tab { get; } = tab;
}

public partial class NoteTabStripView : UserControl
{
    public NoteTabStripView()
    {
        InitializeComponent();
    }

    public event EventHandler<NoteTabClosedEventArgs>? TabClosed;

    private void SetNoteEditModeMenuItem_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => SetMode("edit");
    private void SetNotePreviewModeMenuItem_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => SetMode("preview");
    private void SetNoteSplitModeMenuItem_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => SetMode("split");

    private void SetMode(string mode)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.NotePreviewMode = mode == "preview";
            viewModel.NoteSplitPreviewMode = mode == "split";
        }
    }

    private void NotifyTabClosed(NoteEditorTab tab) => TabClosed?.Invoke(this, new NoteTabClosedEventArgs(tab));
}
