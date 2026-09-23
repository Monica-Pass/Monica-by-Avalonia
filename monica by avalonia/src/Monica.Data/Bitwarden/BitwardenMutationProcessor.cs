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

/// <summary>
/// Uploads the changes the drift scan queued. It deliberately writes no conflict backup: a rejected push
/// destroys nothing local, so the edit stays on screen and stays owed, and the pull that follows in the
/// same synchronization is the single place content is backed up right before remote overwrites it.
/// Backing up here too would add one copy per failed sync, in the outgoing server-cipher shape that a
/// restore keyed on item_kind cannot read.
/// </summary>
public sealed class BitwardenMutationProcessor(
    IBitwardenPendingOperationStore operationStore,
    IBitwardenSyncStateStore syncStateStore,
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
                    var statusResult = await operationStore.RecordFailureAsync(
                        operation.Id,
                        failureClass,
                        response.ErrorMessage,
                        now,
                        response.RetryAfter,
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
                    var statusResult = await operationStore.RecordFailureAsync(
                        operation.Id,
                        failureClass,
                        exception.Message,
                        now,
                        null,
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

    private async Task ApplySuccessAsync(
        LocalItems local,
        BitwardenPendingOperation operation,
        BitwardenMutationResponse response,
        CancellationToken cancellationToken)
    {
        var remoteCipherId = response.RemoteCipherId ?? operation.CipherId;
        // The server now holds exactly the content this upload carried, so it becomes the new
        // baseline. Without this the idempotency key of a completed change would be re-queued and
        // resurrected on the next sync.
        if (operation.LocalPayloadHash is { } pushedHash)
        {
            await syncStateStore.AdvanceAsync(
                operation.VaultId,
                remoteCipherId,
                pushedHash,
                DateTimeOffset.UtcNow,
                cancellationToken);
        }

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
        IReadOnlyDictionary<string, SecureItem> SecureItems);
}
