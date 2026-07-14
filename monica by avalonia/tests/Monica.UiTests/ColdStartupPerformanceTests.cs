using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Features.Passwords;
using Monica.App.ViewModels;
using Xunit.Abstractions;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class ColdStartupPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public void Cold_locked_shell_composition_reports_each_phase_and_stays_within_budget()
    {
        var total = Stopwatch.StartNew();
        var phase = Stopwatch.StartNew();

        TestAppBuilder.EnsureInitialized();
        var frameworkMilliseconds = phase.Elapsed.TotalMilliseconds;

        phase.Restart();
        var window = new Monica.App.MainWindow();
        var shellMilliseconds = phase.Elapsed.TotalMilliseconds;

        phase.Restart();
        using var services = Monica.App.App.ConfigureServices(window);
        var providerMilliseconds = phase.Elapsed.TotalMilliseconds;

        phase.Restart();
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var viewModelMilliseconds = phase.Elapsed.TotalMilliseconds;
        total.Stop();

        output.WriteLine(
            $"framework={frameworkMilliseconds:F3} ms, shell={shellMilliseconds:F3} ms, " +
            $"provider={providerMilliseconds:F3} ms, viewModel={viewModelMilliseconds:F3} ms, " +
            $"total={total.Elapsed.TotalMilliseconds:F3} ms");

        Assert.NotNull(viewModel);
        Assert.True(frameworkMilliseconds < 1500, $"Headless Avalonia initialization took {frameworkMilliseconds:F3} ms.");
        Assert.True(shellMilliseconds < 500, $"Locked shell construction took {shellMilliseconds:F3} ms.");
        Assert.True(providerMilliseconds < 500, $"Service provider construction took {providerMilliseconds:F3} ms.");
        Assert.True(viewModelMilliseconds < 500, $"Main ViewModel resolution took {viewModelMilliseconds:F3} ms.");
        Assert.True(total.ElapsedMilliseconds < 2500, $"Cold locked-shell composition took {total.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public void Cold_password_editor_construction_reports_first_use_cost()
    {
        TestAppBuilder.EnsureInitialized();

        var phase = Stopwatch.StartNew();
        var coldEditor = new Monica.App.PasswordEditorDialog();
        var coldMilliseconds = phase.Elapsed.TotalMilliseconds;

        phase.Restart();
        var warmEditor = new Monica.App.PasswordEditorDialog();
        var warmMilliseconds = phase.Elapsed.TotalMilliseconds;

        output.WriteLine($"passwordEditorCold={coldMilliseconds:F3} ms, passwordEditorWarm={warmMilliseconds:F3} ms");
        Assert.NotNull(coldEditor);
        Assert.NotNull(warmEditor);
        Assert.True(coldMilliseconds < 500, $"Cold password editor construction took {coldMilliseconds:F3} ms.");
        Assert.True(warmMilliseconds < 100, $"Warm password editor construction took {warmMilliseconds:F3} ms.");
    }

    [Fact]
    public void Password_editor_idle_warmup_removes_first_command_path_construction_cost()
    {
        TestAppBuilder.EnsureInitialized();

        var phase = Stopwatch.StartNew();
        PasswordEditorDialogWarmup.EnsureWarmed();
        var warmupMilliseconds = phase.Elapsed.TotalMilliseconds;

        phase.Restart();
        var editor = new Monica.App.PasswordEditorDialog();
        var commandPathMilliseconds = phase.Elapsed.TotalMilliseconds;

        output.WriteLine(
            $"passwordEditorWarmup={warmupMilliseconds:F3} ms, " +
            $"passwordEditorCommandPath={commandPathMilliseconds:F3} ms");
        Assert.True(PasswordEditorDialogWarmup.IsWarmed);
        Assert.NotNull(editor);
        Assert.True(
            commandPathMilliseconds < 50,
            $"Password editor construction after idle warmup took {commandPathMilliseconds:F3} ms.");
    }
}
