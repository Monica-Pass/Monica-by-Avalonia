using Monica.Core.Models;
using Monica.Data.Mdbx;

namespace Monica.Data.Repositories;

public sealed class MdbxBackedMonicaRepository(
    IMonicaRepository inner,
    IMdbxVaultStore mdbxVaultStore) : IMonicaRepository
{
    public async Task<IReadOnlyList<PasswordEntry>> GetPasswordsAsync(bool includeDeleted = false, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            return await inner.GetPasswordsAsync(includeDeleted, includeArchived, cancellationToken);
        }

        await MirrorUnboundPasswordsAsync(database, cancellationToken);
        return await mdbxVaultStore.GetPasswordsAsync(database, includeDeleted, includeArchived, cancellationToken);
    }

    public async Task<long> SavePasswordAsync(PasswordEntry entry, CancellationToken cancellationToken = default)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            return await inner.SavePasswordAsync(entry, cancellationToken);
        }

        await inner.SavePasswordAsync(entry, cancellationToken);
        await mdbxVaultStore.SavePasswordAsync(database, entry, cancellationToken);
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

    public Task DeletePasswordPermanentlyAsync(long id, CancellationToken cancellationToken = default) =>
        inner.DeletePasswordPermanentlyAsync(id, cancellationToken);

    public Task<IReadOnlyList<CustomField>> GetCustomFieldsAsync(long entryId, CancellationToken cancellationToken = default) =>
        inner.GetCustomFieldsAsync(entryId, cancellationToken);

    public Task<IReadOnlyDictionary<long, IReadOnlyList<CustomField>>> GetCustomFieldsByEntryIdsAsync(IReadOnlyList<long> entryIds, CancellationToken cancellationToken = default) =>
        inner.GetCustomFieldsByEntryIdsAsync(entryIds, cancellationToken);

    public Task ReplaceCustomFieldsAsync(long entryId, IReadOnlyList<CustomField> fields, CancellationToken cancellationToken = default) =>
        inner.ReplaceCustomFieldsAsync(entryId, fields, cancellationToken);

    public Task<IReadOnlyList<long>> SearchEntryIdsByCustomFieldContentAsync(string query, CancellationToken cancellationToken = default) =>
        inner.SearchEntryIdsByCustomFieldContentAsync(query, cancellationToken);

    public Task<IReadOnlyList<Attachment>> GetAttachmentsAsync(string ownerType, long ownerId, CancellationToken cancellationToken = default) =>
        inner.GetAttachmentsAsync(ownerType, ownerId, cancellationToken);

    public Task<IReadOnlyDictionary<long, IReadOnlyList<Attachment>>> GetAttachmentsByOwnerIdsAsync(string ownerType, IReadOnlyList<long> ownerIds, CancellationToken cancellationToken = default) =>
        inner.GetAttachmentsByOwnerIdsAsync(ownerType, ownerIds, cancellationToken);

    public Task<long> SaveAttachmentAsync(Attachment attachment, CancellationToken cancellationToken = default) =>
        inner.SaveAttachmentAsync(attachment, cancellationToken);

    public Task DeleteAttachmentAsync(long id, CancellationToken cancellationToken = default) =>
        inner.DeleteAttachmentAsync(id, cancellationToken);

    public Task<IReadOnlyList<PasswordHistoryEntry>> GetPasswordHistoryAsync(long entryId, CancellationToken cancellationToken = default) =>
        inner.GetPasswordHistoryAsync(entryId, cancellationToken);

    public Task<long> SavePasswordHistoryAsync(PasswordHistoryEntry entry, CancellationToken cancellationToken = default) =>
        inner.SavePasswordHistoryAsync(entry, cancellationToken);

    public Task TrimPasswordHistoryAsync(long entryId, int limit, CancellationToken cancellationToken = default) =>
        inner.TrimPasswordHistoryAsync(entryId, limit, cancellationToken);

    public Task DeletePasswordHistoryAsync(long id, CancellationToken cancellationToken = default) =>
        inner.DeletePasswordHistoryAsync(id, cancellationToken);

    public Task ClearPasswordHistoryAsync(long entryId, CancellationToken cancellationToken = default) =>
        inner.ClearPasswordHistoryAsync(entryId, cancellationToken);

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

        await MirrorUnboundSecureItemsAsync(database, cancellationToken);
        return await mdbxVaultStore.GetSecureItemsAsync(database, itemType, includeDeleted, cancellationToken);
    }

    public async Task<IReadOnlyList<SecureItem>> GetSecureItemsByBoundPasswordIdAsync(long passwordId, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            return await inner.GetSecureItemsByBoundPasswordIdAsync(passwordId, includeDeleted, cancellationToken);
        }

        await MirrorUnboundSecureItemsAsync(database, cancellationToken);
        var items = await mdbxVaultStore.GetSecureItemsAsync(database, VaultItemType.Totp, includeDeleted, cancellationToken);
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
        await mdbxVaultStore.SaveSecureItemAsync(database, item, cancellationToken);
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

    public Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
        inner.GetCategoriesAsync(cancellationToken);

    public Task<long> SaveCategoryAsync(Category category, CancellationToken cancellationToken = default) =>
        inner.SaveCategoryAsync(category, cancellationToken);

    public Task DeleteCategoryAsync(long id, CancellationToken cancellationToken = default) =>
        inner.DeleteCategoryAsync(id, cancellationToken);

    public Task<IReadOnlyList<LocalMdbxDatabase>> GetMdbxDatabasesAsync(CancellationToken cancellationToken = default) =>
        inner.GetMdbxDatabasesAsync(cancellationToken);

    public Task<long> SaveMdbxDatabaseAsync(LocalMdbxDatabase database, CancellationToken cancellationToken = default) =>
        inner.SaveMdbxDatabaseAsync(database, cancellationToken);

    public Task<IReadOnlyList<OperationLog>> GetOperationLogsAsync(int limit = 100, string? itemType = null, CancellationToken cancellationToken = default) =>
        inner.GetOperationLogsAsync(limit, itemType, cancellationToken);

    public Task LogAsync(OperationLog log, CancellationToken cancellationToken = default) =>
        inner.LogAsync(log, cancellationToken);

    public Task ClearVaultDataAsync(VaultClearScope scope, CancellationToken cancellationToken = default) =>
        inner.ClearVaultDataAsync(scope, cancellationToken);

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

    private async Task MirrorUnboundPasswordsAsync(LocalMdbxDatabase database, CancellationToken cancellationToken)
    {
        var sqlitePasswords = await inner.GetPasswordsAsync(includeDeleted: true, includeArchived: true, cancellationToken);
        foreach (var entry in sqlitePasswords.Where(IsUnboundFromMdbx))
        {
            await mdbxVaultStore.SavePasswordAsync(database, entry, cancellationToken);
            if (entry.IsDeleted)
            {
                await mdbxVaultStore.SoftDeletePasswordAsync(database, entry, cancellationToken);
            }

            await inner.SavePasswordAsync(entry, cancellationToken);
        }
    }

    private async Task MirrorUnboundSecureItemsAsync(LocalMdbxDatabase database, CancellationToken cancellationToken)
    {
        var sqliteItems = await inner.GetSecureItemsAsync(includeDeleted: true, cancellationToken: cancellationToken);
        foreach (var item in sqliteItems.Where(IsUnboundFromMdbx))
        {
            await mdbxVaultStore.SaveSecureItemAsync(database, item, cancellationToken);
            if (item.IsDeleted)
            {
                await mdbxVaultStore.SoftDeleteSecureItemAsync(database, item, cancellationToken);
            }

            await inner.SaveSecureItemAsync(item, cancellationToken);
        }
    }

    private static bool IsUnboundFromMdbx(PasswordEntry entry) =>
        entry.MdbxDatabaseId is null && string.IsNullOrWhiteSpace(entry.MdbxFolderId);

    private static bool IsUnboundFromMdbx(SecureItem item) =>
        item.MdbxDatabaseId is null && string.IsNullOrWhiteSpace(item.MdbxFolderId);
}
