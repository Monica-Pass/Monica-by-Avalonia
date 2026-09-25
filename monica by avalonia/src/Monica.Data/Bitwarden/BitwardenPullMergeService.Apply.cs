using System.Text.Json;
using Monica.Core.Bitwarden;
using Monica.Core.Models;

namespace Monica.Data.Bitwarden;

public sealed partial class BitwardenPullMergeService
{
    private async Task<LocalContext> LoadLocalContextAsync(
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
        var passwordIds = passwords.Select(entry => entry.Id).ToArray();
        var customFields = await repository.GetCustomFieldsByEntryIdsAsync(passwordIds, cancellationToken);
        var histories = await repository.GetPasswordHistoryByEntryIdsAsync(passwordIds, cancellationToken);
        var references = new List<BitwardenLocalCipherReference>(passwords.Count + secureItems.Count);

        foreach (var entry in passwords)
        {
            var fields = customFields.GetValueOrDefault(entry.Id) ?? [];
            var history = histories.GetValueOrDefault(entry.Id) ?? [];
            references.Add(new(
                entry.Id,
                entry.BitwardenCipherId!,
                entry.BitwardenFolderId,
                entry.BitwardenRevisionDate,
                entry.BitwardenCipherType,
                entry.IsDeleted,
                entry.BitwardenLocalModified,
                BitwardenPayloadFingerprint.ForPassword(entry, fields, history),
                "password",
                entry.UpdatedAt));
        }

        foreach (var item in secureItems)
        {
            references.Add(new(
                item.Id,
                item.BitwardenCipherId!,
                item.BitwardenFolderId,
                item.BitwardenRevisionDate,
                ToCipherType(item.ItemType),
                item.IsDeleted,
                item.BitwardenLocalModified,
                BitwardenPayloadFingerprint.ForSecureItem(item),
                "secure-item",
                item.UpdatedAt));
        }

        return new LocalContext(passwords, secureItems, customFields, histories, references);
    }

    private async Task ApplyActiveRemoteAsync(
        long vaultId,
        long? localId,
        BitwardenDecodedCipher decoded,
        IReadOnlyDictionary<string, long> folderBindings,
        CancellationToken cancellationToken)
    {
        long? categoryId = decoded.Metadata.FolderId is { } folderId &&
                           folderBindings.TryGetValue(folderId, out var boundCategoryId)
            ? boundCategoryId
            : null;
        if (decoded.Metadata.CipherType is 1 or 5)
        {
            var entry = NormalizePassword(decoded.Password!, decoded.Metadata, vaultId, categoryId);
            entry.Id = localId ?? 0;
            await repository.SavePasswordAsync(entry, cancellationToken);
            await repository.ReplaceCustomFieldsAsync(entry.Id, decoded.CustomFields, cancellationToken);
            await repository.ClearPasswordHistoryAsync(entry.Id, cancellationToken);
            foreach (var history in decoded.PasswordHistory)
            {
                await repository.SavePasswordHistoryAsync(new PasswordHistoryEntry
                {
                    EntryId = entry.Id,
                    Password = history.Password,
                    LastUsedAt = history.LastUsedAt
                }, cancellationToken);
            }

            return;
        }

        var item = NormalizeSecureItem(decoded.SecureItem!, decoded.Metadata, vaultId, categoryId);
        item.Id = localId ?? 0;
        await repository.SaveSecureItemAsync(item, cancellationToken);
    }

    private async Task ApplyRemoteDeletionAsync(
        LocalContext local,
        BitwardenMergeDecision decision,
        CancellationToken cancellationToken)
    {
        if (decision.LocalId is null)
        {
            throw new BitwardenProtocolException("A remote deletion has no local identity.");
        }

        var password = FindPassword(local, decision.CipherId);
        if (password is not null)
        {
            password.IsDeleted = true;
            password.DeletedAt = DateTimeOffset.UtcNow;
            password.BitwardenRevisionDate = decision.RemoteRevisionDate;
            password.BitwardenLocalModified = false;
            await repository.SavePasswordAsync(password, cancellationToken);
            return;
        }

        var secureItem = FindSecureItem(local, decision.CipherId);
        secureItem.IsDeleted = true;
        secureItem.DeletedAt = DateTimeOffset.UtcNow;
        secureItem.BitwardenRevisionDate = decision.RemoteRevisionDate;
        secureItem.BitwardenLocalModified = false;
        await repository.SaveSecureItemAsync(secureItem, cancellationToken);
    }

