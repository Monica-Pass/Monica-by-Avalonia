using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Features.Wallet;
using Monica.App.Services;
using Monica.App.ViewModels;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class WalletWorkflowUiTests
{
    public WalletWorkflowUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Wallet_workspace_exposes_search_selection_and_empty_state_actions()
    {
        var view = new WalletWorkspaceView();

        Assert.NotNull(view.FindControl<TextBox>("WalletSearchBox"));
        Assert.NotNull(view.FindControl<Button>("WalletSearchClearButton"));
        Assert.NotNull(view.FindControl<ListBox>("WalletItemList"));
        Assert.NotNull(view.FindControl<StackPanel>("WalletEmptyState"));
        Assert.NotNull(view.FindControl<Button>("EmptyWalletAddButton"));
        Assert.NotNull(view.FindControl<Button>("EmptyWalletClearSearchButton"));
        Assert.NotNull(view.FindControl<Button>("WalletMoreActionsButton"));
    }

    [Fact]
    public void Wallet_search_controls_describe_exact_actions_and_announce_results()
    {
        var view = new WalletWorkspaceView();
        var xaml = File.ReadAllText(FindWalletFeatureFile("WalletWorkspaceView.axaml"));

        Assert.NotNull(view.FindControl<Button>("WalletSearchClearButton"));
        Assert.Contains(
            "AutomationProperties.Name=\"{Binding ClearWalletSearchText}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.HelpText=\"{Binding WalletSearchHelpText}\"",
            xaml,
            StringComparison.Ordinal);
        var status = view.FindControl<TextBlock>("WalletFilteredStatusText");
        Assert.NotNull(status);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
    }

    [Fact]
    public void Wallet_search_actions_are_localized_for_english_and_chinese()
    {
        var localization = new LocalizationService();

        Assert.Equal("Clear wallet search", localization.Get("ClearWalletSearch"));
        Assert.Contains("Ctrl+F", localization.Get("WalletSearchHelp"), StringComparison.Ordinal);

        localization.SetLanguage("zh-CN");

        Assert.Equal("清除卡包搜索", localization.Get("ClearWalletSearch"));
        Assert.Contains("Ctrl+F", localization.Get("WalletSearchHelp"), StringComparison.Ordinal);
    }

    [Fact]
    public void Wallet_search_escape_is_scoped_to_the_focused_search_box()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            viewModel.SelectSectionCommand.Execute("Cards");
            Dispatcher.UIThread.RunJobs();
            var workspace = Assert.Single(window.GetVisualDescendants().OfType<WalletWorkspaceView>());
            var searchBox = workspace.FindControl<TextBox>("WalletSearchBox")!;
            var workbench = workspace.FindControl<Border>("WalletWorkbenchRegion")!;
            viewModel.WalletSearchText = "visa";
            searchBox.Focus();
            var searchArgs = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Source = searchBox,
                Key = Key.Escape
            };

            window.HandleWalletWorkspaceShortcut(viewModel, searchArgs);

            Assert.True(searchArgs.Handled);
            Assert.Empty(viewModel.WalletSearchText);

            viewModel.WalletSearchText = "passport";
            workbench.Focus();
            var workbenchArgs = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Source = workbench,
                Key = Key.Escape
            };

            window.HandleWalletWorkspaceShortcut(viewModel, workbenchArgs);

            Assert.False(workbenchArgs.Handled);
            Assert.Equal("passport", viewModel.WalletSearchText);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Wallet_workspace_exposes_wide_medium_and_narrow_regions()
    {
        var view = new WalletWorkspaceView();

        Assert.NotNull(view.FindControl<Grid>("WalletMasterDetailGrid"));
        Assert.NotNull(view.FindControl<Border>("WalletListRegion"));
        Assert.NotNull(view.FindControl<Border>("WalletWorkbenchRegion"));
        Assert.NotNull(view.FindControl<Border>("WalletInspectorRegion"));
        var workbench = Assert.IsType<WalletWorkbenchView>(
            view.FindControl<WalletWorkbenchView>("WalletWorkbench"));
        Assert.NotNull(workbench.FindControl<Button>("BackToWalletListButton"));

        view.UpdateResponsiveLayoutForWidth(680);
        Assert.True(view.IsNarrowLayout);
        Assert.True(view.FindControl<Border>("WalletListRegion")!.IsVisible);
        Assert.False(view.FindControl<Border>("WalletWorkbenchRegion")!.IsVisible);
        Assert.False(view.FindControl<Border>("WalletInspectorRegion")!.IsVisible);

        view.UpdateResponsiveLayoutForWidth(900);
        Assert.True(view.IsMediumLayout);
        Assert.True(view.FindControl<Border>("WalletListRegion")!.IsVisible);
        Assert.True(view.FindControl<Border>("WalletWorkbenchRegion")!.IsVisible);
        Assert.False(view.FindControl<Border>("WalletInspectorRegion")!.IsVisible);

        view.UpdateResponsiveLayoutForWidth(1200);
        Assert.False(view.IsNarrowLayout);
        Assert.False(view.IsMediumLayout);
        Assert.True(view.FindControl<Border>("WalletListRegion")!.IsVisible);
        Assert.True(view.FindControl<Border>("WalletWorkbenchRegion")!.IsVisible);
        Assert.True(view.FindControl<Border>("WalletInspectorRegion")!.IsVisible);
    }

    [Fact]
    public void Wallet_editor_masks_sensitive_inputs_and_exposes_visibility_controls()
    {
        var editor = new WalletItemEditorDialog();

        Assert.NotNull(editor.FindControl<TextBox>("DocumentNumberInput"));
        Assert.NotNull(editor.FindControl<Button>("ToggleDocumentNumberVisibilityButton"));
        Assert.NotNull(editor.FindControl<TextBox>("CardNumberInput"));
        Assert.NotNull(editor.FindControl<Button>("ToggleCardNumberVisibilityButton"));
        Assert.NotNull(editor.FindControl<TextBox>("CardCvvInput"));
        Assert.NotNull(editor.FindControl<Button>("ToggleCardCvvVisibilityButton"));
    }

    private static string FindWalletFeatureFile(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "Monica.App",
                "Features",
                "Wallet",
                fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate {fileName} from the test output directory.");
    }
}
