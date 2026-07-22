using Monica.Core.Bitwarden;
using Monica.Core.Models;
using Monica.Data.Repositories;

namespace Monica.Data.Bitwarden;

public interface IBitwardenPullMergeService
{
    Task<BitwardenPullMergeResult> ApplyAsync(
        long vaultId,
        BitwardenPullSnapshot snapshot,
        IReadOnlyList<BitwardenDecodedCipher> decodedCiphers,
        CancellationToken cancellationToken = default);
}

public sealed partial class BitwardenPullMergeService(
    IMonicaRepository repository,
    IBitwardenRemoteFolderStore folderStore,
    IBitwardenConflictBackupStore conflictStore) : IBitwardenPullMergeService
{
    public async Task<BitwardenPullMergeResult> ApplyAsync(
        long vaultId,
        BitwardenPullSnapshot snapshot,
        IReadOnlyList<BitwardenDecodedCipher> decodedCiphers,
        CancellationToken cancellationToken = default)
    {
        if (vaultId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(vaultId));
        }

        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(decodedCiphers);
        var decodedById = ValidateDecoded(snapshot, decodedCiphers);
        var local = await LoadLocalContextAsync(vaultId, cancellationToken);
        var decisions = BitwardenMergeEngine.Plan(snapshot, local.References);

        await folderStore.ReplaceCompleteSnapshotAsync(
            vaultId,
            snapshot.Folders,
            snapshot.ReceivedAt,
            cancellationToken);
        var storedFolders = await folderStore.GetAsync(vaultId, cancellationToken);
        await EnsureLocalCategoriesAsync(vaultId, storedFolders, cancellationToken);
        var folderBindings = (await folderStore.GetAsync(vaultId, cancellationToken))
            .Where(folder => !folder.IsDeleted && folder.LocalCategoryId is not null)
            .ToDictionary(
                folder => folder.RemoteFolderId,
                folder => folder.LocalCategoryId!.Value,
                StringComparer.Ordinal);

        var added = 0;
        var updated = 0;
        var deleted = 0;
        var conflicts = 0;
        var markedClean = 0;
        var preserved = 0;
        var unchanged = 0;

        foreach (var decision in decisions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (decision.Action)
            {
                case BitwardenMergeAction.NoChange:
                    unchanged++;
                    break;
                case BitwardenMergeAction.PreserveLocalUnmatched:
                    preserved++;
                    break;
                case BitwardenMergeAction.MarkLocalClean:
                    await MarkLocalCleanAsync(local, decision, cancellationToken);
                    markedClean++;
                    break;
                case BitwardenMergeAction.AddRemote:
                    await ApplyActiveRemoteAsync(
                        vaultId,
                        null,
                        RequiredDecoded(decodedById, decision.CipherId),
                        folderBindings,
                        cancellationToken);
                    added++;
                    break;
                case BitwardenMergeAction.ApplyRemoteUpdate:
                    await ApplyActiveRemoteAsync(
                        vaultId,
                        decision.LocalId,
                        RequiredDecoded(decodedById, decision.CipherId),
                        folderBindings,
                        cancellationToken);
                    updated++;
                    break;
                case BitwardenMergeAction.ApplyRemoteDeletion:
                    await ApplyRemoteDeletionAsync(local, decision, cancellationToken);
                    deleted++;
                    break;
                case BitwardenMergeAction.CreateConflictBackupThenApplyRemote:
                    await SaveConflictBackupAsync(vaultId, local, decision, cancellationToken);
                    conflicts++;
                    if (snapshot.Ciphers.Single(cipher => cipher.CipherId == decision.CipherId).IsDeleted)
                    {
                        await ApplyRemoteDeletionAsync(local, decision, cancellationToken);
                        deleted++;
                    }
                    else
                    {
                        await ApplyActiveRemoteAsync(
                            vaultId,
                            decision.LocalId,
                            RequiredDecoded(decodedById, decision.CipherId),
                            folderBindings,
                            cancellationToken);
                        updated++;
                    }

                    break;
                default:
                    throw new BitwardenProtocolException(
                        $"Unsupported Bitwarden merge action: {decision.Action}.");
            }
        }

        return new BitwardenPullMergeResult(
            added,
            updated,
            deleted,
            conflicts,
            markedClean,
            preserved,
            unchanged);
    }

    private async Task EnsureLocalCategoriesAsync(
        long vaultId,
        IReadOnlyList<BitwardenStoredRemoteFolder> folders,
        CancellationToken cancellationToken)
    {
        var categories = await repository.GetCategoriesAsync(cancellationToken);
        var categoryByFolder = categories
            .Where(category => category.BitwardenVaultId == vaultId && category.BitwardenFolderId is not null)
            .ToDictionary(category => category.BitwardenFolderId!, StringComparer.Ordinal);
        var folderById = folders.ToDictionary(folder => folder.RemoteFolderId, StringComparer.Ordinal);

        foreach (var folder in folders
                     .Where(folder => !folder.IsDeleted && folder.LocalCategoryId is null)
                     .OrderBy(folder => FolderDepth(folder, folderById))
                     .ThenBy(folder => folder.RemoteFolderId, StringComparer.Ordinal))
        {
            if (!categoryByFolder.TryGetValue(folder.RemoteFolderId, out var category))
            {
                category = new Category
                {
                    Name = folder.Name,
                    BitwardenVaultId = vaultId,
                    BitwardenFolderId = folder.RemoteFolderId,
                    BitwardenParentFolderId = folder.ParentRemoteFolderId,
                    BitwardenLocalModified = false,
                    ParentCategoryId = folder.ParentRemoteFolderId is { } parentId &&
                                       categoryByFolder.TryGetValue(parentId, out var parent)
                        ? parent.Id
                        : null
                };
                await repository.SaveCategoryAsync(category, cancellationToken);
                categoryByFolder[folder.RemoteFolderId] = category;
            }

            await folderStore.BindLocalCategoryAsync(vaultId, folder.RemoteFolderId, category.Id, cancellationToken);
        }
    }

    private static int FolderDepth(
        BitwardenStoredRemoteFolder folder,
        IReadOnlyDictionary<string, BitwardenStoredRemoteFolder> folderById)
    {
        var depth = 0;
        var current = folder.ParentRemoteFolderId;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (current is not null && folderById.TryGetValue(current, out var parent) && seen.Add(current))
        {
            depth++;
            current = parent.ParentRemoteFolderId;
        }

        return depth;
    }

    private static IReadOnlyDictionary<string, BitwardenDecodedCipher> ValidateDecoded(
        BitwardenPullSnapshot snapshot,
        IReadOnlyList<BitwardenDecodedCipher> decodedCiphers)
    {
        var decodedById = new Dictionary<string, BitwardenDecodedCipher>(StringComparer.Ordinal);
        foreach (var decoded in decodedCiphers)
        {
            if (!decodedById.TryAdd(decoded.Metadata.CipherId, decoded))
            {
                throw new BitwardenProtocolException(
                    $"Duplicate decoded Bitwarden cipher identity: {decoded.Metadata.CipherId}.");
            }
        }

        var snapshotIds = snapshot.Ciphers.Select(cipher => cipher.CipherId).ToHashSet(StringComparer.Ordinal);
        if (decodedById.Keys.Any(cipherId => !snapshotIds.Contains(cipherId)))
        {
            throw new BitwardenProtocolException(
                "Decoded Bitwarden payload contains an identity outside the remote snapshot.");
        }

        foreach (var metadata in snapshot.Ciphers.Where(cipher => !cipher.IsDeleted))
        {
            var decoded = RequiredDecoded(decodedById, metadata.CipherId);
            if (decoded.Metadata != metadata)
            {
                throw new BitwardenProtocolException(
                    $"Decoded Bitwarden cipher metadata does not match snapshot identity {metadata.CipherId}.");
            }

            ValidateDecodedPayload(decoded);
        }

        return decodedById;
    }

    private static void ValidateDecodedPayload(BitwardenDecodedCipher decoded)
    {
        var type = decoded.Metadata.CipherType;
        var isPassword = type is 1 or 5;
        if (isPassword == (decoded.Password is null) || isPassword == (decoded.SecureItem is not null))
        {
            throw new BitwardenProtocolException(
                $"Decoded Bitwarden cipher {decoded.Metadata.CipherId} has an invalid payload for type {type}.");
        }

        if (!isPassword && (decoded.CustomFields.Count != 0 || decoded.PasswordHistory.Count != 0))
        {
            throw new BitwardenProtocolException(
                $"Decoded Bitwarden secure item {decoded.Metadata.CipherId} contains login-only child data.");
        }

        if (decoded.SecureItem is { } secureItem && !MatchesSecureItemType(type, secureItem.ItemType))
        {
            throw new BitwardenProtocolException(
                $"Decoded Bitwarden secure item {decoded.Metadata.CipherId} does not match type {type}.");
        }

        var fingerprint = isPassword
            ? BitwardenPayloadFingerprint.ForPassword(
                NormalizePassword(decoded.Password!, decoded.Metadata, null, null),
                decoded.CustomFields,
                decoded.PasswordHistory)
            : BitwardenPayloadFingerprint.ForSecureItem(
                NormalizeSecureItem(decoded.SecureItem!, decoded.Metadata, null, null));
        if (!string.Equals(fingerprint, decoded.Metadata.PayloadHash, StringComparison.Ordinal))
        {
            throw new BitwardenProtocolException(
                $"Decoded Bitwarden cipher {decoded.Metadata.CipherId} does not match its payload fingerprint.");
        }
    }

    private static bool MatchesSecureItemType(int cipherType, VaultItemType itemType) => cipherType switch
    {
        2 => itemType == VaultItemType.Note,
        3 => itemType == VaultItemType.BankCard,
        4 => itemType == VaultItemType.Document,
        _ => false
    };

    private static BitwardenDecodedCipher RequiredDecoded(
        IReadOnlyDictionary<string, BitwardenDecodedCipher> decodedById,
        string cipherId) =>
        decodedById.TryGetValue(cipherId, out var decoded)
            ? decoded
            : throw new BitwardenProtocolException(
                $"Active Bitwarden cipher {cipherId} has no decoded payload.");
}
