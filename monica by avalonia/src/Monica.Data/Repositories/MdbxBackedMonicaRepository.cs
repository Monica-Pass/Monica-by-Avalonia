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
                var boundTotps = await inner.GetSecureItemsByBoundPasswordIdAsync(id, includeDeleted: true, cancellationToken);
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
                var boundTotps = await inner.GetSecureItemsByBoundPasswordIdAsync(id, includeDeleted: true, cancellationToken);
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
                foreach (var attachment in await inner.GetAttachmentsAsync("PASSWORD", id, cancellationToken))
                {
                    await mdbxVaultStore.DeleteAttachmentAsync(database, attachment, cancellationToken);
                }

                await mdbxVaultStore.SoftDeletePasswordAsync(database, entry, cancellationToken);
                var boundTotps = await inner.GetSecureItemsByBoundPasswordIdAsync(id, includeDeleted: true, cancellationToken);
                foreach (var item in boundTotps)
                {
                    await mdbxVaultStore.SoftDeleteSecureItemAsync(database, item, cancellationToken);
                }
            }
        }

        await inner.DeletePasswordPermanentlyAsync(id, cancellationToken);
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
        var attachments = await inner.GetAttachmentsAsync(ownerType, ownerId, cancellationToken);
        return await MigratePasswordAttachmentsAsync(ownerType, attachments, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<long, IReadOnlyList<Attachment>>> GetAttachmentsByOwnerIdsAsync(string ownerType, IReadOnlyList<long> ownerIds, CancellationToken cancellationToken = default)
    {
        var attachmentsByOwnerId = await inner.GetAttachmentsByOwnerIdsAsync(ownerType, ownerIds, cancellationToken);
        if (!IsPasswordOwnerType(ownerType) || attachmentContentStore is null)
        {
            return attachmentsByOwnerId;
        }

        var migrated = await MigratePasswordAttachmentsAsync(
            ownerType,
            attachmentsByOwnerId.Values.SelectMany(item => item).ToArray(),
            cancellationToken);

        return migrated
            .GroupBy(attachment => attachment.OwnerId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Attachment>)group.ToList());
    }

    public Task<long> SaveAttachmentAsync(Attachment attachment, CancellationToken cancellationToken = default) =>
        inner.SaveAttachmentAsync(attachment, cancellationToken);

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

        return await inner.SaveAttachmentAsync(attachment, cancellationToken);
    }

    public Task DeleteAttachmentAsync(long id, CancellationToken cancellationToken = default) =>
        inner.DeleteAttachmentAsync(id, cancellationToken);

    public async Task DeleteAttachmentAsync(long id, Attachment attachment, CancellationToken cancellationToken = default)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is not null)
        {
            await mdbxVaultStore.DeleteAttachmentAsync(database, attachment, cancellationToken);
        }

        await inner.DeleteAttachmentAsync(id, attachment, cancellationToken);
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
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            return await inner.SaveSecureItemAsync(item, cancellationToken);
        }

        await inner.SaveSecureItemAsync(item, cancellationToken);
        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        await mdbxVaultStore.SaveSecureItemAsync(database, item, categories.ToDictionary(category => category.Id), cancellationToken);
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
                await mdbxVaultStore.SaveSecureItemAsync(database, item, categories, cancellationToken);
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
            .Where(database => database.StorageLocation is MdbxStorageLocation.Internal or MdbxStorageLocation.External)
            .Where(database => !string.IsNullOrWhiteSpace(database.EncryptedPassword))
            .Where(database => !string.IsNullOrWhiteSpace(database.WorkingCopyPath ?? database.FilePath))
            .OrderBy(database => database.SortOrder)
            .ThenBy(database => database.Id)
            .FirstOrDefault();
    }

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
        foreach (var entry in sqlitePasswords.Where(IsUnboundFromMdbx))
        {
            await mdbxVaultStore.SavePasswordAsync(
                database,
                entry,
                customFieldsByEntryId.TryGetValue(entry.Id, out var fields) ? fields : [],
                await inner.GetPasswordHistoryAsync(entry.Id, cancellationToken),
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
            await mdbxVaultStore.SaveSecureItemAsync(database, item, categories, cancellationToken);
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
        await mdbxVaultStore.SavePasswordAsync(database, entry, customFields, passwordHistory, categories, cancellationToken);
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

    private static bool IsUnboundFromMdbx(SecureItem item) =>
        item.MdbxDatabaseId is null && string.IsNullOrWhiteSpace(item.MdbxFolderId);

    private static bool IsPasswordOwnerType(string ownerType) =>
        string.Equals(ownerType, "PASSWORD", StringComparison.OrdinalIgnoreCase);
}
