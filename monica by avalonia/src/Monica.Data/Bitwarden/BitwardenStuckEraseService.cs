using Monica.Core.Bitwarden;

namespace Monica.Data.Bitwarden;

/// <summary>
/// One permanent erase this device owes the server and is never going to send again. The cipher identity
/// is all there is to show: an erase carries no content (the route decides it, see
/// <see cref="BitwardenPurgeQueue"/>), the purged local row is a tombstone that kept only its ids, and no
/// stored table maps a cipher back to a title - which is precisely why the queue row is the last surviving
/// record of the decision.
/// </summary>
public sealed record BitwardenStuckErase(
    long OperationId,
    string CipherId,
    BitwardenMutationStatus Status,
    DateTimeOffset LastAttemptAt);

public interface IBitwardenStuckEraseService
{
    Task<IReadOnlyList<BitwardenStuckErase>> GetStuckAsync(
        long vaultId,
        CancellationToken cancellationToken = default);

    Task AbandonAsync(long vaultId, long operationId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Gives the user the decision a stuck erase took away. A delete ends up here when the server would not
/// take it - another client edited the cipher in between, or the answer was a refusal that retried until
/// the queue gave up - and at that point the row is terminal: <c>ClaimReadyAsync</c> only claims pending
/// rows with attempts left, so nothing will ever send it again. Meanwhile the merge engine keeps honouring
/// the booking, so the entry stays gone here while the server still holds it. Both of those are correct
/// until somebody chooses, and no automatic re-judging of the revision is that choice: this is the one
/// place the user can say "the copy I threw away should not have been thrown away". Completing the row
/// stops the suppression, so the next pull re-applies the remote cipher through the ordinary add path -
/// which also hands the entry a revision the server actually confirms, so erasing it a second time is a
/// guarded erase again rather than one that sticks.
/// </summary>
public sealed class BitwardenStuckEraseService(
    IBitwardenPendingOperationStore operationStore) : IBitwardenStuckEraseService
{
    public async Task<IReadOnlyList<BitwardenStuckErase>> GetStuckAsync(
        long vaultId,
        CancellationToken cancellationToken = default)
    {
        var operations = await operationStore.GetAsync(vaultId, cancellationToken);
        return operations
            .Where(IsStuck)
            .Select(operation => new BitwardenStuckErase(
                operation.Id,
                operation.CipherId,
                operation.Status,
                operation.UpdatedAt))
            .ToList();
    }

    public async Task AbandonAsync(
        long vaultId,
        long operationId,
        CancellationToken cancellationToken = default)
    {
        // Re-read rather than trust the row on screen: the list is rendered while a synchronization runs,
        // and a row that has since been re-booked as pending - which a re-purge does - is one the queue is
        // about to send. Marking that completed would silently drop an erase the user just asked for.
        var stuck = await GetStuckAsync(vaultId, cancellationToken);
        _ = stuck.FirstOrDefault(erase => erase.OperationId == operationId) ??
            throw new KeyNotFoundException("The Bitwarden erase is not stuck for this vault.");
        await operationStore.CompleteAsync(operationId, cancellationToken);
    }

    private static bool IsStuck(BitwardenPendingOperation operation) =>
        // Only a hard delete. A soft delete that would not land leaves the local row in the trash and
        // bound to the vault, so the next pull still sees the decision and the entry cannot grow back.
        operation.OperationType == BitwardenMutationOperationType.Delete &&
        operation.Status is BitwardenMutationStatus.Conflict or BitwardenMutationStatus.Failed;
}
