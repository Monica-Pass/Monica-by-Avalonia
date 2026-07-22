using System.Collections.Concurrent;
using System.Net;
using Monica.Core.Bitwarden;
using Monica.Core.Models;
using Monica.Data.Repositories;

namespace Monica.Data.Bitwarden;

public interface IBitwardenMutationProcessor
{
    Task<BitwardenMutationBatchResult> ProcessReadyAsync(
        long vaultId,
        DateTimeOffset now,
        IBitwardenMutationTransport transport,
        CancellationToken cancellationToken = default);
}

public sealed class BitwardenMutationProcessor(
    IBitwardenPendingOperationStore operationStore,
    IBitwardenConflictBackupStore conflictStore,
    IMonicaRepository repository) : IBitwardenMutationProcessor
{
    private static readonly ConcurrentDictionary<long, SemaphoreSlim> VaultLocks = new();

    public async Task<BitwardenMutationBatchResult> ProcessReadyAsync(
        long vaultId,
        DateTimeOffset now,
        IBitwardenMutationTransport transport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var gate = VaultLocks.GetOrAdd(vaultId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var operations = await operationStore.ClaimReadyAsync(vaultId, now, cancellationToken: cancellationToken);
            if (operations.Count == 0)
            {
                return new BitwardenMutationBatchResult(0, 0, 0, 0, 0);
            }

            var local = await LoadLocalItemsAsync(vaultId, cancellationToken);
            var completed = 0;
            var deferred = 0;
            var conflicts = 0;
            var failed = 0;

            foreach (var operation in operations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var response = await transport.SendAsync(ToRequest(operation), cancellationToken);
                    BitwardenMutationGuard.ValidateResponse(operation, response);
                    if (response.Succeeded)
                    {
                        await ApplySuccessAsync(local, operation, response, cancellationToken);
                        await operationStore.CompleteAsync(operation.Id, cancellationToken);
                        completed++;
                        continue;
                    }

                    var failureClass = response.HttpStatusCode is { } status
                        ? BitwardenRetryPolicy.ClassifyHttpStatus((HttpStatusCode)status)
                        : BitwardenFailureClass.Permanent;
                    var statusResult = await RecordFailureAsync(
                        operation,
                        failureClass,
                        response.ErrorMessage,
                        response.RetryAfter,
                        now,
                        local,
                        cancellationToken);
                    Count(statusResult, ref deferred, ref conflicts, ref failed);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    var failureClass = BitwardenRetryPolicy.ClassifyException(exception);
                    var statusResult = await RecordFailureAsync(
                        operation,
                        failureClass,
                        exception.Message,
                        null,
                        now,
                        local,
                        cancellationToken);
                    Count(statusResult, ref deferred, ref conflicts, ref failed);
                }
            }

            return new BitwardenMutationBatchResult(
                operations.Count,
                completed,
                deferred,
                conflicts,
                failed);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<BitwardenMutationStatus> RecordFailureAsync(
        BitwardenPendingOperation operation,
        BitwardenFailureClass failureClass,
        string? error,
        TimeSpan? retryAfter,
        DateTimeOffset now,
        LocalItems local,
        CancellationToken cancellationToken)
    {
        if (failureClass == BitwardenFailureClass.Conflict)
        {
            await SaveConflictAsync(operation, local, error, cancellationToken);
        }

        return await operationStore.RecordFailureAsync(
            operation.Id,
            failureClass,
            error,
            now,
            retryAfter,
            cancellationToken);
    }

    private async Task SaveConflictAsync(
        BitwardenPendingOperation operation,
        LocalItems local,
        string? error,
        CancellationToken cancellationToken)
    {
        if (!local.TryFind(operation.CipherId, out var localId, out var itemKind))
        {
            return;
        }

        await conflictStore.SaveAsync(new BitwardenConflictBackup(
            0,
            operation.VaultId,
            operation.CipherId,
            itemKind,
            localId,
            operation.ExpectedRemoteRevision,
            null,
            operation.PayloadJson,
            string.IsNullOrWhiteSpace(error) ? "Remote revision conflict." : error,
            DateTimeOffset.UtcNow), cancellationToken);
    }

    private async Task ApplySuccessAsync(
        LocalItems local,
        BitwardenPendingOperation operation,
        BitwardenMutationResponse response,
        CancellationToken cancellationToken)
    {
        var remoteCipherId = response.RemoteCipherId ?? operation.CipherId;
        if (local.Passwords.TryGetValue(operation.CipherId, out var password))
        {
            password.BitwardenCipherId = remoteCipherId;
            password.BitwardenRevisionDate = response.RemoteRevision ?? password.BitwardenRevisionDate;
            password.BitwardenLocalModified = false;
            await repository.SavePasswordAsync(password, cancellationToken);
            return;
        }

        if (local.SecureItems.TryGetValue(operation.CipherId, out var secureItem))
        {
            secureItem.BitwardenCipherId = remoteCipherId;
            secureItem.BitwardenRevisionDate = response.RemoteRevision ?? secureItem.BitwardenRevisionDate;
            secureItem.BitwardenLocalModified = false;
            await repository.SaveSecureItemAsync(secureItem, cancellationToken);
        }
    }

    private async Task<LocalItems> LoadLocalItemsAsync(long vaultId, CancellationToken cancellationToken)
    {
        var passwords = (await repository.GetPasswordsAsync(true, true, cancellationToken))
            .Where(item => item.BitwardenVaultId == vaultId && item.BitwardenCipherId is not null)
            .ToDictionary(item => item.BitwardenCipherId!, StringComparer.Ordinal);
        var secureItems = (await repository.GetSecureItemsAsync(null, true, cancellationToken))
            .Where(item => item.BitwardenVaultId == vaultId && item.BitwardenCipherId is not null)
            .ToDictionary(item => item.BitwardenCipherId!, StringComparer.Ordinal);
        return new LocalItems(passwords, secureItems);
    }

    private static BitwardenMutationRequest ToRequest(BitwardenPendingOperation operation) => new(
        operation.Id,
        operation.VaultId,
        operation.CipherId,
        operation.OperationType,
        operation.ExpectedRemoteRevision,
        operation.PayloadJson,
        operation.IdempotencyKey);

    private static void Count(
        BitwardenMutationStatus status,
        ref int deferred,
        ref int conflicts,
        ref int failed)
    {
        switch (status)
        {
            case BitwardenMutationStatus.Pending:
                deferred++;
                break;
            case BitwardenMutationStatus.Conflict:
                conflicts++;
                break;
            default:
                failed++;
                break;
        }
    }

    private sealed record LocalItems(
        IReadOnlyDictionary<string, PasswordEntry> Passwords,
        IReadOnlyDictionary<string, SecureItem> SecureItems)
    {
        public bool TryFind(string cipherId, out long localId, out string itemKind)
        {
            if (Passwords.TryGetValue(cipherId, out var password))
            {
                localId = password.Id;
                itemKind = "password";
                return true;
            }

            if (SecureItems.TryGetValue(cipherId, out var secureItem))
            {
                localId = secureItem.Id;
                itemKind = "secure-item";
                return true;
            }

            localId = 0;
            itemKind = "unknown";
            return false;
        }
    }
}
