using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Features.Sync.Bitwarden;
using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.Bitwarden;
using Monica.Data.Bitwarden;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class BitwardenSyncWorkflowUiTests
{
    public BitwardenSyncWorkflowUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Bitwarden_source_surface_exposes_secure_desktop_controls()
    {
        var view = new BitwardenSyncSourceView();

        Assert.NotNull(view.FindControl<ComboBox>("BitwardenAccountSelector"));
        Assert.NotNull(view.FindControl<Border>("BitwardenAccountStatusCard"));
        Assert.NotNull(view.FindControl<Button>("BitwardenSyncNowButton"));
        Assert.NotNull(view.FindControl<Button>("BitwardenReconnectButton"));
        Assert.NotNull(view.FindControl<Button>("BitwardenDisconnectButton"));
        Assert.NotNull(view.FindControl<StackPanel>("BitwardenConnectionForm"));
        Assert.NotNull(view.FindControl<StackPanel>("BitwardenConflictSection"));
        Assert.NotNull(view.FindControl<Button>("BitwardenAuthenticateButton"));
        Assert.NotNull(view.FindControl<Button>("BitwardenCancelConnectionButton"));
        Assert.Equal('*', view.FindControl<TextBox>("BitwardenMasterPasswordBox")!.PasswordChar);
    }

    [Fact]
    public async Task Bitwarden_two_factor_challenge_preserves_primary_credentials_until_continued()
    {
        var authentication = new FakeAuthenticationService(_ => new BitwardenAuthenticationResult(
            false,
            null,
            null,
            BitwardenLoginChallengeKind.TwoFactor,
            Factors: [new BitwardenLoginFactor(0, "Authenticator app")]));
        using var fixture = CreateFixture(authentication);
        var viewModel = fixture.ViewModel;
        viewModel.IsUnlocked = true;
        viewModel.BitwardenEmail = "person@example.com";
        viewModel.BitwardenMasterPassword = "correct horse battery staple";

        await viewModel.AuthenticateBitwardenCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsBitwardenTwoFactorChallenge);
        Assert.Equal("correct horse battery staple", viewModel.BitwardenMasterPassword);
        Assert.Single(viewModel.BitwardenLoginFactors);
        Assert.NotNull(viewModel.SelectedBitwardenLoginFactor);
        Assert.False(viewModel.CanAuthenticateBitwarden);

        viewModel.BitwardenTwoFactorToken = "123456";
        Assert.True(viewModel.CanAuthenticateBitwarden);
        viewModel.SelectedSyncPage = "Sources";

        var view = new BitwardenSyncSourceView { DataContext = viewModel };
        var host = new Window { Width = 1000, Height = 760, Content = view };
        host.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.FindControl<StackPanel>("BitwardenTwoFactorChallengePanel")!.IsVisible);
            Assert.False(view.FindControl<StackPanel>("BitwardenCaptchaChallengePanel")!.IsVisible);
            Assert.False(view.FindControl<StackPanel>("BitwardenNewDeviceChallengePanel")!.IsVisible);
        }
        finally
        {
            host.Close();
        }
    }

    [Fact]
    public async Task Bitwarden_success_and_vault_lock_clear_transient_authentication_secrets()
    {
        var account = CreateAccount(id: 0, connected: true);
        var authentication = new FakeAuthenticationService(_ => new BitwardenAuthenticationResult(
            true,
            account,
            CreateSecrets(),
            BitwardenLoginChallengeKind.None));
        using var fixture = CreateFixture(authentication);
        var viewModel = fixture.ViewModel;
        viewModel.IsUnlocked = true;
        viewModel.BitwardenEmail = account.Email;
        viewModel.BitwardenMasterPassword = "master password";
        viewModel.BitwardenTwoFactorToken = "123456";
        viewModel.BitwardenCaptchaResponse = "captcha-token";
        viewModel.BitwardenNewDeviceOtp = "654321";

        await viewModel.AuthenticateBitwardenCommand.ExecuteAsync(null);

        Assert.Equal("", viewModel.BitwardenMasterPassword);
        Assert.Equal("", viewModel.BitwardenTwoFactorToken);
        Assert.Equal("", viewModel.BitwardenCaptchaResponse);
        Assert.Equal("", viewModel.BitwardenNewDeviceOtp);
        Assert.False(viewModel.IsBitwardenConnectionEditorVisible);
        Assert.Single(fixture.AccountStore.Accounts);
        Assert.Equal(41, fixture.SessionManager.AccountId);

        viewModel.BitwardenMasterPassword = "temporary";
        viewModel.BitwardenTwoFactorToken = "temporary-code";
        viewModel.IsUnlocked = false;

        Assert.Equal("", viewModel.BitwardenMasterPassword);
        Assert.Equal("", viewModel.BitwardenTwoFactorToken);
        Assert.Equal("", viewModel.BitwardenEmail);
        Assert.Empty(viewModel.BitwardenAccounts);
        Assert.False(viewModel.IsBitwardenSyncActive);
    }

    [Fact]
    public async Task Bitwarden_initial_sync_failure_keeps_the_account_connected_and_exposes_retry_feedback()
    {
        var account = CreateAccount(id: 0, connected: true);
        var authentication = new FakeAuthenticationService(_ => new BitwardenAuthenticationResult(
            true,
            account,
            CreateSecrets(),
            BitwardenLoginChallengeKind.None));
        using var fixture = CreateFixture(authentication, failSynchronization: true);
        var viewModel = fixture.ViewModel;
        viewModel.IsUnlocked = true;
        viewModel.BitwardenEmail = account.Email;
        viewModel.BitwardenMasterPassword = "master password";

        await viewModel.AuthenticateBitwardenCommand.ExecuteAsync(null);

        Assert.Single(fixture.AccountStore.Accounts);
        Assert.True(fixture.AccountStore.Accounts[0].IsConnected);
        Assert.True(viewModel.HasBitwardenOperationError);
        Assert.Contains("Sync now", viewModel.BitwardenOperationError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Bitwarden_conflict_surface_lists_the_overwritten_edit_and_uploads_a_restore()
    {
        var account = CreateAccount(id: 7, connected: true);
        var authentication = new FakeAuthenticationService(_ => new BitwardenAuthenticationResult(
            true,
            account,
            CreateSecrets(),
            BitwardenLoginChallengeKind.None));
        var conflicts = new FakeConflictRestoreService();
        using var fixture = CreateFixture(authentication, conflictRestore: conflicts);
        var viewModel = fixture.ViewModel;
        viewModel.IsUnlocked = true;
        viewModel.BitwardenEmail = account.Email;
        viewModel.BitwardenMasterPassword = "master password";

        await viewModel.AuthenticateBitwardenCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        // Selecting the account is what opens the list; nothing else asks for it.
        var row = Assert.Single(viewModel.BitwardenConflicts);
        Assert.Equal("Renamed on this device", row.Title);
        Assert.True(viewModel.HasBitwardenConflicts);
        Assert.True(viewModel.CanResolveBitwardenConflicts);

        await viewModel.RestoreBitwardenConflictCommand.ExecuteAsync(row);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal([(7L, row.BackupId)], conflicts.Restored);
        Assert.Empty(viewModel.BitwardenConflicts);
    }

    // A row lives in an ItemsControl's DataTemplate, where the commands are reached through the
    // UserControl's name rather than its own binding source. Only a materialized template proves that
    // hop resolves; a broken name would ship as a list of dead buttons.
    [Fact]
    public async Task A_conflict_row_resolves_the_command_declared_outside_its_template()
    {
        var account = CreateAccount(id: 7, connected: true);
        var authentication = new FakeAuthenticationService(_ => new BitwardenAuthenticationResult(
            true,
            account,
            CreateSecrets(),
            BitwardenLoginChallengeKind.None));
        var conflicts = new FakeConflictRestoreService();
        using var fixture = CreateFixture(authentication, conflictRestore: conflicts);
        var viewModel = fixture.ViewModel;
        viewModel.IsUnlocked = true;
        viewModel.BitwardenEmail = account.Email;
        viewModel.BitwardenMasterPassword = "master password";
        await viewModel.AuthenticateBitwardenCommand.ExecuteAsync(null);
        viewModel.SelectedSyncPage = "Sources";

        var view = new BitwardenSyncSourceView { DataContext = viewModel };
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();

            // Read the row here, not earlier: reloading the account list republishes the conflict rows,
            // so only the instance standing after the last dispatcher pass can be the one the
            // materialized template is holding.
            var row = Assert.Single(viewModel.BitwardenConflicts);
            var buttons = view.GetSelfAndVisualDescendants()
                .OfType<Button>()
                .Where(button => button.Name is "BitwardenConflictRestoreButton" or "BitwardenConflictDiscardButton")
                .ToArray();

            Assert.Equal(2, buttons.Length);
            var restore = buttons.Single(button => button.Name == "BitwardenConflictRestoreButton");
            var discard = buttons.Single(button => button.Name == "BitwardenConflictDiscardButton");

            // Raising Button.ClickEvent was measured not to run a Command-bound button - only the
            // pointer pipeline calls OnClick - so what is asserted here is the hop the template has to
            // get right on its own: the command object, the row it carries, and that the call is live.
            Assert.Same(viewModel.RestoreBitwardenConflictCommand, restore.Command);
            Assert.Same(viewModel.DiscardBitwardenConflictCommand, discard.Command);
            Assert.Same(row, restore.CommandParameter);
            Assert.True(restore.IsEnabled);
            Assert.True(restore.Command!.CanExecute(restore.CommandParameter));

            restore.Command.Execute(restore.CommandParameter);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal([(7L, row.BackupId)], conflicts.Restored);
            Assert.Empty(viewModel.BitwardenConflicts);
        }
        finally
        {
            window.Close();
        }
    }

    // The list is read without being awaited, so switching accounts while a read is still on disk lets
    // the previous vault's rows land afterwards: they would be listed under the account now selected,
    // and restoring one would ask that account for a backup it does not own.
    [Fact]
    public async Task A_conflict_read_that_lands_after_the_account_changed_is_dropped()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var conflicts = new FakeConflictRestoreService { Gate = gate.Task };
        var account = CreateAccount(id: 7, connected: true);
        var authentication = new FakeAuthenticationService(_ => new BitwardenAuthenticationResult(
            true,
            account,
            CreateSecrets(),
            BitwardenLoginChallengeKind.None));
        using var fixture = CreateFixture(authentication, conflictRestore: conflicts);
        var viewModel = fixture.ViewModel;
        viewModel.IsUnlocked = true;
        viewModel.BitwardenEmail = account.Email;
        viewModel.BitwardenMasterPassword = "master password";
        await viewModel.AuthenticateBitwardenCommand.ExecuteAsync(null);
        Assert.Equal(1, conflicts.Reads);

        fixture.AccountStore.Accounts.Add(CreateAccount(id: 8, connected: true));
        await viewModel.LoadBitwardenAccountsCommand.ExecuteAsync(null);
        viewModel.SelectedBitwardenAccount = viewModel.BitwardenAccounts.Single(item => item.Id == 8);
        Assert.True(conflicts.Reads >= 2, $"expected the new account to be read too, saw {conflicts.Reads} reads");

        gate.SetResult();
        await Task.WhenAll(conflicts.Landed);
        Dispatcher.UIThread.RunJobs();

        var row = Assert.Single(viewModel.BitwardenConflicts);
        Assert.Equal("Renamed on the other device", row.Title);
    }

    // Giving up is the branch that throws local content away, so the row it names matters: the discard
    // must address the vault now selected and leave the remote version the merge wrote.
    [Fact]
    public async Task Discarding_a_row_removes_it_and_leaves_the_rest_alone()
    {
        var account = CreateAccount(id: 7, connected: true);
        var authentication = new FakeAuthenticationService(_ => new BitwardenAuthenticationResult(
            true,
            account,
            CreateSecrets(),
            BitwardenLoginChallengeKind.None));
        var conflicts = new FakeConflictRestoreService();
        using var fixture = CreateFixture(authentication, conflictRestore: conflicts);
        var viewModel = fixture.ViewModel;
        viewModel.IsUnlocked = true;
        viewModel.BitwardenEmail = account.Email;
        viewModel.BitwardenMasterPassword = "master password";
        await viewModel.AuthenticateBitwardenCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        var row = Assert.Single(viewModel.BitwardenConflicts);
        await viewModel.DiscardBitwardenConflictCommand.ExecuteAsync(row);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal([(7L, row.BackupId)], conflicts.Discarded);
        Assert.Empty(conflicts.Restored);
        Assert.Empty(viewModel.BitwardenConflicts);
        Assert.False(viewModel.HasBitwardenConflicts);
        Assert.Empty(viewModel.BitwardenOperationError);
    }

    private static Fixture CreateFixture(
        IBitwardenAuthenticationService authentication,
        bool failSynchronization = false,
        IBitwardenConflictRestoreService? conflictRestore = null)
    {
        var accountStore = new FakeAccountStore();
        var sessionManager = new FakeSessionManager();
        var coordinator = new FakeSyncCoordinator(accountStore, failSynchronization);
        var window = new Monica.App.MainWindow();
        var services = Monica.App.App.ConfigureServices(window, collection =>
        {
            collection.AddSingleton<IBitwardenAccountStore>(accountStore);
            collection.AddSingleton(authentication);
            collection.AddSingleton<IBitwardenSyncCoordinator>(coordinator);
            collection.AddSingleton<IBitwardenSessionManager>(sessionManager);
            collection.AddSingleton<IBitwardenDeviceIdentityProvider>(new FakeDeviceIdentityProvider());
            if (conflictRestore is not null)
            {
                collection.AddSingleton(conflictRestore);
            }
        });
        return new Fixture(
            window,
            services,
            services.GetRequiredService<MainWindowViewModel>(),
            accountStore,
            sessionManager);
    }

    private static BitwardenAccount CreateAccount(long id, bool connected) => new()
    {
        Id = id,
        Email = "person@example.com",
        DisplayName = "Personal Bitwarden",
        AccountKey = "bw:v1:test-account",
        Endpoints = BitwardenEndpointSet.UnitedStates,
        Kdf = BitwardenKdfParameters.Pbkdf2(),
        IsConnected = connected,
        IsDefault = true,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static BitwardenAccountSecrets CreateSecrets() => new(
        new byte[] { 1 },
        new byte[] { 2 },
        new byte[32],
        new byte[32],
        new byte[32]);

    private sealed record Fixture(
        Window Window,
        ServiceProvider Services,
        MainWindowViewModel ViewModel,
        FakeAccountStore AccountStore,
        FakeSessionManager SessionManager) : IDisposable
    {
        public void Dispose()
        {
            Window.Close();
            Services.Dispose();
        }
    }

    private sealed class FakeAuthenticationService(
        Func<BitwardenAuthenticationRequest, BitwardenAuthenticationResult> authenticate) :
        IBitwardenAuthenticationService
    {
        public Task<BitwardenKdfParameters> PreloginAsync(
            string email,
            BitwardenEndpointSet endpoints,
            BitwardenTlsOptions tls,
            string? clientCertificatePassword = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(BitwardenKdfParameters.Pbkdf2());

        public Task<BitwardenAuthenticationResult> AuthenticateAsync(
            BitwardenAuthenticationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(authenticate(request));

        public Task<(BitwardenAccount Account, BitwardenAccountSecrets Secrets)> RefreshAsync(
            BitwardenAccount account,
            BitwardenAccountSecrets currentSecrets,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeAccountStore : IBitwardenAccountStore
    {
        public List<BitwardenAccount> Accounts { get; } = [];

        public Task<IReadOnlyList<BitwardenAccount>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BitwardenAccount>>(Accounts.ToArray());

        public Task<BitwardenAccount?> GetAsync(long accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Accounts.FirstOrDefault(account => account.Id == accountId));

        public Task<BitwardenAccount> SaveConnectedAsync(
            BitwardenAccount account,
            BitwardenAccountSecrets secrets,
            CancellationToken cancellationToken = default)
        {
            var saved = account with { Id = account.Id == 0 ? 41 : account.Id, IsConnected = true };
            Accounts.RemoveAll(existing => existing.Id == saved.Id);
            Accounts.Add(saved);
            return Task.FromResult(saved);
        }

        public Task<BitwardenAccountSecrets?> LoadSecretsAsync(
            long accountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BitwardenAccountSecrets?>(null);

        public Task DisconnectAsync(long accountId, CancellationToken cancellationToken = default)
        {
            var index = Accounts.FindIndex(account => account.Id == accountId);
            if (index >= 0)
            {
                Accounts[index] = Accounts[index] with { IsConnected = false };
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(long accountId, CancellationToken cancellationToken = default)
        {
            Accounts.RemoveAll(account => account.Id == accountId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSessionManager : IBitwardenSessionManager
    {
        public long? AccountId { get; private set; }

        public bool HasSession(long accountId) => AccountId == accountId;

        public void Open(long accountId, BitwardenAccountSecrets secrets, DateTimeOffset? accessTokenExpiresAt) =>
            AccountId = accountId;

        public bool TryCreateLease(long accountId, out BitwardenSessionLease? lease)
        {
            lease = null;
            return false;
        }

        public void Clear() => AccountId = null;
    }

    private sealed class FakeSyncCoordinator(
        FakeAccountStore accountStore,
        bool failSynchronization) : IBitwardenSyncCoordinator
    {
        private BitwardenSyncState _state = new(
            0,
            BitwardenSyncPhase.Idle,
            BitwardenSyncTrigger.Manual,
            DateTimeOffset.UtcNow);

        public event EventHandler<BitwardenSyncState>? StateChanged;

        public BitwardenSyncState GetState(long accountId) => _state with { AccountId = accountId };

        public Task<BitwardenSyncResult> SyncAsync(
            long accountId,
            BitwardenSyncTrigger trigger,
            CancellationToken cancellationToken = default)
        {
            if (failSynchronization)
            {
                throw new HttpRequestException("Simulated synchronization failure.");
            }

            _state = new BitwardenSyncState(
                accountId,
                BitwardenSyncPhase.Completed,
                trigger,
                DateTimeOffset.UtcNow);
            StateChanged?.Invoke(this, _state);
            var account = accountStore.Accounts.Single(item => item.Id == accountId);
            return Task.FromResult(new BitwardenSyncResult(
                account,
                new BitwardenMutationBatchResult(0, 0, 0, 0, 0),
                new BitwardenPullMergeResult(0, 0, 0, 0, 0, 0, 0)));
        }
    }

    private sealed class FakeDeviceIdentityProvider : IBitwardenDeviceIdentityProvider
    {
        public string DeviceIdentifier => "0123456789abcdef0123456789abcdef";
        public string DeviceName => "Monica test desktop";
    }

    private sealed class FakeConflictRestoreService : IBitwardenConflictRestoreService
    {
        private static readonly BitwardenConflictSummary VaultSevenRow = new(
            BackupId: 900,
            CipherId: "cipher-overwritten-by-pull",
            IsPassword: true,
            Title: "Renamed on this device",
            Reason: "Local and remote changed at the same revision.",
            CreatedAt: new DateTimeOffset(2026, 9, 20, 8, 30, 0, TimeSpan.Zero));

        private static readonly BitwardenConflictSummary VaultEightRow = new(
            BackupId: 901,
            CipherId: "cipher-overwritten-in-the-other-vault",
            IsPassword: true,
            Title: "Renamed on the other device",
            Reason: "Local and remote changed at the same revision.",
            CreatedAt: new DateTimeOffset(2026, 9, 21, 8, 30, 0, TimeSpan.Zero));

        private readonly Dictionary<long, List<BitwardenConflictSummary>> unresolved = new()
        {
            [7] = [VaultSevenRow],
            [8] = [VaultEightRow]
        };

        public List<(long VaultId, long BackupId)> Restored { get; } = [];

        public List<(long VaultId, long BackupId)> Discarded { get; } = [];

        public int Reads { get; private set; }

        public Task? Gate { get; set; }

        public List<Task> Landed { get; } = [];

        public async Task<IReadOnlyList<BitwardenConflictSummary>> GetSummariesAsync(
            long vaultId,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            var landed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Landed.Add(landed.Task);
            if (Gate is { } gate)
            {
                await gate;
            }

            var result = unresolved.TryGetValue(vaultId, out var rows)
                ? rows.ToArray()
                : [];
            landed.SetResult();
            return result;
        }

        public Task RestoreAsync(long vaultId, long backupId, CancellationToken cancellationToken = default)
        {
            Restored.Add((vaultId, backupId));
            Remove(backupId);
            return Task.CompletedTask;
        }

        public Task DiscardAsync(long vaultId, long backupId, CancellationToken cancellationToken = default)
        {
            Discarded.Add((vaultId, backupId));
            Remove(backupId);
            return Task.CompletedTask;
        }

        private void Remove(long backupId)
        {
            foreach (var rows in unresolved.Values)
            {
                rows.RemoveAll(summary => summary.BackupId == backupId);
            }
        }
    }
}
