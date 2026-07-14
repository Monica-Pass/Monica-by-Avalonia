using Avalonia.Controls;
using Monica.App.Features.Archive;
using Monica.App.Features.RecycleBin;
using Monica.App.Features.Timeline;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class LifecycleWorkflowUiTests
{
    public LifecycleWorkflowUiTests()
    {
        TestAppBuilder.EnsureInitialized();
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
        Assert.NotNull(archive.FindControl<Button>("EmptyArchiveClearSearchButton"));
        Assert.NotNull(recycleBin.FindControl<TextBox>("RecycleBinSearchBox"));
        Assert.NotNull(recycleBin.FindControl<Button>("RecycleBinSearchClearButton"));
        Assert.NotNull(recycleBin.FindControl<ListBox>("RecycleBinPasswordList"));
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
}
