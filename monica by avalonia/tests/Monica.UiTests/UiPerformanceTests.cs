using System.Diagnostics;
using System.Collections.Specialized;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Controls;
using Monica.App.Features.Generator;
using Monica.App.Features.Notes;
using Monica.App.Features.Passwords;
using Monica.App.Features.Vault;
using Monica.App.ViewModels;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data.Repositories;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class UiPerformanceTests
{
    public UiPerformanceTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Performance_budget_warm_locked_shell_defers_vault_workspaces()
    {
        var warmupWindow = new Monica.App.MainWindow();
        var warmupShellHost = warmupWindow.FindControl<ContentControl>("UnlockedShellHost");

        Assert.NotNull(warmupShellHost);
        Assert.Null(warmupShellHost.Content);
        Assert.DoesNotContain(warmupWindow.GetVisualDescendants(), control => control is WorkspaceHostView);

        var stopwatch = Stopwatch.StartNew();
        var window = new Monica.App.MainWindow();
        stopwatch.Stop();

        var shellHost = window.FindControl<ContentControl>("UnlockedShellHost");

        Assert.NotNull(shellHost);
        Assert.Null(shellHost.Content);
        Assert.DoesNotContain(window.GetVisualDescendants(), control => control is WorkspaceHostView);
    }

    // Wall-clock budget: run it where the cores are not shared with everything else the
    // developer has open. Locally: -filter "/[Category!=perf-budget]".
    [Fact]
    [Trait("Category", "perf-budget")]
    public void Performance_budget_warm_locked_shell_construction_stays_within_budget()
    {
        _ = new Monica.App.MainWindow();

        var stopwatch = Stopwatch.StartNew();
        var window = new Monica.App.MainWindow();
        stopwatch.Stop();

        Assert.True(
            stopwatch.ElapsedMilliseconds < 250,
            $"Locked shell construction took {stopwatch.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public void Performance_budget_workspace_host_creates_once_and_reuses_instances()
    {
        var host = new WorkspaceHostView { IsActive = true, Section = "Passwords" };
        Dispatcher.UIThread.RunJobs(DispatcherPriority.ContextIdle);
        var libraryView = Assert.IsType<VaultWorkspaceView>(host.CurrentWorkspace);

        host.Section = "Notes";
        Dispatcher.UIThread.RunJobs(DispatcherPriority.ContextIdle);
        Assert.Same(libraryView, host.CurrentWorkspace);

        host.Section = "Generator";
        Dispatcher.UIThread.RunJobs(DispatcherPriority.ContextIdle);
        Assert.IsType<GeneratorWorkspaceView>(host.CurrentWorkspace);

        host.Section = "Vault";
        Dispatcher.UIThread.RunJobs(DispatcherPriority.ContextIdle);
        Assert.Same(libraryView, host.CurrentWorkspace);
        Assert.Equal(["Vault", "Generator"], host.CreatedSections);

        host.IsActive = false;
        Assert.Null(host.CurrentWorkspace);
        Assert.Empty(host.CreatedSections);

        host.IsActive = true;
        Dispatcher.UIThread.RunJobs(DispatcherPriority.ContextIdle);
        Assert.IsType<VaultWorkspaceView>(host.CurrentWorkspace);
        Assert.NotSame(libraryView, host.CurrentWorkspace);
    }

    [Fact]
    public void Vault_password_and_totp_preparation_keep_the_ui_dispatcher_responsive()
    {
        var repository = DispatchProxy.Create<IMonicaRepository, VaultLoadRepositoryProxy>();
        var repositoryProbe = (VaultLoadRepositoryProxy)(object)repository;
        var totpService = new DispatcherHeartbeatTotpService();
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window, overrides =>
        {
            overrides.AddSingleton(repository);
            overrides.AddSingleton<ITotpService>(totpService);
        });
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        RunVaultLoad(viewModel);
        Assert.Equal([true, true], totpService.HeartbeatObservations);
        var displayed = Assert.Single(viewModel.Passwords);
        Assert.NotSame(repositoryProbe.Password, displayed);
        Assert.Equal("------", repositoryProbe.Password.TotpCode);
        Assert.Equal("654321", displayed.TotpCode);
        var displayedTotp = Assert.Single(viewModel.TotpItems);
        Assert.NotSame(repositoryProbe.Totp, displayedTotp);
        Assert.Equal("------", repositoryProbe.Totp.TotpCode);
        Assert.Equal("654321", displayedTotp.TotpCode);
    }

    [Fact]
    public void Library_rematerializes_a_large_vault_with_one_reset()
    {
        RematerializeLargeVault();
    }

    // Wall-clock budget: run it where the cores are not shared with everything else the
    // developer has open. Locally: -filter "/[Category!=perf-budget]".
    [Fact]
    [Trait("Category", "perf-budget")]
    public void Performance_budget_library_rematerialization_stays_within_budget()
    {
        const int ceilingMs = 400;
        var (fastestMs, materialized) = RematerializeLargeVault();

        Assert.True(
            fastestMs < ceilingMs,
            $"Re-materializing {materialized} library rows took {fastestMs} ms at best.");
    }

    // Clearing filters is the widest rebuild the library can be asked for: every row is rebuilt and
    // handed to the bound tree at once. Warm on this harness the builder costs ~5ms per 5,000 entries
    // and the view-model publish 10-40ms; the rest is the dispatcher pass that lets the tree take the
    // rows. Handing the tree a fresh collection measured 197-224ms for that pass against 241-295ms
    // for refilling the bound one row by row - bands that overlap this harness's noise, which is why
    // the one-reset contract is asserted structurally here and only the ceiling is a budget.
    // Best of three rounds decides the verdict, because any single round can be inflated by an
    // unrelated collection.
    private static (long FastestMs, int Materialized) RematerializeLargeVault()
    {
        const int passwordCount = 5_000;
        var repository = DispatchProxy.Create<IMonicaRepository, VaultLoadRepositoryProxy>();
        var probe = (VaultLoadRepositoryProxy)(object)repository;
        probe.PasswordItems = Enumerable.Range(1, passwordCount)
            .Select(id => new PasswordEntry { Id = id, Title = $"Password {id:D5}" })
            .ToArray();
        var window = new Monica.App.MainWindow { Width = 1280, Height = 820 };
        using var services = Monica.App.App.ConfigureServices(window, overrides =>
            overrides.AddSingleton(repository));
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            viewModel.SelectSectionCommand.Execute(VaultPresets.LibrarySection);
            RunVaultLoad(viewModel);
            Dispatcher.UIThread.RunJobs();

            var materialized = viewModel.VaultTreeRows.Count;
            Assert.True(
                materialized >= passwordCount,
                $"Expected the library to show {passwordCount} entries, but it published {materialized}.");

            long fastestMs = long.MaxValue;
            for (var round = 0; round < 3; round++)
            {
                CollapseVaultFilters(viewModel);
                var previousPublication = viewModel.VaultTreeRows;
                var pass = Stopwatch.StartNew();
                viewModel.ClearVaultFiltersCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                pass.Stop();
                Assert.Equal(materialized, viewModel.VaultTreeRows.Count);
                // A rebuild that refills the bound collection costs the tree one collection-changed event
                // per row while the previous rows are still in place; publishing a fresh collection costs
                // it one reset.
                Assert.True(
                    !ReferenceEquals(previousPublication, viewModel.VaultTreeRows),
                    "The library rebuilt its rows in place instead of handing the tree one reset.");
                fastestMs = Math.Min(fastestMs, pass.ElapsedMilliseconds);
            }

            // The rebuild hands the tree a new collection rather than refilling the old one, so a binding
            // that stopped following it would keep showing stale rows while the numbers looked healthy.
            Assert.Contains(
                window.GetVisualDescendants().OfType<ListBox>(),
                list => ReferenceEquals(list.ItemsSource, viewModel.VaultTreeRows));
            return (fastestMs, materialized);
        }
        finally
        {
            window.Close();
        }
    }

    private static void CollapseVaultFilters(MainWindowViewModel viewModel)
    {
        viewModel.VaultFavoritesOnly = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(viewModel.VaultTreeRows);
    }

    [Fact]
    public void Password_detail_source_snapshot_keeps_only_relevant_vault_references()
    {
        const int unrelatedPasswordCount = 10_000;
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var category = new Category { Id = 41, Name = "Relevant category" };
        var boundNote = new SecureItem
        {
            Id = 42,
            ItemType = VaultItemType.Note,
            Title = "Relevant note"
        };
        viewModel.Categories.Add(category);
        viewModel.NoteItems.Add(boundNote);
        for (var id = 1; id <= unrelatedPasswordCount; id++)
        {
            viewModel.Passwords.Add(new PasswordEntry
            {
                Id = id,
                Title = $"Unrelated password {id:D5}",
                ReplicaGroupId = $"unrelated-{id}"
            });
        }

        const string replicaGroupId = "detail-snapshot-group";
        var selected = new PasswordEntry
        {
            Id = unrelatedPasswordCount + 1,
            Title = "Selected password",
            ReplicaGroupId = replicaGroupId,
            CategoryId = category.Id,
            BoundNoteId = boundNote.Id
        };
        var sibling = new PasswordEntry
        {
            Id = unrelatedPasswordCount + 2,
            Title = "Selected password replica",
            ReplicaGroupId = replicaGroupId
        };
        viewModel.Passwords.Add(selected);
        viewModel.Passwords.Add(sibling);

        var buildSnapshot = typeof(MainWindowViewModel).GetMethod(
            "BuildPasswordDetailSourceSnapshot",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var snapshot = Assert.IsType<PasswordDetailSourceSnapshot>(
            buildSnapshot?.Invoke(viewModel, [selected]));

        Assert.Equal([selected.Id, sibling.Id], snapshot.Siblings.Select(item => item.Id));
        Assert.Same(category, snapshot.Category);
        Assert.Same(boundNote, snapshot.BoundNote);
    }

    [Fact]
    public void Secondary_list_projection_builds_once_per_filter_invalidation()
    {
        const int itemCount = 5000;
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var totpItemData = TotpDataResolver.ToItemData(
            new TotpData("JBSWY3DPEHPK3PXP", "Monica", "desktop@example.com"));

        for (var id = 1; id <= itemCount; id++)
        {
            var title = id % 2 == 0 ? $"Target item {id}" : $"Other item {id}";
            viewModel.TotpItems.Add(new SecureItem
            {
                Id = id,
                ItemType = VaultItemType.Totp,
                Title = title,
                ItemData = totpItemData
            });
            viewModel.WalletItems.Add(new SecureItem
            {
                Id = itemCount + id,
                ItemType = VaultItemType.BankCard,
                Title = title
            });
        }

        viewModel.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(MainWindowViewModel.FilteredTotpItems):
                    _ = viewModel.FilteredTotpItems.Count;
                    break;
                case nameof(MainWindowViewModel.HasFilteredTotpItems):
                    _ = viewModel.HasFilteredTotpItems;
                    break;
                case nameof(MainWindowViewModel.TotpFilteredStatusText):
                    _ = viewModel.TotpFilteredStatusText;
                    break;
                case nameof(MainWindowViewModel.FilteredWalletItems):
                    _ = viewModel.FilteredWalletItems.Count;
                    break;
                case nameof(MainWindowViewModel.HasFilteredWalletItems):
                    _ = viewModel.HasFilteredWalletItems;
                    break;
                case nameof(MainWindowViewModel.WalletFilteredStatusText):
                    _ = viewModel.WalletFilteredStatusText;
                    break;
            }
        };

        viewModel.TotpSearchText = "Target";
        viewModel.WalletSearchText = "Target";

        Assert.True(
            viewModel.FilteredTotpProjectionBuildCount == 1 &&
            viewModel.FilteredWalletProjectionBuildCount == 1,
            $"Expected one secondary projection build per invalidation, but observed " +
            $"TOTP={viewModel.FilteredTotpProjectionBuildCount} and " +
            $"Wallet={viewModel.FilteredWalletProjectionBuildCount}.");
        Assert.Equal(itemCount / 2, viewModel.FilteredTotpItems.Count);
        Assert.Equal(itemCount / 2, viewModel.FilteredWalletItems.Count);
    }

    [Fact]
    public void Note_tree_projection_builds_once_and_decodes_each_payload_once()
    {
        const int itemCount = 5000;
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var payload = NoteContentCodec.BuildSavePayload(
            "Performance note",
            "# Recovery\n\nDesktop note content",
            "performance, private",
            isMarkdown: true);

        for (var id = 1; id <= itemCount; id++)
        {
            viewModel.NoteItems.Add(new SecureItem
            {
                Id = id,
                ItemType = VaultItemType.Note,
                Title = id % 2 == 0 ? $"Target note {id}" : $"Other note {id}",
                Notes = payload.NotesCache,
                ItemData = payload.ItemData
            });
        }

        viewModel.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(MainWindowViewModel.FavoriteNoteItems):
                    _ = viewModel.FavoriteNoteItems.Count;
                    break;
                case nameof(MainWindowViewModel.FilteredNoteItems):
                    _ = viewModel.FilteredNoteItems.Count;
                    break;
                case nameof(MainWindowViewModel.NoteTreeGroups):
                    _ = viewModel.NoteTreeGroups.Count;
                    break;
                case nameof(MainWindowViewModel.FavoriteNoteCount):
                    _ = viewModel.FavoriteNoteCount;
                    break;
                case nameof(MainWindowViewModel.HasFavoriteNoteItems):
                    _ = viewModel.HasFavoriteNoteItems;
                    break;
                case nameof(MainWindowViewModel.HasFilteredNoteItems):
                    _ = viewModel.HasFilteredNoteItems;
                    break;
                case nameof(MainWindowViewModel.HasNoteTreeGroups):
                    _ = viewModel.HasNoteTreeGroups;
                    break;
                case nameof(MainWindowViewModel.ShowClearNoteSearchInEmptyTree):
                    _ = viewModel.ShowClearNoteSearchInEmptyTree;
                    break;
                case nameof(MainWindowViewModel.NoteTreeEmptyText):
                    _ = viewModel.NoteTreeEmptyText;
                    break;
                case nameof(MainWindowViewModel.NoteTreeStatusText):
                    _ = viewModel.NoteTreeStatusText;
                    break;
            }
        };

        viewModel.NoteSearchText = "Target";

        Assert.True(
            viewModel.FilteredNoteProjectionBuildCount == 1 &&
            viewModel.NoteTreeGroupProjectionBuildCount == 1 &&
            viewModel.NotePayloadDecodeCount == itemCount,
            $"Expected one note projection build and one decode per item, but observed " +
            $"filtered={viewModel.FilteredNoteProjectionBuildCount}, " +
            $"groups={viewModel.NoteTreeGroupProjectionBuildCount}, " +
            $"decodes={viewModel.NotePayloadDecodeCount}.");
        Assert.Equal(itemCount / 2, viewModel.FilteredNoteItems.Count);
        Assert.Equal(2, viewModel.NoteTreeGroups.Count);
        Assert.All(viewModel.NoteTreeGroups, group => Assert.Equal(itemCount / 2, group.Count));
    }

    private static void RunVaultLoad(MainWindowViewModel viewModel)
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await viewModel.LoadAsync();
                completion.TrySetResult(null);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });

        var timeout = Stopwatch.StartNew();
        while (!completion.Task.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }

        Assert.True(completion.Task.IsCompleted, "Vault load did not finish before the responsiveness timeout.");
        Assert.True(completion.Task.IsCompletedSuccessfully, completion.Task.Exception?.ToString());
    }

    public class VaultLoadRepositoryProxy : DispatchProxy
    {
        public PasswordEntry Password { get; } = new()
        {
            Id = 1,
            Title = "Responsive TOTP",
            Username = "desktop",
            AuthenticatorKey = "otpauth://totp/Monica:desktop?secret=JBSWY3DPEHPK3PXP&issuer=Monica"
        };

        public SecureItem Totp { get; } = new()
        {
            Id = 2,
            ItemType = VaultItemType.Totp,
            Title = "Responsive TOTP",
            BoundPasswordId = 1,
            ItemData = TotpDataResolver.ToItemData(
                TotpDataResolver.FromAuthenticatorKey("JBSWY3DPEHPK3PXP", "Responsive TOTP", "desktop")!)
        };

        public IReadOnlyList<PasswordEntry>? PasswordItems { get; set; }

        public IReadOnlyList<Category> Categories { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            return targetMethod.Name switch
            {
                nameof(IMonicaRepository.GetPasswordsAsync) => Task.FromResult<IReadOnlyList<PasswordEntry>>(
                    PasswordItems ?? [Password]),
                nameof(IMonicaRepository.GetCustomFieldsByEntryIdsAsync) =>
                    Task.FromResult<IReadOnlyDictionary<long, IReadOnlyList<CustomField>>>(
                        new Dictionary<long, IReadOnlyList<CustomField>>()),
                nameof(IMonicaRepository.GetAttachmentsByOwnerIdsAsync) =>
                    Task.FromResult<IReadOnlyDictionary<long, IReadOnlyList<Attachment>>>(
                        new Dictionary<long, IReadOnlyList<Attachment>>()),
                nameof(IMonicaRepository.GetAttachmentOwnerIdsAsync) =>
                    Task.FromResult<IReadOnlyList<long>>([]),
                nameof(IMonicaRepository.GetSecureItemsAsync) =>
                    Task.FromResult<IReadOnlyList<SecureItem>>([Totp]),
                nameof(IMonicaRepository.GetCategoriesAsync) =>
                    Task.FromResult(Categories),
                nameof(IMonicaRepository.GetPasswordQuickAccessRecordsAsync) =>
                    Task.FromResult<IReadOnlyList<PasswordQuickAccessRecord>>([]),
                nameof(IMonicaRepository.GetMdbxDatabasesAsync) =>
                    Task.FromResult<IReadOnlyList<LocalMdbxDatabase>>([]),
                nameof(IMonicaRepository.GetOperationLogsAsync) =>
                    Task.FromResult<IReadOnlyList<OperationLog>>([]),
                _ => throw new NotSupportedException($"Unexpected repository call: {targetMethod.Name}")
            };
        }
    }

    private sealed class DispatcherHeartbeatTotpService : ITotpService
    {
        private readonly object _gate = new();
        private readonly List<bool> _heartbeatObservations = [];

        public IReadOnlyList<bool> HeartbeatObservations
        {
            get
            {
                lock (_gate)
                {
                    return [.. _heartbeatObservations];
                }
            }
        }

        public string GenerateCode(
            string secretKey,
            int period = 30,
            int digits = 6,
            string otpType = "TOTP",
            long counter = 0)
        {
            var heartbeat = new ManualResetEventSlim();
            Dispatcher.UIThread.Post(heartbeat.Set, DispatcherPriority.Background);
            var observed = heartbeat.Wait(TimeSpan.FromMilliseconds(300));
            lock (_gate)
            {
                _heartbeatObservations.Add(observed);
            }

            return "654321";
        }

        public int GetRemainingSeconds(int period = 30, DateTimeOffset? now = null) => 15;

        public double GetProgress(int period = 30, DateTimeOffset? now = null) => 50;
    }
}
