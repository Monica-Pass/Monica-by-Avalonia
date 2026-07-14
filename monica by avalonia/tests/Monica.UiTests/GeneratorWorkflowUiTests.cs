using Avalonia.Controls;
using Monica.App.Features.Generator;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class GeneratorWorkflowUiTests
{
    public GeneratorWorkflowUiTests()
    {
        TestAppBuilder.EnsureInitialized();
    }

    [Fact]
    public void Generator_workspace_exposes_result_options_validation_and_history_actions()
    {
        var view = new GeneratorWorkspaceView();

        Assert.NotNull(view.FindControl<Grid>("GeneratorHeaderGrid"));
        Assert.NotNull(view.FindControl<Grid>("GeneratorContentGrid"));
        Assert.NotNull(view.FindControl<Border>("GeneratorResultRegion"));
        Assert.NotNull(view.FindControl<Border>("GeneratorOptionsRegion"));
        Assert.NotNull(view.FindControl<TextBox>("GeneratedPasswordBox"));
        Assert.NotNull(view.FindControl<TextBlock>("GeneratorValidationMessage"));
        Assert.NotNull(view.FindControl<Button>("GeneratePasswordButton"));
        Assert.NotNull(view.FindControl<Button>("CopyGeneratedPasswordButton"));
        Assert.NotNull(view.FindControl<Button>("ClearGeneratorHistoryButton"));
    }

    [Fact]
    public void Generator_workspace_reflows_for_wide_medium_and_narrow_widths()
    {
        var view = new GeneratorWorkspaceView();
        var content = view.FindControl<Grid>("GeneratorContentGrid")!;
        var result = view.FindControl<Border>("GeneratorResultRegion")!;
        var options = view.FindControl<Border>("GeneratorOptionsRegion")!;

        view.UpdateResponsiveLayoutForWidth(680);
        Assert.True(view.IsNarrowLayout);
        Assert.Equal(0, Grid.GetColumn(result));
        Assert.Equal(0, Grid.GetColumn(options));
        Assert.Equal(1, Grid.GetRow(options));
        Assert.Single(content.ColumnDefinitions);

        view.UpdateResponsiveLayoutForWidth(900);
        Assert.True(view.IsMediumLayout);
        Assert.Equal(1, Grid.GetColumn(options));
        Assert.Equal(2, content.ColumnDefinitions.Count);

        view.UpdateResponsiveLayoutForWidth(1200);
        Assert.False(view.IsNarrowLayout);
        Assert.False(view.IsMediumLayout);
        Assert.Equal(340, content.ColumnDefinitions[1].Width.Value);
    }
}
