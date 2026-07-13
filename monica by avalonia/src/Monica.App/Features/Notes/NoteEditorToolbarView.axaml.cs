using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Monica.App.Features.Notes;

public sealed class NoteEditorActionRequestedEventArgs(string action) : EventArgs
{
    public string Action { get; } = action;
}

public partial class NoteEditorToolbarView : UserControl
{
    public NoteEditorToolbarView()
    {
        InitializeComponent();
    }

    public event EventHandler<NoteEditorActionRequestedEventArgs>? ActionRequested;

    private void ToolbarButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string action })
        {
            ActionRequested?.Invoke(this, new NoteEditorActionRequestedEventArgs(action));
        }
    }
}
