using Avalonia.Controls;
using Monica.App.ViewModels;

namespace Monica.App.Features.Passwords;

public partial class PasswordVaultView : UserControl
{
    public PasswordVaultView()
    {
        InitializeComponent();
    }

    public bool IsSearchBox(object? source) => ReferenceEquals(source, PasswordSearchBox);

    public void FocusSearch()
    {
        PasswordSearchBox.Focus();
        PasswordSearchBox.SelectAll();
    }

    public bool IsSearchFocused => PasswordSearchBox.IsFocused;

    public void ScrollIntoView(PasswordListRow row) => PasswordListBox.ScrollIntoView(row);

    public void FocusDetails() => PasswordDetailRegion.Focus();

    public bool IsDetailFocused => PasswordDetailRegion.IsFocused;
}
