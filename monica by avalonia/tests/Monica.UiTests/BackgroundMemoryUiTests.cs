using System.Diagnostics;
using System.Reflection;
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
using Monica.App.Features.Vault;
using Monica.App.ViewModels;
using Monica.Core.Models;
using Monica.Data.Repositories;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class BackgroundMemoryUiTests
{
    private const int CollectionBudgetMilliseconds = 2500;

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

            viewModel.SelectSectionCommand.Execute("Cards");
            Dispatcher.UIThread.RunJobs();

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
            Assert.Equal(["Vault"], restoredHost.CreatedSections);
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
        viewModel.VaultSearchText = "Memory";
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
            viewModel.SelectSectionCommand.Execute("Passwords");
            Dispatcher.UIThread.RunJobs();

            window.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();

            await AssertEventuallyCollectedAsync(cacheReferences, cancellationToken);
            Assert.Empty(viewModel.NoteImagePreviewItems);
            Assert.Empty(viewModel.SecuritySummaryItems);
            Assert.Empty(viewModel.SecurityIssueItems);
            Assert.Empty(viewModel.FilteredSecurityIssueItems);
            Assert.Empty(viewModel.VaultTreeRows);
            Assert.Equal(itemCount, viewModel.Passwords.Count);
            Assert.Equal(itemCount, viewModel.TotpItems.Count);
            Assert.Equal(itemCount, viewModel.WalletItems.Count);
            Assert.Equal(itemCount, viewModel.NoteItems.Count);
            Assert.Equal("Memory", viewModel.VaultSearchText);
            Assert.Equal("Memory", viewModel.TotpSearchText);
            Assert.Equal("Memory", viewModel.WalletSearchText);
            Assert.Equal("Memory", viewModel.NoteSearchText);
            Assert.StartsWith("# Unsaved memory draft", viewModel.NoteContent, StringComparison.Ordinal);

            window.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs();

            Assert.NotEmpty(viewModel.VaultTreeRows);
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
    public async Task Library_search_narrows_from_memory_immediately_and_adds_the_database_hit_after_return()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = DispatchProxy.Create<IMonicaRepository, LibraryMetadataSearchProxy>();
        var probe = (LibraryMetadataSearchProxy)(object)repository;
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window, overrides => overrides.AddSingleton(repository));
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        PopulatePasswordSearchSources(viewModel);
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            viewModel.SelectSectionCommand.Execute("Passwords");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["p:1", "p:2"], VaultEntryKeys(viewModel));

            viewModel.VaultSearchText = "Target";

            // The tree narrows on the rows it already holds. Flushing the in-memory coalesce lands that
            // answer deterministically rather than racing its 60 ms timer against the 250 ms repository
            // pass, which is the whole point: a search shows what it can without waiting on MDBX I/O, so
            // the repository must still be untouched at this moment.
            viewModel.FlushVaultTreeRefresh();
            Assert.Equal(["p:1"], VaultEntryKeys(viewModel));
            Assert.Equal(0, probe.MetadataSearchCalls);

            window.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(400, cancellationToken);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(viewModel.VaultTreeRows);
            Assert.Equal(0, probe.MetadataSearchCalls);

            window.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["p:1"], VaultEntryKeys(viewModel));

            // The row that only a custom-field hit can produce arrives with the metadata pass the
            // page asked for as it came back on screen.
            await PumpUntilAsync(
                () => VaultEntryKeys(viewModel).SequenceEqual(["p:1", "p:2"]),
                "the metadata hit to reach the tree",
                cancellationToken);
            Assert.Equal(1, probe.MetadataSearchCalls);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public async Task Library_search_waits_for_the_library_page_before_its_metadata_pass()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = DispatchProxy.Create<IMonicaRepository, LibraryMetadataSearchProxy>();
        var probe = (LibraryMetadataSearchProxy)(object)repository;
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window, overrides => overrides.AddSingleton(repository));
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        PopulatePasswordSearchSources(viewModel);
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            viewModel.SelectSectionCommand.Execute("Generator");
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(viewModel.VaultTreeRows);

            window.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();
            viewModel.VaultSearchText = "Target";
            await Task.Delay(400, cancellationToken);
            window.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(400, cancellationToken);
            Dispatcher.UIThread.RunJobs();

            // A search the user cannot see must not spend a repository scan behind another page's back.
            Assert.Equal(0, probe.MetadataSearchCalls);
            Assert.Empty(viewModel.VaultTreeRows);

            viewModel.SelectSectionCommand.Execute("Passwords");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["p:1"], VaultEntryKeys(viewModel));
            await PumpUntilAsync(
                () => VaultEntryKeys(viewModel).SequenceEqual(["p:1", "p:2"]),
                "the metadata hit to reach the tree",
                cancellationToken);
            Assert.Equal(1, probe.MetadataSearchCalls);
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
        Assert.Equal(["Vault"], host.CreatedSections);
        Assert.NotNull(host.CurrentWorkspace);
        return (new WeakReference(host), new WeakReference(host.CurrentWorkspace));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (IReadOnlyList<WeakReference> References, ProjectionBuildCounts Builds)
        CaptureRebuildableCacheReferences(MainWindowViewModel viewModel)
    {
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
                new WeakReference(totpItems),
                new WeakReference(walletItems),
                new WeakReference(noteTreeGroups),
                new WeakReference(notePreviewMarkdown),
                new WeakReference(previewBitmap),
                new WeakReference(securityIssue)
            ],
            new ProjectionBuildCounts(
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

    // Row order is a sort concern other library tests already cover, so these probes compare the set of
    // entries a search lets through.
    private static List<string> VaultEntryKeys(MainWindowViewModel viewModel) =>
        [.. viewModel.VaultTreeRows.OfType<VaultTreeEntryRow>().Select(row => row.Key).Order()];

    // A coalesced tree rebuild and a debounced repository pass both need real dispatcher time, and a bare
    // sleep would hide which of the two a test is actually waiting for. This is a wait, not a budget:
    // every caller asserts the outcome it is waiting for, so a longer deadline cannot hide a regression,
    // while a desktop that is competing for cores can easily take longer than two seconds to get here.
    private static async Task PumpUntilAsync(
        Func<bool> condition,
        string description,
        CancellationToken cancellationToken,
        int budgetMilliseconds = 10_000)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.ElapsedMilliseconds < budgetMilliseconds)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
            {
                return;
            }

            await Task.Delay(20, cancellationToken);
        }

        Dispatcher.UIThread.RunJobs();
        Assert.True(condition(), $"{description} did not happen within {deadline.ElapsedMilliseconds} ms.");
    }

    // The metadata pass is the only half of a library search that leaves the process, so a test that cares
    // about when the shell may spend it counts these calls instead of watching for a row to appear.
    public class LibraryMetadataSearchProxy : DispatchProxy
    {
        private int _metadataSearchCalls;

        public int MetadataSearchCalls => Volatile.Read(ref _metadataSearchCalls);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            if (targetMethod.Name == nameof(IMonicaRepository.SearchPasswordMetadataAsync))
            {
                Interlocked.Increment(ref _metadataSearchCalls);
                return Task.FromResult(new PasswordMetadataSearchResult([2], []));
            }

            throw new NotSupportedException($"Unexpected repository call: {targetMethod.Name}");
        }
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
        // The navigation selection animation holds a DispatcherTimer for 700 ms after the last selection
        // change, and that timer keeps the just-detached workspace alive until it elapses.
        var deadline = Stopwatch.StartNew();
        var pumps = 0;
        while (deadline.ElapsedMilliseconds < CollectionBudgetMilliseconds)
        {
            pumps++;
            Dispatcher.UIThread.RunJobs();
            ForceFullCollection();
            if (!references.Any(reference => reference.IsAlive))
            {
                return;
            }

            await Task.Delay(50, cancellationToken);
        }

        // Name what survived and how often the loop actually ran: release timing depends on how much
        // of the dispatcher the test managed to pump, so a bare "Assert.False failed" red cannot be
        // diagnosed once the process is gone.
        var survivors = references
            .Where(reference => reference.IsAlive)
            .Select(reference => reference.Target?.GetType().Name ?? "unknown")
            .Distinct()
            .Order()
            .ToArray();
        Assert.True(
            survivors.Length == 0,
            $"{string.Join(", ", survivors)} still live after {deadline.ElapsedMilliseconds} ms and " +
            $"{pumps} dispatcher pumps (budget {CollectionBudgetMilliseconds} ms).");
    }

    private sealed record ProjectionBuildCounts(
        int Totp,
        int Wallet,
        int NoteTree,
        int NotePreview);
}
