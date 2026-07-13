using Avalonia.Controls;
using Avalonia.Threading;
using Monica.App.ViewModels;

namespace Monica.App.Features.Passwords;

public partial class PasswordVaultView : UserControl
{
    public PasswordVaultView()
    {
        InitializeComponent();
    }

    public bool IsSearchBox(object? source) => ReferenceEquals(source, PasswordSearchBox);

    public bool IsNonSearchTextEditingSource(object? source) =>
        source is TextBox textBox && !IsSearchBox(textBox);

    public void SelectAdjacentPassword(MainWindowViewModel viewModel, int delta)
    {
        var visiblePasswords = viewModel.VisiblePasswordNavigationEntries.ToList();
        if (visiblePasswords.Count == 0)
        {
            return;
        }

        var currentIndex = viewModel.SelectedPassword is null
            ? -1
            : visiblePasswords.FindIndex(item => item.Id == viewModel.SelectedPassword.Id);
        var nextIndex = currentIndex < 0
            ? (delta > 0 ? 0 : visiblePasswords.Count - 1)
            : Math.Clamp(currentIndex + delta, 0, visiblePasswords.Count - 1);

        viewModel.SelectedPassword = visiblePasswords[nextIndex];
        if (viewModel.SelectedPasswordListRow is { } row)
        {
            Dispatcher.UIThread.Post(() => ScrollIntoView(row));
        }
    }

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
