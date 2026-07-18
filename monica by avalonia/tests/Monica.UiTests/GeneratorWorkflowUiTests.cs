using Avalonia.Controls;
using Monica.App.Features.Generator;
using Monica.App.ViewModels;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class GeneratorWorkflowUiTests
{
    public GeneratorWorkflowUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Generator_workspace_exposes_result_options_validation_and_history_actions()
    {
        var view = new GeneratorWorkspaceView();

        Assert.NotNull(view.FindControl<Grid>("GeneratorHeaderGrid"));
        Assert.NotNull(view.FindControl<Grid>("GeneratorContentGrid"));
        var resultView = Assert.IsType<GeneratorResultView>(
            view.FindControl<GeneratorResultView>("GeneratorResultView"));
        Assert.NotNull(resultView.FindControl<Border>("GeneratorResultRegion"));
        Assert.NotNull(view.FindControl<GeneratorOptionsView>("GeneratorOptionsView"));
        Assert.NotNull(resultView.GeneratedPasswordBox);
        Assert.NotNull(resultView.FindControl<TextBlock>("GeneratorValidationMessage"));
        Assert.NotNull(resultView.FindControl<Button>("GeneratePasswordButton"));
        Assert.NotNull(resultView.FindControl<Button>("CopyGeneratedPasswordButton"));
        Assert.NotNull(resultView.FindControl<Button>("ClearGeneratorHistoryButton"));
    }

    [Fact]
    public void Generator_history_masks_generated_secrets_until_explicitly_revealed()
    {
        const string secret = "correct-horse-battery-staple";
        var item = new GeneratorHistoryItem(secret, "Passphrase", "Strong", "21:10");

        Assert.Equal("••••••••••", item.DisplayValue);
        Assert.False(item.IsRevealed);

        item.ToggleVisibilityCommand.Execute(null);

        Assert.Equal(secret, item.DisplayValue);
        Assert.True(item.IsRevealed);

        item.ClearSensitiveState();

        Assert.Empty(item.Value);
        Assert.Empty(item.DisplayValue);
        Assert.False(item.IsRevealed);
    }

    [Fact]
    public void Generator_history_ui_uses_masked_display_and_accessible_reveal_controls()
    {
        var xaml = File.ReadAllText(FindGeneratorFeatureFile("GeneratorResultView.axaml"));

        Assert.Contains("Text=\"{Binding DisplayValue}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShowGeneratorHistorySecretButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HideGeneratorHistorySecretButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.Name=\"{Binding #GeneratorWorkspaceRoot.DataContext.L[ShowSensitiveField]}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.Name=\"{Binding #GeneratorWorkspaceRoot.DataContext.L[HideSensitiveField]}\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_command_surface_is_singular_and_scroll_ownership_is_explicit()
    {
        var xaml = File.ReadAllText(FindGeneratorFeatureFile("GeneratorWorkspaceView.axaml"));
        var resultXaml = File.ReadAllText(FindGeneratorFeatureFile("GeneratorResultView.axaml"));
        var optionsXaml = File.ReadAllText(FindGeneratorFeatureFile("GeneratorOptionsView.axaml"));

        Assert.Contains("<views:GeneratorResultView x:Name=\"GeneratorResultView\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<views:GeneratorOptionsView x:Name=\"GeneratorOptionsView\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("HeaderGeneratePasswordButton", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("HeaderCopyGeneratedPasswordButton", xaml, StringComparison.Ordinal);
        Assert.Contains("<fa:FACommandBar", resultXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GeneratorHistoryScrollViewer\"", resultXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GeneratorOptionsScrollViewer\"", optionsXaml, StringComparison.Ordinal);
        Assert.Equal(5, CountOccurrences(resultXaml, "Width=\"40\" Height=\"40\""));
    }

    [Fact]
    public void Generator_workspace_reflows_for_wide_medium_and_narrow_widths()
    {
        var view = new GeneratorWorkspaceView();
        var content = view.FindControl<Grid>("GeneratorContentGrid")!;
        var result = view.FindControl<GeneratorResultView>("GeneratorResultView")!;
        var options = view.FindControl<GeneratorOptionsView>("GeneratorOptionsView")!;

        view.UpdateResponsiveLayoutForWidth(680);
        Assert.True(view.IsNarrowLayout);
        Assert.Equal(0, Grid.GetColumn(result));
        Assert.Equal(0, Grid.GetColumn(options));
        Assert.Equal(1, Grid.GetRow(options));
        Assert.Single(content.ColumnDefinitions);
        Assert.All(content.RowDefinitions, row => Assert.True(row.Height.IsStar));

        view.UpdateResponsiveLayoutForWidth(900);
        Assert.True(view.IsMediumLayout);
        Assert.Equal(1, Grid.GetColumn(options));
        Assert.Equal(2, content.ColumnDefinitions.Count);
        Assert.True(Assert.Single(content.RowDefinitions).Height.IsStar);

        view.UpdateResponsiveLayoutForWidth(1200);
        Assert.False(view.IsNarrowLayout);
        Assert.False(view.IsMediumLayout);
        Assert.Equal(340, content.ColumnDefinitions[1].Width.Value);
    }

    private static string FindGeneratorFeatureFile(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "Monica.App",
                "Features",
                "Generator",
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
