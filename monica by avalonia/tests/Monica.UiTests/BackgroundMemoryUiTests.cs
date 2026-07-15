using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Controls;
using Monica.App.Features;
using Monica.App.ViewModels;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class BackgroundMemoryUiTests
{
    public BackgroundMemoryUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Background_memory_minimize_releases_unlocked_shell_and_prepared_editors()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            viewModel.NoteContent = "Unsaved background draft";
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

            foreach (var section in new[] { "Notes", "Totp", "Cards" })
            {
                viewModel.SelectSectionCommand.Execute(section);
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
            }

            VaultEditorDialogWarmup.EnsurePasswordWarmed();
            VaultEditorDialogWarmup.EnsureTotpWarmed();
            VaultEditorDialogWarmup.EnsureWalletWarmed();
            Assert.True(VaultEditorDialogWarmup.IsPasswordWarmed);
            Assert.True(VaultEditorDialogWarmup.IsTotpWarmed);
            Assert.True(VaultEditorDialogWarmup.IsWalletWarmed);

            var shellHost = window.FindControl<ContentControl>("UnlockedShellHost");
            Assert.NotNull(shellHost);
            var (workspaceHostReference, activeWorkspaceReference) =
                CaptureWorkspaceReferences(window);

            window.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();
            ForceFullCollection();

            Assert.Null(shellHost.Content);
            Assert.DoesNotContain(window.GetVisualDescendants(), control => control is WorkspaceHostView);
            Assert.False(workspaceHostReference.IsAlive);
            Assert.False(activeWorkspaceReference.IsAlive);
            Assert.False(VaultEditorDialogWarmup.IsPasswordWarmed);
            Assert.False(VaultEditorDialogWarmup.IsTotpWarmed);
            Assert.False(VaultEditorDialogWarmup.IsWalletWarmed);
            Assert.True(viewModel.IsUnlocked);
            Assert.Equal("Unsaved background draft", viewModel.NoteContent);

            window.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

            Assert.Same(viewModel, shellHost.Content);
            var restoredHost = Assert.Single(window.GetVisualDescendants().OfType<WorkspaceHostView>());
            Assert.Equal(["Cards"], restoredHost.CreatedSections);
            Assert.False(VaultEditorDialogWarmup.IsPasswordWarmed);
            Assert.False(VaultEditorDialogWarmup.IsTotpWarmed);
            Assert.True(VaultEditorDialogWarmup.IsWalletWarmed);
            Assert.True(viewModel.IsUnlocked);
            Assert.Equal("Unsaved background draft", viewModel.NoteContent);
        }
        finally
        {
            window.Close();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Host, WeakReference Workspace) CaptureWorkspaceReferences(
        Monica.App.MainWindow window)
    {
        var host = Assert.Single(window.GetVisualDescendants().OfType<WorkspaceHostView>());
        Assert.Equal(["Passwords", "Notes", "Totp", "Cards"], host.CreatedSections);
        Assert.NotNull(host.CurrentWorkspace);
        return (new WeakReference(host), new WeakReference(host.CurrentWorkspace));
    }

    private static void ForceFullCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
