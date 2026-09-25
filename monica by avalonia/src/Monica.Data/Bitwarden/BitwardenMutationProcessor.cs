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
                    if (operation.OperationType == BitwardenMutationOperationType.Create &&
                        !local.HasPublishableEntry(operation.CipherId))
                    {
                        // Nothing local is owed any more: the entry was published twice and the first
                        // upload already gave it a cipher, or it was deleted before the push. Posting the
                        // create again would add a second copy of an entry the server already holds.
                        await operationStore.CompleteAsync(operation.Id, cancellationToken);
                        completed++;
                        continue;
                    }

                    if (operation.OperationType == BitwardenMutationOperationType.SoftDelete &&
                        local.IsHeldAlive(operation.CipherId))
                    {
                        // The entry came back out of the trash before this delete left the device.
                        // Trashing the remote copy anyway would take an item away from every other client
                        // that the user kept here; completing the row lets the next scan re-decide from
                        // the content that actually stands.
                        //
                        // A permanent erase deliberately keeps no such escape: nothing restores a purged
                        // row, so the only way a purged cipher stands on the shelf again is a pull that
                        // re-added it while the erase was still waiting to leave - a machine artifact, not
                        // the last word from the user. Letting that cancel the erase would quietly undo a
                        // decision nobody could take back on screen.
                        await operationStore.CompleteAsync(operation.Id, cancellationToken);
                        completed++;
                        continue;
                    }

                    if (operation.OperationType == BitwardenMutationOperationType.Restore &&
                        !local.IsHeldAlive(operation.CipherId))
                    {
                        // The mirror of that race: this entry went back into the trash - or out of the
                        // vault altogether - while the restore was still queued, so reviving the remote
                        // copy would hand back what the user has since removed. Completing the row lets
                        // the next scan re-decide from the state that actually stands.
                        await operationStore.CompleteAsync(operation.Id, cancellationToken);
                        completed++;
                        continue;
                    }

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
                    if (failureClass == BitwardenFailureClass.Validation &&
                        response.HttpStatusCode == (int)HttpStatusCode.NotFound &&
                        operation.OperationType is BitwardenMutationOperationType.Delete
                            or BitwardenMutationOperationType.SoftDelete)
                    {
                        // The server is not holding this cipher any more - another client erased it, or the
                        // trash this delete aimed at was emptied. A deletion whose target is gone has been
                        // answered in full, and recording it as a failure would leave a row no retry can
                        // ever satisfy sitting in the queue for the rest of the vault's life.
                        await operationStore.CompleteAsync(operation.Id, cancellationToken);
                        completed++;
                        continue;
                    }

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
        // For a create the queue row carries a local key, so only the response can say what the
        // cipher is called; guessing here would bind the local entry to an identity nothing owns.
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

        if (local.Passwords.TryGetValue(operation.CipherId, out var password) ||
            local.UnboundPasswords.TryGetValue(operation.CipherId, out password))
        {
            password.BitwardenCipherId = remoteCipherId;
            password.BitwardenRevisionDate = response.RemoteRevision ?? password.BitwardenRevisionDate;
            password.BitwardenLocalModified = false;
            await repository.SavePasswordAsync(password, cancellationToken);
            return;
        }

        // A note or card published from the library carries a local key until this point, and the response
        // is the only thing that can name the cipher the server just made for it.
        if (local.SecureItems.TryGetValue(operation.CipherId, out var secureItem) ||
            local.UnboundSecureItems.TryGetValue(operation.CipherId, out secureItem))
        {
            secureItem.BitwardenCipherId = remoteCipherId;
            secureItem.BitwardenRevisionDate = response.RemoteRevision ?? secureItem.BitwardenRevisionDate;
            secureItem.BitwardenLocalModified = false;
            await repository.SaveSecureItemAsync(secureItem, cancellationToken);
        }
    }

    private async Task<LocalItems> LoadLocalItemsAsync(long vaultId, CancellationToken cancellationToken)
    {
        var passwords = await repository.GetPasswordsAsync(true, true, cancellationToken);
        var secureItems = await repository.GetSecureItemsAsync(null, true, cancellationToken);
        var bound = secureItems.Where(item => item.BitwardenVaultId == vaultId).ToArray();
        return new LocalItems(
            passwords
                .Where(item => item.BitwardenVaultId == vaultId && item.BitwardenCipherId is not null)
                .ToDictionary(item => item.BitwardenCipherId!, StringComparer.Ordinal),
            // A published entry that reached the trash before its first upload owes nothing: posting it
            // would put back on the server the very entry the user removed locally.
            passwords
                .Where(item => item.BitwardenVaultId == vaultId &&
                               item.BitwardenCipherId is null &&
                               !item.IsDeleted)
                .ToDictionary(item => BitwardenLocalCipherIdentity.ForPassword(item.Id), StringComparer.Ordinal),
            bound
                .Where(item => item.BitwardenCipherId is not null)
                .ToDictionary(item => item.BitwardenCipherId!, StringComparer.Ordinal),
            // Same shape as the unbound passwords above: published from the library, never uploaded, so
            // the create's local key is the only handle back to the row the server must be told about.
            bound
                .Where(item => item.BitwardenCipherId is null && !item.IsDeleted)
                .ToDictionary(item => BitwardenLocalCipherIdentity.ForSecureItem(item.Id), StringComparer.Ordinal));
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
        IReadOnlyDictionary<string, PasswordEntry> UnboundPasswords,
        IReadOnlyDictionary<string, SecureItem> SecureItems,
        IReadOnlyDictionary<string, SecureItem> UnboundSecureItems)
    {
        public bool HasPublishableEntry(string localIdentity) =>
            UnboundPasswords.ContainsKey(localIdentity) || UnboundSecureItems.ContainsKey(localIdentity);

        /// <summary>
        /// A bound row the user has since put back on the shelf. The dictionaries are loaded with
        /// deleted rows on purpose, so a missing key means the entry is gone for good locally and the
        /// remote copy really is the last one standing.
        /// </summary>
        public bool IsHeldAlive(string cipherId) =>
            (Passwords.TryGetValue(cipherId, out var password) && !password.IsDeleted) ||
            (SecureItems.TryGetValue(cipherId, out var secureItem) && !secureItem.IsDeleted);
    }
}
