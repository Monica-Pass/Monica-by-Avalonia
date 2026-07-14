using Avalonia.Controls;

namespace Monica.App.Features.Notes;

public partial class NoteTreeView : UserControl
{
    public NoteTreeView()
    {
        InitializeComponent();
    }

    public void FocusSearch()
    {
        NoteSearchBox.Focus();
        NoteSearchBox.SelectAll();
    }
}
