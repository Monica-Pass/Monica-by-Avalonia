using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Features.Authenticator;
using Monica.App.ViewModels;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class AuthenticatorWorkflowUiTests
{
    public AuthenticatorWorkflowUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Authenticator_workspace_exposes_search_empty_state_and_accessible_commands()
    {
        var view = new AuthenticatorWorkspaceView();

        Assert.NotNull(view.FindControl<TextBox>("AuthenticatorSearchBox"));
        Assert.NotNull(view.FindControl<Button>("AuthenticatorSearchClearButton"));
        var accountListView = Assert.IsType<AuthenticatorAccountListView>(
            view.FindControl<AuthenticatorAccountListView>("AuthenticatorAccountListView"));
        Assert.NotNull(accountListView.AccountList);
        Assert.NotNull(view.FindControl<StackPanel>("AuthenticatorEmptyState"));
        Assert.NotNull(view.FindControl<Button>("EmptyAuthenticatorAddButton"));
        Assert.NotNull(view.FindControl<Button>("EmptyAuthenticatorClearFiltersButton"));
        var console = Assert.IsType<AuthenticatorCodeConsoleView>(
            view.FindControl<AuthenticatorCodeConsoleView>("AuthenticatorCodeConsole"));
        Assert.NotNull(console.FindControl<TextBlock>("AuthenticatorCurrentCode"));
        Assert.NotNull(console.FindControl<Button>("CopyAuthenticatorCodeButton"));
        Assert.NotNull(console.FindControl<Button>("AdvanceTotpButton"));
    }

    [Fact]
    public void Authenticator_search_controls_describe_exact_actions_and_announce_results()
    {
        var view = new AuthenticatorWorkspaceView();
        var xaml = File.ReadAllText(FindAuthenticatorFeatureFile("AuthenticatorWorkspaceView.axaml"));

        Assert.NotNull(view.FindControl<Button>("AuthenticatorSearchClearButton"));
        Assert.Contains(
            "AutomationProperties.Name=\"{Binding ClearTotpSearchText}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.HelpText=\"{Binding TotpSearchHelpText}\"",
            xaml,
            StringComparison.Ordinal);
        var filterPane = Assert.IsType<AuthenticatorFilterPaneView>(
            view.FindControl<AuthenticatorFilterPaneView>("AuthenticatorFilterPane"));
        var status = filterPane.FindControl<TextBlock>("TotpFilteredStatusText");
        Assert.NotNull(status);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
    }

    [Fact]
    public void Authenticator_header_owns_primary_add_and_single_scan_action()
    {
        var xaml = File.ReadAllText(FindAuthenticatorFeatureFile("AuthenticatorWorkspaceView.axaml"));

        Assert.Equal(2, CountOccurrences(xaml, "Command=\"{Binding AddTotpCommand}\""));
        Assert.Equal(1, CountOccurrences(xaml, "Command=\"{Binding ScanTotpQrCommand}\""));
        Assert.Contains("IconSource=\"QrCode\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Icon=\"Document\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Authenticator_more_menu_contains_only_executable_actions()
    {
        var xaml = File.ReadAllText(FindAuthenticatorFeatureFile("AuthenticatorWorkspaceView.axaml"));

        Assert.DoesNotContain("TotpShowHiddenText", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SortTitleText", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TotpHelpText", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Authenticator_code_console_preserves_hotp_counter_semantics()
    {
        var xaml = File.ReadAllText(FindAuthenticatorFeatureFile("AuthenticatorCodeConsoleView.axaml"));

        Assert.Contains("Command=\"{Binding AdvanceTotpCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedTotpDetails.IsCounterBased", xaml, StringComparison.Ordinal);
        Assert.Contains("L[TotpCounter]", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedTotpDetails.CounterText", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Authenticator_batch_commands_use_desktop_targets_and_named_automation()
    {
        var view = new AuthenticatorWorkspaceView();
        var xaml = File.ReadAllText(FindAuthenticatorFeatureFile("AuthenticatorWorkspaceView.axaml"));

        Assert.NotNull(view.FindControl<Button>("FavoriteSelectedTotpButton"));
        Assert.NotNull(view.FindControl<Button>("DeleteSelectedTotpButton"));
        Assert.NotNull(view.FindControl<Button>("ClearTotpSelectionButton"));
        Assert.Contains("x:Name=\"FavoriteSelectedTotpButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DeleteSelectedTotpButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ClearTotpSelectionButton\"", xaml, StringComparison.Ordinal);
        Assert.Equal(3, CountOccurrences(xaml, "Width=\"40\" Height=\"40\""));
    }

    [Fact]
    public void Authenticator_workspace_separates_filter_and_account_scroll_ownership()
    {
        var view = new AuthenticatorWorkspaceView();
        var xaml = File.ReadAllText(FindAuthenticatorFeatureFile("AuthenticatorWorkspaceView.axaml"));
        var accountListXaml = File.ReadAllText(FindAuthenticatorFeatureFile("AuthenticatorAccountListView.axaml"));

        Assert.Contains("<views:AuthenticatorFilterPaneView x:Name=\"AuthenticatorFilterPane\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<views:AuthenticatorAccountListView x:Name=\"AuthenticatorAccountListView\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Classes=\"totpAccountRail\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Classes=\"workspaceSidebar\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "Classes=\"totpCodeSurface\"",
            File.ReadAllText(FindAuthenticatorFeatureFile("AuthenticatorCodeConsoleView.axaml")),
            StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AuthenticatorAccountListScrollViewer\"", accountListXaml, StringComparison.Ordinal);
        Assert.NotNull(view.FindControl<Control>("AuthenticatorFilterPane"));
        Assert.NotNull(view.FindControl<Control>("AuthenticatorAccountListView"));
    }

    [Fact]
    public void Authenticator_search_escape_preserves_active_filter()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            viewModel.SelectSectionCommand.Execute("Totp");
            Dispatcher.UIThread.RunJobs();
            var workspace = Assert.Single(window.GetVisualDescendants().OfType<AuthenticatorWorkspaceView>());
            var searchBox = workspace.FindControl<TextBox>("AuthenticatorSearchBox")!;
            viewModel.SelectedTotpFilterKey = "favorites";
            viewModel.TotpSearchText = "github";
            searchBox.Focus();
            var args = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Source = searchBox,
                Key = Key.Escape
            };

            window.HandleAuthenticatorWorkspaceShortcut(viewModel, args);

            Assert.True(args.Handled);
            Assert.Empty(viewModel.TotpSearchText);
            Assert.Equal("favorites", viewModel.SelectedTotpFilterKey);
            Assert.True(viewModel.HasTotpFilterOrSearch);
        }
        finally
        {
            window.Close();
        }
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

    private static string FindAuthenticatorFeatureFile(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "Monica.App",
                "Features",
                "Authenticator",
                fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate {fileName} from the test output directory.");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }
}
