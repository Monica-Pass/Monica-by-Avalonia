using Avalonia.Controls;
using Monica.App.Features.SecurityAnalysis;
using Monica.App.Features.Settings;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class SettingsSecurityWorkflowUiTests
{
    public SettingsSecurityWorkflowUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Settings_workspace_reflows_to_single_column_at_narrow_width()
    {
        var view = new SettingsWorkspaceView();

        view.UpdateResponsiveLayoutForWidth(680);

        Assert.True(view.IsNarrowLayout);
        Assert.Single(view.FindControl<Grid>("SettingsWorkspaceLayoutGrid")!.ColumnDefinitions);
        Assert.Equal(1, Grid.GetRow(view.FindControl<ScrollViewer>("SettingsContentRegion")!));

        view.UpdateResponsiveLayoutForWidth(1100);

        Assert.False(view.IsNarrowLayout);
        Assert.Equal(2, view.FindControl<Grid>("SettingsWorkspaceLayoutGrid")!.ColumnDefinitions.Count);
    }

    [Fact]
    public void Settings_sensitive_and_danger_controls_use_safe_desktop_surfaces()
    {
        var security = new SettingsSecurityView();
        var recovery = new SettingsRecoveryView();
        var danger = new SettingsDangerView();

        Assert.Equal('*', security.FindControl<TextBox>("CurrentMasterPasswordBox")!.PasswordChar);
        Assert.Equal('*', security.FindControl<TextBox>("NewMasterPasswordBox")!.PasswordChar);
        Assert.Equal('*', recovery.FindControl<TextBox>("RecoveryAnswer1Box")!.PasswordChar);
        Assert.NotNull(danger.FindControl<Button>("ClearPasswordsButton"));
        Assert.NotNull(danger.FindControl<Button>("ClearAllVaultDataButton"));
        Assert.Null(danger.FindControl<TextBox>("DangerConfirmationTextBox"));
    }

    [Fact]
    public void Security_analysis_exposes_search_cancel_and_single_pane_layout()
    {
        var view = new SecurityAnalysisWorkspaceView();

        Assert.NotNull(view.FindControl<TextBox>("SecurityIssueSearchBox"));
        Assert.NotNull(view.FindControl<Button>("SecurityIssueSearchClearButton"));
        Assert.NotNull(view.FindControl<Button>("CancelCompromisedCheckButton"));
        Assert.NotNull(view.FindControl<Button>("CancelSecurityAnalysisRefreshButton"));
        Assert.NotNull(view.FindControl<ListBox>("SecurityIssueList"));
        Assert.NotNull(view.FindControl<Button>("SecurityIssueSeverityAllButton"));
        Assert.NotNull(view.FindControl<Button>("SecurityIssueSeverityHighButton"));
        Assert.NotNull(view.FindControl<Button>("SecurityIssueSeverityMediumButton"));
        Assert.NotNull(view.FindControl<Button>("SecurityIssueSeverityLowButton"));
        Assert.NotNull(view.FindControl<Button>("ClearSecurityIssueFiltersButton"));

        view.UpdateResponsiveLayoutForWidth(680);

        Assert.True(view.IsNarrowLayout);
        Assert.Single(view.FindControl<Grid>("SecurityAnalysisLayoutGrid")!.ColumnDefinitions);
        Assert.True(view.FindControl<Border>("SecurityIssueListRegion")!.IsVisible);
        Assert.False(view.FindControl<Border>("SecurityIssueDetailRegion")!.IsVisible);
    }
}
