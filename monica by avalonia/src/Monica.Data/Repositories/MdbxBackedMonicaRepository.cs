using Monica.Core.Models;
using Monica.Data.Mdbx;
using Monica.Data.Services;

namespace Monica.Data.Repositories;

public sealed class MdbxBackedMonicaRepository(
    IMonicaRepository inner,
    IMdbxVaultStore mdbxVaultStore,
    IAttachmentContentStore? attachmentContentStore = null) : IMonicaRepository
{
    public async Task<IReadOnlyList<PasswordEntry>> GetPasswordsAsync(bool includeDeleted = false, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            return await inner.GetPasswordsAsync(includeDeleted, includeArchived, cancellationToken);
        }

        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var categoryById = categories.ToDictionary(category => category.Id);
        await MirrorUnboundPasswordsAsync(database, categoryById, cancellationToken);
        var entries = await mdbxVaultStore.GetPasswordsAsync(database, categories, includeDeleted, includeArchived, cancellationToken);
        return includeDeleted
            ? await FilterDeletedPasswordsBySqliteTombstonesAsync(entries, cancellationToken)
            : entries;
    }

    public async Task<long> SavePasswordAsync(PasswordEntry entry, CancellationToken cancellationToken = default)
    {
        ClearForeignMdbxBindingForNewPassword(entry);
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            return await inner.SavePasswordAsync(entry, cancellationToken);
        }

        await inner.SavePasswordAsync(entry, cancellationToken);
        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        await SavePasswordToMdbxAsync(database, entry, categories.ToDictionary(category => category.Id), cancellationToken);
        await inner.SavePasswordAsync(entry, cancellationToken);
        return entry.Id;
    }

    public async Task SoftDeletePasswordAsync(long id, CancellationToken cancellationToken = default)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is not null)
        {
            var entry = (await inner.GetPasswordsAsync(includeDeleted: true, includeArchived: true, cancellationToken))
                .FirstOrDefault(item => item.Id == id);
            if (entry is not null)
            {
                await mdbxVaultStore.SoftDeletePasswordAsync(database, entry, cancellationToken);
                var boundTotps = await GetMdbxBoundTotpsByPasswordIdAsync(database, id, includeDeleted: true, cancellationToken);
                foreach (var item in boundTotps)
                {
                    await mdbxVaultStore.SoftDeleteSecureItemAsync(database, item, cancellationToken);
                }
            }
        }

        await inner.SoftDeletePasswordAsync(id, cancellationToken);
    }

    public async Task RestorePasswordAsync(long id, CancellationToken cancellationToken = default)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is not null)
        {
            var entry = (await inner.GetPasswordsAsync(includeDeleted: true, includeArchived: true, cancellationToken))
                .FirstOrDefault(item => item.Id == id);
            if (entry is not null)
            {
                await mdbxVaultStore.RestorePasswordAsync(database, entry, cancellationToken);
                var boundTotps = await GetMdbxBoundTotpsByPasswordIdAsync(database, id, includeDeleted: true, cancellationToken);
                foreach (var item in boundTotps)
                {
                    await mdbxVaultStore.RestoreSecureItemAsync(database, item, cancellationToken);
                }
            }
        }

        await inner.RestorePasswordAsync(id, cancellationToken);
    }

    public async Task DeletePasswordPermanentlyAsync(long id, CancellationToken cancellationToken = default)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is not null)
        {
            var entry = (await inner.GetPasswordsAsync(includeDeleted: true, includeArchived: true, cancellationToken))
                .FirstOrDefault(item => item.Id == id);
            if (entry is not null)
            {
                foreach (var attachment in await GetPasswordAttachmentsForMdbxSaveAsync(
                             database,
                             id,
                             excludeAttachment: null,
                             includeMdbxOnlyAttachments: true,
                             preferMdbxPayload: false,
                             cancellationToken))
                {
                    await mdbxVaultStore.DeleteAttachmentAsync(database, attachment, cancellationToken);
                }

                await mdbxVaultStore.SoftDeletePasswordAsync(database, entry, cancellationToken);
                var boundTotps = await GetMdbxBoundTotpsByPasswordIdAsync(database, id, includeDeleted: true, cancellationToken);
                foreach (var item in boundTotps)
                {
                    await mdbxVaultStore.SoftDeleteSecureItemAsync(database, item, cancellationToken);
                }
            }
        }

        await inner.DeletePasswordPermanentlyAsync(id, cancellationToken);
    }

    private async Task<IReadOnlyList<Attachment>> GetPasswordAttachmentsForMdbxSaveAsync(
        LocalMdbxDatabase database,
        long entryId,
        Attachment? excludeAttachment,
        bool includeMdbxOnlyAttachments,
        bool preferMdbxPayload,
        CancellationToken cancellationToken)
    {
        var result = new List<Attachment>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var excludedKey = excludeAttachment is null ? null : GetAttachmentIdentityKey(excludeAttachment);
        var sqliteAttachments = await inner.GetAttachmentsAsync("PASSWORD", entryId, cancellationToken);
        if (!includeMdbxOnlyAttachments)
        {
            AddUniqueAttachments(result, seenKeys, sqliteAttachments, excludedKey);
            return result;
        }

        var mdbxAttachmentsByEntryId = await mdbxVaultStore.GetPasswordAttachmentsByEntryIdsAsync(database, [entryId], cancellationToken);
        if (preferMdbxPayload && mdbxAttachmentsByEntryId.TryGetValue(entryId, out var preferredMdbxAttachments))
        {
            AddUniqueAttachments(result, seenKeys, preferredMdbxAttachments, excludedKey);
            return result;
        }

        AddUniqueAttachments(result, seenKeys, sqliteAttachments, excludedKey);
        if (mdbxAttachmentsByEntryId.TryGetValue(entryId, out var mdbxAttachments))
        {
            AddUniqueAttachments(result, seenKeys, mdbxAttachments, excludedKey);
        }

        return result;
    }

    private static void AddUniqueAttachments(List<Attachment> result, HashSet<string> seenKeys, IReadOnlyList<Attachment> attachments, string? excludedKey)
    {
        foreach (var attachment in attachments)
        {
            var key = GetAttachmentIdentityKey(attachment);
            if (!string.Equals(key, excludedKey, StringComparison.OrdinalIgnoreCase) && seenKeys.Add(key))
            {
                result.Add(attachment);
            }
        }
    }

    private static string GetAttachmentIdentityKey(Attachment attachment)
    {
        if (attachment.Id > 0)
        {
            return $"id:{attachment.Id}";
        }

        if (!string.IsNullOrWhiteSpace(attachment.StoragePath))
        {
            return $"path:{attachment.StoragePath.Trim()}";
        }

        return $"name:{attachment.OwnerType}:{attachment.OwnerId}:{attachment.FileName}:{attachment.CreatedAt.ToUnixTimeMilliseconds()}";
    }

    public async Task<IReadOnlyList<CustomField>> GetCustomFieldsAsync(long entryId, CancellationToken cancellationToken = default)
    {
        var fieldsByEntryId = await GetCustomFieldsByEntryIdsAsync([entryId], cancellationToken);
        return fieldsByEntryId.TryGetValue(entryId, out var fields) ? fields : [];
    }

    public async Task<IReadOnlyDictionary<long, IReadOnlyList<CustomField>>> GetCustomFieldsByEntryIdsAsync(IReadOnlyList<long> entryIds, CancellationToken cancellationToken = default)
    {
        var ids = entryIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<long, IReadOnlyList<CustomField>>();
        }

        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            return await inner.GetCustomFieldsByEntryIdsAsync(ids, cancellationToken);
        }

        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        await MirrorUnboundPasswordsAsync(database, categories.ToDictionary(category => category.Id), cancellationToken);

        var existingIds = (await inner.GetPasswordsAsync(includeDeleted: true, includeArchived: true, cancellationToken))
            .Select(entry => entry.Id)
            .ToHashSet();
        ids = ids.Where(existingIds.Contains).ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<long, IReadOnlyList<CustomField>>();
        }

        var mdbxFields = new Dictionary<long, IReadOnlyList<CustomField>>(
            await mdbxVaultStore.GetPasswordCustomFieldsByEntryIdsAsync(database, ids, cancellationToken));
        var missingIds = ids.Where(id => !mdbxFields.ContainsKey(id)).ToArray();
        if (missingIds.Length == 0)
        {
            return mdbxFields;
        }

        foreach (var item in await inner.GetCustomFieldsByEntryIdsAsync(missingIds, cancellationToken))
        {
            mdbxFields[item.Key] = item.Value;
        }

        return mdbxFields;
    }

    public async Task ReplaceCustomFieldsAsync(long entryId, IReadOnlyList<CustomField> fields, CancellationToken cancellationToken = default)
    {
        await inner.ReplaceCustomFieldsAsync(entryId, fields, cancellationToken);

        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            return;
        }

        var entry = (await inner.GetPasswordsAsync(includeDeleted: true, includeArchived: true, cancellationToken))
            .FirstOrDefault(item => item.Id == entryId);
        if (entry is null)
        {
            return;
        }

        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        await SavePasswordToMdbxAsync(database, entry, categories.ToDictionary(category => category.Id), cancellationToken);
        await inner.SavePasswordAsync(entry, cancellationToken);
    }

    public async Task<IReadOnlyList<long>> SearchEntryIdsByCustomFieldContentAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            return await inner.SearchEntryIdsByCustomFieldContentAsync(query, cancellationToken);
        }

        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        await MirrorUnboundPasswordsAsync(database, categories.ToDictionary(category => category.Id), cancellationToken);

        var existingIds = (await inner.GetPasswordsAsync(includeDeleted: true, includeArchived: true, cancellationToken))
            .Select(entry => entry.Id)
            .ToHashSet();
        if (existingIds.Count == 0)
        {
            return [];
        }

        var mdbxMatches = await mdbxVaultStore.SearchPasswordEntryIdsByCustomFieldContentAsync(database, query, cancellationToken);
        mdbxMatches = mdbxMatches.Where(existingIds.Contains).ToList();
        var sqliteMatches = await inner.SearchEntryIdsByCustomFieldContentAsync(query, cancellationToken);
        if (sqliteMatches.Count == 0)
        {
            return mdbxMatches;
        }

        var mdbxFieldCoverage = await mdbxVaultStore.GetPasswordCustomFieldsByEntryIdsAsync(database, sqliteMatches, cancellationToken);
        return mdbxMatches
            .Concat(sqliteMatches.Where(id => !mdbxFieldCoverage.ContainsKey(id)))
            .Distinct()
            .OrderBy(id => id)
            .ToList();
    }

    public async Task<IReadOnlyList<Attachment>> GetAttachmentsAsync(string ownerType, long ownerId, CancellationToken cancellationToken = default)
    {
        if (!IsPasswordOwnerType(ownerType))
        {
            return await inner.GetAttachmentsAsync(ownerType, ownerId, cancellationToken);
        }

        var attachmentsByOwnerId = await GetAttachmentsByOwnerIdsAsync(ownerType, [ownerId], cancellationToken);
        return attachmentsByOwnerId.TryGetValue(ownerId, out var attachments) ? attachments : [];
    }

    public async Task<IReadOnlyDictionary<long, IReadOnlyList<Attachment>>> GetAttachmentsByOwnerIdsAsync(string ownerType, IReadOnlyList<long> ownerIds, CancellationToken cancellationToken = default)
    {
        if (!IsPasswordOwnerType(ownerType))
        {
            return await inner.GetAttachmentsByOwnerIdsAsync(ownerType, ownerIds, cancellationToken);
        }

        var ids = ownerIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<long, IReadOnlyList<Attachment>>();
        }

        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            return await inner.GetAttachmentsByOwnerIdsAsync(ownerType, ids, cancellationToken);
        }

        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        await MirrorUnboundPasswordsAsync(database, categories.ToDictionary(category => category.Id), cancellationToken);

        var existingIds = (await inner.GetPasswordsAsync(includeDeleted: true, includeArchived: true, cancellationToken))
            .Select(entry => entry.Id)
            .ToHashSet();
        ids = ids.Where(existingIds.Contains).ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<long, IReadOnlyList<Attachment>>();
        }

        var mdbxAttachments = new Dictionary<long, IReadOnlyList<Attachment>>(
            await mdbxVaultStore.GetPasswordAttachmentsByEntryIdsAsync(database, ids, cancellationToken));
        if (attachmentContentStore is not null)
        {
            var payloadMigrationCandidates = mdbxAttachments.Values
                .SelectMany(item => item)
                .Where(attachment => MdbxVaultStore.TryParseAttachmentStoragePath(attachment.StoragePath) is null)
                .ToArray();
            if (payloadMigrationCandidates.Length > 0)
            {
                var migratedPayloadAttachments = await MigratePasswordAttachmentsAsync(ownerType, payloadMigrationCandidates, cancellationToken);
                foreach (var group in migratedPayloadAttachments
                             .GroupBy(attachment => attachment.OwnerId)
                             .ToDictionary(group => group.Key, group => (IReadOnlyList<Attachment>)group.ToList()))
                {
                    mdbxAttachments[group.Key] = group.Value;
                }
            }
        }

        var missingIds = ids.Where(id => !mdbxAttachments.ContainsKey(id)).ToArray();
        if (missingIds.Length == 0)
        {
            return mdbxAttachments;
        }

        var sqliteAttachments = await inner.GetAttachmentsByOwnerIdsAsync(ownerType, missingIds, cancellationToken);
        if (attachmentContentStore is null)
        {
            foreach (var item in sqliteAttachments)
            {
                mdbxAttachments[item.Key] = item.Value;
            }

            return mdbxAttachments;
        }

        var migrated = await MigratePasswordAttachmentsAsync(
            ownerType,
            sqliteAttachments.Values.SelectMany(item => item).ToArray(),
            cancellationToken);

        foreach (var group in migrated
            .GroupBy(attachment => attachment.OwnerId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Attachment>)group.ToList()))
        {
            mdbxAttachments[group.Key] = group.Value;
        }

        return mdbxAttachments;
    }

    public async Task<byte[]?> TryReadAttachmentContentAsync(Attachment attachment, CancellationToken cancellationToken = default)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is not null && MdbxVaultStore.TryParseAttachmentStoragePath(attachment.StoragePath) is not null)
        {
            var content = await mdbxVaultStore.TryReadAttachmentContentAsync(database, attachment, cancellationToken);
            if (content is not null)
            {
                return content;
            }
        }

        return attachmentContentStore is null
            ? await inner.TryReadAttachmentContentAsync(attachment, cancellationToken)
            : await attachmentContentStore.TryReadAttachmentContentAsync(attachment, cancellationToken);
    }

    public async Task<long> SaveAttachmentAsync(Attachment attachment, CancellationToken cancellationToken = default)
    {
        var id = await inner.SaveAttachmentAsync(attachment, cancellationToken);
        if (IsPasswordOwnerType(attachment.OwnerType))
        {
            await SyncPasswordAttachmentsOwnerToMdbxAsync(attachment.OwnerId, cancellationToken);
        }

        return id;
    }

    public async Task<long> SaveAttachmentAsync(Attachment attachment, byte[] content, CancellationToken cancellationToken = default)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is not null &&
            content.Length > 0 &&
            string.Equals(attachment.OwnerType, "PASSWORD", StringComparison.OrdinalIgnoreCase))
        {
            var entry = (await inner.GetPasswordsAsync(includeDeleted: true, includeArchived: true, cancellationToken))
                .FirstOrDefault(item => item.Id == attachment.OwnerId);
            if (entry is not null)
            {
                if (IsUnboundFromMdbx(entry))
                {
                    await SavePasswordToMdbxAsync(database, entry, (await EnsureMdbxCategoriesAsync(database, cancellationToken)).ToDictionary(category => category.Id), cancellationToken);
                    await inner.SavePasswordAsync(entry, cancellationToken);
                }

                await mdbxVaultStore.SavePasswordAttachmentAsync(database, entry, attachment, content, cancellationToken);
            }
        }

        var id = await inner.SaveAttachmentAsync(attachment, cancellationToken);
        if (string.Equals(attachment.OwnerType, "PASSWORD", StringComparison.OrdinalIgnoreCase))
        {
            await SyncPasswordAttachmentsOwnerToMdbxAsync(attachment.OwnerId, cancellationToken);
        }

        return id;
    }

    public async Task DeleteAttachmentAsync(long id, CancellationToken cancellationToken = default)
    {
        var attachment = await FindPasswordAttachmentAsync(id, cancellationToken);
        if (attachment is null)
        {
            await inner.DeleteAttachmentAsync(id, cancellationToken);
            return;
        }

        await DeleteAttachmentAsync(id, attachment, cancellationToken);
    }

    private async Task DeletePasswordAttachmentContentAsync(Attachment attachment, CancellationToken cancellationToken)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is not null)
        {
            await mdbxVaultStore.DeleteAttachmentAsync(database, attachment, cancellationToken);
        }
    }

    private async Task SyncPasswordAttachmentOwnerAfterDeleteAsync(long? ownerId, Attachment deletedAttachment, CancellationToken cancellationToken)
    {
        if (ownerId is not null)
        {
            await SyncPasswordAttachmentsOwnerToMdbxAsync(ownerId.Value, deletedAttachment, cancellationToken);
        }
    }

    public async Task DeleteAttachmentAsync(long id, Attachment attachment, CancellationToken cancellationToken = default)
    {
        var ownerId = IsPasswordOwnerType(attachment.OwnerType) && attachment.OwnerId > 0
            ? attachment.OwnerId
            : await FindPasswordAttachmentOwnerIdAsync(id, cancellationToken);
        await DeletePasswordAttachmentContentAsync(attachment, cancellationToken);

        await inner.DeleteAttachmentAsync(id, attachment, cancellationToken);
        await SyncPasswordAttachmentOwnerAfterDeleteAsync(ownerId, attachment, cancellationToken);
    }

    public async Task<IReadOnlyList<PasswordHistoryEntry>> GetPasswordHistoryAsync(long entryId, CancellationToken cancellationToken = default)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            return await inner.GetPasswordHistoryAsync(entryId, cancellationToken);
        }

        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        await MirrorUnboundPasswordsAsync(database, categories.ToDictionary(category => category.Id), cancellationToken);

        var existing = (await inner.GetPasswordsAsync(includeDeleted: true, includeArchived: true, cancellationToken))
            .Any(entry => entry.Id == entryId);
        if (!existing)
        {
            return [];
        }

        return await mdbxVaultStore.GetPasswordHistoryAsync(database, entryId, cancellationToken)
            ?? await inner.GetPasswordHistoryAsync(entryId, cancellationToken);
    }

    public async Task<long> SavePasswordHistoryAsync(PasswordHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        var id = await inner.SavePasswordHistoryAsync(entry, cancellationToken);
        await SyncPasswordHistoryOwnerToMdbxAsync(entry.EntryId, cancellationToken);
        return id;
    }

    public async Task TrimPasswordHistoryAsync(long entryId, int limit, CancellationToken cancellationToken = default)
    {
        await inner.TrimPasswordHistoryAsync(entryId, limit, cancellationToken);
        await SyncPasswordHistoryOwnerToMdbxAsync(entryId, cancellationToken);
    }

    public async Task DeletePasswordHistoryAsync(long id, CancellationToken cancellationToken = default)
    {
        var entryId = await FindPasswordHistoryOwnerIdAsync(id, cancellationToken);
        await inner.DeletePasswordHistoryAsync(id, cancellationToken);
        if (entryId is not null)
        {
            await SyncPasswordHistoryOwnerToMdbxAsync(entryId.Value, cancellationToken);
        }
    }

    public async Task ClearPasswordHistoryAsync(long entryId, CancellationToken cancellationToken = default)
    {
        await inner.ClearPasswordHistoryAsync(entryId, cancellationToken);
        await SyncPasswordHistoryOwnerToMdbxAsync(entryId, cancellationToken);
    }

    public Task RecordPasswordQuickAccessAsync(long passwordId, CancellationToken cancellationToken = default) =>
        inner.RecordPasswordQuickAccessAsync(passwordId, cancellationToken);

    public Task<IReadOnlyList<PasswordQuickAccessRecord>> GetPasswordQuickAccessRecordsAsync(CancellationToken cancellationToken = default) =>
        inner.GetPasswordQuickAccessRecordsAsync(cancellationToken);

    public async Task<IReadOnlyList<SecureItem>> GetSecureItemsAsync(VaultItemType? itemType = null, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            return await inner.GetSecureItemsAsync(itemType, includeDeleted, cancellationToken);
        }

        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var categoryById = categories.ToDictionary(category => category.Id);
        await MirrorUnboundSecureItemsAsync(database, categoryById, cancellationToken);
        var items = await mdbxVaultStore.GetSecureItemsAsync(database, categories, itemType, includeDeleted, cancellationToken);
        return includeDeleted
            ? await FilterDeletedSecureItemsBySqliteTombstonesAsync(items, cancellationToken)
            : items;
    }

    public async Task<IReadOnlyList<SecureItem>> GetSecureItemsByBoundPasswordIdAsync(long passwordId, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            return await inner.GetSecureItemsByBoundPasswordIdAsync(passwordId, includeDeleted, cancellationToken);
        }

        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var categoryById = categories.ToDictionary(category => category.Id);
        await MirrorUnboundSecureItemsAsync(database, categoryById, cancellationToken);
        var items = await mdbxVaultStore.GetSecureItemsAsync(database, categories, VaultItemType.Totp, includeDeleted, cancellationToken);
        if (includeDeleted)
        {
            items = await FilterDeletedSecureItemsBySqliteTombstonesAsync(items, cancellationToken);
        }

        return items.Where(item => item.BoundPasswordId == passwordId).ToList();
    }

    public async Task<long> SaveSecureItemAsync(SecureItem item, CancellationToken cancellationToken = default)
    {
        ClearForeignMdbxBindingForNewSecureItem(item);
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            return await inner.SaveSecureItemAsync(item, cancellationToken);
        }

        await inner.SaveSecureItemAsync(item, cancellationToken);
        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        await SaveSecureItemToMdbxAsync(database, item, categories.ToDictionary(category => category.Id), cancellationToken);
        await inner.SaveSecureItemAsync(item, cancellationToken);
        return item.Id;
    }

    public async Task SoftDeleteSecureItemAsync(long id, CancellationToken cancellationToken = default)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is not null)
        {
            var item = (await inner.GetSecureItemsAsync(includeDeleted: true, cancellationToken: cancellationToken))
                .FirstOrDefault(item => item.Id == id);
            if (item is not null)
            {
                await mdbxVaultStore.SoftDeleteSecureItemAsync(database, item, cancellationToken);
            }
        }

        await inner.SoftDeleteSecureItemAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        return database is null
            ? await inner.GetCategoriesAsync(cancellationToken)
            : await EnsureMdbxCategoriesAsync(database, cancellationToken);
    }

    public async Task<long> SaveCategoryAsync(Category category, CancellationToken cancellationToken = default)
    {
        ClearForeignMdbxBindingForNewCategory(category);
        await inner.SaveCategoryAsync(category, cancellationToken);
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is not null)
        {
            await mdbxVaultStore.SaveCategoryAsync(database, category, cancellationToken);
            await inner.SaveCategoryAsync(category, cancellationToken);
        }

        return category.Id;
    }

    public async Task DeleteCategoryAsync(long id, CancellationToken cancellationToken = default)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is not null)
        {
            var categories = (await EnsureMdbxCategoriesAsync(database, cancellationToken))
                .Where(category => category.Id != id)
                .ToDictionary(category => category.Id);
            foreach (var entry in (await inner.GetPasswordsAsync(includeDeleted: true, includeArchived: true, cancellationToken))
                         .Where(entry => entry.CategoryId == id))
            {
                entry.CategoryId = null;
                await SavePasswordToMdbxAsync(database, entry, categories, cancellationToken);
                await inner.SavePasswordAsync(entry, cancellationToken);
            }

            foreach (var item in (await inner.GetSecureItemsAsync(includeDeleted: true, cancellationToken: cancellationToken))
                         .Where(item => item.CategoryId == id))
            {
                item.CategoryId = null;
                await SaveSecureItemToMdbxAsync(database, item, categories, cancellationToken);
                await inner.SaveSecureItemAsync(item, cancellationToken);
            }
        }

        await inner.DeleteCategoryAsync(id, cancellationToken);
    }

    public Task<IReadOnlyList<LocalMdbxDatabase>> GetMdbxDatabasesAsync(CancellationToken cancellationToken = default) =>
        inner.GetMdbxDatabasesAsync(cancellationToken);

    public Task<long> SaveMdbxDatabaseAsync(LocalMdbxDatabase database, CancellationToken cancellationToken = default) =>
        inner.SaveMdbxDatabaseAsync(database, cancellationToken);

    public Task<IReadOnlyList<OperationLog>> GetOperationLogsAsync(int limit = 100, string? itemType = null, CancellationToken cancellationToken = default) =>
        inner.GetOperationLogsAsync(limit, itemType, cancellationToken);

    public Task LogAsync(OperationLog log, CancellationToken cancellationToken = default) =>
        inner.LogAsync(log, cancellationToken);

    public async Task ClearVaultDataAsync(VaultClearScope scope, CancellationToken cancellationToken = default)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is not null)
        {
            var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
            switch (scope)
            {
                case VaultClearScope.Passwords:
                    await mdbxVaultStore.DetachSecureItemsFromPasswordsAsync(database, categories, cancellationToken);
                    await mdbxVaultStore.SoftDeletePasswordEntriesAsync(database, cancellationToken);
                    break;
                case VaultClearScope.SecureItems:
                    await mdbxVaultStore.SoftDeleteSecureItemEntriesAsync(database, cancellationToken);
                    break;
                default:
                    await mdbxVaultStore.SoftDeletePasswordEntriesAsync(database, cancellationToken);
                    await mdbxVaultStore.SoftDeleteSecureItemEntriesAsync(database, cancellationToken);
                    break;
            }
        }

        await inner.ClearVaultDataAsync(scope, cancellationToken);
    }

    private async Task<LocalMdbxDatabase?> GetDefaultLocalMdbxDatabaseAsync(CancellationToken cancellationToken)
    {
        if (!mdbxVaultStore.IsAvailable)
        {
            return null;
        }

        var databases = await inner.GetMdbxDatabasesAsync(cancellationToken);
        return databases
            .Where(database => database.IsDefault)
            .Where(CanUseMdbxWorkingCopy)
            .Where(database => !string.IsNullOrWhiteSpace(database.EncryptedPassword))
            .Where(database => !string.IsNullOrWhiteSpace(database.WorkingCopyPath ?? database.FilePath))
            .OrderBy(database => database.SortOrder)
            .ThenBy(database => database.Id)
            .FirstOrDefault();
    }

    private static bool CanUseMdbxWorkingCopy(LocalMdbxDatabase database) =>
        database.StorageLocation is MdbxStorageLocation.Internal or MdbxStorageLocation.External ||
        !string.IsNullOrWhiteSpace(database.WorkingCopyPath);

    private async Task<IReadOnlyList<Category>> EnsureMdbxCategoriesAsync(LocalMdbxDatabase database, CancellationToken cancellationToken)
    {
        var categories = (await inner.GetCategoriesAsync(cancellationToken)).ToList();
        foreach (var category in categories.Where(category => category.MdbxDatabaseId != database.Id || string.IsNullOrWhiteSpace(category.MdbxFolderId)))
        {
            await mdbxVaultStore.SaveCategoryAsync(database, category, cancellationToken);
            await inner.SaveCategoryAsync(category, cancellationToken);
        }

        return categories;
    }

    private async Task MirrorUnboundPasswordsAsync(LocalMdbxDatabase database, IReadOnlyDictionary<long, Category> categories, CancellationToken cancellationToken)
    {
        var sqlitePasswords = await inner.GetPasswordsAsync(includeDeleted: true, includeArchived: true, cancellationToken);
        var customFieldsByEntryId = await inner.GetCustomFieldsByEntryIdsAsync(
            sqlitePasswords.Select(entry => entry.Id).ToArray(),
            cancellationToken);
        var attachmentsByEntryId = await inner.GetAttachmentsByOwnerIdsAsync(
            "PASSWORD",
            sqlitePasswords.Select(entry => entry.Id).ToArray(),
            cancellationToken);
        foreach (var entry in sqlitePasswords.Where(IsUnboundFromMdbx))
        {
            await mdbxVaultStore.SavePasswordAsync(
                database,
                entry,
                customFieldsByEntryId.TryGetValue(entry.Id, out var fields) ? fields : [],
                await inner.GetPasswordHistoryAsync(entry.Id, cancellationToken),
                attachmentsByEntryId.TryGetValue(entry.Id, out var attachments) ? attachments : [],
                categories,
                cancellationToken);
            if (entry.IsDeleted)
            {
                await mdbxVaultStore.SoftDeletePasswordAsync(database, entry, cancellationToken);
            }

            await inner.SavePasswordAsync(entry, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<PasswordEntry>> FilterDeletedPasswordsBySqliteTombstonesAsync(
        IReadOnlyList<PasswordEntry> entries,
        CancellationToken cancellationToken)
    {
        if (!entries.Any(entry => entry.IsDeleted))
        {
            return entries;
        }

        var deletedMdbxEntryIds = (await inner.GetPasswordsAsync(includeDeleted: true, includeArchived: true, cancellationToken))
            .Where(entry => entry.IsDeleted)
            .Select(entry => entry.MdbxFolderId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return entries
            .Where(entry => !entry.IsDeleted || (!string.IsNullOrWhiteSpace(entry.MdbxFolderId) && deletedMdbxEntryIds.Contains(entry.MdbxFolderId)))
            .ToList();
    }

    private async Task MirrorUnboundSecureItemsAsync(LocalMdbxDatabase database, IReadOnlyDictionary<long, Category> categories, CancellationToken cancellationToken)
    {
        var sqliteItems = await inner.GetSecureItemsAsync(includeDeleted: true, cancellationToken: cancellationToken);
        foreach (var item in sqliteItems.Where(IsUnboundFromMdbx))
        {
            await SaveSecureItemToMdbxAsync(database, item, categories, cancellationToken);
            if (item.IsDeleted)
            {
                await mdbxVaultStore.SoftDeleteSecureItemAsync(database, item, cancellationToken);
            }

            await inner.SaveSecureItemAsync(item, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<SecureItem>> FilterDeletedSecureItemsBySqliteTombstonesAsync(
        IReadOnlyList<SecureItem> items,
        CancellationToken cancellationToken)
    {
        if (!items.Any(item => item.IsDeleted))
        {
            return items;
        }

        var deletedMdbxEntryIds = (await inner.GetSecureItemsAsync(includeDeleted: true, cancellationToken: cancellationToken))
            .Where(item => item.IsDeleted)
            .Select(item => item.MdbxFolderId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return items
            .Where(item => !item.IsDeleted || (!string.IsNullOrWhiteSpace(item.MdbxFolderId) && deletedMdbxEntryIds.Contains(item.MdbxFolderId)))
            .ToList();
    }

    private async Task<IReadOnlyList<SecureItem>> GetMdbxBoundTotpsByPasswordIdAsync(
        LocalMdbxDatabase database,
        long passwordId,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        await MirrorUnboundSecureItemsAsync(database, categories.ToDictionary(category => category.Id), cancellationToken);
        var items = await mdbxVaultStore.GetSecureItemsAsync(database, categories, VaultItemType.Totp, includeDeleted, cancellationToken);
        return items
            .Where(item => item.BoundPasswordId == passwordId)
            .ToList();
    }

    private async Task<IReadOnlyList<Attachment>> MigratePasswordAttachmentsAsync(
        string ownerType,
        IReadOnlyList<Attachment> attachments,
        CancellationToken cancellationToken)
    {
        if (!IsPasswordOwnerType(ownerType) || attachmentContentStore is null || attachments.Count == 0)
        {
            return attachments;
        }

        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            return attachments;
        }

        var categoryById = (await EnsureMdbxCategoriesAsync(database, cancellationToken))
            .ToDictionary(category => category.Id);
        var candidates = attachments
            .Where(attachment => attachment.OwnerId > 0)
            .Where(attachment => !string.IsNullOrWhiteSpace(attachment.StoragePath))
            .Where(attachment => MdbxVaultStore.TryParseAttachmentStoragePath(attachment.StoragePath) is null)
            .ToArray();
        if (candidates.Length == 0)
        {
            return attachments;
        }

        var ownerIds = candidates.Select(attachment => attachment.OwnerId).Distinct().ToHashSet();
        var passwords = (await inner.GetPasswordsAsync(includeDeleted: true, includeArchived: true, cancellationToken))
            .Where(password => ownerIds.Contains(password.Id))
            .ToDictionary(password => password.Id);
        var migratedById = new Dictionary<long, Attachment>();

        foreach (var attachment in candidates)
        {
            try
            {
                if (!passwords.TryGetValue(attachment.OwnerId, out var entry))
                {
                    continue;
                }

                var content = await attachmentContentStore.TryReadAttachmentContentAsync(attachment, cancellationToken);
                if (content is null || content.Length == 0)
                {
                    continue;
                }

                var original = new Attachment
                {
                    Id = attachment.Id,
                    OwnerType = attachment.OwnerType,
                    OwnerId = attachment.OwnerId,
                    FileName = attachment.FileName,
                    ContentType = attachment.ContentType,
                    StoragePath = attachment.StoragePath,
                    SizeBytes = attachment.SizeBytes,
                    CreatedAt = attachment.CreatedAt,
                    BitwardenVaultId = attachment.BitwardenVaultId,
                    KeepassBinaryRef = attachment.KeepassBinaryRef
                };

                if (IsUnboundFromMdbx(entry))
                {
                    await SavePasswordToMdbxAsync(database, entry, categoryById, cancellationToken);
                    await inner.SavePasswordAsync(entry, cancellationToken);
                }

                await mdbxVaultStore.SavePasswordAttachmentAsync(database, entry, attachment, content, cancellationToken);
                await inner.SaveAttachmentAsync(attachment, cancellationToken);
                await SyncPasswordAttachmentsOwnerToMdbxAsync(entry.Id, cancellationToken);
                await attachmentContentStore.DeleteAttachmentContentAsync(original, cancellationToken);
                migratedById[attachment.Id] = attachment;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                continue;
            }
        }

        if (migratedById.Count == 0)
        {
            return attachments;
        }

        return attachments
            .Select(attachment => migratedById.TryGetValue(attachment.Id, out var migrated) ? migrated : attachment)
            .ToArray();
    }

    private static bool IsUnboundFromMdbx(PasswordEntry entry) =>
        entry.MdbxDatabaseId is null && string.IsNullOrWhiteSpace(entry.MdbxFolderId);

    private static void ClearForeignMdbxBindingForNewPassword(PasswordEntry entry)
    {
        if (entry.Id != 0)
        {
            return;
        }

        entry.MdbxDatabaseId = null;
        entry.MdbxFolderId = null;
    }

    private async Task SavePasswordToMdbxAsync(
        LocalMdbxDatabase database,
        PasswordEntry entry,
        IReadOnlyDictionary<long, Category> categories,
        CancellationToken cancellationToken)
    {
        var customFields = entry.Id > 0
            ? await inner.GetCustomFieldsAsync(entry.Id, cancellationToken)
            : [];
        var passwordHistory = entry.Id > 0
            ? await inner.GetPasswordHistoryAsync(entry.Id, cancellationToken)
            : [];
        var attachments = entry.Id > 0
            ? await GetPasswordAttachmentsForMdbxSaveAsync(
                database,
                entry.Id,
                excludeAttachment: null,
                includeMdbxOnlyAttachments: entry.MdbxDatabaseId is not null,
                preferMdbxPayload: true,
                cancellationToken)
            : [];
        await mdbxVaultStore.SavePasswordAsync(database, entry, customFields, passwordHistory, attachments, categories, cancellationToken);
    }

    private Task SyncPasswordAttachmentsOwnerToMdbxAsync(long entryId, CancellationToken cancellationToken) =>
        SyncPasswordAttachmentsOwnerToMdbxAsync(entryId, deletedAttachment: null, cancellationToken);

    private async Task SyncPasswordAttachmentsOwnerToMdbxAsync(long entryId, Attachment? deletedAttachment, CancellationToken cancellationToken)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            return;
        }

        var entry = (await inner.GetPasswordsAsync(includeDeleted: true, includeArchived: true, cancellationToken))
            .FirstOrDefault(item => item.Id == entryId);
        if (entry is null)
        {
            return;
        }

        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var customFields = entry.Id > 0
            ? await inner.GetCustomFieldsAsync(entry.Id, cancellationToken)
            : [];
        var passwordHistory = entry.Id > 0
            ? await inner.GetPasswordHistoryAsync(entry.Id, cancellationToken)
            : [];
        var attachments = entry.Id > 0
            ? await GetPasswordAttachmentsForMdbxSaveAsync(
                database,
                entry.Id,
                deletedAttachment,
                includeMdbxOnlyAttachments: entry.MdbxDatabaseId is not null,
                preferMdbxPayload: false,
                cancellationToken)
            : [];
        await mdbxVaultStore.SavePasswordAsync(database, entry, customFields, passwordHistory, attachments, categories.ToDictionary(category => category.Id), cancellationToken);
        await inner.SavePasswordAsync(entry, cancellationToken);
    }

    private async Task<long?> FindPasswordAttachmentOwnerIdAsync(long attachmentId, CancellationToken cancellationToken)
    {
        return (await FindPasswordAttachmentAsync(attachmentId, cancellationToken))?.OwnerId;
    }

    private async Task<Attachment?> FindPasswordAttachmentAsync(long attachmentId, CancellationToken cancellationToken)
    {
        if (attachmentId <= 0)
        {
            return null;
        }

        var passwords = await inner.GetPasswordsAsync(includeDeleted: true, includeArchived: true, cancellationToken);
        foreach (var password in passwords)
        {
            var attachment = (await inner.GetAttachmentsAsync("PASSWORD", password.Id, cancellationToken))
                .FirstOrDefault(attachment => attachment.Id == attachmentId);
            if (attachment is not null)
            {
                return attachment;
            }
        }

        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            return null;
        }

        var ids = passwords.Select(password => password.Id).Where(id => id > 0).Distinct().ToArray();
        var mdbxAttachmentsByEntryId = await mdbxVaultStore.GetPasswordAttachmentsByEntryIdsAsync(database, ids, cancellationToken);
        return mdbxAttachmentsByEntryId.Values
            .SelectMany(attachments => attachments)
            .FirstOrDefault(attachment => attachment.Id == attachmentId);
    }

    private async Task SyncPasswordHistoryOwnerToMdbxAsync(long entryId, CancellationToken cancellationToken)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            return;
        }

        var entry = (await inner.GetPasswordsAsync(includeDeleted: true, includeArchived: true, cancellationToken))
            .FirstOrDefault(item => item.Id == entryId);
        if (entry is null)
        {
            return;
        }

        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        await SavePasswordToMdbxAsync(database, entry, categories.ToDictionary(category => category.Id), cancellationToken);
        await inner.SavePasswordAsync(entry, cancellationToken);
    }

    private async Task<long?> FindPasswordHistoryOwnerIdAsync(long historyId, CancellationToken cancellationToken)
    {
        if (historyId <= 0)
        {
            return null;
        }

        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is not null)
        {
            var ownerId = await mdbxVaultStore.FindPasswordHistoryOwnerIdAsync(database, historyId, cancellationToken);
            if (ownerId is not null)
            {
                return ownerId;
            }
        }

        foreach (var password in await inner.GetPasswordsAsync(includeDeleted: true, includeArchived: true, cancellationToken))
        {
            if ((await inner.GetPasswordHistoryAsync(password.Id, cancellationToken)).Any(entry => entry.Id == historyId))
            {
                return password.Id;
            }
        }

        return null;
    }

    private async Task SaveSecureItemToMdbxAsync(
        LocalMdbxDatabase database,
        SecureItem item,
        IReadOnlyDictionary<long, Category> categories,
        CancellationToken cancellationToken)
    {
        await mdbxVaultStore.SaveSecureItemAsync(database, item, categories, cancellationToken);
        if (attachmentContentStore is not null && await MigrateSecureItemImagePathsAsync(database, item, cancellationToken))
        {
            await mdbxVaultStore.SaveSecureItemAsync(database, item, categories, cancellationToken);
        }
    }

    private async Task<bool> MigrateSecureItemImagePathsAsync(LocalMdbxDatabase database, SecureItem item, CancellationToken cancellationToken)
    {
        if (attachmentContentStore is null || item.Id <= 0 || string.IsNullOrWhiteSpace(item.MdbxFolderId))
        {
            return false;
        }

        var imagePaths = DecodeSecureItemImagePaths(item);
        if (imagePaths.Count == 0 || imagePaths.All(path => MdbxVaultStore.TryParseAttachmentStoragePath(path) is not null))
        {
            return false;
        }

        var changed = false;
        var migratedPaths = new List<string>(imagePaths.Count);
        foreach (var path in imagePaths)
        {
            if (MdbxVaultStore.TryParseAttachmentStoragePath(path) is not null)
            {
                migratedPaths.Add(path);
                continue;
            }

            var sourceAttachment = CreateSecureItemImageAttachment(item, path);
            var content = await attachmentContentStore.TryReadAttachmentContentAsync(sourceAttachment, cancellationToken);
            if (content is null || content.Length == 0)
            {
                migratedPaths.Add(path);
                continue;
            }

            var mdbxAttachment = CreateSecureItemImageAttachment(item, path);
            await mdbxVaultStore.SaveSecureItemAttachmentAsync(database, item, mdbxAttachment, content, cancellationToken);
            migratedPaths.Add(mdbxAttachment.StoragePath);
            await attachmentContentStore.DeleteAttachmentContentAsync(sourceAttachment, cancellationToken);
            changed = true;
        }

        if (changed)
        {
            ApplySecureItemImagePaths(item, migratedPaths);
        }

        return changed;
    }

    private static IReadOnlyList<string> DecodeSecureItemImagePaths(SecureItem item) => item.ItemType switch
    {
        VaultItemType.Document => WalletItemDataCodec.DecodeDocument(item).ImagePaths,
        VaultItemType.BankCard => WalletItemDataCodec.DecodeBankCard(item).ImagePaths,
        VaultItemType.Note => NoteContentCodec.DecodeImagePaths(item.ImagePaths),
        _ => WalletItemDataCodec.DecodeImagePaths(item.ImagePaths)
    };

    private static void ApplySecureItemImagePaths(SecureItem item, IReadOnlyList<string> imagePaths)
    {
        var encoded = WalletItemDataCodec.EncodeImagePaths(imagePaths);
        item.ImagePaths = encoded;
        if (item.ItemType == VaultItemType.Note)
        {
            var note = NoteContentCodec.DecodeFromItem(item);
            item.ItemData = NoteContentCodec.BuildSavePayload(
                item.Title,
                note.Content,
                string.Join(",", note.Tags),
                note.IsMarkdown,
                imagePaths).ItemData;
            return;
        }

        if (item.ItemType == VaultItemType.Document)
        {
            var data = WalletItemDataCodec.DecodeDocument(item);
            data.ImagePaths = imagePaths.ToList();
            item.ItemData = WalletItemDataCodec.EncodeDocument(data);
            return;
        }

        if (item.ItemType == VaultItemType.BankCard)
        {
            var data = WalletItemDataCodec.DecodeBankCard(item);
            data.ImagePaths = imagePaths.ToList();
            item.ItemData = WalletItemDataCodec.EncodeBankCard(data);
        }
    }

    private static Attachment CreateSecureItemImageAttachment(SecureItem item, string imagePath)
    {
        var fileName = Path.GetFileName(imagePath.Replace('\\', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = item.ItemType == VaultItemType.BankCard ? "card-image" : "secure-item-image";
        }

        return new Attachment
        {
            OwnerType = "SECURE_ITEM",
            OwnerId = item.Id,
            FileName = fileName,
            ContentType = InferImageContentType(fileName),
            StoragePath = imagePath,
            CreatedAt = item.UpdatedAt == default ? DateTimeOffset.UtcNow : item.UpdatedAt
        };
    }

    private static string InferImageContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            ".pdf" => "application/pdf",
            _ => ""
        };
    }

    private static bool IsUnboundFromMdbx(SecureItem item) =>
        item.MdbxDatabaseId is null && string.IsNullOrWhiteSpace(item.MdbxFolderId);

    private static void ClearForeignMdbxBindingForNewSecureItem(SecureItem item)
    {
        if (item.Id != 0)
        {
            return;
        }

        item.MdbxDatabaseId = null;
        item.MdbxFolderId = null;
    }

    private static void ClearForeignMdbxBindingForNewCategory(Category category)
    {
        if (category.Id != 0)
        {
            return;
        }

        category.MdbxDatabaseId = null;
        category.MdbxFolderId = null;
    }

    private static bool IsPasswordOwnerType(string ownerType) =>
        string.Equals(ownerType, "PASSWORD", StringComparison.OrdinalIgnoreCase);
}
