using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.ViewModels;
using System.Runtime.CompilerServices;

namespace Monica.UiTests;

/// <summary>
/// Density guards for the locked workspace shell: one screen must not print the same section name
/// twice, must not offer the same command as two identical buttons, and must not leak raw paths or
/// dead controls into the rails.
/// </summary>
[Collection(AvaloniaUiTestCollection.Name)]
public sealed class WorkspaceDensityGuardUiTests
{
    private static readonly string[] Sections =
    [
        "Vault", "Passwords", "Notes", "Totp", "Cards", "Generator", "Archive", "RecycleBin",
        "SecurityAnalysis", "Timeline", "Mdbx", "DatabaseManagement", "Settings"
    ];

    public WorkspaceDensityGuardUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Section_labels_never_repeat_text_already_on_screen()
    {
        ForEachSection((window, _, section) =>
        {
            var rendered = VisibleTexts(window).ToList();
            var offenders = VisibleLabeledClasses(window, "workspaceSectionLabel")
                .Where(text => rendered.Count(other => other == text) > 1)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"{section}: rail labels repeat text already rendered elsewhere on the same screen: " +
                string.Join(" | ", offenders));
        });
    }

    [Fact]
    public void No_command_is_offered_by_two_identical_buttons()
    {
        ForEachSection((window, _, section) =>
        {
            var offenders = window.GetVisualDescendants().OfType<Button>()
                .Where(IsEffectivelyVisible)
                .Select(button => (
                    Label: string.Concat(button.GetVisualDescendants().OfType<TextBlock>()
                        .Where(IsEffectivelyVisible)
                        .Select(text => (text.Text ?? "").Trim())),
                    Command: button.Command is null
                        ? "none"
                        : RuntimeHelpers.GetHashCode(button.Command).ToString("X")
                        + "/" + button.CommandParameter))
                .Where(entry => entry.Label.Length > 0)
                .GroupBy(entry => entry)
                .Where(group => group.Count() > 1)
                .Select(group => $"{group.Key.Label} x{group.Count()}")
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"{section}: the same command is offered by more than one visible button: " +
                string.Join(" | ", offenders));
        });
    }

    [Fact]
    public void Rails_never_render_a_raw_filesystem_path()
    {
        ForEachSection((window, _, section) =>
        {
            var offenders = window.GetVisualDescendants().OfType<Border>()
                .Where(border => IsEffectivelyVisible(border)
                    && border.Name is not null
                    && border.Name.EndsWith("ListRegion", StringComparison.Ordinal))
                .SelectMany(region => region.GetVisualDescendants().OfType<TextBlock>()
                    .Where(text => IsEffectivelyVisible(text)
                        && (text.Text ?? "").Contains('\\', StringComparison.Ordinal))
                    .Select(text => $"{region.Name}: {text.Text}"))
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"{section}: a list row renders a raw path, which belongs in a tooltip: " +
                string.Join(" | ", offenders));
        });
    }

    [Fact]
    public void Search_clear_buttons_are_hidden_while_the_query_is_empty()
    {
        ForEachSection((window, viewModel, section) =>
        {
            var offenders = window.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Name is not null
                    && button.Name.Contains("SearchClear", StringComparison.Ordinal))
                .Where(button => button.IsVisible)
                .Select(button => button.Name!)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"{section}: {string.Join(", ", offenders)} shown with an empty query — a dead control " +
                "in a search field. Hide it like the other pages do.");
        });
    }

    private static void ForEachSection(Action<Window, MainWindowViewModel, string> assert)
    {
        var window = new Monica.App.MainWindow { Width = 1280, Height = 800 };
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.Show();
        window.DataContext = viewModel;
        viewModel.IsUnlocked = true;
        viewModel.L.SetLanguage("zh-CN");
        Drain();

        foreach (var section in Sections)
        {
            viewModel.SelectSectionCommand.Execute(section);
            Drain();
            assert(window, viewModel, section);
        }
    }

    private static IEnumerable<string> VisibleTexts(Visual root) =>
        VisibleLabeledClasses(root, null);

    private static IEnumerable<string> VisibleLabeledClasses(Visual root, string? styleClass) =>
        root.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => IsEffectivelyVisible(text)
                && !string.IsNullOrWhiteSpace(text.Text)
                && (styleClass is null || text.Classes.Contains(styleClass)))
            .Select(text => text.Text!.Trim());

    private static bool IsEffectivelyVisible(Visual visual)
    {
        for (Visual? current = visual; current is not null; current = current.GetVisualParent())
        {
            if (current is Control { IsVisible: false })
            {
                return false;
            }
        }

        return true;
    }

    private static void Drain()
    {
        for (var i = 0; i < 60; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }
}
