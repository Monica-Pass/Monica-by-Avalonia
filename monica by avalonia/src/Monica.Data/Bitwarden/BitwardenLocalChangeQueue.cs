using Monica.Core.Bitwarden;
using Monica.Core.Models;
using Monica.Data.Repositories;

namespace Monica.Data.Bitwarden;

/// <summary>
/// What one scan of the vault owed Bitwarden: work it booked, and the rows it could not book with the
/// reason the encoder gave. <see cref="Refused"/> always equals <see cref="Unsyncable"/>.Count, so a caller
/// that only reads the count cannot understate the list it is about to show.
/// </summary>
public sealed record BitwardenLocalChangeQueueResult(
    int Enqueued,
    int Refused,
    IReadOnlyList<BitwardenUnsyncableLocalChange> Unsyncable)
{
    public BitwardenLocalChangeQueueResult(int Enqueued, int Refused)
        : this(Enqueued, Refused, [])
    {
    }

    public int Drifted => Enqueued + Refused;
}

public interface IBitwardenLocalChangeQueue
{
    Task<BitwardenLocalChangeQueueResult> EnqueueDriftedAsync(
        long vaultId,
        BitwardenSymmetricKey vaultKey,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Turns local edits into upload work before the next pull runs. Drift is measured against the
/// baseline written by the last completed synchronization, because ordinary editor saves do not set
/// the dirty flag and a content hash is the only signal that survives a restart. Moving an entry between
/// folders is measured the same way, through the remote folder its local category resolves to rather than
/// the column a move leaves untouched (<see cref="BitwardenLocalFolderProjection"/>). An entry bound to this
/// vault by identity alone - published here, never confirmed by the server - is the other kind of work,
/// and it is queued as a create so the next pull cannot mistake it for a resurrected cipher. The third
/// kind is a trashed entry the server still holds live: the pull treats that difference as a local edit
/// it is about to overwrite, so without a queued delete every synchronization hands the entry back and
/// leaves another conflict backup behind. The fourth is that pair reversed - the entry restored here
/// while the server keeps it in its trash - and it needs its own route, because an update is accepted for
/// a trashed cipher without clearing the deletion, which leaves the pull to re-trash the row again.
/// </summary>
public sealed class BitwardenLocalChangeQueue(
    IMonicaRepository repository,
    IBitwardenSyncStateStore syncStateStore,
    IBitwardenPendingOperationStore operationStore,
    IBitwardenRemoteFolderStore folderStore) : IBitwardenLocalChangeQueue
{
    public async Task<BitwardenLocalChangeQueueResult> EnqueueDriftedAsync(
        long vaultId,
        BitwardenSymmetricKey vaultKey,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vaultKey);
        var baseline = await syncStateStore.GetPayloadHashesAsync(vaultId, cancellationToken);
        var enqueued = 0;
        var refused = 0;
        var unsyncable = new List<BitwardenUnsyncableLocalChange>();
        foreach (var candidate in await LoadCandidatesAsync(vaultId, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isNew = candidate.CipherId is null;
            var syncedHash = isNew ? null : baseline.GetValueOrDefault(candidate.CipherId!);
            // The baseline carries the server's marker, not content, for a cipher it is only keeping in its
            // trash, and a row we trashed too is therefore settled however the hashes differ; without this
            // a quiet vault owed one delete per synchronization.
            var heldInRemoteTrash = syncedHash is not null &&
                                    BitwardenPayloadFingerprint.IsRemoteDeletionMarker(syncedHash);
            // The entry came out of Monica's recycle bin while the last completed synchronization left the
            // server holding it in its own. Only a restore reconciles the two: a plain update is accepted
            // for a trashed cipher but does not clear the deletion, so every following pull would put the
            // row back in the trash and back up a change nobody made.
            var restoring = heldInRemoteTrash && !candidate.Deleted;
            // An entry with no baseline is one the remote snapshot did not confirm, so uploading it
            // would resurrect a cipher the server no longer has; only tracked identities may drift.
            // An entry with no identity at all is the other case: it was published here and the
            // server has never seen it, so the remote snapshot cannot speak for it either way.
            if (!isNew &&
                (syncedHash is null ||
                 string.Equals(syncedHash, candidate.PayloadHash, StringComparison.Ordinal) ||
                 (candidate.Deleted && heldInRemoteTrash)))
            {
                continue;
            }

            if (!isNew && string.IsNullOrWhiteSpace(candidate.ExpectedRemoteRevision))
            {
                // The identity came from the server but its revision did not, so an update here would have
                // to be sent unguarded and could quietly overwrite a change made elsewhere.
                refused++;
                unsyncable.Add(UnsyncableOf(
                    candidate,
                    new BitwardenPayloadRefusalInfo(
                        BitwardenPayloadRefusal.MissingRemoteRevision,
                        "The cipher has no remote revision to update against.")));
                continue;
            }

            // The route decides a deletion or a restore, so no cipher payload travels; the store still
            // requires something parseable in that column. That is also why trashing or reviving a note or
            // card needs no encoder for its content, while editing one does.
            var deletion = !isNew && candidate.Deleted;
            var encoded = deletion || restoring
                ? new EncoderOutcome("{}", null)
                : Encode(candidate, vaultKey);
            if (encoded.PayloadJson is null)
            {
                // Local shapes Bitwarden cannot carry stay local rather than failing the whole sync.
                refused++;
                unsyncable.Add(UnsyncableOf(candidate, encoded.Refusal!));
                continue;
            }

            var payload = encoded.PayloadJson;

            // The loader only keeps a row with no identity when it stands for something the server has
            // never seen, so one is derived from whichever local table it came from.
            var identity = candidate.CipherId ?? (candidate.Entry is not null
                ? BitwardenLocalCipherIdentity.ForPassword(candidate.Entry.Id)
                : BitwardenLocalCipherIdentity.ForSecureItem(candidate.SecureItem!.Id));
            await operationStore.EnqueueAsync(new BitwardenPendingOperation(
                Id: 0,
                VaultId: vaultId,
                CipherId: identity,
                OperationType: isNew
                    ? BitwardenMutationOperationType.Create
                    : deletion
                        ? BitwardenMutationOperationType.SoftDelete
                        : restoring
                            ? BitwardenMutationOperationType.Restore
                            : BitwardenMutationOperationType.Update,
                // A create has no remote state to guard against, and a revision there would make the
                // queue guard reject it as an update.
                ExpectedRemoteRevision: isNew ? null : candidate.ExpectedRemoteRevision,
                PayloadJson: payload,
                // Deliberately content-free: an entry edited three times before its first upload is one
                // cipher owed, not three, and each of those creates would have been posted separately.
                // A deletion keys the same way, because trashing an entry twice is one trash, not two.
                // A restore keys apart from both, because an entry revived after it was trashed owes the
                // server two different decisions and reusing the delete's key would resurrect the row the
                // delete already completed.
                IdempotencyKey: isNew
                    ? $"local-create:{vaultId}:{identity}"
                    : deletion
                        ? $"local-delete:{vaultId}:{identity}"
                        : restoring
                            ? $"local-restore:{vaultId}:{identity}"
                            : $"local-update:{vaultId}:{identity}:{candidate.PayloadHash}",
                Status: BitwardenMutationStatus.Pending,
                LastFailureClass: BitwardenFailureClass.None,
                AttemptCount: 0,
                NextAttemptAt: now,
                ClaimedAt: null,
                LastError: null,
                CreatedAt: now,
                UpdatedAt: now,
                // A completed push normally rewrites the baseline with the content the server now holds.
                // A restore carried no content, so recording this hash would claim the trash held the local
                // copy and an edit made in the same session would never be owed again.
                LocalPayloadHash: restoring ? null : candidate.PayloadHash), cancellationToken);
            enqueued++;
        }

        return new BitwardenLocalChangeQueueResult(enqueued, refused, unsyncable);
    }

    /// <summary>
    /// Either the payload to upload or the single reason this row cannot travel. The reason is read out of
    /// the encoder itself rather than guessed at by the caller, so the sync page can list an entry only when
    /// the queue would truly have refused it.
    /// </summary>
    private static EncoderOutcome Encode(Candidate candidate, BitwardenSymmetricKey vaultKey)
    {
        try
        {
            return new EncoderOutcome(candidate.Entry is not null
                ? BitwardenCipherPayloadBuilder.BuildLoginCipher(
                    candidate.Entry,
                    vaultKey,
                    candidate.CustomFields,
                    candidate.History)
                : BitwardenCipherPayloadBuilder.BuildSecureItemCipher(candidate.SecureItem!, vaultKey), null);
        }
        catch (BitwardenPayloadRefusalException refusal)
        {
            return new EncoderOutcome(null, new BitwardenPayloadRefusalInfo(refusal.Reason, refusal.Message));
        }
        catch (BitwardenProtocolException exception)
        {
            // A projection gate that named its field without classifying it: still a refusal, still worth
            // showing, and the encoder is the only place that knew.
            return new EncoderOutcome(null, new BitwardenPayloadRefusalInfo(
                BitwardenPayloadRefusal.UnsupportedContent,
                exception.Message));
        }
    }

    private static BitwardenUnsyncableLocalChange UnsyncableOf(
        Candidate candidate,
        BitwardenPayloadRefusalInfo refusal) =>
        new(
            (candidate.Entry?.Title ?? candidate.SecureItem?.Title ?? string.Empty).Trim(),
            candidate.Entry is not null,
            refusal.Reason);

    private sealed record EncoderOutcome(string? PayloadJson, BitwardenPayloadRefusalInfo? Refusal);

    private async Task<IReadOnlyList<Candidate>> LoadCandidatesAsync(
        long vaultId,
        CancellationToken cancellationToken)
    {
        var passwords = (await repository.GetPasswordsAsync(
                includeDeleted: true,
                includeArchived: true,
                cancellationToken))
            .Where(entry => entry.BitwardenVaultId == vaultId &&
                            // A published entry that has not been uploaded yet owes a create, unless it
                            // is already in the trash: the server never had it, so there is nothing to
                            // delete there and pushing it would create the very thing the user removed.
                            (entry.BitwardenCipherId is not null || !entry.IsDeleted))
            .ToList();
        var secureItems = (await repository.GetSecureItemsAsync(
                itemType: null,
                includeDeleted: true,
                cancellationToken))
            .Where(item => item.BitwardenVaultId == vaultId &&
                          // Same rule as passwords: a note or card published here but never uploaded owes
                          // a create, and one trashed before it ever left owes nothing at all.
                          (item.BitwardenCipherId is not null || !item.IsDeleted))
            .ToList();
        var customFields = await repository.GetCustomFieldsByEntryIdsAsync(
            passwords.Select(entry => entry.Id).ToArray(),
            cancellationToken);
        var histories = await repository.GetPasswordHistoryByEntryIdsAsync(
            passwords.Select(entry => entry.Id).ToArray(),
            cancellationToken);
        // Both the comparison below and the payload the encoder builds read the location through here, so a
        // move the user made between folders cannot be a difference this scan is blind to while the encoder
        // is already ready to send it.
        var boundCategories = BitwardenLocalFolderProjection.BoundCategories(
            await folderStore.GetAsync(vaultId, cancellationToken));
        var candidates = new List<Candidate>(passwords.Count + secureItems.Count);

        foreach (var stored in passwords)
        {
            var entry = BitwardenLocalFolderProjection.Project(stored, boundCategories);
            var fields = customFields.GetValueOrDefault(entry.Id) ?? [];
            var history = histories.GetValueOrDefault(entry.Id) ?? [];
            candidates.Add(new(
                entry.BitwardenCipherId,
                entry.BitwardenRevisionDate,
                BitwardenPayloadFingerprint.ForPassword(entry, fields, history),
                entry,
                null,
                entry.IsDeleted,
                fields,
                history));
        }

        foreach (var stored in secureItems)
        {
            var item = BitwardenLocalFolderProjection.Project(stored, boundCategories);
            candidates.Add(new(
                item.BitwardenCipherId,
                item.BitwardenRevisionDate,
                BitwardenPayloadFingerprint.ForSecureItem(item),
                null,
                item,
                item.IsDeleted,
                [],
                []));
        }

        return candidates;
    }

    private sealed record Candidate(
        string? CipherId,
        string? ExpectedRemoteRevision,
        string PayloadHash,
        PasswordEntry? Entry,
        SecureItem? SecureItem,
        bool Deleted,
        IReadOnlyList<CustomField> CustomFields,
        IReadOnlyList<PasswordHistoryEntry> History);
}
