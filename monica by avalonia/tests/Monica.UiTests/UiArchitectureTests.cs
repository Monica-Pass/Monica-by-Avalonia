using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using FluentIcons.Common;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Controls;
using Monica.App.Features.Archive;
using Monica.App.Features.Authenticator;
using Monica.App.Features.DatabaseManagement;
using Monica.App.Features.Generator;
using Monica.App.Features.Mdbx;
using Monica.App.Features.Notes;
using Monica.App.Features.Passwords;
using Monica.App.Features.RecycleBin;
using Monica.App.Features.SecurityAnalysis;
using Monica.App.Features.Settings;
using Monica.App.Features.Sync;
using Monica.App.Features.Timeline;
using Monica.App.Features.Unlock;
using Monica.App.Features.Vault;
using Monica.App.Features.Wallet;

namespace Monica.UiTests;

public static class AvaloniaUiThreadTestContext
{
    public static void VerifyAccess() => Dispatcher.UIThread.VerifyAccess();
}

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class UiArchitectureTests
{
    public UiArchitectureTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Feature_workspaces_load_in_headless_ui()
    {
        UserControl[] workspaces =
        [
            new UnlockView(),
            new PasswordVaultView(),
            new NoteWorkspaceView(),
            new AuthenticatorWorkspaceView(),
            new WalletWorkspaceView(),
            new GeneratorWorkspaceView(),
            new ArchiveWorkspaceView(),
            new RecycleBinWorkspaceView(),
            new SettingsWorkspaceView(),
            new SyncWorkspaceView(),
            new MdbxWorkspaceView(),
            new TimelineWorkspaceView(),
            new DatabaseManagementWorkspaceView(),
            new SecurityAnalysisWorkspaceView()
        ];

        Assert.All(workspaces, workspace => Assert.NotNull(workspace.Content));
    }

    [Fact]
    public void Main_window_is_composed_from_feature_hosts()
    {
        var window = new Monica.App.MainWindow();
        var shellHost = window.FindControl<ContentControl>("UnlockedShellHost");

        Assert.NotNull(shellHost);
        Assert.Null(shellHost.Content);
        Assert.DoesNotContain(window.GetVisualDescendants(), control => control is WorkspaceHostView);
    }

    [Fact]
    public void Unlocked_navigation_shell_is_materialized_on_demand()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<Monica.App.ViewModels.MainWindowViewModel>();
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            Dispatcher.UIThread.RunJobs();

            var shellHost = window.FindControl<ContentControl>("UnlockedShellHost");
            Assert.NotNull(shellHost);
            Assert.Same(viewModel, shellHost.Content);
            var workspaceHost = Assert.Single(window.GetVisualDescendants().OfType<WorkspaceHostView>());
            Assert.IsType<VaultWorkspaceView>(workspaceHost.CurrentWorkspace);
            Assert.Equal(["Vault"], workspaceHost.CreatedSections);

            var navigation = Assert.Single(window.GetVisualDescendants().OfType<FANavigationView>());
            var navigationGroups = navigation.MenuItems
                .OfType<FANavigationViewItemHeader>()
                .Select(item => item.Tag?.ToString() ?? "")
                .ToArray();
            Assert.Equal(["Vault", "Tools", "Storage"], navigationGroups);
            var navigationTags = navigation.MenuItems
                .Concat(navigation.FooterMenuItems)
                .OfType<FANavigationViewItem>()
                .Select(item => item.Tag?.ToString() ?? "")
                .ToArray();
            Assert.Equal(
                [
                    "Vault", "Passwords", "Notes", "Totp", "Cards", "Generator", "SecurityAnalysis",
                    "Timeline", "Archive", "RecycleBin", "Mdbx",
                    "DatabaseManagement", "Sync", "Settings", "Lock"
                ],
                navigationTags);
            AssertSingleSelectedRailItem(navigation, "Vault");
            var lockItem = navigation.FooterMenuItems
                .OfType<FANavigationViewItem>()
                .Single(item => string.Equals(item.Tag?.ToString(), "Lock", StringComparison.Ordinal));
            Assert.False(lockItem.SelectsOnInvoked);
            Assert.DoesNotContain(
                window.GetVisualDescendants().OfType<Button>(),
                button => ReferenceEquals(button.Command, viewModel.ExportDataCommand));
            var notesItem = navigation.MenuItems
                .OfType<FANavigationViewItem>()
                .Single(item => string.Equals(item.Tag?.ToString(), "Notes", StringComparison.Ordinal));
            navigation.SelectedItem = notesItem;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Notes", viewModel.SelectedSection);
            Assert.IsType<NoteWorkspaceView>(workspaceHost.CurrentWorkspace);
            AssertSingleSelectedRailItem(navigation, "Notes");

            var timelineItem = navigation.MenuItems
                .OfType<FANavigationViewItem>()
                .Single(item => string.Equals(item.Tag?.ToString(), "Timeline", StringComparison.Ordinal));
            viewModel.SelectedSection = "Timeline";
            Dispatcher.UIThread.RunJobs();
            Assert.Same(timelineItem, navigation.SelectedItem);
            AssertSingleSelectedRailItem(navigation, "Timeline");

            var shell = Assert.Single(window.GetVisualDescendants().OfType<Monica.App.Features.UnlockedShellView>());
            shell.ActivateNavigationTag("Lock");
            Dispatcher.UIThread.RunJobs();
            Assert.False(viewModel.IsUnlocked);
            Assert.Null(shellHost.Content);
            Assert.DoesNotContain(window.GetVisualDescendants(), control => control is WorkspaceHostView);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Library_item_reaches_the_library_page_and_no_two_rail_items_share_a_glyph()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<Monica.App.ViewModels.MainWindowViewModel>();
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            Dispatcher.UIThread.RunJobs();

            var workspaceHost = Assert.Single(window.GetVisualDescendants().OfType<WorkspaceHostView>());
            var navigation = Assert.Single(window.GetVisualDescendants().OfType<FANavigationView>());
            var railItems = navigation.MenuItems
                .Concat(navigation.FooterMenuItems)
                .OfType<FANavigationViewItem>()
                .ToArray();

            // What breaks a rail is two pages wearing the same mark, not which mark either one chose,
            // so the invariant is pinned rather than the individual symbols.
            var glyphs = railItems.ToDictionary(item => item.Tag?.ToString() ?? "", item => RailGlyph(item));
            Assert.Equal(glyphs.Count, glyphs.Values.Distinct().Count());

            var libraryItem = railItems.Single(item => string.Equals(item.Tag?.ToString(), "Vault", StringComparison.Ordinal));
            navigation.SelectedItem = libraryItem;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Vault", viewModel.SelectedSection);
            Assert.IsType<VaultWorkspaceView>(workspaceHost.CurrentWorkspace);
        }
        finally
        {
            window.Close();
        }
    }

    private static Symbol RailGlyph(FANavigationViewItem item) =>
        Assert.IsType<FluentSymbolIconSource>(item.IconSource).Symbol;

    private static void AssertSingleSelectedRailItem(FANavigationView navigation, string section) =>
        Assert.Equal(
            new[] { section },
            navigation.MenuItems
                .Concat(navigation.FooterMenuItems)
                .OfType<FANavigationViewItem>()
                .Where(item => item.IsSelected)
                .Select(item => item.Tag?.ToString()));

    [Fact]
    public void Rail_paints_no_accent_indicator_even_when_the_section_moves_between_panes()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<Monica.App.ViewModels.MainWindowViewModel>();
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            Dispatcher.UIThread.RunJobs();

            viewModel.SelectSectionCommand.Execute("DatabaseManagement");
            Dispatcher.UIThread.RunJobs();
            viewModel.SelectSectionCommand.Execute("Passwords");
            Dispatcher.UIThread.RunJobs();

            var navigation = Assert.Single(window.GetVisualDescendants().OfType<FANavigationView>());
            var railItems = navigation.MenuItems
                .Concat(navigation.FooterMenuItems)
                .OfType<FANavigationViewItem>()
                .ToArray();
            Assert.DoesNotContain(railItems, IndicatorIsPainted);
            Assert.Equal(["Passwords"], railItems.Where(item => item.IsSelected).Select(item => item.Tag?.ToString()));
        }
        finally
        {
            window.Close();
        }
    }

    // The rail fades the bars it should not show, so a painted bar is one that is both in the
    // layout and not faded out.
    private static bool IndicatorIsPainted(FANavigationViewItem item) =>
        item.GetVisualDescendants()
            .OfType<Border>()
            .SingleOrDefault(border => border.Name == "SelectionIndicator") is { } bar &&
        bar.IsEffectivelyVisible &&
        bar.Opacity > 0;

    [Fact]
    public void Locked_shell_does_not_eagerly_load_feature_style_dictionaries()
    {
        var window = new Monica.App.MainWindow();

        Assert.DoesNotContain(window.Styles, style => style is Avalonia.Styling.Styles);
    }

    [Fact]
    public void Unlock_view_host_releases_hidden_sensitive_visual_tree_after_unlock()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<Monica.App.ViewModels.MainWindowViewModel>();
        window.Show();
        try
        {
            window.DataContext = viewModel;
            Dispatcher.UIThread.RunJobs();

            var host = Assert.Single(window.GetVisualDescendants().OfType<UnlockViewHost>());
            var releasedView = Assert.IsType<UnlockView>(host.Content);

            viewModel.IsUnlocked = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Null(host.Content);
            Assert.DoesNotContain(window.GetVisualDescendants(), control => control is UnlockView);
            Assert.Null(TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement());
            Assert.Null(releasedView.DataContext);
            Assert.Null(releasedView.Content);

            viewModel.IsUnlocked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.NotSame(releasedView, Assert.IsType<UnlockView>(host.Content));
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Feature_workspaces_own_their_style_dictionaries()
    {
        UserControl[] workspaces =
        [
            new PasswordVaultView(),
            new NoteWorkspaceView(),
            new AuthenticatorWorkspaceView(),
            new WalletWorkspaceView(),
            new GeneratorWorkspaceView(),
            new ArchiveWorkspaceView(),
            new RecycleBinWorkspaceView(),
            new SettingsWorkspaceView(),
            new SyncWorkspaceView(),
            new MdbxWorkspaceView(),
            new TimelineWorkspaceView(),
            new DatabaseManagementWorkspaceView()
        ];

        Assert.All(
            workspaces,
            workspace => Assert.Contains(workspace.Styles, style => style is Avalonia.Styling.Styles));
    }

    [Fact]
    public void Shared_controls_never_bind_to_the_shell_view_model()
    {
        var sources = XamlSource.InDirectory("Controls");

        Assert.NotEmpty(sources);
        Assert.All(
            sources,
            path => Assert.DoesNotContain(
                "MainWindowViewModel",
                File.ReadAllText(path),
                StringComparison.Ordinal));
    }

    [Fact]
    public void Tests_never_locate_desktop_source_by_a_hand_rolled_path()
    {
        // A test that rebuilds "src/Monica.App/<folder>/<file>" fails the moment a view moves,
        // which reports a refactor as a defect. XamlSource resolves by file name instead.
        var offenders = XamlSource.TestSources()
            .Where(path => !path.EndsWith(nameof(XamlSource) + ".cs", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("\"src\", \"Monica.App\"", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Empty(offenders);
    }
}
