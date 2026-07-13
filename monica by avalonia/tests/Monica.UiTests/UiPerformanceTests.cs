using System.Diagnostics;
using Avalonia.Controls;
using Monica.App.Controls;
using Monica.App.Features.Notes;
using Monica.App.Features.Passwords;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class UiPerformanceTests
{
    public UiPerformanceTests()
    {
        TestAppBuilder.EnsureInitialized();
    }

    [Fact]
    public void Performance_budget_locked_shell_defers_vault_workspaces()
    {
        var stopwatch = Stopwatch.StartNew();
        var window = new Monica.App.MainWindow();
        stopwatch.Stop();

        var host = window.FindControl<WorkspaceHostView>("WorkspaceHost");

        Assert.NotNull(host);
        Assert.Empty(host.CreatedSections);
        Assert.True(
            stopwatch.ElapsedMilliseconds < 250,
            $"Locked shell construction took {stopwatch.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public void Performance_budget_workspace_host_creates_once_and_reuses_instances()
    {
        var host = new WorkspaceHostView { IsActive = true, Section = "Passwords" };
        var passwordView = Assert.IsType<PasswordVaultView>(host.CurrentWorkspace);

        host.Section = "Notes";
        Assert.IsType<NoteWorkspaceView>(host.CurrentWorkspace);

        host.Section = "Passwords";
        Assert.Same(passwordView, host.CurrentWorkspace);
        Assert.Equal(["Passwords", "Notes"], host.CreatedSections);

        host.IsActive = false;
        Assert.Null(host.CurrentWorkspace);
        Assert.Empty(host.CreatedSections);

        host.IsActive = true;
        Assert.IsType<PasswordVaultView>(host.CurrentWorkspace);
        Assert.NotSame(passwordView, host.CurrentWorkspace);
    }
}
