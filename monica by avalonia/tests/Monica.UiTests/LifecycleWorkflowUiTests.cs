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
        Assert.NotNull(recycleBin.FindControl<TextBox>("RecycleBinSearchBox"));
        Assert.NotNull(recycleBin.FindControl<Button>("RecycleBinSearchClearButton"));
        Assert.NotNull(recycleBin.FindControl<ListBox>("RecycleBinPasswordList"));
        Assert.NotNull(recycleBin.FindControl<CheckBox>("RecycleBinSelectAllCheckBox"));
        Assert.NotNull(recycleBin.FindControl<Border>("RecycleBinSelectionBar"));
        Assert.NotNull(recycleBin.FindControl<Button>("RestoreSelectedPasswordsButton"));
        Assert.NotNull(recycleBin.FindControl<Button>("DeleteSelectedPasswordsPermanentlyButton"));
        Assert.NotNull(recycleBin.FindControl<Button>("ClearRecycleBinSelectionButton"));
        Assert.NotNull(recycleBin.FindControl<Button>("EmptyRecycleBinClearSearchButton"));
        Assert.NotNull(timeline.FindControl<TextBox>("TimelineSearchBox"));
        Assert.NotNull(timeline.FindControl<Button>("TimelineSearchClearButton"));
        Assert.NotNull(timeline.FindControl<ListBox>("TimelineEntryList"));
        Assert.NotNull(timeline.FindControl<Button>("EmptyTimelineClearSearchButton"));
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
}
