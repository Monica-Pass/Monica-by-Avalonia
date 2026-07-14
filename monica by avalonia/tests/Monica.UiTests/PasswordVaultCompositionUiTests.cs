using Avalonia.Controls;
using Monica.App.Features.Passwords;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class PasswordVaultCompositionUiTests
{
    public PasswordVaultCompositionUiTests()
    {
        TestAppBuilder.EnsureInitialized();
    }

    [Fact]
    public void Password_vault_is_composed_from_focused_workspace_views()
    {
        var view = new PasswordVaultView();

        Assert.NotNull(view.FindControl<PasswordVaultToolbarView>("PasswordVaultToolbar"));
        Assert.NotNull(view.FindControl<PasswordFolderFilterView>("PasswordFolderFilters"));
        Assert.NotNull(view.FindControl<PasswordListPaneView>("PasswordListPane"));
        Assert.NotNull(view.FindControl<PasswordDetailPaneView>("PasswordDetailPane"));
    }

    [Fact]
    public void Focused_password_views_expose_required_keyboard_and_state_controls()
    {
        var toolbar = new PasswordVaultToolbarView();
        var list = new PasswordListPaneView();
        var details = new PasswordDetailPaneView();

        Assert.NotNull(toolbar.FindControl<TextBox>("PasswordSearchBox"));
        Assert.NotNull(toolbar.FindControl<Button>("PasswordSearchClearButton"));
        Assert.NotNull(list.FindControl<ListBox>("PasswordListBox"));
        Assert.NotNull(list.FindControl<CheckBox>("SelectAllVisiblePasswordsCheckBox"));
        Assert.NotNull(details.FindControl<Button>("BackToPasswordListButton"));
        Assert.NotNull(details.FindControl<Button>("RetryPasswordDetailsButton"));
    }
}
