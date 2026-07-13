using Avalonia.Controls;
using Monica.App.Features.Passwords;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class PasswordWorkflowUiTests
{
    public PasswordWorkflowUiTests()
    {
        TestAppBuilder.EnsureInitialized();
    }

    [Fact]
    public void Password_workflow_exposes_search_selection_and_empty_state_actions()
    {
        var view = new PasswordVaultView();

        Assert.NotNull(view.FindControl<TextBox>("PasswordSearchBox"));
        Assert.NotNull(view.FindControl<Button>("PasswordSearchClearButton"));
        Assert.NotNull(view.FindControl<CheckBox>("SelectAllVisiblePasswordsCheckBox"));
        Assert.NotNull(view.FindControl<StackPanel>("PasswordListEmptyState"));
        Assert.NotNull(view.FindControl<Button>("EmptyStateAddPasswordButton"));
        Assert.NotNull(view.FindControl<Button>("EmptyStateClearFiltersButton"));
    }
}