    private async Task MarkLocalCleanAsync(
        LocalContext local,
        BitwardenMergeDecision decision,
        CancellationToken cancellationToken)
    {
        var password = FindPassword(local, decision.CipherId);
        if (password is not null)
        {
            password.BitwardenLocalModified = false;
            await repository.SavePasswordAsync(password, cancellationToken);
            return;
        }

        var item = FindSecureItem(local, decision.CipherId);
        item.BitwardenLocalModified = false;
        await repository.SaveSecureItemAsync(item, cancellationToken);
    }

    // The two local tables number their rows independently, so resolving a row by its id alone can land
    // on the other table's row. Measured against a real server: trashing a note's cipher threw a live
    // login into the recycle bin and left the note's own row stuck on a stale revision. Cipher ids are
    // unique across both tables, so they are the only identity safe to locate a row with.
    private static PasswordEntry? FindPassword(LocalContext local, string cipherId) =>
        local.Passwords.SingleOrDefault(entry =>
            string.Equals(entry.BitwardenCipherId, cipherId, StringComparison.Ordinal));

    private static SecureItem FindSecureItem(LocalContext local, string cipherId) =>
        local.SecureItems.SingleOrDefault(item =>
            string.Equals(item.BitwardenCipherId, cipherId, StringComparison.Ordinal))
        ?? throw new BitwardenProtocolException($"No local secure item is bound to cipher {cipherId}.");

    private async Task SaveConflictBackupAsync(
        long vaultId,
        LocalContext local,
        BitwardenMergeDecision decision,
        CancellationToken cancellationToken)
    {
        string payload;
        string itemKind;
        long localItemId;
        var password = FindPassword(local, decision.CipherId);
        if (password is not null)
        {
            payload = JsonSerializer.Serialize(new
            {
                password,
                customFields = local.CustomFields.GetValueOrDefault(password.Id) ?? [],
                passwordHistory = local.Histories.GetValueOrDefault(password.Id) ?? []
            });
            itemKind = "password";
            localItemId = password.Id;
        }
        else
        {
            var secureItem = FindSecureItem(local, decision.CipherId);
            payload = JsonSerializer.Serialize(new { secureItem });
            itemKind = "secure-item";
            localItemId = secureItem.Id;
        }

        await conflictStore.SaveAsync(new BitwardenConflictBackup(
            0,
            vaultId,
            decision.CipherId,
            itemKind,
            localItemId,
            decision.LocalRevisionDate,
            decision.RemoteRevisionDate,
            payload,
            decision.Reason,
            DateTimeOffset.UtcNow), cancellationToken);
    }

    private static PasswordEntry NormalizePassword(
        PasswordEntry source,
        BitwardenRemoteCipherMetadata metadata,
        long? vaultId,
        long? categoryId)
    {
        var entry = source.CreateDetachedCopy();
        entry.BitwardenVaultId = vaultId;
        entry.BitwardenCipherId = metadata.CipherId;
        entry.BitwardenFolderId = metadata.FolderId;
        entry.BitwardenRevisionDate = metadata.RevisionDate;
        entry.BitwardenCipherType = metadata.CipherType;
        entry.BitwardenLocalModified = false;
        entry.CategoryId = categoryId;
        entry.IsDeleted = metadata.IsDeleted;
        entry.DeletedAt = metadata.IsDeleted ? metadata.UpdatedAt ?? DateTimeOffset.UtcNow : null;
        return entry;
    }

    private static SecureItem NormalizeSecureItem(
        SecureItem source,
        BitwardenRemoteCipherMetadata metadata,
        long? vaultId,
        long? categoryId)
    {
        var item = source.CreateDetachedCopy();
        item.BitwardenVaultId = vaultId;
        item.BitwardenCipherId = metadata.CipherId;
        item.BitwardenFolderId = metadata.FolderId;
        item.BitwardenRevisionDate = metadata.RevisionDate;
        item.BitwardenLocalModified = false;
        item.CategoryId = categoryId;
        item.IsDeleted = metadata.IsDeleted;
        item.DeletedAt = metadata.IsDeleted ? metadata.UpdatedAt ?? DateTimeOffset.UtcNow : null;
        return item;
    }

    private static int ToCipherType(VaultItemType itemType) => itemType switch
    {
        VaultItemType.Note => 2,
        VaultItemType.BankCard => 3,
        VaultItemType.Document => 4,
        _ => 2
    };

    private sealed record LocalContext(
        IReadOnlyList<PasswordEntry> Passwords,
        IReadOnlyList<SecureItem> SecureItems,
        IReadOnlyDictionary<long, IReadOnlyList<CustomField>> CustomFields,
        IReadOnlyDictionary<long, IReadOnlyList<PasswordHistoryEntry>> Histories,
        IReadOnlyList<BitwardenLocalCipherReference> References);
}
