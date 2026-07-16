using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Features.Passwords;
using Monica.App.ViewModels;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class PasswordWorkflowUiTests
{
    public PasswordWorkflowUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
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
    public void Password_search_controls_describe_exact_actions_and_announce_results()
    {
        var toolbar = new PasswordVaultToolbarView();
        var list = new PasswordListPaneView();
        var toolbarXaml = File.ReadAllText(FindPasswordFeatureFile("PasswordVaultToolbarView.axaml"));

        Assert.NotNull(toolbar.FindControl<Button>("PasswordSearchClearButton"));
        Assert.Contains(
            "AutomationProperties.Name=\"{Binding ClearPasswordSearchText}\"",
            toolbarXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.HelpText=\"{Binding PasswordSearchHelpText}\"",
            toolbarXaml,
            StringComparison.Ordinal);
        var status = list.FindControl<TextBlock>("PasswordListStatusText");
        Assert.NotNull(status);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
    }

    [Fact]
    public void Password_search_escape_preserves_non_search_filters()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            Dispatcher.UIThread.RunJobs();
            var workspace = Assert.Single(window.GetVisualDescendants().OfType<PasswordVaultView>());
            var searchBox = workspace
                .FindControl<PasswordVaultToolbarView>("PasswordVaultToolbar")!
                .FindControl<TextBox>("PasswordSearchBox")!;
            viewModel.QuickFilterFavorite = true;
            viewModel.PasswordSearchText = "github";
            viewModel.PasswordSearchQuery = "github";
            searchBox.Focus();
            var args = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Source = searchBox,
                Key = Key.Escape
            };

            window.TryHandlePasswordWorkspaceShortcut(viewModel, args);

            Assert.True(args.Handled);
            Assert.Empty(viewModel.PasswordSearchText);
            Assert.Empty(viewModel.PasswordSearchQuery);
            Assert.True(viewModel.QuickFilterFavorite);
            Assert.True(viewModel.HasPasswordFilters);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Password_workflow_exposes_responsive_detail_recovery_controls()
    {
        var view = new PasswordVaultView();
        view.FocusDetails();
        var detailHost = view.FindControl<ContentControl>("PasswordDetailPaneHost")!;
        var details = Assert.IsType<PasswordDetailPaneView>(detailHost.Content);

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

    private static string FindPasswordFeatureFile(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "Monica.App",
                "Features",
                "Passwords",
                fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate {fileName} from the test output directory.");
    }
}
