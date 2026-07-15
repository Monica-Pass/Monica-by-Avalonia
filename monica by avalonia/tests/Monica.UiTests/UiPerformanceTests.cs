using System.Diagnostics;
using System.Collections.Specialized;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Controls;
using Monica.App.Features.Notes;
using Monica.App.Features.Passwords;
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
        TestAppBuilder.EnsureInitialized();
    }

    [Fact]
    public void Performance_budget_warm_locked_shell_defers_vault_workspaces()
    {
        var warmupWindow = new Monica.App.MainWindow();
        var warmupHost = warmupWindow.FindControl<WorkspaceHostView>("WorkspaceHost");

        Assert.NotNull(warmupHost);
        Assert.Empty(warmupHost.CreatedSections);

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
    public void Vault_password_projection_and_folder_publication_are_coalesced_during_load()
    {
        const int categoryCount = 500;
        const int passwordCount = 5000;
        var repository = DispatchProxy.Create<IMonicaRepository, VaultLoadRepositoryProxy>();
        var probe = (VaultLoadRepositoryProxy)(object)repository;
        probe.Categories = Enumerable.Range(1, categoryCount)
            .Select(id => new Category { Id = id, Name = $"Folder {id:D4}", SortOrder = id })
            .ToArray();
        probe.PasswordItems = Enumerable.Range(1, passwordCount)
            .Select(id => new PasswordEntry
            {
                Id = id,
                Title = $"Password {id:D5}",
                CategoryId = (id % categoryCount) + 1
            })
            .ToArray();
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window, overrides =>
            overrides.AddSingleton(repository));
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var collectionChanges = new List<NotifyCollectionChangedAction>();
        var filteredPasswordsNotifications = 0;
        var filteredPasswordRowsNotifications = 0;
        viewModel.PasswordFolderFilters.CollectionChanged += (_, args) => collectionChanges.Add(args.Action);
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.FilteredPasswords))
            {
                filteredPasswordsNotifications++;
                _ = viewModel.FilteredPasswords.Count;
            }

            if (args.PropertyName == nameof(MainWindowViewModel.FilteredPasswordRows))
            {
                filteredPasswordRowsNotifications++;
                _ = viewModel.FilteredPasswordRows.Count;
            }
        };

        RunVaultLoad(viewModel);

        Assert.Equal([NotifyCollectionChangedAction.Reset], collectionChanges);
        Assert.True(
            filteredPasswordsNotifications == 1 && filteredPasswordRowsNotifications == 1,
            $"Expected one final password projection publication, but observed " +
            $"FilteredPasswords={filteredPasswordsNotifications} and " +
            $"FilteredPasswordRows={filteredPasswordRowsNotifications}.");
        Assert.True(viewModel.LastVaultLoadDurationMilliseconds > 0);
        Assert.Equal(1, viewModel.FilteredPasswordsProjectionBuildCount);
        Assert.Equal(passwordCount, viewModel.FilteredPasswords.Count);
        Assert.Equal(passwordCount, viewModel.FilteredPasswordRows.Count);
        Assert.Equal(categoryCount + 3, viewModel.PasswordFolderFilters.Count);
        Assert.Equal(categoryCount, viewModel.RegularPasswordFolderFilters.Count());
        Assert.All(
            viewModel.RegularPasswordFolderFilters,
            folder => Assert.Equal(passwordCount / categoryCount, folder.Count));
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
