using Monica.Core.Bitwarden;
using Monica.Core.Models;

namespace Monica.Data.Bitwarden;

public interface IBitwardenPurgeQueue
{
    Task<bool> EnqueuePasswordAsync(PasswordEntry entry, CancellationToken cancellationToken = default);

    Task<bool> EnqueueSecureItemAsync(SecureItem item, CancellationToken cancellationToken = default);
}

/// <summary>
/// Books the remote erase at the moment the local row is destroyed, because nothing else can. The drift
/// scan turns a trashed entry into upload work by reading its vault binding off the stored row, and a
/// permanent purge erases exactly that: the writer leaves a tombstone carrying only its ids
/// (MdbxBackedMonicaRepository.CreatePasswordTombstone / CreateSecureItemTombstone), so from that
/// instant on no scan filtering on BitwardenVaultId can tell the entry ever belonged to a vault - let
/// alone which cipher the server still holds. The queue is therefore handed the identity while the row
/// it came from is still readable.
/// </summary>
public sealed class BitwardenPurgeQueue(
    IBitwardenPendingOperationStore operationStore) : IBitwardenPurgeQueue
{
    public Task<bool> EnqueuePasswordAsync(
        PasswordEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return EnqueueAsync(
            entry.BitwardenVaultId,
            entry.BitwardenCipherId,
            entry.BitwardenRevisionDate,
            cancellationToken);
    }

    public Task<bool> EnqueueSecureItemAsync(
        SecureItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        return EnqueueAsync(
            item.BitwardenVaultId,
            item.BitwardenCipherId,
            item.BitwardenRevisionDate,
            cancellationToken);
    }

    private async Task<bool> EnqueueAsync(
        long? vaultId,
        string? cipherId,
        string? expectedRemoteRevision,
        CancellationToken cancellationToken)
    {
        if (vaultId is not { } vault ||
            string.IsNullOrWhiteSpace(cipherId) ||
            string.IsNullOrWhiteSpace(expectedRemoteRevision))
        {
            // Two different reasons to stay quiet: the server never took this row, so there is no copy
            // of it to erase, and a row whose revision was never read cannot guard the erase against an
            // edit that landed in between - deleting the version the user was not looking at is worse
            // than leaving one behind. BitwardenMutationGuard refuses an unguarded delete all the same.
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        await operationStore.EnqueueAsync(new BitwardenPendingOperation(
            Id: 0,
            VaultId: vault,
            CipherId: cipherId,
            OperationType: BitwardenMutationOperationType.Delete,
            ExpectedRemoteRevision: expectedRemoteRevision,
            // The route decides the erase, so nothing travels; the store still requires parseable JSON.
            PayloadJson: "{}",
            // Its own prefix rather than the trash's: the store raises a completed row back to pending
            // when an enqueue lands on the same key, so reusing local-delete would reopen a trashing the
            // server already granted - and a cipher the user threw away is not the same promise as one
            // they erased.
            IdempotencyKey: $"local-purge:{vault}:{cipherId}",
            Status: BitwardenMutationStatus.Pending,
            LastFailureClass: BitwardenFailureClass.None,
            AttemptCount: 0,
            NextAttemptAt: now,
            ClaimedAt: null,
            LastError: null,
            CreatedAt: now,
            UpdatedAt: now,
            // No content reached the server, so there is nothing to record as what the server holds - and
            // this row is the only thing keeping the erased cipher's baseline from being re-advanced.
            LocalPayloadHash: null), cancellationToken);
        return true;
    }
}
