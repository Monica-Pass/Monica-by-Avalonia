using Avalonia.Controls;
using Avalonia.Interactivity;
using Monica.App.ViewModels;

namespace Monica.App.Features.Notes;

public sealed class NoteLineRequestedEventArgs(int lineNumber) : EventArgs
{
    public int LineNumber { get; } = lineNumber;
}

public partial class NoteInspectorView : UserControl
{
    public NoteInspectorView()
    {
        InitializeComponent();
    }

    public event EventHandler<NoteLineRequestedEventArgs>? LineRequested;

    private void NoteOutlineItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: NoteOutlineItem item })
        {
            LineRequested?.Invoke(this, new NoteLineRequestedEventArgs(item.LineNumber));
        }
    }
}
