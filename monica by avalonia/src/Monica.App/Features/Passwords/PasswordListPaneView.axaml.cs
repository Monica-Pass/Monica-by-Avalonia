using Avalonia.Controls;
using Monica.App.ViewModels;

namespace Monica.App.Features.Passwords;

public partial class PasswordListPaneView : UserControl
{
    public PasswordListPaneView()
    {
        InitializeComponent();
    }

    public void ScrollIntoView(PasswordListRow row) => PasswordListBox.ScrollIntoView(row);

    public void FocusList() => PasswordListBox.Focus();
}
