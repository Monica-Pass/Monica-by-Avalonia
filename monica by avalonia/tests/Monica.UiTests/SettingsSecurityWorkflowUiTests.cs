using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
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
    public void Settings_page_host_constructs_only_the_selected_page()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<Monica.App.ViewModels.MainWindowViewModel>();
        var host = new SettingsPageHostView { DataContext = viewModel };
        var content = host.FindControl<ContentControl>("SettingsPageContent")!;

        Assert.IsType<SettingsGeneralView>(content.Content);

        viewModel.SelectedSettingsPage = "Danger";
        Assert.IsType<SettingsDangerView>(content.Content);

        host.DataContext = null;
        Assert.Null(content.Content);

        host.DataContext = viewModel;
        Assert.IsType<SettingsDangerView>(content.Content);
    }

    [Fact]
    public void Settings_page_host_releases_cached_pages_when_detached()
    {
        var appWindow = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(appWindow);
        var viewModel = services.GetRequiredService<Monica.App.ViewModels.MainWindowViewModel>();
        var host = new SettingsPageHostView { DataContext = viewModel };
        var window = new Window { Content = host };

        window.Show();
        viewModel.SelectedSettingsPage = "Security";
        Assert.IsType<SettingsSecurityView>(host.FindControl<ContentControl>("SettingsPageContent")!.Content);

        window.Close();
        window.Content = null;
        Dispatcher.UIThread.RunJobs();

        Assert.Null(host.FindControl<ContentControl>("SettingsPageContent")!.Content);

        var restoredWindow = new Window { Content = host };
        restoredWindow.Show();

        Assert.IsType<SettingsSecurityView>(host.FindControl<ContentControl>("SettingsPageContent")!.Content);
        restoredWindow.Close();
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
    public void Settings_security_exposes_optional_window_capture_protection()
    {
        var security = new SettingsSecurityView();

        Assert.NotNull(security.FindControl<ToggleSwitch>("WindowCaptureProtectionToggle"));
    }

    [Fact]
    public void Security_analysis_exposes_search_cancel_and_single_pane_layout()
    {
        var view = new SecurityAnalysisWorkspaceView();
        var commands = Assert.IsType<SecurityAnalysisCommandBarView>(
            view.FindControl<SecurityAnalysisCommandBarView>("SecurityAnalysisCommandBar"));
        var filter = Assert.IsType<SecurityAnalysisFilterPaneView>(
            view.FindControl<SecurityAnalysisFilterPaneView>("SecurityAnalysisFilterPane"));
        var list = Assert.IsType<SecurityAnalysisIssueListView>(
            view.FindControl<SecurityAnalysisIssueListView>("SecurityAnalysisIssueListView"));

        Assert.NotNull(filter.SearchBox);
        Assert.NotNull(filter.FindControl<Button>("SecurityIssueSearchClearButton"));
        Assert.NotNull(commands.FindControl<Control>("CancelCompromisedCheckButton"));
        Assert.NotNull(commands.FindControl<Control>("CancelSecurityAnalysisRefreshButton"));
        Assert.NotNull(list.IssueList);
        Assert.NotNull(filter.FindControl<Button>("SecurityIssueSeverityAllButton"));
        Assert.NotNull(filter.FindControl<Button>("SecurityIssueSeverityHighButton"));
        Assert.NotNull(filter.FindControl<Button>("SecurityIssueSeverityMediumButton"));
        Assert.NotNull(filter.FindControl<Button>("SecurityIssueSeverityLowButton"));
        Assert.NotNull(filter.FindControl<Button>("ClearSecurityIssueFiltersButton"));

        view.UpdateResponsiveLayoutForWidth(680);

        Assert.True(view.IsNarrowLayout);
        Assert.Single(view.FindControl<Grid>("SecurityAnalysisLayoutGrid")!.ColumnDefinitions);
        Assert.True(view.FindControl<Border>("SecurityIssueListRegion")!.IsVisible);
        Assert.False(view.FindControl<Border>("SecurityIssueDetailRegion")!.IsVisible);
    }
}
