using Monica.Core.Bitwarden;
using Monica.Core.Models;
using Monica.Data.Repositories;

namespace Monica.Data.Bitwarden;

public sealed record BitwardenLocalChangeQueueResult(int Enqueued, int Refused)
{
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
/// the dirty flag and a content hash is the only signal that survives a restart. An entry bound to this
/// vault by identity alone - published here, never confirmed by the server - is the other kind of work,
/// and it is queued as a create so the next pull cannot mistake it for a resurrected cipher. The third
/// kind is a trashed entry the server still holds live: the pull treats that difference as a local edit
/// it is about to overwrite, so without a queued delete every synchronization hands the entry back and
/// leaves another conflict backup behind.
/// </summary>
public sealed class BitwardenLocalChangeQueue(
    IMonicaRepository repository,
    IBitwardenSyncStateStore syncStateStore,
    IBitwardenPendingOperationStore operationStore) : IBitwardenLocalChangeQueue
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
        foreach (var candidate in await LoadCandidatesAsync(vaultId, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isNew = candidate.CipherId is null;
            // An entry with no baseline is one the remote snapshot did not confirm, so uploading it
            // would resurrect a cipher the server no longer has; only tracked identities may drift.
            // An entry with no identity at all is the other case: it was published here and the
            // server has never seen it, so the remote snapshot cannot speak for it either way.
            if (!isNew && (!baseline.TryGetValue(candidate.CipherId!, out var syncedHash) ||
                           string.Equals(syncedHash, candidate.PayloadHash, StringComparison.Ordinal) ||
                           // The baseline carries the server's marker, not content, for a cipher it is only
                           // keeping in its trash. A row we trashed too is therefore settled, however the
                           // hashes differ; without this a quiet vault owed one delete per synchronization.
                           (candidate.Deleted && BitwardenPayloadFingerprint.IsRemoteDeletionMarker(syncedHash))))
            {
                continue;
            }

            if (!isNew && string.IsNullOrWhiteSpace(candidate.ExpectedRemoteRevision))
            {
                refused++;
                continue;
            }

            // The route decides a deletion, so no cipher payload travels; the store still requires
            // something parseable in that column. That is also why trashing a note or card needs no
            // encoder for its content, while editing one does.
            var deletion = !isNew && candidate.Deleted;
            var payload = deletion ? "{}" : BuildPayload(candidate, vaultKey);
            if (payload is null)
            {
                // Local shapes Bitwarden cannot carry stay local rather than failing the whole sync.
                refused++;
                continue;
            }

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
                        : BitwardenMutationOperationType.Update,
                // A create has no remote state to guard against, and a revision there would make the
                // queue guard reject it as an update.
                ExpectedRemoteRevision: isNew ? null : candidate.ExpectedRemoteRevision,
                PayloadJson: payload,
                // Deliberately content-free: an entry edited three times before its first upload is one
                // cipher owed, not three, and each of those creates would have been posted separately.
                // A deletion keys the same way, because trashing an entry twice is one trash, not two.
                IdempotencyKey: isNew
                    ? $"local-create:{vaultId}:{identity}"
                    : deletion
                        ? $"local-delete:{vaultId}:{identity}"
                        : $"local-update:{vaultId}:{identity}:{candidate.PayloadHash}",
                Status: BitwardenMutationStatus.Pending,
                LastFailureClass: BitwardenFailureClass.None,
                AttemptCount: 0,
                NextAttemptAt: now,
                ClaimedAt: null,
                LastError: null,
                CreatedAt: now,
                UpdatedAt: now,
                LocalPayloadHash: candidate.PayloadHash), cancellationToken);
            enqueued++;
        }

        return new BitwardenLocalChangeQueueResult(enqueued, refused);
    }

    private static string? BuildPayload(Candidate candidate, BitwardenSymmetricKey vaultKey)
    {
        try
        {
            return candidate.Entry is not null
                ? BitwardenCipherPayloadBuilder.BuildLoginCipher(
                    candidate.Entry,
                    vaultKey,
                    candidate.CustomFields,
                    candidate.History)
                : BitwardenCipherPayloadBuilder.BuildSecureItemCipher(candidate.SecureItem!, vaultKey);
        }
        catch (BitwardenProtocolException)
        {
            return null;
        }
    }

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
        var candidates = new List<Candidate>(passwords.Count + secureItems.Count);

        foreach (var entry in passwords)
        {
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

        foreach (var item in secureItems)
        {
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
