using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Controls;
using Monica.App.Features.Archive;
using Monica.App.Features.Authenticator;
using Monica.App.Features.DatabaseManagement;
using Monica.App.Features.Generator;
using Monica.App.Features.Mdbx;
using Monica.App.Features.Notes;
using Monica.App.Features.Passwords;
using Monica.App.Features.RecycleBin;
using Monica.App.Features.SecurityAnalysis;
using Monica.App.Features.Settings;
using Monica.App.Features.Sync;
using Monica.App.Features.Timeline;
using Monica.App.Features.Unlock;
using Monica.App.Features.Wallet;

namespace Monica.UiTests;

public static class AvaloniaUiThreadTestContext
{
    public static void VerifyAccess() => Dispatcher.UIThread.VerifyAccess();
}

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class UiArchitectureTests
{
    public UiArchitectureTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Feature_workspaces_load_in_headless_ui()
    {
        UserControl[] workspaces =
        [
            new UnlockView(),
            new PasswordVaultView(),
            new NoteWorkspaceView(),
            new AuthenticatorWorkspaceView(),
            new WalletWorkspaceView(),
            new GeneratorWorkspaceView(),
            new ArchiveWorkspaceView(),
            new RecycleBinWorkspaceView(),
            new SettingsWorkspaceView(),
            new SyncWorkspaceView(),
            new MdbxWorkspaceView(),
            new TimelineWorkspaceView(),
            new DatabaseManagementWorkspaceView(),
            new SecurityAnalysisWorkspaceView()
        ];

        Assert.All(workspaces, workspace => Assert.NotNull(workspace.Content));
    }

    [Fact]
    public void Main_window_is_composed_from_feature_hosts()
    {
        var window = new Monica.App.MainWindow();
        var shellHost = window.FindControl<ContentControl>("UnlockedShellHost");

        Assert.NotNull(shellHost);
        Assert.Null(shellHost.Content);
        Assert.DoesNotContain(window.GetVisualDescendants(), control => control is WorkspaceHostView);
    }

    [Fact]
    public void Unlocked_navigation_shell_is_materialized_on_demand()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<Monica.App.ViewModels.MainWindowViewModel>();
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            Dispatcher.UIThread.RunJobs();

            var shellHost = window.FindControl<ContentControl>("UnlockedShellHost");
            Assert.NotNull(shellHost);
            Assert.Same(viewModel, shellHost.Content);
            var workspaceHost = Assert.Single(window.GetVisualDescendants().OfType<WorkspaceHostView>());
            Assert.IsType<PasswordVaultView>(workspaceHost.CurrentWorkspace);
            Assert.Equal(["Passwords"], workspaceHost.CreatedSections);

            viewModel.IsUnlocked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Null(shellHost.Content);
            Assert.DoesNotContain(window.GetVisualDescendants(), control => control is WorkspaceHostView);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Locked_shell_does_not_eagerly_load_feature_style_dictionaries()
    {
        var window = new Monica.App.MainWindow();

        Assert.DoesNotContain(window.Styles, style => style is Avalonia.Styling.Styles);
    }

    [Fact]
    public void Feature_workspaces_own_their_style_dictionaries()
    {
        UserControl[] workspaces =
        [
            new PasswordVaultView(),
            new NoteWorkspaceView(),
            new AuthenticatorWorkspaceView(),
            new WalletWorkspaceView(),
            new GeneratorWorkspaceView(),
            new ArchiveWorkspaceView(),
            new RecycleBinWorkspaceView(),
            new SettingsWorkspaceView(),
            new SyncWorkspaceView(),
            new MdbxWorkspaceView(),
            new TimelineWorkspaceView(),
            new DatabaseManagementWorkspaceView()
        ];

        Assert.All(
            workspaces,
            workspace => Assert.Contains(workspace.Styles, style => style is Avalonia.Styling.Styles));
    }
}
