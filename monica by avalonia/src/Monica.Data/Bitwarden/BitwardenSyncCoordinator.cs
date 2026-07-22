using Monica.Core.Bitwarden;
using Monica.Core.Services;

namespace Monica.Data.Bitwarden;

public sealed class BitwardenSyncCoordinator(
    IBitwardenAccountStore accountStore,
    IBitwardenSessionManager sessionManager,
    IBitwardenAuthenticationService authenticationService,
    IBitwardenMutationProcessor mutationProcessor,
    IBitwardenMutationTransportFactory mutationTransportFactory,
    IBitwardenSyncTransport syncTransport,
    IBitwardenPullMergeService pullMergeService,
    IVaultSessionService vaultSessionService,
    TimeProvider? timeProvider = null) : IBitwardenSyncCoordinator
{
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(2);
    private const int MaximumMutationBatches = 100;
    private readonly object _sync = new();
    private readonly Dictionary<long, Task<BitwardenSyncResult>> _runs = [];
    private readonly Dictionary<long, BitwardenSyncState> _states = [];
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public event EventHandler<BitwardenSyncState>? StateChanged;

    public BitwardenSyncState GetState(long accountId)
    {
        lock (_sync)
        {
            return _states.GetValueOrDefault(accountId) ?? new BitwardenSyncState(
                accountId,
                BitwardenSyncPhase.Idle,
                BitwardenSyncTrigger.Background,
                _timeProvider.GetUtcNow());
        }
    }

    public async Task<BitwardenSyncResult> SyncAsync(
        long accountId,
        BitwardenSyncTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        if (accountId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId));
        }

        Task<BitwardenSyncResult> run;
        lock (_sync)
        {
            if (!_runs.TryGetValue(accountId, out run!))
            {
                var completion = new TaskCompletionSource<BitwardenSyncResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                run = completion.Task;
                _runs.Add(accountId, run);
                _ = ExecuteOwnedAsync(accountId, trigger, run, completion);
            }
        }

        return await run.WaitAsync(cancellationToken);
    }

    private async Task ExecuteOwnedAsync(
        long accountId,
        BitwardenSyncTrigger trigger,
        Task<BitwardenSyncResult> ownedTask,
        TaskCompletionSource<BitwardenSyncResult> completion)
    {
        try
        {
            completion.TrySetResult(await ExecuteAsync(accountId, trigger));
        }
        catch (OperationCanceledException exception)
        {
            completion.TrySetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
        finally
        {
            lock (_sync)
            {
                if (_runs.GetValueOrDefault(accountId) == ownedTask)
                {
                    _runs.Remove(accountId);
                }
            }
        }
    }

    private async Task<BitwardenSyncResult> ExecuteAsync(long accountId, BitwardenSyncTrigger trigger)
    {
        if (!vaultSessionService.IsUnlocked)
        {
            Publish(accountId, trigger, BitwardenSyncPhase.Locked, "The Monica vault is locked.");
            throw new InvalidOperationException("The Monica vault must be unlocked before Bitwarden synchronization.");
        }

        var lockToken = vaultSessionService.SessionCancellationToken;
        Publish(accountId, trigger, BitwardenSyncPhase.Preparing);
        var account = await accountStore.GetAsync(accountId, lockToken) ??
                      throw new KeyNotFoundException("The Bitwarden account does not exist.");
        var failureAccount = account;
        if (!account.IsConnected)
        {
            throw new InvalidOperationException("The Bitwarden account is disconnected.");
        }

        try
        {
            await EnsureSessionAsync(account, lockToken);
            using var lease = RequiredLease(accountId);
            var activeAccount = account;
            var activeLease = lease;
            BitwardenSessionLease? refreshedLease = null;
            try
            {
                if (RequiresRefresh(activeAccount, activeLease))
                {
                    Publish(accountId, trigger, BitwardenSyncPhase.RefreshingToken);
                    var refreshed = await authenticationService.RefreshAsync(
                        activeAccount,
                        activeLease.Secrets,
                        lockToken);
                    using var refreshedSecrets = refreshed.Secrets;
                    activeAccount = await accountStore.SaveConnectedAsync(
                        refreshed.Account,
                        refreshedSecrets,
                        lockToken);
                    failureAccount = activeAccount;
                    sessionManager.Open(activeAccount.Id, refreshedSecrets, activeAccount.AccessTokenExpiresAt);
                    refreshedLease = RequiredLease(accountId);
                    activeLease = refreshedLease;
                }

                Publish(accountId, trigger, BitwardenSyncPhase.Uploading);
                using var mutationTransport = mutationTransportFactory.Create(activeAccount, activeLease.Secrets);
                var mutations = await ProcessMutationsAsync(
                    accountId,
                    mutationTransport,
                    lockToken);

                Publish(accountId, trigger, BitwardenSyncPhase.Downloading);
                var remote = await syncTransport.DownloadAsync(activeAccount, activeLease.Secrets, lockToken);
                Publish(accountId, trigger, BitwardenSyncPhase.Applying);
                var merge = await pullMergeService.ApplyAsync(
                    accountId,
                    remote.Snapshot,
                    remote.DecodedCiphers,
                    lockToken);
                var now = _timeProvider.GetUtcNow();
                activeAccount = await accountStore.SaveConnectedAsync(activeAccount with
                {
                    UserId = remote.UserId ?? activeAccount.UserId,
                    DisplayName = remote.DisplayName ?? activeAccount.DisplayName,
                    LastSyncAt = now,
                    LastFullSyncAt = now,
                    RevisionDate = remote.Snapshot.RevisionDate,
                    LastSyncStatus = "success",
                    LastSyncError = null,
                    UpdatedAt = now
                }, activeLease.Secrets, lockToken);
                failureAccount = activeAccount;
                Publish(accountId, trigger, BitwardenSyncPhase.Completed);
                return new BitwardenSyncResult(activeAccount, mutations, merge);
            }
            finally
            {
                refreshedLease?.Dispose();
            }
        }
        catch (OperationCanceledException) when (lockToken.IsCancellationRequested)
        {
            Publish(accountId, trigger, BitwardenSyncPhase.Locked, "Synchronization stopped because the vault locked.");
            throw;
        }
        catch (Exception exception)
        {
            Publish(accountId, trigger, BitwardenSyncPhase.Failed, SanitizeMessage(exception.Message));
            await TrySaveFailureAsync(failureAccount, exception, lockToken);
            throw;
        }
    }

    private async Task<BitwardenMutationBatchResult> ProcessMutationsAsync(
        long accountId,
        IBitwardenMutationTransport transport,
        CancellationToken cancellationToken)
    {
        var total = new BitwardenMutationBatchResult(0, 0, 0, 0, 0);
        for (var batchIndex = 0; batchIndex < MaximumMutationBatches; batchIndex++)
        {
            var batch = await mutationProcessor.ProcessReadyAsync(
                accountId,
                _timeProvider.GetUtcNow(),
                transport,
                cancellationToken);
            total = new BitwardenMutationBatchResult(
                total.Claimed + batch.Claimed,
                total.Completed + batch.Completed,
                total.Deferred + batch.Deferred,
                total.Conflicts + batch.Conflicts,
                total.Failed + batch.Failed);
            if (batch.Claimed == 0)
            {
                return total;
            }
        }

        throw new BitwardenProtocolException("Bitwarden mutation queue exceeds the per-sync processing limit.");
    }

    private async Task EnsureSessionAsync(BitwardenAccount account, CancellationToken cancellationToken)
    {
        if (sessionManager.HasSession(account.Id))
        {
            return;
        }

        using var secrets = await accountStore.LoadSecretsAsync(account.Id, cancellationToken) ??
                            throw new InvalidOperationException("The Bitwarden account has no stored session secrets.");
        sessionManager.Open(account.Id, secrets, account.AccessTokenExpiresAt);
    }

    private BitwardenSessionLease RequiredLease(long accountId) =>
        sessionManager.TryCreateLease(accountId, out var lease)
            ? lease!
            : throw new InvalidOperationException("The Bitwarden session is unavailable.");

    private bool RequiresRefresh(BitwardenAccount account, BitwardenSessionLease lease) =>
        lease.IsAccessTokenExpired(_timeProvider) ||
        account.AccessTokenExpiresAt is { } expiry && expiry - _timeProvider.GetUtcNow() <= RefreshWindow;

    private async Task TrySaveFailureAsync(
        BitwardenAccount account,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested ||
            !sessionManager.TryCreateLease(account.Id, out var lease) ||
            lease is null)
        {
            return;
        }

        using (lease)
        {
            try
            {
                await accountStore.SaveConnectedAsync(account with
                {
                    LastSyncStatus = "failed",
                    LastSyncError = SanitizeMessage(exception.Message),
                    UpdatedAt = _timeProvider.GetUtcNow()
                }, lease.Secrets, cancellationToken);
            }
            catch
            {
                // The original synchronization error remains authoritative.
            }
        }
    }

    private void Publish(
        long accountId,
        BitwardenSyncTrigger trigger,
        BitwardenSyncPhase phase,
        string? message = null)
    {
        var state = new BitwardenSyncState(accountId, phase, trigger, _timeProvider.GetUtcNow(), message);
        lock (_sync)
        {
            _states[accountId] = state;
        }

        var handlers = StateChanged?.GetInvocationList();
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.Cast<EventHandler<BitwardenSyncState>>())
        {
            try
            {
                handler(this, state);
            }
            catch
            {
                // A view subscriber cannot interrupt synchronization.
            }
        }
    }

    private static string SanitizeMessage(string? message)
    {
        var source = string.IsNullOrWhiteSpace(message) ? "Bitwarden synchronization failed." : message;
        var sanitized = new string(source.Where(character => !char.IsControl(character)).ToArray());
        return sanitized.Length <= 4096 ? sanitized : sanitized[..4096];
    }
}
