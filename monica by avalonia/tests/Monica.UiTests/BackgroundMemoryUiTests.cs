using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Controls;
using Monica.App.Features;
using Monica.App.Features.Notes;
using Monica.App.Features.SecurityAnalysis;
using Monica.App.ViewModels;
using Monica.Core.Models;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class BackgroundMemoryUiTests
{
    public BackgroundMemoryUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public async Task Background_memory_minimize_releases_unlocked_shell_and_prepared_editors()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            viewModel.NoteContent = "Unsaved background draft";
            Dispatcher.UIThread.RunJobs();

            foreach (var section in new[] { "Notes", "Totp", "Cards" })
            {
                viewModel.SelectSectionCommand.Execute(section);
                Dispatcher.UIThread.RunJobs();
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

            Assert.Null(shellHost.Content);
            Assert.DoesNotContain(window.GetVisualDescendants(), control => control is WorkspaceHostView);
            await AssertEventuallyCollectedAsync([workspaceHostReference, activeWorkspaceReference], cancellationToken);
            Assert.False(VaultEditorDialogWarmup.IsPasswordWarmed);
            Assert.False(VaultEditorDialogWarmup.IsTotpWarmed);
            Assert.False(VaultEditorDialogWarmup.IsWalletWarmed);
            Assert.True(viewModel.IsUnlocked);
            Assert.Equal("Unsaved background draft", viewModel.NoteContent);

            window.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs();

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

    [Fact]
    public async Task Background_memory_minimize_releases_rebuildable_view_model_caches()
    {
        const int itemCount = 256;
        var cancellationToken = TestContext.Current.CancellationToken;
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        PopulateVaultSources(viewModel, itemCount);
        viewModel.PasswordSearchText = "Memory";
        viewModel.TotpSearchText = "Memory";
        viewModel.WalletSearchText = "Memory";
        viewModel.NoteSearchText = "Memory";
        viewModel.NoteContent = $"# Unsaved memory draft\n\n{new string('x', 256 * 1024)}";
        await Task.Delay(350, cancellationToken);
        Dispatcher.UIThread.RunJobs();

        var (cacheReferences, initialBuilds) = CaptureRebuildableCacheReferences(viewModel);
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            Dispatcher.UIThread.RunJobs();

            window.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();

            await AssertEventuallyCollectedAsync(cacheReferences, cancellationToken);
            Assert.Empty(viewModel.NoteImagePreviewItems);
            Assert.Empty(viewModel.SecuritySummaryItems);
            Assert.Empty(viewModel.SecurityIssueItems);
            Assert.Empty(viewModel.FilteredSecurityIssueItems);
            Assert.Equal(itemCount, viewModel.Passwords.Count);
            Assert.Equal(itemCount, viewModel.TotpItems.Count);
            Assert.Equal(itemCount, viewModel.WalletItems.Count);
            Assert.Equal(itemCount, viewModel.NoteItems.Count);
            Assert.Equal("Memory", viewModel.PasswordSearchText);
            Assert.Equal("Memory", viewModel.TotpSearchText);
            Assert.Equal("Memory", viewModel.WalletSearchText);
            Assert.Equal("Memory", viewModel.NoteSearchText);
            Assert.StartsWith("# Unsaved memory draft", viewModel.NoteContent, StringComparison.Ordinal);

            window.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs();

            Assert.True(viewModel.FilteredPasswordsProjectionBuildCount > initialBuilds.Passwords);
            Assert.Equal(initialBuilds.Totp, viewModel.FilteredTotpProjectionBuildCount);
            Assert.Equal(initialBuilds.Wallet, viewModel.FilteredWalletProjectionBuildCount);
            Assert.Equal(initialBuilds.NoteTree, viewModel.FilteredNoteProjectionBuildCount);
            Assert.Equal(initialBuilds.NotePreview, viewModel.NotePreviewProjectionBuildCount);

            Assert.Equal(itemCount, viewModel.FilteredTotpItems.Count);
            Assert.Equal(itemCount, viewModel.FilteredWalletItems.Count);
            Assert.Equal(itemCount, viewModel.FilteredNoteItems.Count);
            Assert.StartsWith("# Unsaved memory draft", viewModel.NotePreviewMarkdown, StringComparison.Ordinal);
            Assert.True(viewModel.FilteredTotpProjectionBuildCount > initialBuilds.Totp);
            Assert.True(viewModel.FilteredWalletProjectionBuildCount > initialBuilds.Wallet);
            Assert.True(viewModel.FilteredNoteProjectionBuildCount > initialBuilds.NoteTree);
            Assert.True(viewModel.NotePreviewProjectionBuildCount > initialBuilds.NotePreview);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public async Task Background_password_search_stays_suspended_until_passwords_restore()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        PopulatePasswordSearchSources(viewModel);
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
            var initialBuilds = viewModel.FilteredPasswordsProjectionBuildCount;

            viewModel.PasswordSearchText = "Target";
            window.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(350, cancellationToken);
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(viewModel.PasswordSearchQuery);
            Assert.Equal(initialBuilds, viewModel.FilteredPasswordsProjectionBuildCount);

            window.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

            Assert.Equal("Target", viewModel.PasswordSearchQuery);
            Assert.Equal(["Target account"], viewModel.FilteredPasswords.Select(item => item.Title));
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public async Task Background_password_search_waits_for_inactive_password_workspace()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        PopulatePasswordSearchSources(viewModel);
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
            var initialBuilds = viewModel.FilteredPasswordsProjectionBuildCount;

            viewModel.SelectSectionCommand.Execute("Cards");
            window.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();
            viewModel.PasswordSearchText = "Target";
            await Task.Delay(350, cancellationToken);
            Dispatcher.UIThread.RunJobs();

            window.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

            Assert.Empty(viewModel.PasswordSearchQuery);
            Assert.Equal(initialBuilds, viewModel.FilteredPasswordsProjectionBuildCount);

            viewModel.SelectSectionCommand.Execute("Passwords");
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

            Assert.Equal("Target", viewModel.PasswordSearchQuery);
            Assert.Equal(["Target account"], viewModel.FilteredPasswords.Select(item => item.Title));
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (IReadOnlyList<WeakReference> References, ProjectionBuildCounts Builds)
        CaptureRebuildableCacheReferences(MainWindowViewModel viewModel)
    {
        var passwordRows = viewModel.FilteredPasswordRows;
        var totpItems = viewModel.FilteredTotpItems;
        var walletItems = viewModel.FilteredWalletItems;
        var noteTreeGroups = viewModel.NoteTreeGroups;
        var notePreviewMarkdown = viewModel.NotePreviewMarkdown;
        var previewBitmap = new WriteableBitmap(
            new PixelSize(1024, 1024),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        viewModel.NoteImagePreviewItems.Add(new NoteImagePreviewItem(
            "memory-preview.png",
            "Memory preview",
            "4 MB",
            previewBitmap));
        var securityIssue = new SecurityIssueItem(
            "Memory issue",
            "Rebuildable details",
            "Memory",
            "Medium",
            SecurityIssueSeverityLevel.Medium,
            viewModel.Passwords[0].Id,
            viewModel.Passwords[0],
            50);
        viewModel.SecuritySummaryItems.Add(new SecuritySummaryItem("Memory", "1", "Rebuildable"));
        viewModel.SecurityIssueItems.Add(securityIssue);
        viewModel.FilteredSecurityIssueItems.Add(securityIssue);

        return (
            [
                new WeakReference(passwordRows),
                new WeakReference(totpItems),
                new WeakReference(walletItems),
                new WeakReference(noteTreeGroups),
                new WeakReference(notePreviewMarkdown),
                new WeakReference(previewBitmap),
                new WeakReference(securityIssue)
            ],
            new ProjectionBuildCounts(
                viewModel.FilteredPasswordsProjectionBuildCount,
                viewModel.FilteredTotpProjectionBuildCount,
                viewModel.FilteredWalletProjectionBuildCount,
                viewModel.FilteredNoteProjectionBuildCount,
                viewModel.NotePreviewProjectionBuildCount));
    }

    private static void PopulateVaultSources(MainWindowViewModel viewModel, int itemCount)
    {
        for (var index = 0; index < itemCount; index++)
        {
            var id = index + 1;
            viewModel.Passwords.Add(new PasswordEntry
            {
                Id = id,
                Title = $"Memory account {id}",
                Username = $"user-{id}"
            });
            viewModel.TotpItems.Add(new SecureItem
            {
                Id = id,
                ItemType = VaultItemType.Totp,
                Title = $"Memory TOTP {id}"
            });
            viewModel.WalletItems.Add(new SecureItem
            {
                Id = id,
                ItemType = VaultItemType.BankCard,
                Title = $"Memory card {id}"
            });
            viewModel.NoteItems.Add(new SecureItem
            {
                Id = id,
                ItemType = VaultItemType.Note,
                Title = $"Memory note {id}"
            });
        }
    }

    private static void PopulatePasswordSearchSources(MainWindowViewModel viewModel)
    {
        viewModel.Passwords.Add(new PasswordEntry
        {
            Id = 1,
            Title = "Target account"
        });
        viewModel.Passwords.Add(new PasswordEntry
        {
            Id = 2,
            Title = "Other account"
        });
    }

    private static void ForceFullCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static async Task AssertEventuallyCollectedAsync(
        IReadOnlyList<WeakReference> references,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5 && references.Any(reference => reference.IsAlive); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            ForceFullCollection();
            if (references.Any(reference => reference.IsAlive))
            {
                await Task.Delay(20, cancellationToken);
            }
        }

        Assert.All(references, reference => Assert.False(reference.IsAlive));
    }

    private sealed record ProjectionBuildCounts(
        int Passwords,
        int Totp,
        int Wallet,
        int NoteTree,
        int NotePreview);
}
