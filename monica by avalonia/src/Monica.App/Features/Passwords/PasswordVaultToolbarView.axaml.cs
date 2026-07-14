using Avalonia.Controls;

namespace Monica.App.Features.Passwords;

public partial class PasswordVaultToolbarView : UserControl
{
    public PasswordVaultToolbarView()
    {
        InitializeComponent();
    }

    public bool IsSearchBox(object? source) => ReferenceEquals(source, PasswordSearchBox);

    public bool IsNonSearchTextEditingSource(object? source) =>
        source is TextBox textBox && !IsSearchBox(textBox);

    public void FocusSearch()
    {
        PasswordSearchBox.Focus();
        PasswordSearchBox.SelectAll();
    }

    public bool IsSearchFocused => PasswordSearchBox.IsFocused;
}
