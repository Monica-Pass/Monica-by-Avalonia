using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
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
        var host = window.FindControl<WorkspaceHostView>("WorkspaceHost");

        Assert.NotNull(host);
        Assert.Empty(host.CreatedSections);
    }
}
