using Avalonia.Controls;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using Monica.App;
using Monica.App.Features.Archive;
using Monica.App.Features.RecycleBin;
using Monica.App.Features.Timeline;
using Monica.App.ViewModels;
using Monica.Core.Models;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class LifecycleWorkflowUiTests
{
    public LifecycleWorkflowUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Lifecycle_workspaces_expose_search_recovery_and_named_regions()
    {
        var archive = new ArchiveWorkspaceView();
        var recycleBin = new RecycleBinWorkspaceView();
        var timeline = new TimelineWorkspaceView();

        Assert.NotNull(archive.FindControl<TextBox>("ArchiveSearchBox"));
        Assert.NotNull(archive.FindControl<Button>("ArchiveSearchClearButton"));
        Assert.NotNull(archive.FindControl<ListBox>("ArchivePasswordList"));
        Assert.NotNull(archive.FindControl<CheckBox>("ArchiveSelectAllCheckBox"));
        Assert.NotNull(archive.FindControl<Border>("ArchiveSelectionBar"));
        Assert.NotNull(archive.FindControl<Button>("UnarchiveSelectedPasswordsButton"));
        Assert.NotNull(archive.FindControl<Button>("ClearArchiveSelectionButton"));
        Assert.NotNull(archive.FindControl<Button>("EmptyArchiveClearSearchButton"));
        Assert.NotNull(archive.FindControl<Border>("ArchiveRecoveryCommandSurface"));
        Assert.NotNull(recycleBin.FindControl<TextBox>("RecycleBinSearchBox"));
        Assert.NotNull(recycleBin.FindControl<Button>("RecycleBinSearchClearButton"));
        Assert.NotNull(recycleBin.FindControl<ListBox>("RecycleBinPasswordList"));
        Assert.NotNull(recycleBin.FindControl<CheckBox>("RecycleBinSelectAllCheckBox"));
        Assert.NotNull(recycleBin.FindControl<Border>("RecycleBinSelectionBar"));
        Assert.NotNull(recycleBin.FindControl<Button>("RestoreSelectedPasswordsButton"));
        Assert.NotNull(recycleBin.FindControl<Button>("DeleteSelectedPasswordsPermanentlyButton"));
        Assert.NotNull(recycleBin.FindControl<Button>("ClearRecycleBinSelectionButton"));
        Assert.NotNull(recycleBin.FindControl<Button>("EmptyRecycleBinClearSearchButton"));
        Assert.NotNull(recycleBin.FindControl<Border>("RecycleBinLifecycleCommandSurface"));
        Assert.NotNull(recycleBin.FindControl<Border>("RecycleBinRetentionStatus"));
        Assert.NotNull(timeline.FindControl<TextBox>("TimelineSearchBox"));
        Assert.NotNull(timeline.FindControl<Button>("TimelineSearchClearButton"));
        var timelineList = Assert.IsType<TimelineEntryListView>(
            timeline.FindControl<TimelineEntryListView>("TimelineEntryListView"));
        Assert.NotNull(timelineList.EntryList);
        Assert.NotNull(timelineList.FindControl<Button>("EmptyTimelineClearSearchButton"));
    }

    [Fact]
    public void Password_lifecycle_commands_use_page_and_item_overflow_ownership()
    {
        var archive = new ArchiveWorkspaceView();
        var recycleBin = new RecycleBinWorkspaceView();

        var archivePageMenu = archive.FindControl<Button>("ArchivePageMenuButton");
        var archiveDetailPrimary = archive.FindControl<Button>("ArchiveDetailPrimaryActionButton");
        var archiveDetailMenu = archive.FindControl<Button>("ArchiveDetailMenuButton");
        Assert.NotNull(archivePageMenu);
        Assert.NotNull(archiveDetailPrimary);
        Assert.NotNull(archiveDetailMenu);
        Assert.Single(archive.FindControl<StackPanel>("ArchiveHeaderCommands")!.Children.OfType<Button>());
        Assert.Equal(
            ["OpenPasswordsFromArchiveMenuItem", "OpenRecycleBinFromArchiveMenuItem"],
            MenuItemNames(Assert.IsType<MenuFlyout>(archivePageMenu.Flyout)));
        Assert.Equal(
            ["OpenArchivedPasswordDetailsMenuItem", "MoveArchivedPasswordToRecycleBinMenuItem"],
            MenuItemNames(Assert.IsType<MenuFlyout>(archiveDetailMenu.Flyout)));

        var recyclePageMenu = recycleBin.FindControl<Button>("RecycleBinPageMenuButton");
        var recycleDetailPrimary = recycleBin.FindControl<Button>("RecycleBinDetailPrimaryActionButton");
        var recycleDetailMenu = recycleBin.FindControl<Button>("RecycleBinDetailMenuButton");
        Assert.NotNull(recyclePageMenu);
        Assert.NotNull(recycleDetailPrimary);
        Assert.NotNull(recycleDetailMenu);
        Assert.Single(recycleBin.FindControl<StackPanel>("RecycleBinHeaderCommands")!.Children.OfType<Button>());
        Assert.Equal(
            ["OpenPasswordsFromRecycleBinMenuItem", "OpenArchiveFromRecycleBinMenuItem", "RecycleBinRetentionMenuItem", "EmptyRecycleBinMenuItem"],
            MenuItemNames(Assert.IsType<MenuFlyout>(recyclePageMenu.Flyout)));
        Assert.Equal(
            ["OpenDeletedPasswordDetailsMenuItem", "DeletePasswordPermanentlyMenuItem"],
            MenuItemNames(Assert.IsType<MenuFlyout>(recycleDetailMenu.Flyout)));
    }

    [Fact]
    public void Password_lifecycle_selection_and_detail_surfaces_are_integrated_not_nested_cards()
    {
        var archive = new ArchiveWorkspaceView();
        var recycleBin = new RecycleBinWorkspaceView();

        var archiveSelection = archive.FindControl<Border>("ArchiveSelectionBar")!;
        var recycleSelection = recycleBin.FindControl<Border>("RecycleBinSelectionBar")!;
        Assert.Equal(0, archiveSelection.CornerRadius.TopLeft);
        Assert.Equal(1, archiveSelection.BorderThickness.Bottom);
        Assert.Equal(0, archiveSelection.Margin.Left);
        Assert.Equal(0, recycleSelection.CornerRadius.TopLeft);
        Assert.Equal(1, recycleSelection.BorderThickness.Bottom);
        Assert.Equal(0, recycleSelection.Margin.Left);
        Assert.Contains("archiveInspector", archive.FindControl<Border>("ArchiveDetailRegion")!.Classes);
        Assert.Contains("recycleInspector", recycleBin.FindControl<Border>("RecycleBinDetailRegion")!.Classes);

        var archiveXaml = File.ReadAllText(FindFeatureFile("Archive", "ArchiveWorkspaceView.axaml"));
        var recycleXaml = File.ReadAllText(FindFeatureFile("RecycleBin", "RecycleBinWorkspaceView.axaml"));
        Assert.Contains("x:Name=\"ArchiveRecoveryCommandSurface\"", archiveXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RecycleBinLifecycleCommandSurface\"", recycleXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Lifecycle_workspaces_switch_to_single_pane_at_narrow_width()
    {
        var archive = new ArchiveWorkspaceView();
        var recycleBin = new RecycleBinWorkspaceView();
        var timeline = new TimelineWorkspaceView();

        archive.UpdateResponsiveLayoutForWidth(680);
        recycleBin.UpdateResponsiveLayoutForWidth(680);
        timeline.UpdateResponsiveLayoutForWidth(680);

        Assert.True(archive.IsNarrowLayout);
        Assert.True(recycleBin.IsNarrowLayout);
        Assert.True(timeline.IsNarrowLayout);
        Assert.True(archive.FindControl<Border>("ArchiveListRegion")!.IsVisible);
        Assert.False(archive.FindControl<Border>("ArchiveDetailRegion")!.IsVisible);
        Assert.True(recycleBin.FindControl<Border>("RecycleBinListRegion")!.IsVisible);
        Assert.False(recycleBin.FindControl<Border>("RecycleBinDetailRegion")!.IsVisible);
        Assert.True(timeline.FindControl<Border>("TimelineListRegion")!.IsVisible);
        Assert.False(timeline.FindControl<Border>("TimelineDetailRegion")!.IsVisible);

        archive.UpdateResponsiveLayoutForWidth(1100);
        recycleBin.UpdateResponsiveLayoutForWidth(1100);
        timeline.UpdateResponsiveLayoutForWidth(1100);

        Assert.True(archive.FindControl<Border>("ArchiveDetailRegion")!.IsVisible);
        Assert.True(recycleBin.FindControl<Border>("RecycleBinDetailRegion")!.IsVisible);
        Assert.True(timeline.FindControl<Border>("TimelineDetailRegion")!.IsVisible);
    }

    [Fact]
    public void Password_lifecycle_workspaces_use_medium_list_width_and_reflowed_header()
    {
        var archive = new ArchiveWorkspaceView();
        var recycleBin = new RecycleBinWorkspaceView();

        archive.UpdateResponsiveLayoutForWidth(900);
        recycleBin.UpdateResponsiveLayoutForWidth(900);

        Assert.True(archive.IsMediumLayout);
        Assert.True(recycleBin.IsMediumLayout);
        Assert.Equal(280, archive.FindControl<Grid>("ArchiveMasterDetailGrid")!.ColumnDefinitions[0].Width.Value);
        Assert.Equal(280, recycleBin.FindControl<Grid>("RecycleBinMasterDetailGrid")!.ColumnDefinitions[0].Width.Value);
        Assert.Equal(1, Grid.GetRow(archive.FindControl<Grid>("ArchiveSearchRegion")!));
        Assert.Equal(1, Grid.GetRow(recycleBin.FindControl<Grid>("RecycleBinSearchRegion")!));
    }

    [Fact]
    public void Lifecycle_selection_shortcuts_select_visible_entries_and_escape_clears_selection()
    {
        var window = new MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();

        viewModel.ArchivedPasswords.Add(new PasswordEntry { Title = "Archive one" });
        viewModel.ArchivedPasswords.Add(new PasswordEntry { Title = "Archive two" });
        viewModel.ArchiveSearchText = "Archive";
        viewModel.SelectSectionCommand.Execute("Archive");

        var archiveSelectAll = HandleShortcut(window, viewModel, Key.A, KeyModifiers.Control);
        Assert.True(archiveSelectAll.Handled);
        Assert.All(viewModel.ArchivedPasswords, item => Assert.True(item.IsSelected));

        var archiveEscape = HandleShortcut(window, viewModel, Key.Escape);
        Assert.True(archiveEscape.Handled);
        Assert.All(viewModel.ArchivedPasswords, item => Assert.False(item.IsSelected));

        viewModel.DeletedPasswords.Add(new PasswordEntry { Title = "Deleted one", IsDeleted = true });
        viewModel.DeletedPasswords.Add(new PasswordEntry { Title = "Deleted two", IsDeleted = true });
        viewModel.RecycleBinSearchText = "Deleted";
        viewModel.SelectSectionCommand.Execute("RecycleBin");

        var recycleSelectAll = HandleShortcut(window, viewModel, Key.A, KeyModifiers.Control);
        Assert.True(recycleSelectAll.Handled);
        Assert.All(viewModel.DeletedPasswords, item => Assert.True(item.IsSelected));

        var recycleEscape = HandleShortcut(window, viewModel, Key.Escape);
        Assert.True(recycleEscape.Handled);
        Assert.All(viewModel.DeletedPasswords, item => Assert.False(item.IsSelected));
    }

    private static KeyEventArgs HandleShortcut(
        MainWindow window,
        MainWindowViewModel viewModel,
        Key key,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Source = window,
            Key = key,
            KeyModifiers = modifiers
        };
        window.TryHandleLifecycleWorkspaceShortcut(viewModel, args);
        return args;
    }

    private static string[] MenuItemNames(MenuFlyout menu) =>
        menu.Items
            .OfType<MenuItem>()
            .Select(item => item.Name ?? "")
            .ToArray();

    private static string FindFeatureFile(string feature, string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Monica.App", "Features", feature, fileName);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"Could not locate {feature}/{fileName} from the test output directory.");
    }
}
