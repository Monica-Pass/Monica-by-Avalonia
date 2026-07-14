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

    [Fact]
    public void Password_workflow_exposes_responsive_detail_recovery_controls()
    {
        var view = new PasswordVaultView();

        Assert.NotNull(view.FindControl<Grid>("PasswordMasterDetailGrid"));
        Assert.NotNull(view.FindControl<Border>("PasswordListRegion"));
        Assert.NotNull(view.FindControl<Border>("PasswordDetailRegion"));
        Assert.NotNull(view.FindControl<Button>("BackToPasswordListButton"));
        Assert.NotNull(view.FindControl<StackPanel>("PasswordDetailLoadingState"));
        Assert.NotNull(view.FindControl<StackPanel>("PasswordDetailErrorState"));
        Assert.NotNull(view.FindControl<Button>("RetryPasswordDetailsButton"));

        view.UpdateResponsiveLayoutForWidth(680);
        Assert.True(view.IsNarrowLayout);
        Assert.True(view.FindControl<Border>("PasswordListRegion")!.IsVisible);
        Assert.False(view.FindControl<Border>("PasswordDetailRegion")!.IsVisible);

        view.UpdateResponsiveLayoutForWidth(900);
        Assert.False(view.IsNarrowLayout);
        Assert.True(view.FindControl<Border>("PasswordListRegion")!.IsVisible);
        Assert.True(view.FindControl<Border>("PasswordDetailRegion")!.IsVisible);
    }
}
