using Avalonia.Controls;
using Monica.App.Features.Timeline;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class TimelineWorkflowUiTests
{
    public TimelineWorkflowUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Timeline_uses_command_list_and_inspector_components()
    {
        var view = new TimelineWorkspaceView();

        Assert.IsType<TimelineCommandBarView>(view.FindControl<TimelineCommandBarView>("TimelineCommandBar"));
        Assert.IsType<TimelineEntryListView>(view.FindControl<TimelineEntryListView>("TimelineEntryListView"));
        Assert.IsType<TimelineInspectorView>(view.FindControl<TimelineInspectorView>("TimelineInspectorView"));
    }

    [Fact]
    public void Timeline_command_bar_owns_export_and_refresh_and_list_scroll_is_virtualized()
    {
        var workspaceXaml = File.ReadAllText(FindFeatureFile("TimelineWorkspaceView.axaml"));
        var commandXaml = File.ReadAllText(FindFeatureFile("TimelineCommandBarView.axaml"));
        var listXaml = File.ReadAllText(FindFeatureFile("TimelineEntryListView.axaml"));

        Assert.DoesNotContain("Command=\"{Binding SaveTimelineExportCommand}\"", workspaceXaml, StringComparison.Ordinal);
        Assert.Contains("<fa:FACommandBar", commandXaml, StringComparison.Ordinal);
        Assert.Contains("SaveTimelineExportCommand", commandXaml, StringComparison.Ordinal);
        Assert.Contains("LoadCommand", commandXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer", listXaml, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.VerticalScrollBarVisibility=\"Auto\"", listXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TimelineDetailScrollViewer\"", File.ReadAllText(FindFeatureFile("TimelineInspectorView.axaml")), StringComparison.Ordinal);
    }

    [Fact]
    public void Timeline_supports_wide_medium_and_narrow_list_detail_states()
    {
        var view = new TimelineWorkspaceView();
        var list = view.FindControl<Border>("TimelineListRegion")!;
        var detail = view.FindControl<Border>("TimelineDetailRegion")!;

        view.UpdateResponsiveLayoutForWidth(680);
        Assert.True(view.IsNarrowLayout);
        Assert.True(list.IsVisible);
        Assert.False(detail.IsVisible);

        view.UpdateResponsiveLayoutForWidth(900);
        Assert.True(view.IsMediumLayout);
        Assert.True(list.IsVisible);
        Assert.True(detail.IsVisible);

        view.UpdateResponsiveLayoutForWidth(1200);
        Assert.False(view.IsNarrowLayout);
        Assert.False(view.IsMediumLayout);
        Assert.True(list.IsVisible);
        Assert.True(detail.IsVisible);
    }

    private static string FindFeatureFile(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Monica.App", "Features", "Timeline", fileName);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"Could not locate {fileName} from the test output directory.");
    }
}
