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
    DateTimeOffset LastAttemptAt,
    bool SuppressedThisRound = false);

public interface IBitwardenStuckEraseService
{
    Task<IReadOnlyList<BitwardenStuckErase>> GetStuckAsync(
        long vaultId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The full set the decision list offers: every erase that is terminal about (the ordinary
    /// <see cref="GetStuckAsync"/> rows) plus any erase whose booking just suppressed a remote resurrection
    /// this round. A still-pending erase sits in the second group only: the merge engine already honours its
    /// suppression (<see cref="BitwardenPullMergeService"/> reads "Delete and not Completed"), so without a
    /// row here the server copy it is holding back would have no visible owner for as long as the retries run.
    /// </summary>
    Task<IReadOnlyList<BitwardenStuckErase>> GetSuppressedAsync(
        long vaultId,
        IReadOnlySet<string> suppressedCipherIds,
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

    public async Task<IReadOnlyList<BitwardenStuckErase>> GetSuppressedAsync(
        long vaultId,
        IReadOnlySet<string> suppressedCipherIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(suppressedCipherIds);
        var operations = await operationStore.GetAsync(vaultId, cancellationToken);
        return operations
            .Where(operation => IsStuck(operation) || IsSuppressed(operation, suppressedCipherIds))
            // An erase is one decision about one cipher; a row that is both terminal about and just
            // suppressed a resurrection renders once, flagged, so the reason text reads "the server kept
            // its copy" rather than leaving the user to reconcile two rows for a single delete they made.
            .GroupBy(operation => operation.CipherId, StringComparer.Ordinal)
            .Select(group => group
                .Select(operation => new BitwardenStuckErase(
                    operation.Id,
                    operation.CipherId,
                    operation.Status,
                    operation.UpdatedAt,
                    suppressedCipherIds.Contains(operation.CipherId)))
                .OrderByDescending(erase => erase.SuppressedThisRound)
                .First())
            .ToList();
    }

    public async Task AbandonAsync(
        long vaultId,
        long operationId,
        CancellationToken cancellationToken = default)
    {
        // Re-read rather than trust the row on screen: the list is rendered while a synchronization runs,
        // and a row that has since been re-booked as pending - which a re-purge does - is one the queue is
        // about to send. Marking that completed would silently drop an erase the user just asked for. The
        // abandoned set is wider than "stuck" because a still-pending erase the user can see is one they are
        // allowed to drop, so the re-read here is by id against the operations themselves, not the old list.
        var operations = await operationStore.GetAsync(vaultId, cancellationToken);
        var operation = operations.FirstOrDefault(candidate => candidate.Id == operationId);
        if (operation is null || !IsAbandonable(operation))
        {
            throw new KeyNotFoundException("The Bitwarden erase is not one this vault can abandon.");
        }

        await operationStore.CompleteAsync(operationId, cancellationToken);
    }

    private static bool IsSuppressed(
        BitwardenPendingOperation operation,
        IReadOnlySet<string> suppressedCipherIds) =>
        operation.OperationType == BitwardenMutationOperationType.Delete &&
        suppressedCipherIds.Contains(operation.CipherId);

    private static bool IsAbandonable(BitwardenPendingOperation operation) =>
        // Completing a row the queue is mid-send on is a race the user cannot see; the send will land or
        // fail on its own, and the row only becomes a decision once it has settled. Everything else the
        // decision list can show - stuck or still retrying - is a choice the user is allowed to take back.
        operation.OperationType == BitwardenMutationOperationType.Delete &&
        operation.Status != BitwardenMutationStatus.Completed &&
        operation.Status != BitwardenMutationStatus.InFlight;

    private static bool IsStuck(BitwardenPendingOperation operation) =>
        // Only a hard delete. A soft delete that would not land leaves the local row in the trash and
        // bound to the vault, so the next pull still sees the decision and the entry cannot grow back.
        operation.OperationType == BitwardenMutationOperationType.Delete &&
        operation.Status is BitwardenMutationStatus.Conflict or BitwardenMutationStatus.Failed;
}
