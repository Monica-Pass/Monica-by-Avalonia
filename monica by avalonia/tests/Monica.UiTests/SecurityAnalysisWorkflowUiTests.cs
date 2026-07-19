using Avalonia.Controls;
using Monica.App.Features.SecurityAnalysis;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class SecurityAnalysisWorkflowUiTests
{
    public SecurityAnalysisWorkflowUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Security_analysis_uses_command_filter_list_and_inspector_components()
    {
        var view = new SecurityAnalysisWorkspaceView();

        Assert.IsType<SecurityAnalysisCommandBarView>(view.FindControl<SecurityAnalysisCommandBarView>("SecurityAnalysisCommandBar"));
        Assert.IsType<SecurityAnalysisFilterPaneView>(view.FindControl<SecurityAnalysisFilterPaneView>("SecurityAnalysisFilterPane"));
        Assert.IsType<SecurityAnalysisIssueListView>(view.FindControl<SecurityAnalysisIssueListView>("SecurityAnalysisIssueListView"));
        Assert.IsType<SecurityAnalysisInspectorView>(view.FindControl<SecurityAnalysisInspectorView>("SecurityAnalysisInspectorView"));
    }

    [Fact]
    public void Security_analysis_commands_are_not_duplicated_and_lists_keep_virtualized_scroll_ownership()
    {
        var workspaceXaml = File.ReadAllText(FindFeatureFile("SecurityAnalysisWorkspaceView.axaml"));
        var commandXaml = File.ReadAllText(FindFeatureFile("SecurityAnalysisCommandBarView.axaml"));
        var listXaml = File.ReadAllText(FindFeatureFile("SecurityAnalysisIssueListView.axaml"));

        Assert.DoesNotContain("Command=\"{Binding RefreshSecurityAnalysisCommand}\"", workspaceXaml, StringComparison.Ordinal);
        Assert.Contains("<fa:FACommandBar", commandXaml, StringComparison.Ordinal);
        Assert.Contains("CheckCompromisedPasswordsCommand", commandXaml, StringComparison.Ordinal);
        Assert.Contains("RefreshSecurityAnalysisCommand", commandXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer", listXaml, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.VerticalScrollBarVisibility=\"Auto\"", listXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Security_analysis_supports_wide_medium_and_narrow_list_detail_states()
    {
        var view = new SecurityAnalysisWorkspaceView();
        var list = view.FindControl<Border>("SecurityIssueListRegion")!;
        var detail = view.FindControl<Border>("SecurityIssueDetailRegion")!;

        view.UpdateResponsiveLayoutForWidth(680);
        Assert.True(view.IsNarrowLayout);
        Assert.True(list.IsVisible);
        Assert.False(detail.IsVisible);
        Assert.Equal(1, Grid.GetRow(view.FindControl<SecurityAnalysisCommandBarView>("SecurityAnalysisCommandBar")!));

        view.UpdateResponsiveLayoutForWidth(900);
        Assert.True(view.IsMediumLayout);
        Assert.True(list.IsVisible);
        Assert.True(detail.IsVisible);

        view.UpdateResponsiveLayoutForWidth(1200);
        Assert.False(view.IsNarrowLayout);
        Assert.False(view.IsMediumLayout);
        Assert.True(list.IsVisible);
        Assert.True(detail.IsVisible);
        Assert.Equal(0, Grid.GetRow(view.FindControl<SecurityAnalysisCommandBarView>("SecurityAnalysisCommandBar")!));
    }

    private static string FindFeatureFile(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Monica.App", "Features", "SecurityAnalysis", fileName);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"Could not locate {fileName} from the test output directory.");
    }
}
