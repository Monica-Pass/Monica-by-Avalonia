using System.Diagnostics;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Controls;
using Monica.App.Features;
using Monica.App.Features.Authenticator;
using Monica.App.Features.Passwords;
using Monica.App.Features.Wallet;
using Monica.App.ViewModels;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class ColdStartupPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public void Cold_locked_shell_composition_reports_each_phase_and_stays_within_budget()
    {
        var total = Stopwatch.StartNew();
        var phase = Stopwatch.StartNew();

        AvaloniaUiThreadTestContext.VerifyAccess();
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
        AvaloniaUiThreadTestContext.VerifyAccess();

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
        Assert.True(warmMilliseconds < 500, $"Warm password editor construction took {warmMilliseconds:F3} ms.");
    }

    [Fact]
    public void Password_editor_idle_warmup_removes_first_command_path_construction_cost()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();

        var phase = Stopwatch.StartNew();
        VaultEditorDialogWarmup.EnsurePasswordWarmed();
        var warmupMilliseconds = phase.Elapsed.TotalMilliseconds;

        Assert.True(VaultEditorDialogWarmup.IsPasswordWarmed);
        phase.Restart();
        var editor = VaultEditorDialogWarmup.TakePasswordEditorView();
        var commandPathMilliseconds = phase.Elapsed.TotalMilliseconds;

        output.WriteLine(
            $"passwordEditorWarmup={warmupMilliseconds:F3} ms, " +
            $"passwordEditorCommandPath={commandPathMilliseconds:F3} ms");
        Assert.NotNull(editor);
        Assert.True(
            commandPathMilliseconds < 50,
            $"Password editor construction after idle warmup took {commandPathMilliseconds:F3} ms.");
    }

    [Fact]
    public void Feature_workspace_first_navigation_stays_within_budget()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
        var host = new WorkspaceHostView { IsActive = true };
        string[] sections =
        [
            "Passwords", "Notes", "Totp", "Cards", "Generator", "Archive", "RecycleBin",
            "Settings", "Sync", "Mdbx", "Timeline", "DatabaseManagement", "SecurityAnalysis"
        ];
        var measurements = new List<string>(sections.Length);
        var total = Stopwatch.StartNew();

        foreach (var section in sections)
        {
            var phase = Stopwatch.StartNew();
            host.Section = section;
            phase.Stop();
            measurements.Add($"{section}={phase.Elapsed.TotalMilliseconds:F3} ms");
            Assert.NotNull(host.CurrentWorkspace);
            Assert.True(
                phase.ElapsedMilliseconds < 250,
                $"First navigation to {section} took {phase.Elapsed.TotalMilliseconds:F3} ms.");
        }

        total.Stop();
        output.WriteLine(string.Join(", ", measurements));
        output.WriteLine($"featureNavigationTotal={total.Elapsed.TotalMilliseconds:F3} ms");
        Assert.True(
            total.ElapsedMilliseconds < 1000,
            $"First navigation through all feature workspaces took {total.Elapsed.TotalMilliseconds:F3} ms.");
    }

    [Fact]
    public void Unlocked_navigation_shell_materialization_stays_within_budget()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.Show();
        try
        {
            window.DataContext = viewModel;
            var phase = Stopwatch.StartNew();
            viewModel.IsUnlocked = true;
            var propertyMilliseconds = phase.Elapsed.TotalMilliseconds;
            phase.Restart();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
            phase.Stop();
            var firstFrameDispatcherMilliseconds = phase.Elapsed.TotalMilliseconds;

            var host = Assert.Single(window.GetVisualDescendants().OfType<WorkspaceHostView>());
            Assert.Null(host.CurrentWorkspace);
            phase.Restart();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
            phase.Stop();
            var deferredWorkspaceMilliseconds = phase.Elapsed.TotalMilliseconds;

            output.WriteLine(
                $"workspaceHostActive={host.IsActive}, section={host.Section}, created={host.CreatedSections.Count}");
            Assert.IsType<PasswordVaultView>(host.CurrentWorkspace);
            var totalMilliseconds = propertyMilliseconds + firstFrameDispatcherMilliseconds + deferredWorkspaceMilliseconds;
            output.WriteLine(
                $"unlockedShellProperty={propertyMilliseconds:F3} ms, " +
                $"unlockedShellFirstFrame={firstFrameDispatcherMilliseconds:F3} ms, " +
                $"unlockedShellDeferredWorkspace={deferredWorkspaceMilliseconds:F3} ms, " +
                $"unlockedShellMaterialization={totalMilliseconds:F3} ms");
            Assert.True(
                propertyMilliseconds + firstFrameDispatcherMilliseconds < 700,
                $"Unlocked navigation shell first frame took " +
                $"{propertyMilliseconds + firstFrameDispatcherMilliseconds:F3} ms.");
            Assert.True(
                deferredWorkspaceMilliseconds < 600,
                $"Deferred password workspace materialization took {deferredWorkspaceMilliseconds:F3} ms.");
            Assert.True(
                totalMilliseconds < 1200,
                $"Unlocked navigation shell materialization took {totalMilliseconds:F3} ms.");
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Secondary_editor_cold_construction_reports_first_use_cost()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();

        var phase = Stopwatch.StartNew();
        var coldTotpEditor = new Monica.App.TotpEditorDialog();
        var coldTotpMilliseconds = phase.Elapsed.TotalMilliseconds;

        phase.Restart();
        var warmTotpEditor = new Monica.App.TotpEditorDialog();
        var warmTotpMilliseconds = phase.Elapsed.TotalMilliseconds;

        phase.Restart();
        var coldWalletEditor = new WalletItemEditorDialog();
        var coldWalletMilliseconds = phase.Elapsed.TotalMilliseconds;

        phase.Restart();
        var warmWalletEditor = new WalletItemEditorDialog();
        var warmWalletMilliseconds = phase.Elapsed.TotalMilliseconds;

        output.WriteLine(
            $"totpCold={coldTotpMilliseconds:F3} ms, totpWarm={warmTotpMilliseconds:F3} ms, " +
            $"walletCold={coldWalletMilliseconds:F3} ms, walletWarm={warmWalletMilliseconds:F3} ms");
        Assert.NotNull(coldTotpEditor);
        Assert.NotNull(warmTotpEditor);
        Assert.NotNull(coldWalletEditor);
        Assert.NotNull(warmWalletEditor);
        Assert.True(coldTotpMilliseconds < 500, $"Cold TOTP editor construction took {coldTotpMilliseconds:F3} ms.");
        Assert.True(coldWalletMilliseconds < 500, $"Cold Wallet editor construction took {coldWalletMilliseconds:F3} ms.");
        Assert.True(warmTotpMilliseconds < 50, $"Warm TOTP editor construction took {warmTotpMilliseconds:F3} ms.");
        Assert.True(warmWalletMilliseconds < 50, $"Warm Wallet editor construction took {warmWalletMilliseconds:F3} ms.");
    }

    [Fact]
    public void Secondary_editor_workspace_idle_warmup_removes_first_command_path_cost()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();

        var authenticatorWorkspace = new AuthenticatorWorkspaceView();
        var walletWorkspace = new WalletWorkspaceView();
        Dispatcher.UIThread.RunJobs();

        Assert.True(VaultEditorDialogWarmup.IsTotpWarmed);
        Assert.True(VaultEditorDialogWarmup.IsWalletWarmed);
        var phase = Stopwatch.StartNew();
        var totpEditor = VaultEditorDialogWarmup.TakeTotpEditorView();
        var totpCommandPathMilliseconds = phase.Elapsed.TotalMilliseconds;

        phase.Restart();
        var walletEditor = VaultEditorDialogWarmup.TakeWalletEditorView();
        var walletCommandPathMilliseconds = phase.Elapsed.TotalMilliseconds;

        output.WriteLine(
            $"totpCommandPath={totpCommandPathMilliseconds:F3} ms, " +
            $"walletCommandPath={walletCommandPathMilliseconds:F3} ms");
        Assert.NotNull(authenticatorWorkspace);
        Assert.NotNull(walletWorkspace);
        Assert.NotNull(totpEditor);
        Assert.NotNull(walletEditor);
        Assert.True(
            totpCommandPathMilliseconds < 50,
            $"TOTP editor construction after workspace idle warmup took {totpCommandPathMilliseconds:F3} ms.");
        Assert.True(
            walletCommandPathMilliseconds < 50,
            $"Wallet editor construction after workspace idle warmup took {walletCommandPathMilliseconds:F3} ms.");
    }
}
