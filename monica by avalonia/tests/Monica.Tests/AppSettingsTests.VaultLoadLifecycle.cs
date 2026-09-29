using System.Reflection;
using Monica.Core.Bitwarden;
using Monica.Core.Models;
using Monica.Data.Bitwarden;
using Monica.Data.Repositories;

namespace Monica.Tests;

public sealed partial class AppSettingsTests
{
    [Theory]
    [InlineData("VaultLoadProjectingPasswords")]
    [InlineData("VaultLoadSecureItems")]
    [InlineData("VaultLoadSources")]
    [InlineData("VaultLoadAuthenticators")]
    public async Task Vault_load_does_not_repopulate_after_lock_during_projection(string stage)
    {
        var repository = DispatchProxy.Create<IMonicaRepository, SessionVaultRepositoryProxy>();
        var viewModel = CreateViewModel(GetTempPath(), repository: repository);
        viewModel.IsUnlocked = true;
        var locked = false;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (!locked && args.PropertyName == nameof(viewModel.VaultLoadStageText) &&
                viewModel.VaultLoadStageText == viewModel.L.Get(stage))
            {
                locked = true;
                viewModel.IsUnlocked = false;
            }
        };

        await viewModel.LoadAsync();

        Assert.True(locked);
        Assert.False(viewModel.IsUnlocked);
        Assert.Empty(viewModel.Passwords);
        Assert.Empty(viewModel.NoteItems);
        Assert.Empty(viewModel.Categories);
        Assert.Empty(viewModel.VaultLoadStageText);
        Assert.False(viewModel.IsLoadingVault);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("cancel")]
    [InlineData("failure")]
    public async Task Old_vault_load_completion_cannot_clear_or_lock_a_new_session(string outcome)
    {
        var repository = DispatchProxy.Create<IMonicaRepository, SessionVaultRepositoryProxy>();
        var probe = (SessionVaultRepositoryProxy)(object)repository;
        probe.BlockFirstRead = true;
        var viewModel = CreateViewModel(GetTempPath(), repository: repository);
        viewModel.IsUnlocked = true;
        var oldLoad = viewModel.LoadAsync();
        await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            viewModel.IsUnlocked = false;
            viewModel.IsUnlocked = true;
            await viewModel.LoadAsync();
            var current = Assert.Single(viewModel.Passwords);
            var stage = viewModel.VaultLoadStageText;
            if (outcome == "success")
                probe.Release.TrySetResult([new PasswordEntry { Id = 99, Title = "Old fixture" }]);
            else
                probe.Release.TrySetException(outcome == "cancel"
                    ? new OperationCanceledException()
                    : new InvalidOperationException("fixture read failed"));
            await oldLoad;

            Assert.True(viewModel.IsUnlocked);
            Assert.Same(current, Assert.Single(viewModel.Passwords));
            Assert.Equal(stage, viewModel.VaultLoadStageText);
            Assert.False(viewModel.IsLoadingVault);
        }
        finally
        {
            probe.Release.TrySetResult([]);
            await oldLoad;
        }
    }

    [Theory]
    [InlineData(false, "success")]
    [InlineData(true, "success")]
    [InlineData(false, "cancel")]
    [InlineData(true, "cancel")]
    [InlineData(false, "failure")]
    [InlineData(true, "failure")]
    public async Task Old_account_load_cannot_repopulate_after_lock(bool unlockAgain, string outcome)
    {
        var store = DispatchProxy.Create<IBitwardenAccountStore, SessionAccountStoreProxy>();
        var probe = (SessionAccountStoreProxy)(object)store;
        var viewModel = CreateViewModel(GetTempPath(), bitwardenAccountStore: store);
        viewModel.IsUnlocked = true;
        var loading = viewModel.LoadBitwardenAccountsCommand.ExecuteAsync(null);
        await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        viewModel.IsUnlocked = false;
        if (unlockAgain)
            viewModel.IsUnlocked = true;
        var error = viewModel.BitwardenOperationError;
        if (outcome == "success")
        {
            probe.Release.SetResult([CreateFixtureAccount()]);
        }
        else
        {
            probe.Release.SetException(outcome == "cancel"
                ? new OperationCanceledException()
                : new InvalidOperationException("fixture read failed"));
        }
        await loading;

        Assert.Empty(viewModel.BitwardenAccounts);
        Assert.Null(viewModel.SelectedBitwardenAccount);
        Assert.Equal(error, viewModel.BitwardenOperationError);
        Assert.False(viewModel.IsLoadingBitwardenAccounts);
    }

    [Fact]
    public async Task Old_account_load_finally_cannot_finish_a_new_sessions_load()
    {
        var store = DispatchProxy.Create<IBitwardenAccountStore, SessionAccountStoreProxy>();
        var probe = (SessionAccountStoreProxy)(object)store;
        var viewModel = CreateViewModel(GetTempPath(), bitwardenAccountStore: store);
        viewModel.IsUnlocked = true;
        var oldLoad = viewModel.LoadBitwardenAccountsCommand.ExecuteAsync(null);
        await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        viewModel.IsUnlocked = false;
        viewModel.IsUnlocked = true;
        var newLoad = viewModel.LoadBitwardenAccountsCommand.ExecuteAsync(null);
        try
        {
            await probe.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            probe.Release.SetResult([CreateFixtureAccount()]);
            await oldLoad;
            Assert.True(viewModel.IsLoadingBitwardenAccounts);
            Assert.Empty(viewModel.BitwardenAccounts);
            probe.SecondRelease.SetResult([CreateFixtureAccount()]);
            await newLoad;
            Assert.Single(viewModel.BitwardenAccounts);
            Assert.False(viewModel.IsLoadingBitwardenAccounts);
        }
        finally
        {
            probe.Release.TrySetResult([]);
            probe.SecondRelease.TrySetResult([]);
            await Task.WhenAll(oldLoad, newLoad);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Old_timeline_load_cannot_repopulate_after_lock(bool unlockAgain)
    {
        var repository = DispatchProxy.Create<IMonicaRepository, SessionVaultRepositoryProxy>();
        var probe = (SessionVaultRepositoryProxy)(object)repository;
        probe.BlockTimeline = true;
        var viewModel = CreateViewModel(GetTempPath(), repository: repository);
        viewModel.IsUnlocked = true;
        var load = (Task)typeof(Monica.App.ViewModels.MainWindowViewModel)
            .GetMethod("LoadTimelineAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(viewModel, null)!;
        viewModel.IsUnlocked = false;
        if (unlockAgain)
        {
            viewModel.IsUnlocked = true;
        }

        probe.TimelineRelease.SetResult([new OperationLog { ItemTitle = "Old fixture" }]);
        await load;

        Assert.Empty(viewModel.TimelineEntries);
    }

    private static BitwardenAccount CreateFixtureAccount() => new()
    {
        Id = 1,
        Email = "fixture@example.org",
        AccountKey = "fixture",
        Endpoints = BitwardenEndpointSet.UnitedStates,
        Kdf = BitwardenKdfParameters.Pbkdf2()
    };

    public class SessionAccountStoreProxy : DispatchProxy
    {
        private int _reads;
        public TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<IReadOnlyList<BitwardenAccount>> SecondRelease { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<IReadOnlyList<BitwardenAccount>> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IBitwardenAccountStore.GetAllAsync))
                throw new NotSupportedException(targetMethod?.Name);
            if (Interlocked.Increment(ref _reads) == 1)
            {
                Started.TrySetResult();
                return Release.Task;
            }

            SecondStarted.TrySetResult();
            return SecondRelease.Task;
        }
    }

    public class SessionVaultRepositoryProxy : DispatchProxy
    {
        private int _reads;
        public bool BlockFirstRead { get; set; }
        public bool BlockTimeline { get; set; }
        public TaskCompletionSource<IReadOnlyList<OperationLog>> TimelineRelease { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<IReadOnlyList<PasswordEntry>> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IMonicaRepository.GetPasswordsAsync))
            {
                if (Interlocked.Increment(ref _reads) == 1 && BlockFirstRead)
                {
                    Started.TrySetResult();
                    return Release.Task;
                }
                return Task.FromResult<IReadOnlyList<PasswordEntry>>([new PasswordEntry { Id = 1, Title = "Current fixture" }]);
            }
            return targetMethod?.Name switch
            {
                nameof(IMonicaRepository.GetOperationLogsAsync) => BlockTimeline
                    ? TimelineRelease.Task : Task.FromResult<IReadOnlyList<OperationLog>>([]),
                nameof(IMonicaRepository.GetSecureItemsAsync) => Task.FromResult<IReadOnlyList<SecureItem>>(
                    [new SecureItem { Id = 2, Title = "Note fixture", ItemType = VaultItemType.Note }]),
                nameof(IMonicaRepository.GetCategoriesAsync) => Task.FromResult<IReadOnlyList<Category>>(
                    [new Category { Id = 3, Name = "Fixture" }]),
                nameof(IMonicaRepository.GetMdbxDatabasesAsync) => Task.FromResult<IReadOnlyList<LocalMdbxDatabase>>([]),
                nameof(IMonicaRepository.GetAttachmentOwnerIdsAsync) => Task.FromResult<IReadOnlyList<long>>([]),
                nameof(IMonicaRepository.GetPasswordQuickAccessRecordsAsync) => Task.FromResult<IReadOnlyList<PasswordQuickAccessRecord>>([]),
                _ => throw new NotSupportedException(targetMethod?.Name)
            };
        }
    }
}
