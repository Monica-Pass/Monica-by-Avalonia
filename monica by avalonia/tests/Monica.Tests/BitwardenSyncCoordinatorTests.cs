using System.Text;
using Monica.Core.Bitwarden;
using Monica.Core.Services;
using Monica.Data.Bitwarden;

namespace Monica.Tests;

public sealed class BitwardenSyncCoordinatorTests
{
    [Fact]
    public async Task SyncAsync_CoalescesConcurrentRequestsPerAccount()
    {
        var harness = CreateHarness();
        var first = harness.Coordinator.SyncAsync(7, BitwardenSyncTrigger.Manual);
        await harness.SyncTransport.Started.Task;
        var second = harness.Coordinator.SyncAsync(7, BitwardenSyncTrigger.Background);
        harness.SyncTransport.Release.TrySetResult(true);

        await Task.WhenAll(first, second);

        Assert.Equal(1, harness.SyncTransport.DownloadCount);
        Assert.Equal(BitwardenSyncPhase.Completed, harness.Coordinator.GetState(7).Phase);
    }

    [Fact]
    public async Task SyncAsync_StopsNetworkWhenVaultLocks()
    {
        var harness = CreateHarness();
        var run = harness.Coordinator.SyncAsync(7, BitwardenSyncTrigger.Manual);
        await harness.SyncTransport.Started.Task;
        harness.VaultSession.MarkLocked();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.Equal(BitwardenSyncPhase.Locked, harness.Coordinator.GetState(7).Phase);
    }

    [Fact]
    public async Task SyncAsync_RefreshesExpiringSessionAndUploadsBeforePull()
    {
        var harness = CreateHarness(expiring: true);
        var events = new List<string>();
        harness.MutationProcessor.OnProcess = _ =>
        {
            events.Add("upload");
            return Task.FromResult(new BitwardenMutationBatchResult(1, 1, 0, 0, 0));
        };
        harness.SyncTransport.OnDownload = () => events.Add("download");
        harness.PullMerge.OnApply = () =>
        {
            events.Add("apply");
            return new BitwardenPullMergeResult(1, 0, 0, 0, 0, 0, 0);
        };
        harness.SyncTransport.Release.TrySetResult(true);

        var result = await harness.Coordinator.SyncAsync(7, BitwardenSyncTrigger.Manual);

        Assert.True(harness.Authentication.RefreshCalled);
        Assert.Equal(["upload", "download", "apply"], events);
        Assert.Equal(1, result.Merge.Added);
    }

    private static Harness CreateHarness(bool expiring = false)
    {
        var vaultSession = new VaultSessionService();
        vaultSession.MarkUnlocked();
        var accountStore = new FakeAccountStore(CreateAccount(expiring), CreateSecrets());
        var sessionManager = new BitwardenSessionManager(vaultSession);
        var authentication = new FakeAuthenticationService();
        var mutationProcessor = new FakeMutationProcessor();
        var syncTransport = new FakeSyncTransport();
        var pullMerge = new FakePullMergeService();
        var coordinator = new BitwardenSyncCoordinator(
            accountStore,
            sessionManager,
            authentication,
            mutationProcessor,
            new FakeMutationTransportFactory(),
            syncTransport,
            pullMerge,
            vaultSession);
        return new Harness(
            coordinator,
            vaultSession,
            authentication,
            mutationProcessor,
            syncTransport,
            pullMerge);
    }

