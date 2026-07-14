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
        var toolbar = view.FindControl<PasswordVaultToolbarView>("PasswordVaultToolbar")!;
        var list = view.FindControl<PasswordListPaneView>("PasswordListPane")!;

        Assert.NotNull(toolbar.FindControl<TextBox>("PasswordSearchBox"));
        Assert.NotNull(toolbar.FindControl<Button>("PasswordSearchClearButton"));
        Assert.NotNull(list.FindControl<CheckBox>("SelectAllVisiblePasswordsCheckBox"));
        Assert.NotNull(list.FindControl<StackPanel>("PasswordListEmptyState"));
        Assert.NotNull(list.FindControl<Button>("EmptyStateAddPasswordButton"));
        Assert.NotNull(list.FindControl<Button>("EmptyStateClearFiltersButton"));
    }

    [Fact]
    public void Password_workflow_exposes_responsive_detail_recovery_controls()
    {
        var view = new PasswordVaultView();
        var details = view.FindControl<PasswordDetailPaneView>("PasswordDetailPane")!;

        Assert.NotNull(view.FindControl<Grid>("PasswordMasterDetailGrid"));
        Assert.NotNull(view.FindControl<Border>("PasswordListRegion"));
        Assert.NotNull(view.FindControl<Border>("PasswordDetailRegion"));
        Assert.NotNull(details.FindControl<Button>("BackToPasswordListButton"));
        Assert.NotNull(details.FindControl<StackPanel>("PasswordDetailLoadingState"));
        Assert.NotNull(details.FindControl<StackPanel>("PasswordDetailErrorState"));
        Assert.NotNull(details.FindControl<Button>("RetryPasswordDetailsButton"));

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
