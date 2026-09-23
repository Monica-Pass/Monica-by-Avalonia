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
/// the dirty flag and a content hash is the only signal that survives a restart.
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
        if (baseline.Count == 0)
        {
            return new BitwardenLocalChangeQueueResult(0, 0);
        }

        var enqueued = 0;
        var refused = 0;
        foreach (var candidate in await LoadCandidatesAsync(vaultId, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // An entry with no baseline is one the remote snapshot did not confirm, so uploading it
            // would resurrect a cipher the server no longer has; only tracked identities may drift.
            if (!baseline.TryGetValue(candidate.CipherId, out var syncedHash) ||
                string.Equals(syncedHash, candidate.PayloadHash, StringComparison.Ordinal))
            {
                continue;
            }

            if (candidate.Entry is null || string.IsNullOrWhiteSpace(candidate.ExpectedRemoteRevision))
            {
                refused++;
                continue;
            }

            string payload;
            try
            {
                payload = BitwardenCipherPayloadBuilder.BuildLoginCipher(
                    candidate.Entry,
                    vaultKey,
                    candidate.CustomFields,
                    candidate.History);
            }
            catch (BitwardenProtocolException)
            {
                // Local shapes Bitwarden cannot carry stay local rather than failing the whole sync.
                refused++;
                continue;
            }

            await operationStore.EnqueueAsync(new BitwardenPendingOperation(
                Id: 0,
                VaultId: vaultId,
                CipherId: candidate.CipherId,
                OperationType: BitwardenMutationOperationType.Update,
                ExpectedRemoteRevision: candidate.ExpectedRemoteRevision,
                PayloadJson: payload,
                IdempotencyKey: $"local-update:{vaultId}:{candidate.CipherId}:{candidate.PayloadHash}",
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

    private async Task<IReadOnlyList<Candidate>> LoadCandidatesAsync(
        long vaultId,
        CancellationToken cancellationToken)
    {
        var passwords = (await repository.GetPasswordsAsync(
                includeDeleted: true,
                includeArchived: true,
                cancellationToken))
            .Where(entry => entry.BitwardenVaultId == vaultId && entry.BitwardenCipherId is not null)
            .ToList();
        var secureItems = (await repository.GetSecureItemsAsync(
                itemType: null,
                includeDeleted: true,
                cancellationToken))
            .Where(item => item.BitwardenVaultId == vaultId && item.BitwardenCipherId is not null)
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
                entry.BitwardenCipherId!,
                entry.BitwardenRevisionDate,
                BitwardenPayloadFingerprint.ForPassword(entry, fields, history),
                entry,
                fields,
                history));
        }

        foreach (var item in secureItems)
        {
            // Secure items are listed so the count of owed-but-unable uploads stays honest; the
            // write-back encoder only carries login ciphers today.
            candidates.Add(new(
                item.BitwardenCipherId!,
                item.BitwardenRevisionDate,
                BitwardenPayloadFingerprint.ForSecureItem(item),
                null,
                [],
                []));
        }

        return candidates;
    }

    private sealed record Candidate(
        string CipherId,
        string? ExpectedRemoteRevision,
        string PayloadHash,
        PasswordEntry? Entry,
        IReadOnlyList<CustomField> CustomFields,
        IReadOnlyList<PasswordHistoryEntry> History);
}
