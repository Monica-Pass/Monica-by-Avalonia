using Avalonia.Controls;
using Monica.App.Features.Authenticator;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class AuthenticatorWorkflowUiTests
{
    public AuthenticatorWorkflowUiTests()
    {
        TestAppBuilder.EnsureInitialized();
    }

    [Fact]
    public void Authenticator_workspace_exposes_search_empty_state_and_accessible_commands()
    {
        var view = new AuthenticatorWorkspaceView();

        Assert.NotNull(view.FindControl<TextBox>("AuthenticatorSearchBox"));
        Assert.NotNull(view.FindControl<Button>("AuthenticatorSearchClearButton"));
        Assert.NotNull(view.FindControl<ListBox>("AuthenticatorAccountList"));
        Assert.NotNull(view.FindControl<StackPanel>("AuthenticatorEmptyState"));
        Assert.NotNull(view.FindControl<Button>("EmptyAuthenticatorAddButton"));
        Assert.NotNull(view.FindControl<Button>("EmptyAuthenticatorClearFiltersButton"));
        var console = Assert.IsType<AuthenticatorCodeConsoleView>(
            view.FindControl<AuthenticatorCodeConsoleView>("AuthenticatorCodeConsole"));
        Assert.NotNull(console.FindControl<TextBlock>("AuthenticatorCurrentCode"));
        Assert.NotNull(console.FindControl<Button>("CopyAuthenticatorCodeButton"));
    }

    [Fact]
    public void Authenticator_workspace_switches_to_single_pane_at_narrow_width()
    {
        var view = new AuthenticatorWorkspaceView();

        Assert.NotNull(view.FindControl<Grid>("AuthenticatorMasterDetailGrid"));
        Assert.NotNull(view.FindControl<Border>("AuthenticatorListRegion"));
        Assert.NotNull(view.FindControl<Border>("AuthenticatorCodeRegion"));
        Assert.NotNull(view.FindControl<Border>("AuthenticatorInspectorRegion"));
        var console = Assert.IsType<AuthenticatorCodeConsoleView>(
            view.FindControl<AuthenticatorCodeConsoleView>("AuthenticatorCodeConsole"));
        Assert.NotNull(console.FindControl<Button>("BackToAuthenticatorListButton"));

        view.UpdateResponsiveLayoutForWidth(680);
        Assert.True(view.IsNarrowLayout);
        Assert.True(view.FindControl<Border>("AuthenticatorListRegion")!.IsVisible);
        Assert.False(view.FindControl<Border>("AuthenticatorCodeRegion")!.IsVisible);
        Assert.False(view.FindControl<Border>("AuthenticatorInspectorRegion")!.IsVisible);

        view.UpdateResponsiveLayoutForWidth(900);
        Assert.True(view.IsMediumLayout);
        Assert.True(view.FindControl<Border>("AuthenticatorListRegion")!.IsVisible);
        Assert.True(view.FindControl<Border>("AuthenticatorCodeRegion")!.IsVisible);
        Assert.False(view.FindControl<Border>("AuthenticatorInspectorRegion")!.IsVisible);

        view.UpdateResponsiveLayoutForWidth(1200);
        Assert.False(view.IsNarrowLayout);
        Assert.True(view.FindControl<Border>("AuthenticatorListRegion")!.IsVisible);
        Assert.True(view.FindControl<Border>("AuthenticatorCodeRegion")!.IsVisible);
        Assert.True(view.FindControl<Border>("AuthenticatorInspectorRegion")!.IsVisible);
    }
}
