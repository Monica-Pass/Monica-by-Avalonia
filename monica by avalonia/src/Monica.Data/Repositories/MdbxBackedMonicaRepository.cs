using System.Security.Cryptography;
using System.Text;
using Monica.Core.Models;
using Monica.Data.Mdbx;
using Monica.Data.Services;

namespace Monica.Data.Repositories;

public sealed partial class MdbxBackedMonicaRepository(
    IMonicaRepository inner,
    IMdbxVaultStore mdbxVaultStore,
    IAttachmentContentStore? attachmentContentStore = null) : IMonicaRepository, ITransientVaultReadCache
{
    private static readonly TimeSpan ReadCacheTtl = TimeSpan.FromMinutes(2);
    public bool PersistsAttachmentContent => true;
    private readonly IPasswordQuickAccessStore _quickAccessStore = inner as IPasswordQuickAccessStore
        ?? throw new ArgumentException("The local repository must provide password quick-access storage.", nameof(inner));
    private long? _defaultLocalMdbxDatabaseId;
    private DateTimeOffset _defaultLocalMdbxDatabaseCachedAt;
    private LocalMdbxDatabase? _defaultLocalMdbxDatabase;
    private long? _categoryReadCacheDatabaseId;
    private DateTimeOffset _categoryReadCacheCachedAt;
    private IReadOnlyList<Category>? _categoryReadCache;
    private long? _passwordReadSnapshotDatabaseId;
    private DateTimeOffset _passwordReadSnapshotCachedAt;
    private MdbxPasswordReadSnapshot? _passwordReadSnapshot;
    private long? _secureItemReadSnapshotDatabaseId;
    private DateTimeOffset _secureItemReadSnapshotCachedAt;
    private IReadOnlyList<SecureItem>? _secureItemReadSnapshot;

    public async Task<IReadOnlyList<PasswordEntry>> GetPasswordsAsync(bool includeDeleted = false, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            return [];
        }

        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var snapshot = await GetPasswordReadSnapshotAsync(database, categories, cancellationToken);
        return snapshot.Passwords
            .Where(entry => includeDeleted || !entry.IsDeleted)
            .Where(entry => includeArchived || !entry.IsArchived)
            .ToList();
    }

    public async Task<long> SavePasswordAsync(PasswordEntry entry, CancellationToken cancellationToken = default)
    {
        ClearForeignMdbxBindingForNewPassword(entry);
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            throw CreateCanonicalVaultUnavailableException();
        }

        if (entry.Id <= 0)
        {
            entry.Id = await AllocatePasswordIdAsync(database, cancellationToken);
        }

        entry.MdbxDatabaseId = database.Id;
        var categories = await EnsureMdbxCategoriesAsync(
            database,
            cancellationToken,
            recoverMdbxCategories: entry.CategoryId is not null);
        await SavePasswordAggregateAsync(database, categories, entry, cancellationToken: cancellationToken);
        ClearPasswordReadSnapshot();
        return entry.Id;
    }

    public async Task SoftDeletePasswordAsync(long id, CancellationToken cancellationToken = default)
    {
        var database = await RequireDefaultMdbxDatabaseAsync(cancellationToken);
        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var entry = await FindPasswordForMdbxOperationAsync(database, categories, id, includeDeleted: true, cancellationToken);
        if (entry is not null)
        {
            await MarkRemoteWorkingCopyPendingAsync(database, cancellationToken);
            var deletedAt = DateTimeOffset.UtcNow;
            entry.IsDeleted = true;
            entry.DeletedAt = deletedAt;
            entry.IsArchived = false;
            entry.ArchivedAt = null;
            entry.UpdatedAt = deletedAt;
            await SavePasswordAggregateAsync(database, categories, entry, cancellationToken: cancellationToken);
            await mdbxVaultStore.SoftDeletePasswordAsync(database, entry, cancellationToken);
            var boundTotps = await GetMdbxBoundTotpsByPasswordIdAsync(database, id, includeDeleted: true, cancellationToken);
            foreach (var item in boundTotps)
            {
                item.IsDeleted = true;
                item.DeletedAt = deletedAt;
                item.UpdatedAt = deletedAt;
                await mdbxVaultStore.SaveSecureItemAsync(database, item, categories.ToDictionary(category => category.Id), cancellationToken);
                await mdbxVaultStore.SoftDeleteSecureItemAsync(database, item, cancellationToken);
            }
        }
        ClearPasswordReadSnapshot();
        ClearSecureItemReadSnapshot();
    }

    public async Task RestorePasswordAsync(long id, CancellationToken cancellationToken = default)
    {
        var database = await RequireDefaultMdbxDatabaseAsync(cancellationToken);
        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var entry = await FindPasswordForMdbxOperationAsync(database, categories, id, includeDeleted: true, cancellationToken);
        if (entry is not null && entry.DeletedAt != DateTimeOffset.UnixEpoch)
        {
            await MarkRemoteWorkingCopyPendingAsync(database, cancellationToken);
            await mdbxVaultStore.RestorePasswordAsync(database, entry, cancellationToken);
            entry.IsDeleted = false;
            entry.DeletedAt = null;
            entry.UpdatedAt = DateTimeOffset.UtcNow;
            await SavePasswordAggregateAsync(database, categories, entry, cancellationToken: cancellationToken);
            var boundTotps = await GetMdbxBoundTotpsByPasswordIdAsync(database, id, includeDeleted: true, cancellationToken);
            foreach (var item in boundTotps)
            {
                if (item.DeletedAt == DateTimeOffset.UnixEpoch)
                {
                    continue;
                }

                await mdbxVaultStore.RestoreSecureItemAsync(database, item, cancellationToken);
                item.IsDeleted = false;
                item.DeletedAt = null;
                item.UpdatedAt = entry.UpdatedAt;
                await mdbxVaultStore.SaveSecureItemAsync(database, item, categories.ToDictionary(category => category.Id), cancellationToken);
            }
        }
        ClearPasswordReadSnapshot();
        ClearSecureItemReadSnapshot();
    }

    public async Task DeletePasswordPermanentlyAsync(long id, CancellationToken cancellationToken = default)
    {
        var database = await RequireDefaultMdbxDatabaseAsync(cancellationToken);
        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var entry = await FindPasswordForMdbxOperationAsync(database, categories, id, includeDeleted: true, cancellationToken);
        if (entry is not null)
        {
            await MarkRemoteWorkingCopyPendingAsync(database, cancellationToken);
            foreach (var attachment in await GetPasswordAttachmentsFromMdbxAsync(database, id, cancellationToken))
            {
                await mdbxVaultStore.DeleteAttachmentAsync(database, attachment, cancellationToken);
            }

            if (entry.IsDeleted)
            {
                await mdbxVaultStore.RestorePasswordAsync(database, entry, cancellationToken);
            }

            var tombstoneAt = DateTimeOffset.UtcNow;
            var passwordTombstone = CreatePasswordTombstone(entry, tombstoneAt);
            await SavePasswordAggregateAsync(
                database,
                categories,
                passwordTombstone,
                customFields: [],
                passwordHistory: [],
                attachments: [],
                cancellationToken: cancellationToken);
            await mdbxVaultStore.SoftDeletePasswordAsync(database, passwordTombstone, cancellationToken);
            var boundTotps = await GetMdbxBoundTotpsByPasswordIdAsync(database, id, includeDeleted: true, cancellationToken);
            foreach (var item in boundTotps)
            {
                if (item.IsDeleted)
                {
                    await mdbxVaultStore.RestoreSecureItemAsync(database, item, cancellationToken);
                }

                var secureItemTombstone = CreateSecureItemTombstone(item, tombstoneAt);
                await mdbxVaultStore.SaveSecureItemAsync(database, secureItemTombstone, categories.ToDictionary(category => category.Id), cancellationToken);
                await mdbxVaultStore.SoftDeleteSecureItemAsync(database, secureItemTombstone, cancellationToken);
            }
        }

        ClearPasswordReadSnapshot();
        ClearSecureItemReadSnapshot();
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
            return new Dictionary<long, IReadOnlyList<CustomField>>();
        }

        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var snapshot = await GetPasswordReadSnapshotAsync(database, categories, cancellationToken);
        var existingIds = snapshot.Passwords
            .Where(entry => !entry.IsDeleted)
            .Select(entry => entry.Id)
            .ToHashSet();
        ids = ids.Where(existingIds.Contains).ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<long, IReadOnlyList<CustomField>>();
        }

        var mdbxFields = new Dictionary<long, IReadOnlyList<CustomField>>();
        foreach (var id in ids)
        {
            if (snapshot.CustomFieldsByEntryId.TryGetValue(id, out var fields))
            {
                mdbxFields[id] = fields;
            }
        }
        return mdbxFields;
    }

    public async Task ReplaceCustomFieldsAsync(long entryId, IReadOnlyList<CustomField> fields, CancellationToken cancellationToken = default)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            throw CreateCanonicalVaultUnavailableException();
        }

        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var entry = await FindPasswordForMdbxOperationAsync(database, categories, entryId, includeDeleted: false, cancellationToken);
        if (entry is null)
        {
            throw new InvalidOperationException($"Password entry {entryId} was not found in the canonical MDBX vault.");
        }

        var normalizedFields = fields
            .Where(field => !string.IsNullOrWhiteSpace(field.Title))
            .Select((field, index) => new CustomField
            {
                Id = field.Id,
                EntryId = entryId,
                Title = field.Title,
                Value = field.Value,
                IsProtected = field.IsProtected,
                SortOrder = index
            })
            .ToArray();
        var passwordHistory = await mdbxVaultStore.GetPasswordHistoryAsync(database, entryId, cancellationToken) ?? [];
        var attachments = await GetPasswordAttachmentsFromMdbxAsync(database, entryId, cancellationToken);
        await MarkRemoteWorkingCopyPendingAsync(database, cancellationToken);
        await mdbxVaultStore.SavePasswordAsync(
            database,
            entry,
            normalizedFields,
            passwordHistory,
            attachments,
            categories.ToDictionary(category => category.Id),
            cancellationToken);
        ClearPasswordReadSnapshot();
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
            return [];
        }

        return (await mdbxVaultStore.SearchPasswordEntryIdsByCustomFieldContentAsync(database, query, cancellationToken))
            .Distinct()
            .OrderBy(id => id)
            .ToList();
    }

    public async Task<IReadOnlyList<Attachment>> GetAttachmentsAsync(string ownerType, long ownerId, CancellationToken cancellationToken = default)
    {
        if (!IsPasswordOwnerType(ownerType) && !IsSecureItemOwnerType(ownerType))
        {
            return [];
        }

        var attachmentsByOwnerId = await GetAttachmentsByOwnerIdsAsync(ownerType, [ownerId], cancellationToken);
        return attachmentsByOwnerId.TryGetValue(ownerId, out var attachments) ? attachments : [];
    }

    public async Task<IReadOnlyDictionary<long, IReadOnlyList<Attachment>>> GetAttachmentsByOwnerIdsAsync(string ownerType, IReadOnlyList<long> ownerIds, CancellationToken cancellationToken = default)
    {
        if (!IsPasswordOwnerType(ownerType) && !IsSecureItemOwnerType(ownerType))
        {
            return new Dictionary<long, IReadOnlyList<Attachment>>();
        }

        var ids = ownerIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<long, IReadOnlyList<Attachment>>();
        }

        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            return new Dictionary<long, IReadOnlyList<Attachment>>();
        }

        if (IsSecureItemOwnerType(ownerType))
        {
            return await mdbxVaultStore.GetSecureItemAttachmentsByItemIdsAsync(database, ids, cancellationToken);
        }

        return await mdbxVaultStore.GetPasswordAttachmentsByEntryIdsAsync(database, ids, cancellationToken);
    }

    public async Task<byte[]?> TryReadAttachmentContentAsync(Attachment attachment, CancellationToken cancellationToken = default)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is not null && MdbxVaultStore.TryParseAttachmentStoragePath(attachment.StoragePath) is not null)
        {
            return await mdbxVaultStore.TryReadAttachmentContentAsync(database, attachment, cancellationToken);
        }

        return null;
    }

    public async Task<long> SaveAttachmentAsync(Attachment attachment, CancellationToken cancellationToken = default)
    {
        var database = await RequireDefaultMdbxDatabaseAsync(cancellationToken);
        if (!IsPasswordOwnerType(attachment.OwnerType))
        {
            throw new InvalidOperationException("Metadata-only attachments are supported only for canonical password entries.");
        }

        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var entry = await mdbxVaultStore.FindPasswordAsync(database, categories, attachment.OwnerId, includeDeleted: true, cancellationToken)
            ?? throw new InvalidOperationException($"Password entry {attachment.OwnerId} was not found in the canonical MDBX vault.");
        if (attachment.Id <= 0)
        {
            attachment.Id = await AllocateAttachmentIdAsync(database, cancellationToken);
        }

        attachment.OwnerType = "PASSWORD";
        attachment.OwnerId = entry.Id;
        var attachments = UpsertAttachment(await GetPasswordAttachmentsFromMdbxAsync(database, entry.Id, cancellationToken), attachment);
        await SavePasswordAggregateAsync(database, categories, entry, attachments: attachments, cancellationToken: cancellationToken);
        ClearPasswordReadSnapshot();
        return attachment.Id;
    }

    public async Task<long> SaveAttachmentAsync(Attachment attachment, byte[] content, CancellationToken cancellationToken = default)
    {
        if (content.Length == 0)
        {
            return await SaveAttachmentAsync(attachment, cancellationToken);
        }

        var database = await RequireDefaultMdbxDatabaseAsync(cancellationToken);
        if (attachment.Id <= 0)
        {
            attachment.Id = await AllocateAttachmentIdAsync(database, cancellationToken);
        }

        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        if (IsPasswordOwnerType(attachment.OwnerType))
        {
            var entry = await mdbxVaultStore.FindPasswordAsync(database, categories, attachment.OwnerId, includeDeleted: true, cancellationToken)
                ?? throw new InvalidOperationException($"Password entry {attachment.OwnerId} was not found in the canonical MDBX vault.");
            attachment.OwnerType = "PASSWORD";
            attachment.OwnerId = entry.Id;
            await MarkRemoteWorkingCopyPendingAsync(database, cancellationToken);
            await mdbxVaultStore.SavePasswordAttachmentAsync(database, entry, attachment, content, cancellationToken);
            var attachments = UpsertAttachment(await GetPasswordAttachmentsFromMdbxAsync(database, entry.Id, cancellationToken), attachment);
            await SavePasswordAggregateAsync(database, categories, entry, attachments: attachments, cancellationToken: cancellationToken);
            ClearPasswordReadSnapshot();
            return attachment.Id;
        }

        if (IsSecureItemOwnerType(attachment.OwnerType))
        {
            var item = await mdbxVaultStore.FindSecureItemAsync(database, categories, attachment.OwnerId, includeDeleted: true, cancellationToken)
                ?? throw new InvalidOperationException($"Secure item {attachment.OwnerId} was not found in the canonical MDBX vault.");
            await MarkRemoteWorkingCopyPendingAsync(database, cancellationToken);
            await mdbxVaultStore.SaveSecureItemAttachmentAsync(database, item, attachment, content, cancellationToken);
            var paths = DecodeSecureItemImagePaths(item).ToList();
            if (!paths.Contains(attachment.StoragePath, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(attachment.StoragePath);
                ApplySecureItemImagePaths(item, paths);
                await mdbxVaultStore.SaveSecureItemAsync(database, item, categories.ToDictionary(category => category.Id), cancellationToken);
            }

            ClearSecureItemReadSnapshot();
            return attachment.Id;
        }

        throw new InvalidOperationException($"Unsupported canonical attachment owner type '{attachment.OwnerType}'.");
    }

    public async Task DeleteAttachmentAsync(long id, CancellationToken cancellationToken = default)
    {
        await RequireDefaultMdbxDatabaseAsync(cancellationToken);
        var attachment = await FindCanonicalAttachmentAsync(id, cancellationToken);
        if (attachment is null)
        {
            return;
        }

        await DeleteAttachmentAsync(id, attachment, cancellationToken);
    }

    public async Task DeleteAttachmentAsync(long id, Attachment attachment, CancellationToken cancellationToken = default)
    {
        var database = await RequireDefaultMdbxDatabaseAsync(cancellationToken);
        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        await MarkRemoteWorkingCopyPendingAsync(database, cancellationToken);
        await mdbxVaultStore.DeleteAttachmentAsync(database, attachment, cancellationToken);
        if (IsPasswordOwnerType(attachment.OwnerType))
        {
            var entry = await mdbxVaultStore.FindPasswordAsync(database, categories, attachment.OwnerId, includeDeleted: true, cancellationToken);
            if (entry is not null)
            {
                var attachments = (await GetPasswordAttachmentsFromMdbxAsync(database, entry.Id, cancellationToken))
                    .Where(item => item.Id != id && !string.Equals(item.StoragePath, attachment.StoragePath, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                await SavePasswordAggregateAsync(database, categories, entry, attachments: attachments, cancellationToken: cancellationToken);
            }

            ClearPasswordReadSnapshot();
            return;
        }

        if (IsSecureItemOwnerType(attachment.OwnerType))
        {
            var item = await mdbxVaultStore.FindSecureItemAsync(database, categories, attachment.OwnerId, includeDeleted: true, cancellationToken);
            if (item is not null)
            {
                var paths = DecodeSecureItemImagePaths(item)
                    .Where(path => !string.Equals(path, attachment.StoragePath, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                ApplySecureItemImagePaths(item, paths);
                await mdbxVaultStore.SaveSecureItemAsync(database, item, categories.ToDictionary(category => category.Id), cancellationToken);
            }

            ClearSecureItemReadSnapshot();
        }
    }

    public async Task<IReadOnlyList<PasswordHistoryEntry>> GetPasswordHistoryAsync(long entryId, CancellationToken cancellationToken = default)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            return [];
        }

        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var existing = (await GetPasswordReadSnapshotAsync(database, categories, cancellationToken)).Passwords
            .Any(entry => entry.Id == entryId && !entry.IsDeleted);
        if (!existing)
        {
            return [];
        }

        return await mdbxVaultStore.GetPasswordHistoryAsync(database, entryId, cancellationToken) ?? [];
    }

    public async Task<long> SavePasswordHistoryAsync(PasswordHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        var database = await RequireDefaultMdbxDatabaseAsync(cancellationToken);
        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var owner = await mdbxVaultStore.FindPasswordAsync(database, categories, entry.EntryId, includeDeleted: true, cancellationToken)
            ?? throw new InvalidOperationException($"Password entry {entry.EntryId} was not found in the canonical MDBX vault.");
        if (entry.Id <= 0)
        {
            entry.Id = await AllocatePasswordHistoryIdAsync(database, cancellationToken);
        }

        var history = (await mdbxVaultStore.GetPasswordHistoryAsync(database, entry.EntryId, cancellationToken) ?? [])
            .Where(item => item.Id != entry.Id)
            .Append(entry)
            .ToArray();
        await SavePasswordAggregateAsync(database, categories, owner, passwordHistory: history, cancellationToken: cancellationToken);
        ClearPasswordReadSnapshot();
        return entry.Id;
    }

    public async Task TrimPasswordHistoryAsync(long entryId, int limit, CancellationToken cancellationToken = default)
    {
        var database = await RequireDefaultMdbxDatabaseAsync(cancellationToken);
        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var owner = await mdbxVaultStore.FindPasswordAsync(database, categories, entryId, includeDeleted: true, cancellationToken);
        if (owner is null)
        {
            return;
        }

        var history = (await mdbxVaultStore.GetPasswordHistoryAsync(database, entryId, cancellationToken) ?? [])
            .OrderByDescending(item => item.LastUsedAt)
            .ThenByDescending(item => item.Id)
            .Take(Math.Max(0, limit))
            .ToArray();
        await SavePasswordAggregateAsync(database, categories, owner, passwordHistory: history, cancellationToken: cancellationToken);
        ClearPasswordReadSnapshot();
    }

    public async Task DeletePasswordHistoryAsync(long id, CancellationToken cancellationToken = default)
    {
        await RequireDefaultMdbxDatabaseAsync(cancellationToken);
        var entryId = await FindPasswordHistoryOwnerIdAsync(id, cancellationToken);
        if (entryId is null)
        {
            return;
        }

        var database = await RequireDefaultMdbxDatabaseAsync(cancellationToken);
        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var owner = await mdbxVaultStore.FindPasswordAsync(database, categories, entryId.Value, includeDeleted: true, cancellationToken);
        if (owner is not null)
        {
            var history = (await mdbxVaultStore.GetPasswordHistoryAsync(database, entryId.Value, cancellationToken) ?? [])
                .Where(item => item.Id != id)
                .ToArray();
            await SavePasswordAggregateAsync(database, categories, owner, passwordHistory: history, cancellationToken: cancellationToken);
            ClearPasswordReadSnapshot();
        }
    }

    public async Task ClearPasswordHistoryAsync(long entryId, CancellationToken cancellationToken = default)
    {
        var database = await RequireDefaultMdbxDatabaseAsync(cancellationToken);
        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var owner = await mdbxVaultStore.FindPasswordAsync(database, categories, entryId, includeDeleted: true, cancellationToken);
        if (owner is not null)
        {
            await SavePasswordAggregateAsync(database, categories, owner, passwordHistory: [], cancellationToken: cancellationToken);
            ClearPasswordReadSnapshot();
        }
    }

    public async Task<PasswordQuickAccessRecord?> RecordPasswordQuickAccessAsync(long passwordId, CancellationToken cancellationToken = default)
    {
        if ((await GetPasswordsAsync(includeDeleted: false, includeArchived: false, cancellationToken)).Any(entry => entry.Id == passwordId))
        {
            return await _quickAccessStore.RecordPasswordQuickAccessAsync(passwordId, cancellationToken);
        }

        return null;
    }

    public async Task<IReadOnlyList<PasswordQuickAccessRecord>> GetPasswordQuickAccessRecordsAsync(CancellationToken cancellationToken = default)
    {
        var activeIds = (await GetPasswordsAsync(includeDeleted: false, includeArchived: false, cancellationToken))
            .Select(entry => entry.Id)
            .ToHashSet();
        return (await _quickAccessStore.GetAllPasswordQuickAccessRecordsAsync(cancellationToken))
            .Where(record => activeIds.Contains(record.PasswordId))
            .ToArray();
    }

    public async Task<IReadOnlyList<SecureItem>> GetSecureItemsAsync(VaultItemType? itemType = null, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            return [];
        }

        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        return (await GetSecureItemReadSnapshotAsync(database, categories, cancellationToken))
            .Where(item => itemType is null || item.ItemType == itemType)
            .Where(item => includeDeleted || !item.IsDeleted)
            .OrderByDescending(item => item.IsFavorite)
            .ThenBy(item => item.SortOrder)
            .ThenByDescending(item => item.UpdatedAt)
            .ToList();
    }

    public async Task<IReadOnlyList<SecureItem>> GetSecureItemsByBoundPasswordIdAsync(long passwordId, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            return [];
        }

        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        return (await GetSecureItemReadSnapshotAsync(database, categories, cancellationToken))
            .Where(item => item.ItemType == VaultItemType.Totp)
            .Where(item => includeDeleted || !item.IsDeleted)
            .Where(item => item.BoundPasswordId == passwordId)
            .ToList();
    }

    public async Task<long> SaveSecureItemAsync(SecureItem item, CancellationToken cancellationToken = default)
    {
        ClearForeignMdbxBindingForNewSecureItem(item);
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            throw CreateCanonicalVaultUnavailableException();
        }

        if (item.Id <= 0)
        {
            item.Id = await AllocateSecureItemIdAsync(database, cancellationToken);
        }

        item.MdbxDatabaseId = database.Id;
        var categories = await EnsureMdbxCategoriesAsync(
            database,
            cancellationToken,
            recoverMdbxCategories: item.CategoryId is not null);
        await SaveSecureItemToMdbxAsync(database, item, categories.ToDictionary(category => category.Id), cancellationToken);
        ClearSecureItemReadSnapshot();
        return item.Id;
    }

    public async Task SoftDeleteSecureItemAsync(long id, CancellationToken cancellationToken = default)
    {
        var database = await RequireDefaultMdbxDatabaseAsync(cancellationToken);
        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var item = await FindSecureItemForMdbxOperationAsync(database, categories, id, includeDeleted: true, cancellationToken);
        if (item is not null)
        {
            await MarkRemoteWorkingCopyPendingAsync(database, cancellationToken);
            item.IsDeleted = true;
            item.DeletedAt = DateTimeOffset.UtcNow;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            await mdbxVaultStore.SaveSecureItemAsync(database, item, categories.ToDictionary(category => category.Id), cancellationToken);
            await mdbxVaultStore.SoftDeleteSecureItemAsync(database, item, cancellationToken);
        }
        ClearSecureItemReadSnapshot();
    }

    public async Task RestoreSecureItemAsync(long id, CancellationToken cancellationToken = default)
    {
        var database = await RequireDefaultMdbxDatabaseAsync(cancellationToken);
        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var item = await FindSecureItemForMdbxOperationAsync(database, categories, id, includeDeleted: true, cancellationToken);
        if (item is not null && item.DeletedAt > DateTimeOffset.UnixEpoch)
        {
            await MarkRemoteWorkingCopyPendingAsync(database, cancellationToken);
            await mdbxVaultStore.RestoreSecureItemAsync(database, item, cancellationToken);
            item.IsDeleted = false;
            item.DeletedAt = null;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            await mdbxVaultStore.SaveSecureItemAsync(database, item, categories.ToDictionary(category => category.Id), cancellationToken);
        }
        ClearSecureItemReadSnapshot();
    }

    public async Task DeleteSecureItemPermanentlyAsync(long id, CancellationToken cancellationToken = default)
    {
        var database = await RequireDefaultMdbxDatabaseAsync(cancellationToken);
        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var item = await FindSecureItemForMdbxOperationAsync(database, categories, id, includeDeleted: true, cancellationToken);
        if (item is not null)
        {
            await MarkRemoteWorkingCopyPendingAsync(database, cancellationToken);
            await mdbxVaultStore.RestoreSecureItemAsync(database, item, cancellationToken);
            var tombstone = CreateSecureItemTombstone(item, DateTimeOffset.UtcNow);
            await mdbxVaultStore.SaveSecureItemAsync(database, tombstone, categories.ToDictionary(category => category.Id), cancellationToken);
            await mdbxVaultStore.SoftDeleteSecureItemAsync(database, tombstone, cancellationToken);
        }
        ClearSecureItemReadSnapshot();
    }

    public async Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        return database is null
            ? []
            : await EnsureMdbxCategoriesAsync(database, cancellationToken);
    }

    public async Task<long> SaveCategoryAsync(Category category, CancellationToken cancellationToken = default)
    {
        ClearForeignMdbxBindingForNewCategory(category);
        var database = await RequireDefaultMdbxDatabaseAsync(cancellationToken);
        await MarkRemoteWorkingCopyPendingAsync(database, cancellationToken);
        await mdbxVaultStore.SaveCategoryAsync(database, category, cancellationToken);
        category.Id = GetStableCategoryId(category.MdbxFolderId!);

        ClearCategoryReadCache();
        ClearPasswordReadSnapshot();
        ClearSecureItemReadSnapshot();
        return category.Id;
    }

    public async Task DeleteCategoryAsync(long id, CancellationToken cancellationToken = default)
    {
        var database = await RequireDefaultMdbxDatabaseAsync(cancellationToken);
        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var category = categories.FirstOrDefault(category => category.Id == id);
        if (category is not null)
        {
            await MarkRemoteWorkingCopyPendingAsync(database, cancellationToken);
            await mdbxVaultStore.UnassignCategoryAsync(database, category, cancellationToken);
        }
        ClearCategoryReadCache();
        ClearPasswordReadSnapshot();
        ClearSecureItemReadSnapshot();
    }

    public Task<IReadOnlyList<LocalMdbxDatabase>> GetMdbxDatabasesAsync(CancellationToken cancellationToken = default) =>
        inner.GetMdbxDatabasesAsync(cancellationToken);

    public async Task<long> SaveMdbxDatabaseAsync(LocalMdbxDatabase database, CancellationToken cancellationToken = default)
    {
        var id = await inner.SaveMdbxDatabaseAsync(database, cancellationToken);
        ClearMdbxReadCaches();
        return id;
    }

    public Task<IReadOnlyList<OperationLog>> GetOperationLogsAsync(int limit = 100, string? itemType = null, CancellationToken cancellationToken = default) =>
        inner.GetOperationLogsAsync(limit, itemType, cancellationToken);

    public Task LogAsync(OperationLog log, CancellationToken cancellationToken = default) =>
        inner.LogAsync(log, cancellationToken);

    public async Task ClearVaultDataAsync(VaultClearScope scope, CancellationToken cancellationToken = default)
    {
        var database = await RequireDefaultMdbxDatabaseAsync(cancellationToken);
        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        await MarkRemoteWorkingCopyPendingAsync(database, cancellationToken);
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

        ClearMdbxReadCaches();
    }

    private async Task<LocalMdbxDatabase?> GetDefaultLocalMdbxDatabaseAsync(CancellationToken cancellationToken)
    {
        if (!mdbxVaultStore.IsAvailable)
        {
            return null;
        }

        if (_defaultLocalMdbxDatabase is not null &&
            IsCacheFresh(_defaultLocalMdbxDatabaseId, _defaultLocalMdbxDatabaseCachedAt, _defaultLocalMdbxDatabase.Id))
        {
            return _defaultLocalMdbxDatabase;
        }

        var databases = await inner.GetMdbxDatabasesAsync(cancellationToken);
        _defaultLocalMdbxDatabase = databases
            .Where(database => database.IsDefault)
            .Where(CanUseMdbxWorkingCopy)
            .Where(database => !string.IsNullOrWhiteSpace(database.EncryptedPassword))
            .Where(database => !string.IsNullOrWhiteSpace(database.WorkingCopyPath ?? database.FilePath))
            .OrderBy(database => database.SortOrder)
            .ThenBy(database => database.Id)
            .FirstOrDefault();
        _defaultLocalMdbxDatabaseId = _defaultLocalMdbxDatabase?.Id;
        _defaultLocalMdbxDatabaseCachedAt = DateTimeOffset.UtcNow;
        return _defaultLocalMdbxDatabase;
    }

    private async Task<LocalMdbxDatabase> RequireDefaultMdbxDatabaseAsync(CancellationToken cancellationToken) =>
        await GetDefaultLocalMdbxDatabaseAsync(cancellationToken) ?? throw CreateCanonicalVaultUnavailableException();

    private static InvalidOperationException CreateCanonicalVaultUnavailableException() =>
        new("A usable default MDBX vault is required for canonical business-data operations.");

    private static long GetStableCategoryId(string projectId) =>
        MdbxStableId.FromString("category", projectId);

    private async Task<long> AllocatePasswordIdAsync(LocalMdbxDatabase database, CancellationToken cancellationToken)
    {
        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var ids = (await GetPasswordReadSnapshotAsync(database, categories, cancellationToken)).Passwords
            .Select(entry => entry.Id)
            .Where(id => id > 0)
            .ToHashSet();
        return AllocateRandomDomainId(ids);
    }

    private async Task<long> AllocateSecureItemIdAsync(LocalMdbxDatabase database, CancellationToken cancellationToken)
    {
        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var ids = (await GetSecureItemReadSnapshotAsync(database, categories, cancellationToken))
            .Select(item => item.Id)
            .Where(id => id > 0)
            .ToHashSet();
        return AllocateRandomDomainId(ids);
    }

    private async Task<long> AllocatePasswordHistoryIdAsync(LocalMdbxDatabase database, CancellationToken cancellationToken)
    {
        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var ids = new HashSet<long>();
        foreach (var entry in (await GetPasswordReadSnapshotAsync(database, categories, cancellationToken)).Passwords)
        {
            foreach (var history in await mdbxVaultStore.GetPasswordHistoryAsync(database, entry.Id, cancellationToken) ?? [])
            {
                if (history.Id > 0)
                {
                    ids.Add(history.Id);
                }
            }
        }

        return AllocateRandomDomainId(ids);
    }

    private async Task<long> AllocateAttachmentIdAsync(LocalMdbxDatabase database, CancellationToken cancellationToken)
    {
        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var ids = new HashSet<long>();
        var passwordSnapshot = await GetPasswordReadSnapshotAsync(database, categories, cancellationToken);
        foreach (var attachment in passwordSnapshot.AttachmentsByEntryId.Values.SelectMany(items => items))
        {
            if (attachment.Id > 0)
            {
                ids.Add(attachment.Id);
            }
        }

        var secureItems = await GetSecureItemReadSnapshotAsync(database, categories, cancellationToken);
        var secureAttachments = await mdbxVaultStore.GetSecureItemAttachmentsByItemIdsAsync(
            database,
            secureItems.Select(item => item.Id).Where(id => id > 0).ToArray(),
            cancellationToken);
        foreach (var attachment in secureAttachments.Values.SelectMany(items => items))
        {
            if (attachment.Id > 0)
            {
                ids.Add(attachment.Id);
            }
        }

        return AllocateRandomDomainId(ids);
    }

    private static long AllocateRandomDomainId(IReadOnlySet<long> existingIds)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        while (true)
        {
            RandomNumberGenerator.Fill(bytes);
            var id = BitConverter.ToInt64(bytes) & long.MaxValue;
            if (id > 0 && !existingIds.Contains(id))
            {
                return id;
            }
        }
    }

    private async Task<IReadOnlyList<Attachment>> GetPasswordAttachmentsFromMdbxAsync(
        LocalMdbxDatabase database,
        long entryId,
        CancellationToken cancellationToken)
    {
        var attachmentsByEntryId = await mdbxVaultStore.GetPasswordAttachmentsByEntryIdsAsync(database, [entryId], cancellationToken);
        return attachmentsByEntryId.TryGetValue(entryId, out var attachments) ? attachments : [];
    }

    private async Task SavePasswordAggregateAsync(
        LocalMdbxDatabase database,
        IReadOnlyList<Category> categories,
        PasswordEntry entry,
        IReadOnlyList<CustomField>? customFields = null,
        IReadOnlyList<PasswordHistoryEntry>? passwordHistory = null,
        IReadOnlyList<Attachment>? attachments = null,
        CancellationToken cancellationToken = default)
    {
        if (customFields is null)
        {
            var fieldsByEntryId = await mdbxVaultStore.GetPasswordCustomFieldsByEntryIdsAsync(database, [entry.Id], cancellationToken);
            customFields = fieldsByEntryId.TryGetValue(entry.Id, out var fields) ? fields : [];
        }

        passwordHistory ??= await mdbxVaultStore.GetPasswordHistoryAsync(database, entry.Id, cancellationToken) ?? [];
        attachments ??= await GetPasswordAttachmentsFromMdbxAsync(database, entry.Id, cancellationToken);
        await MarkRemoteWorkingCopyPendingAsync(database, cancellationToken);
        await mdbxVaultStore.SavePasswordAsync(
            database,
            entry,
            customFields,
            passwordHistory,
            attachments,
            categories.ToDictionary(category => category.Id),
            cancellationToken);
    }

    private static IReadOnlyList<Attachment> UpsertAttachment(
        IReadOnlyList<Attachment> attachments,
        Attachment attachment) =>
        attachments
            .Where(item => item.Id != attachment.Id)
            .Where(item => string.IsNullOrWhiteSpace(attachment.StoragePath) ||
                           !string.Equals(item.StoragePath, attachment.StoragePath, StringComparison.OrdinalIgnoreCase))
            .Append(attachment)
            .ToArray();

    private async Task<Attachment?> FindCanonicalAttachmentAsync(long attachmentId, CancellationToken cancellationToken)
    {
        if (attachmentId <= 0)
        {
            return null;
        }

        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            return null;
        }

        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var passwordSnapshot = await GetPasswordReadSnapshotAsync(database, categories, cancellationToken);
        var passwordAttachment = passwordSnapshot.AttachmentsByEntryId.Values
            .SelectMany(items => items)
            .FirstOrDefault(attachment => attachment.Id == attachmentId);
        if (passwordAttachment is not null)
        {
            return passwordAttachment;
        }

        var secureItems = await GetSecureItemReadSnapshotAsync(database, categories, cancellationToken);
        var secureAttachments = await mdbxVaultStore.GetSecureItemAttachmentsByItemIdsAsync(
            database,
            secureItems.Select(item => item.Id).Where(id => id > 0).ToArray(),
            cancellationToken);
        return secureAttachments.Values
            .SelectMany(items => items)
            .FirstOrDefault(attachment => attachment.Id == attachmentId);
    }

    private static bool CanUseMdbxWorkingCopy(LocalMdbxDatabase database) =>
        database.StorageLocation is MdbxStorageLocation.Internal or MdbxStorageLocation.External ||
        !string.IsNullOrWhiteSpace(database.WorkingCopyPath);

    private async Task<IReadOnlyList<Category>> EnsureMdbxCategoriesAsync(
        LocalMdbxDatabase database,
        CancellationToken cancellationToken,
        bool recoverMdbxCategories = true)
    {
        if (_categoryReadCache is not null &&
            IsCacheFresh(_categoryReadCacheDatabaseId, _categoryReadCacheCachedAt, database.Id))
        {
            return _categoryReadCache;
        }

        var categories = (await mdbxVaultStore.GetCategoriesAsync(database, cancellationToken))
            .Select(category =>
            {
                category.Id = GetStableCategoryId(category.MdbxFolderId!);
                category.MdbxDatabaseId = database.Id;
                return category;
            })
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var duplicateId = categories
            .GroupBy(category => category.Id)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
        {
            throw new InvalidOperationException($"MDBX category identity collision detected for {duplicateId.Key}.");
        }

        _categoryReadCache = categories;
        _categoryReadCacheDatabaseId = database.Id;
        _categoryReadCacheCachedAt = DateTimeOffset.UtcNow;

        return categories;
    }

    private static bool IsCacheFresh(long? cachedDatabaseId, DateTimeOffset cachedAt, long databaseId) =>
        cachedDatabaseId == databaseId && DateTimeOffset.UtcNow - cachedAt < ReadCacheTtl;

    private async Task<IReadOnlyList<SecureItem>> GetMdbxBoundTotpsByPasswordIdAsync(
        LocalMdbxDatabase database,
        long passwordId,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var items = (await GetSecureItemReadSnapshotAsync(database, categories, cancellationToken))
            .Where(item => item.ItemType == VaultItemType.Totp)
            .Where(item => includeDeleted || !item.IsDeleted)
            .ToList();
        return items
            .Where(item => item.BoundPasswordId == passwordId)
            .ToList();
    }

    private async Task<MdbxPasswordReadSnapshot> GetPasswordReadSnapshotAsync(
        LocalMdbxDatabase database,
        IReadOnlyList<Category> categories,
        CancellationToken cancellationToken)
    {
        if (_passwordReadSnapshot is not null &&
            IsCacheFresh(_passwordReadSnapshotDatabaseId, _passwordReadSnapshotCachedAt, database.Id))
        {
            return _passwordReadSnapshot;
        }

        _passwordReadSnapshot = await mdbxVaultStore.GetPasswordReadSnapshotAsync(database, categories, cancellationToken);
        _passwordReadSnapshotDatabaseId = database.Id;
        _passwordReadSnapshotCachedAt = DateTimeOffset.UtcNow;
        return _passwordReadSnapshot;
    }

    private async Task<IReadOnlyList<SecureItem>> GetSecureItemReadSnapshotAsync(
        LocalMdbxDatabase database,
        IReadOnlyList<Category> categories,
        CancellationToken cancellationToken)
    {
        if (_secureItemReadSnapshot is not null &&
            IsCacheFresh(_secureItemReadSnapshotDatabaseId, _secureItemReadSnapshotCachedAt, database.Id))
        {
            return _secureItemReadSnapshot;
        }

        _secureItemReadSnapshot = (await mdbxVaultStore.GetSecureItemsAsync(
                database,
                categories,
                itemType: null,
                includeDeleted: true,
                cancellationToken))
            .ToArray();
        _secureItemReadSnapshotDatabaseId = database.Id;
        _secureItemReadSnapshotCachedAt = DateTimeOffset.UtcNow;
        return _secureItemReadSnapshot;
    }

    private void ClearPasswordReadSnapshot()
    {
        _passwordReadSnapshot = null;
        _passwordReadSnapshotDatabaseId = null;
        _passwordReadSnapshotCachedAt = default;
    }

    private void ClearSecureItemReadSnapshot()
    {
        _secureItemReadSnapshot = null;
        _secureItemReadSnapshotDatabaseId = null;
        _secureItemReadSnapshotCachedAt = default;
    }

    private void ClearCategoryReadCache()
    {
        _categoryReadCache = null;
        _categoryReadCacheDatabaseId = null;
        _categoryReadCacheCachedAt = default;
    }

    private void ClearDefaultLocalMdbxDatabaseCache()
    {
        _defaultLocalMdbxDatabase = null;
        _defaultLocalMdbxDatabaseId = null;
        _defaultLocalMdbxDatabaseCachedAt = default;
    }

    private void ClearMdbxReadCaches()
    {
        ClearDefaultLocalMdbxDatabaseCache();
        ClearCategoryReadCache();
        ClearPasswordReadSnapshot();
        ClearSecureItemReadSnapshot();
    }

    void ITransientVaultReadCache.ReleaseVaultItemSnapshots()
    {
        ClearPasswordReadSnapshot();
        ClearSecureItemReadSnapshot();
    }

    private async Task<PasswordEntry?> FindPasswordForMdbxOperationAsync(
        LocalMdbxDatabase database,
        IReadOnlyList<Category> categories,
        long id,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        return await mdbxVaultStore.FindPasswordAsync(database, categories, id, includeDeleted, cancellationToken);
    }

    private async Task<SecureItem?> FindSecureItemForMdbxOperationAsync(
        LocalMdbxDatabase database,
        IReadOnlyList<Category> categories,
        long id,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        return await mdbxVaultStore.FindSecureItemAsync(database, categories, id, includeDeleted, cancellationToken);
    }

    private static void ClearForeignMdbxBindingForNewPassword(PasswordEntry entry)
    {
        if (entry.Id != 0)
        {
            return;
        }

        entry.MdbxDatabaseId = null;
        entry.MdbxFolderId = null;
    }

    private async Task<long?> FindPasswordHistoryOwnerIdAsync(long historyId, CancellationToken cancellationToken)
    {
        if (historyId <= 0)
        {
            return null;
        }

        var database = await RequireDefaultMdbxDatabaseAsync(cancellationToken);
        return await mdbxVaultStore.FindPasswordHistoryOwnerIdAsync(database, historyId, cancellationToken);
    }

    private async Task SaveSecureItemToMdbxAsync(
        LocalMdbxDatabase database,
        SecureItem item,
        IReadOnlyDictionary<long, Category> categories,
        CancellationToken cancellationToken)
    {
        await MarkRemoteWorkingCopyPendingAsync(database, cancellationToken);
        await mdbxVaultStore.SaveSecureItemAsync(database, item, categories, cancellationToken);
        if (attachmentContentStore is not null && await MigrateSecureItemImagePathsAsync(database, item, cancellationToken))
        {
            await mdbxVaultStore.SaveSecureItemAsync(database, item, categories, cancellationToken);
        }
    }

    private async Task MarkRemoteWorkingCopyPendingAsync(
        LocalMdbxDatabase database,
        CancellationToken cancellationToken)
    {
        if (database.StorageLocation != MdbxStorageLocation.RemoteWebDav ||
            (database.LastSyncStatus == SyncStatus.PendingUpload && database.LastSyncError is null))
        {
            return;
        }

        database.LastSyncStatus = SyncStatus.PendingUpload;
        database.LastSyncError = null;
        await inner.SaveMdbxDatabaseAsync(database, cancellationToken);
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

    private static bool IsSecureItemOwnerType(string ownerType) =>
        string.Equals(ownerType, "SECURE_ITEM", StringComparison.OrdinalIgnoreCase);

    private static PasswordEntry CreatePasswordTombstone(PasswordEntry entry, DateTimeOffset updatedAt) => new()
    {
        Id = entry.Id,
        MdbxDatabaseId = entry.MdbxDatabaseId,
        MdbxFolderId = entry.MdbxFolderId,
        IsDeleted = true,
        DeletedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = updatedAt
    };

    private static SecureItem CreateSecureItemTombstone(SecureItem item, DateTimeOffset updatedAt) => new()
    {
        Id = item.Id,
        ItemType = item.ItemType,
        MdbxDatabaseId = item.MdbxDatabaseId,
        MdbxFolderId = item.MdbxFolderId,
        IsDeleted = true,
        DeletedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = updatedAt
    };
}
