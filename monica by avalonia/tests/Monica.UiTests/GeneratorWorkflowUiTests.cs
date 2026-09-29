using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Controls;
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
        Assert.True(
            xaml.IndexOf("GeneratorOptionsView", StringComparison.Ordinal) <
            xaml.IndexOf("GeneratorResultView", StringComparison.Ordinal));
        Assert.DoesNotContain("HeaderGeneratePasswordButton", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("HeaderCopyGeneratedPasswordButton", xaml, StringComparison.Ordinal);
        Assert.Contains("<fa:FACommandBar", resultXaml, StringComparison.Ordinal);
        Assert.Contains("Classes=\"generatorOutputSurface\"", resultXaml, StringComparison.Ordinal);
        Assert.Contains("Classes=\"generatorHistorySurface\"", resultXaml, StringComparison.Ordinal);
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
        Assert.Equal(0, Grid.GetColumn(options));
        Assert.Equal(1, Grid.GetColumn(result));
        Assert.Equal(2, content.ColumnDefinitions.Count);
        Assert.True(Assert.Single(content.RowDefinitions).Height.IsStar);

        view.UpdateResponsiveLayoutForWidth(1200);
        Assert.False(view.IsNarrowLayout);
        Assert.False(view.IsMediumLayout);
        Assert.Equal(340, content.ColumnDefinitions[0].Width.Value);
        Assert.True(content.ColumnDefinitions[1].Width.IsStar);
    }

    [Fact]
    public void Generator_host_binds_child_state_and_releases_it_on_lock()
    {
        var window = new Monica.App.MainWindow { Width = 1280, Height = 800 };
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            viewModel.SelectedSection = "Generator";
            Dispatcher.UIThread.RunJobs();
            var host = Assert.Single(window.GetVisualDescendants().OfType<WorkspaceHostView>());
            var view = Assert.IsType<GeneratorWorkspaceView>(host.CurrentWorkspace);
            var result = view.FindControl<GeneratorResultView>("GeneratorResultView")!;
            var options = view.FindControl<GeneratorOptionsView>("GeneratorOptionsView")!;
            Assert.Same(viewModel.Generator, view.DataContext);
            Assert.Same(viewModel.Generator, result.DataContext);
            Assert.Same(viewModel.Generator, options.DataContext);

            var generate = result.FindControl<FACommandBarButton>("GeneratePasswordButton")!;
            Assert.Same(viewModel.Generator.GeneratePasswordCommand, generate.Command);
            generate.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(viewModel.Generator.GeneratedPassword, result.GeneratedPasswordBox.Text);
            var retained = Assert.Single(viewModel.Generator.GeneratedPasswordHistory);
            var save = result.FindControl<FACommandBarButton>("SaveGeneratedPasswordButton")!;
            Assert.Same(viewModel.AddPasswordCommand, viewModel.Generator.AddPasswordCommand);
            Assert.Same(viewModel.Generator, save.DataContext);
            Assert.Same(viewModel.AddPasswordCommand, save.Command);

            viewModel.IsUnlocked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(viewModel.Generator.GeneratedPassword);
            Assert.Empty(retained.Value);
            Assert.Null(view.DataContext);
        }
        finally
        {
            window.Close();
        }
    }

    private static string FindGeneratorFeatureFile(string fileName) =>
        XamlSource.PathOf(fileName);

    private static int CountOccurrences(string text, string value) =>
        XamlSource.CountOccurrences(text, value);
}