    private static BitwardenAccount CreateAccount(bool expiring)
    {
        var endpoints = new BitwardenEndpointSet(
            new Uri("https://vault.example.test/"),
            new Uri("https://identity.example.test/"),
            new Uri("https://api.example.test/"));
        return new BitwardenAccount
        {
            Id = 7,
            Email = "test@example.com",
            AccountKey = BitwardenAccountIdentity.CreateAccountKey("test@example.com", endpoints),
            Endpoints = endpoints,
            Kdf = new BitwardenKdfParameters(BitwardenKdfAlgorithm.Pbkdf2Sha256, 100_000),
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.Add(expiring ? TimeSpan.FromSeconds(10) : TimeSpan.FromHours(1)),
            IsConnected = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private static BitwardenAccountSecrets CreateSecrets() => new(
        Encoding.UTF8.GetBytes("access-token"),
        Encoding.UTF8.GetBytes("refresh-token"),
        new byte[32],
        new byte[32],
        new byte[32]);

    private sealed record Harness(
        BitwardenSyncCoordinator Coordinator,
        VaultSessionService VaultSession,
        FakeAuthenticationService Authentication,
        FakeMutationProcessor MutationProcessor,
        FakeSyncTransport SyncTransport,
        FakePullMergeService PullMerge);

    private sealed class FakeAccountStore(BitwardenAccount account, BitwardenAccountSecrets secrets) : IBitwardenAccountStore
    {
        private BitwardenAccount _account = account;

        public Task<IReadOnlyList<BitwardenAccount>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BitwardenAccount>>([_account]);

        public Task<BitwardenAccount?> GetAsync(long accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult<BitwardenAccount?>(accountId == _account.Id ? _account : null);

        public Task<BitwardenAccount> SaveConnectedAsync(
            BitwardenAccount updated,
            BitwardenAccountSecrets values,
            CancellationToken cancellationToken = default)
        {
            _account = updated with { Id = _account.Id };
            return Task.FromResult(_account);
        }

        public Task<BitwardenAccountSecrets?> LoadSecretsAsync(long accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult<BitwardenAccountSecrets?>(accountId == _account.Id ? secrets.Clone() : null);

        public Task DisconnectAsync(long accountId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(long accountId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeAuthenticationService : IBitwardenAuthenticationService
    {
        public bool RefreshCalled { get; set; }

        public Task<BitwardenKdfParameters> PreloginAsync(string email, BitwardenEndpointSet endpoints, BitwardenTlsOptions tls, string? clientCertificatePassword = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(BitwardenKdfParameters.Pbkdf2());

        public Task<BitwardenAuthenticationResult> AuthenticateAsync(BitwardenAuthenticationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<(BitwardenAccount Account, BitwardenAccountSecrets Secrets)> RefreshAsync(BitwardenAccount account, BitwardenAccountSecrets currentSecrets, CancellationToken cancellationToken = default)
        {
            RefreshCalled = true;
            return Task.FromResult((account with { AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1) }, CreateSecrets()));
        }
    }

    private sealed class FakeMutationProcessor : IBitwardenMutationProcessor
    {
        public Func<IBitwardenMutationTransport, Task<BitwardenMutationBatchResult>>? OnProcess { get; set; }
        private int _calls;

        public Task<BitwardenMutationBatchResult> ProcessReadyAsync(long vaultId, DateTimeOffset now, IBitwardenMutationTransport transport, CancellationToken cancellationToken = default)
        {
            _calls++;
            return _calls == 1 && OnProcess is not null
                ? OnProcess(transport)
                : Task.FromResult(new BitwardenMutationBatchResult(0, 0, 0, 0, 0));
        }
    }

    private sealed class FakeMutationTransportFactory : IBitwardenMutationTransportFactory
    {
        public IBitwardenOwnedMutationTransport Create(BitwardenAccount account, BitwardenAccountSecrets secrets) =>
            new FakeOwnedTransport();
    }

    private sealed class FakeOwnedTransport : IBitwardenOwnedMutationTransport
    {
        public Task<BitwardenMutationResponse> SendAsync(BitwardenMutationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BitwardenMutationResponse(true, request.CipherId, "revision"));

        public void Dispose() { }
    }

    private sealed class FakeSyncTransport : IBitwardenSyncTransport
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DownloadCount { get; private set; }
        public Action? OnDownload { get; set; }

        public async Task<BitwardenRemoteSyncResult> DownloadAsync(BitwardenAccount account, BitwardenAccountSecrets secrets, CancellationToken cancellationToken = default)
        {
            DownloadCount++;
            Started.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            OnDownload?.Invoke();
            return new BitwardenRemoteSyncResult(
                new BitwardenPullSnapshot([], [], null, true, DateTimeOffset.UtcNow),
                [],
                null,
                null);
        }
    }

    private sealed class FakePullMergeService : IBitwardenPullMergeService
    {
        public Func<BitwardenPullMergeResult>? OnApply { get; set; }

        public Task<BitwardenPullMergeResult> ApplyAsync(long vaultId, BitwardenPullSnapshot snapshot, IReadOnlyList<BitwardenDecodedCipher> decodedCiphers, CancellationToken cancellationToken = default) =>
            Task.FromResult(OnApply?.Invoke() ?? new BitwardenPullMergeResult(0, 0, 0, 0, 0, 0, 0));
    }
}
