using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data.Repositories;

namespace Monica.Data.Mdbx;

public interface IMdbxVaultStore
{
    bool IsAvailable { get; }
    Task<IReadOnlyList<Category>> GetCategoriesAsync(LocalMdbxDatabase database, CancellationToken cancellationToken = default);
    Task<Category> SaveCategoryAsync(LocalMdbxDatabase database, Category category, CancellationToken cancellationToken = default);
    Task<PasswordEntry> SavePasswordAsync(LocalMdbxDatabase database, PasswordEntry entry, CancellationToken cancellationToken = default);
    Task<PasswordEntry> SavePasswordAsync(LocalMdbxDatabase database, PasswordEntry entry, IReadOnlyDictionary<long, Category> categories, CancellationToken cancellationToken = default);
    Task<PasswordEntry> SavePasswordAsync(LocalMdbxDatabase database, PasswordEntry entry, IReadOnlyList<CustomField> customFields, IReadOnlyDictionary<long, Category> categories, CancellationToken cancellationToken = default);
    Task<PasswordEntry> SavePasswordAsync(LocalMdbxDatabase database, PasswordEntry entry, IReadOnlyList<CustomField> customFields, IReadOnlyList<PasswordHistoryEntry> passwordHistory, IReadOnlyDictionary<long, Category> categories, CancellationToken cancellationToken = default);
    Task<PasswordEntry> SavePasswordAsync(LocalMdbxDatabase database, PasswordEntry entry, IReadOnlyList<CustomField> customFields, IReadOnlyList<PasswordHistoryEntry> passwordHistory, IReadOnlyList<Attachment> attachments, IReadOnlyDictionary<long, Category> categories, CancellationToken cancellationToken = default);
    Task<MdbxPasswordReadSnapshot> GetPasswordReadSnapshotAsync(LocalMdbxDatabase database, IReadOnlyList<Category> categories, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PasswordEntry>> GetPasswordsAsync(LocalMdbxDatabase database, bool includeDeleted = false, bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PasswordEntry>> GetPasswordsAsync(LocalMdbxDatabase database, IReadOnlyList<Category> categories, bool includeDeleted = false, bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<PasswordEntry?> FindPasswordAsync(LocalMdbxDatabase database, IReadOnlyList<Category> categories, long entryId, bool includeDeleted = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<long, IReadOnlyList<CustomField>>> GetPasswordCustomFieldsByEntryIdsAsync(LocalMdbxDatabase database, IReadOnlyList<long> entryIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<long>> SearchPasswordEntryIdsByCustomFieldContentAsync(LocalMdbxDatabase database, string query, CancellationToken cancellationToken = default);
    Task<PasswordMetadataSearchResult> SearchPasswordMetadataAsync(LocalMdbxDatabase database, string query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PasswordHistoryEntry>?> GetPasswordHistoryAsync(LocalMdbxDatabase database, long entryId, CancellationToken cancellationToken = default);
    Task<long?> FindPasswordHistoryOwnerIdAsync(LocalMdbxDatabase database, long historyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<long, IReadOnlyList<Attachment>>> GetPasswordAttachmentsByEntryIdsAsync(LocalMdbxDatabase database, IReadOnlyList<long> entryIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<long>> SearchPasswordEntryIdsByAttachmentMetadataAsync(LocalMdbxDatabase database, string query, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<long, IReadOnlyList<Attachment>>> GetSecureItemAttachmentsByItemIdsAsync(LocalMdbxDatabase database, IReadOnlyList<long> itemIds, CancellationToken cancellationToken = default);
    Task<byte[]?> TryReadAttachmentContentAsync(LocalMdbxDatabase database, Attachment attachment, CancellationToken cancellationToken = default);
    Task SoftDeletePasswordAsync(LocalMdbxDatabase database, PasswordEntry entry, CancellationToken cancellationToken = default);
    Task RestorePasswordAsync(LocalMdbxDatabase database, PasswordEntry entry, CancellationToken cancellationToken = default);
    Task SoftDeletePasswordEntriesAsync(LocalMdbxDatabase database, CancellationToken cancellationToken = default);
    Task<SecureItem> SaveSecureItemAsync(LocalMdbxDatabase database, SecureItem item, CancellationToken cancellationToken = default);
    Task<SecureItem> SaveSecureItemAsync(LocalMdbxDatabase database, SecureItem item, IReadOnlyDictionary<long, Category> categories, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SecureItem>> GetSecureItemsAsync(LocalMdbxDatabase database, VaultItemType? itemType = null, bool includeDeleted = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SecureItem>> GetSecureItemsAsync(LocalMdbxDatabase database, IReadOnlyList<Category> categories, VaultItemType? itemType = null, bool includeDeleted = false, CancellationToken cancellationToken = default);
    Task<SecureItem?> FindSecureItemAsync(LocalMdbxDatabase database, IReadOnlyList<Category> categories, long itemId, bool includeDeleted = false, CancellationToken cancellationToken = default);
    Task SoftDeleteSecureItemAsync(LocalMdbxDatabase database, SecureItem item, CancellationToken cancellationToken = default);
    Task RestoreSecureItemAsync(LocalMdbxDatabase database, SecureItem item, CancellationToken cancellationToken = default);
    Task SoftDeleteSecureItemEntriesAsync(LocalMdbxDatabase database, CancellationToken cancellationToken = default);
    Task DetachSecureItemsFromPasswordsAsync(LocalMdbxDatabase database, IReadOnlyList<Category> categories, CancellationToken cancellationToken = default);
    Task UnassignCategoryAsync(LocalMdbxDatabase database, Category category, CancellationToken cancellationToken = default);
    Task<Attachment> SaveSecureItemAttachmentAsync(LocalMdbxDatabase database, SecureItem item, Attachment attachment, byte[] content, CancellationToken cancellationToken = default);
    Task<Attachment> SavePasswordAttachmentAsync(LocalMdbxDatabase database, PasswordEntry entry, Attachment attachment, byte[] content, CancellationToken cancellationToken = default);
    Task DeleteAttachmentAsync(LocalMdbxDatabase database, Attachment attachment, CancellationToken cancellationToken = default);
}

public sealed record MdbxPasswordReadSnapshot(
    IReadOnlyList<PasswordEntry> Passwords,
    IReadOnlyDictionary<long, IReadOnlyList<CustomField>> CustomFieldsByEntryId,
    IReadOnlyDictionary<long, IReadOnlyList<Attachment>> AttachmentsByEntryId);

public sealed partial class MdbxVaultStore(IMdbxNativeBridge nativeBridge, ICryptoService? cryptoService = null) : IMdbxVaultStore
{
    private const string DefaultProjectTitle = "Monica";
    private const string DeviceId = "monica-avalonia";
    private static readonly string[] PasswordEntryTypes = ["login", "ssh-key"];
    private static readonly string[] SecureEntryTypes = ["note", "totp", "card", "document-ref"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        IgnoreReadOnlyProperties = true
    };

    public bool IsAvailable => nativeBridge.IsAvailable;

    public async Task<IReadOnlyList<Category>> GetCategoriesAsync(LocalMdbxDatabase database, CancellationToken cancellationToken = default)
    {
        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var projects = await EnsureProjectsForReadAsync(vault, cancellationToken);
        var categories = new List<Category>();
        foreach (var project in projects
                     .Where(project => !string.Equals(project.Title, DefaultProjectTitle, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(project => project.Title, StringComparer.OrdinalIgnoreCase))
        {
            categories.Add(new Category
            {
                Name = project.Title,
                SortOrder = categories.Count,
                MdbxDatabaseId = database.Id,
                MdbxFolderId = project.ProjectId
            });
        }

        return categories;
    }

    public async Task<Category> SaveCategoryAsync(LocalMdbxDatabase database, Category category, CancellationToken cancellationToken = default)
    {
        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var project = await EnsureProjectAsync(vault, category.MdbxFolderId, category.Name, cancellationToken);
        category.MdbxDatabaseId = database.Id;
        category.MdbxFolderId = project.ProjectId;
        return category;
    }

    public Task<PasswordEntry> SavePasswordAsync(LocalMdbxDatabase database, PasswordEntry entry, CancellationToken cancellationToken = default) =>
        SavePasswordAsync(database, entry, new Dictionary<long, Category>(), cancellationToken);

    public async Task<PasswordEntry> SavePasswordAsync(LocalMdbxDatabase database, PasswordEntry entry, IReadOnlyDictionary<long, Category> categories, CancellationToken cancellationToken = default)
    {
        return await SavePasswordAsync(database, entry, [], categories, cancellationToken);
    }

    public Task<PasswordEntry> SavePasswordAsync(
        LocalMdbxDatabase database,
        PasswordEntry entry,
        IReadOnlyList<CustomField> customFields,
        IReadOnlyDictionary<long, Category> categories,
        CancellationToken cancellationToken = default) =>
        SavePasswordAsync(database, entry, customFields, [], categories, cancellationToken);

    public Task<PasswordEntry> SavePasswordAsync(
        LocalMdbxDatabase database,
        PasswordEntry entry,
        IReadOnlyList<CustomField> customFields,
        IReadOnlyList<PasswordHistoryEntry> passwordHistory,
        IReadOnlyDictionary<long, Category> categories,
        CancellationToken cancellationToken = default) =>
        SavePasswordAsync(database, entry, customFields, passwordHistory, [], categories, cancellationToken);

    public async Task<PasswordEntry> SavePasswordAsync(
        LocalMdbxDatabase database,
        PasswordEntry entry,
        IReadOnlyList<CustomField> customFields,
        IReadOnlyList<PasswordHistoryEntry> passwordHistory,
        IReadOnlyList<Attachment> attachments,
        IReadOnlyDictionary<long, Category> categories,
        CancellationToken cancellationToken = default)
    {
        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var project = await ResolveProjectAsync(vault, entry.CategoryId, categories, cancellationToken);
        var entryType = ToMdbxEntryType(entry);
        var portableEntry = ClonePasswordEntryForPayload(entry);
        portableEntry.Password = ToPortableSensitiveValue(portableEntry.Password);
        portableEntry.AuthenticatorKey = ToPortableSensitiveValue(portableEntry.AuthenticatorKey);
        var portableFields = NormalizeCustomFields(entry.Id, customFields)
            .Select(field =>
            {
                field.Value = field.IsProtected ? ToPortableSensitiveValue(field.Value) : field.Value;
                return field;
            })
            .ToList();
        var portableHistory = NormalizePasswordHistory(entry.Id, passwordHistory)
            .Select(history =>
            {
                history.Password = ToPortableSensitiveValue(history.Password);
                return history;
            })
            .ToList();
        var boundNoteEntryId = await ResolveSecureItemEntryIdAsync(vault, entry.BoundNoteId, "note", cancellationToken);
        var payload = AndroidMdbxPayloadCodec.EncodePassword(
            portableEntry,
            portableFields,
            boundNoteEntryId,
            portableHistory,
            NormalizeAttachments(entry.Id, attachments).ToList());
        var record = await SaveEntryAsync(vault, project.ProjectId, entry.MdbxFolderId, PasswordEntryTypes, entryType, entry.Title, payload, cancellationToken);

        entry.MdbxDatabaseId = database.Id;
        entry.MdbxFolderId = record.EntryId;
        return entry;
    }

    public Task<IReadOnlyList<PasswordEntry>> GetPasswordsAsync(LocalMdbxDatabase database, bool includeDeleted = false, bool includeArchived = false, CancellationToken cancellationToken = default) =>
        GetPasswordsAsync(database, [], includeDeleted, includeArchived, cancellationToken);

    public async Task<IReadOnlyList<PasswordEntry>> GetPasswordsAsync(LocalMdbxDatabase database, IReadOnlyList<Category> categories, bool includeDeleted = false, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetPasswordReadSnapshotAsync(database, categories, cancellationToken);
        return snapshot.Passwords
            .Where(entry => includeDeleted || !entry.IsDeleted)
            .Where(entry => includeArchived || !entry.IsArchived)
            .ToList();
    }

    public async Task<MdbxPasswordReadSnapshot> GetPasswordReadSnapshotAsync(LocalMdbxDatabase database, IReadOnlyList<Category> categories, CancellationToken cancellationToken = default)
    {
        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var projects = await EnsureProjectsForReadAsync(vault, cancellationToken);
        var categoryByProjectId = BuildCategoryByProjectId(categories, projects);
        var records = await ListPasswordRecordsAsync(vault, projects, includeDeleted: true, cancellationToken);
        var payloads = records
            .Select(record => (Record: record, Payload: DeserializePasswordPayload(record.PayloadJson, record.Title)))
            .Where(item => item.Payload is not null)
            .Select(item =>
            {
                var record = item.Record;
                var entry = item.Payload!.Entry;
                entry.MdbxDatabaseId = database.Id;
                entry.MdbxFolderId = record.EntryId;
                if (categoryByProjectId.TryGetValue(record.ProjectId, out var categoryId))
                {
                    entry.CategoryId = categoryId;
                }

                entry.IsDeleted = record.Deleted;
                return (Record: record, Payload: item.Payload!, Entry: entry);
            })
            .ToList();
        var passwords = payloads
            .Select(item => item.Entry)
            .OrderByDescending(entry => entry.IsFavorite)
            .ThenBy(entry => entry.SortOrder)
            .ThenByDescending(entry => entry.UpdatedAt)
            .ToList();
        var customFieldsByEntryId = new Dictionary<long, IReadOnlyList<CustomField>>();
        var attachmentsByEntryId = new Dictionary<long, IReadOnlyList<Attachment>>();
        foreach (var item in payloads.OrderBy(item => item.Record.Deleted))
        {
            var entryId = item.Entry.Id;
            if (entryId <= 0)
            {
                continue;
            }

            if (item.Payload.CustomFields is not null && !customFieldsByEntryId.ContainsKey(entryId))
            {
                customFieldsByEntryId[entryId] = NormalizeCustomFields(entryId, item.Payload.CustomFields).ToList();
            }

            if (item.Payload.Attachments is not null && !attachmentsByEntryId.ContainsKey(entryId))
            {
                attachmentsByEntryId[entryId] = NormalizeAttachments(entryId, item.Payload.Attachments).ToList();
            }
        }

        return new MdbxPasswordReadSnapshot(passwords, customFieldsByEntryId, attachmentsByEntryId);
    }

    public async Task<PasswordEntry?> FindPasswordAsync(LocalMdbxDatabase database, IReadOnlyList<Category> categories, long entryId, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        if (entryId <= 0)
        {
            return null;
        }

        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var projects = await EnsureProjectsForReadAsync(vault, cancellationToken);
        var categoryByProjectId = BuildCategoryByProjectId(categories, projects);
        var records = await ListPasswordRecordsAsync(vault, projects, includeDeleted, cancellationToken);
        foreach (var item in records
                     .Select(record => (Record: record, Payload: DeserializePasswordPayload(record.PayloadJson, record.Title)))
                     .Where(item => item.Payload?.Entry.Id == entryId)
                     .OrderBy(item => item.Record.Deleted))
        {
            var record = item.Record;
            var entry = item.Payload!.Entry;
            entry.MdbxDatabaseId = database.Id;
            entry.MdbxFolderId = record.EntryId;
            if (categoryByProjectId.TryGetValue(record.ProjectId, out var categoryId))
            {
                entry.CategoryId = categoryId;
            }

            entry.IsDeleted = record.Deleted;
            return entry;
        }

        return null;
    }

    public async Task<IReadOnlyDictionary<long, IReadOnlyList<CustomField>>> GetPasswordCustomFieldsByEntryIdsAsync(
        LocalMdbxDatabase database,
        IReadOnlyList<long> entryIds,
        CancellationToken cancellationToken = default)
    {
        var ids = entryIds.Where(id => id > 0).Distinct().ToHashSet();
        if (ids.Count == 0)
        {
            return new Dictionary<long, IReadOnlyList<CustomField>>();
        }

        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var projects = await EnsureProjectsForReadAsync(vault, cancellationToken);
        var records = await ListPasswordRecordsAsync(vault, projects, includeDeleted: true, cancellationToken);
        var result = new Dictionary<long, IReadOnlyList<CustomField>>();

        foreach (var item in records
                     .Select(record => (Record: record, Payload: DeserializePasswordPayload(record.PayloadJson, record.Title)))
                     .Where(item => item.Payload is not null)
                     .OrderBy(item => item.Record.Deleted))
        {
            var payload = item.Payload!;
            if (!ids.Contains(payload.Entry.Id) || payload.CustomFields is null || result.ContainsKey(payload.Entry.Id))
            {
                continue;
            }

            result[payload.Entry.Id] = NormalizeCustomFields(payload.Entry.Id, payload.CustomFields).ToList();
        }

        return result;
    }

    public async Task<IReadOnlyList<long>> SearchPasswordEntryIdsByCustomFieldContentAsync(
        LocalMdbxDatabase database,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var term = query.Trim();
        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var projects = await EnsureProjectsForReadAsync(vault, cancellationToken);
        var records = await ListPasswordRecordsAsync(vault, projects, includeDeleted: true, cancellationToken);

        return records
            .Where(record => !record.Deleted)
            .Select(record => DeserializePasswordPayload(record.PayloadJson, record.Title))
            .Where(payload => payload?.CustomFields is not null)
            .Select(payload => payload!)
            .Where(payload => payload.CustomFields!.Any(field =>
                field.Title.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                field.Value.Contains(term, StringComparison.CurrentCultureIgnoreCase)))
            .Select(payload => payload.Entry.Id)
            .Where(id => id > 0)
            .Distinct()
            .OrderBy(id => id)
            .ToList();
    }

    public async Task<IReadOnlyList<PasswordHistoryEntry>?> GetPasswordHistoryAsync(
        LocalMdbxDatabase database,
        long entryId,
        CancellationToken cancellationToken = default)
    {
        if (entryId <= 0)
        {
            return [];
        }

        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var projects = await EnsureProjectsForReadAsync(vault, cancellationToken);
        var records = await ListPasswordRecordsAsync(vault, projects, includeDeleted: true, cancellationToken);

        foreach (var item in records
                     .Select(record => (Record: record, Payload: DeserializePasswordPayload(record.PayloadJson, record.Title)))
                     .Where(item => item.Payload is not null)
                     .OrderBy(item => item.Record.Deleted))
        {
            var payload = item.Payload!;
            if (payload.Entry.Id != entryId)
            {
                continue;
            }

            return payload.PasswordHistory is null
                ? null
                : NormalizePasswordHistory(entryId, payload.PasswordHistory).ToList();
        }

        return null;
    }

    public async Task<long?> FindPasswordHistoryOwnerIdAsync(
        LocalMdbxDatabase database,
        long historyId,
        CancellationToken cancellationToken = default)
    {
        if (historyId <= 0)
        {
            return null;
        }

        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var projects = await EnsureProjectsForReadAsync(vault, cancellationToken);
        var records = await ListPasswordRecordsAsync(vault, projects, includeDeleted: true, cancellationToken);

        return records
            .Select(record => DeserializePasswordPayload(record.PayloadJson, record.Title))
            .Where(payload => payload?.PasswordHistory is not null)
            .Select(payload => payload!)
            .FirstOrDefault(payload => payload.PasswordHistory!.Any(entry => entry.Id == historyId))
            ?.Entry.Id;
    }

    public async Task<IReadOnlyDictionary<long, IReadOnlyList<Attachment>>> GetPasswordAttachmentsByEntryIdsAsync(
        LocalMdbxDatabase database,
        IReadOnlyList<long> entryIds,
        CancellationToken cancellationToken = default)
    {
        var ids = entryIds.Where(id => id > 0).Distinct().ToHashSet();
        if (ids.Count == 0)
        {
            return new Dictionary<long, IReadOnlyList<Attachment>>();
        }

        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var projects = await EnsureProjectsForReadAsync(vault, cancellationToken);
        var records = await ListPasswordRecordsAsync(vault, projects, includeDeleted: true, cancellationToken);
        var result = new Dictionary<long, IReadOnlyList<Attachment>>();

        foreach (var item in records
                     .Select(record => (Record: record, Payload: DeserializePasswordPayload(record.PayloadJson, record.Title)))
                     .Where(item => !item.Record.Deleted && item.Payload is not null)
                     .OrderBy(item => item.Record.Deleted))
        {
            var payload = item.Payload!;
            if (!ids.Contains(payload.Entry.Id) || payload.Attachments is null || result.ContainsKey(payload.Entry.Id))
            {
                continue;
            }

            result[payload.Entry.Id] = NormalizeAttachments(payload.Entry.Id, payload.Attachments).ToList();
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<long, IReadOnlyList<Attachment>>> GetSecureItemAttachmentsByItemIdsAsync(
        LocalMdbxDatabase database,
        IReadOnlyList<long> itemIds,
        CancellationToken cancellationToken = default)
    {
        var ids = itemIds.Where(id => id > 0).Distinct().ToHashSet();
        if (ids.Count == 0)
        {
            return new Dictionary<long, IReadOnlyList<Attachment>>();
        }

        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var projects = await EnsureProjectsForReadAsync(vault, cancellationToken);
        var records = await ListSecureRecordsAsync(vault, projects, includeDeleted: true, cancellationToken);
        var result = new Dictionary<long, IReadOnlyList<Attachment>>();

        foreach (var item in records
                     .Select(record => (Record: record, Payload: DeserializeSecureItemPayloadSnapshot(record.PayloadJson, record.Title, record.EntryType)))
                     .Where(item => item.Payload is not null)
                     .OrderBy(item => item.Record.Deleted))
        {
            var payload = item.Payload!;
            if (!ids.Contains(payload.Item.Id) || result.ContainsKey(payload.Item.Id))
            {
                continue;
            }

            var nativeAttachments = (await vault.ListAttachmentsByEntryAsync(item.Record.EntryId, cancellationToken))
                .Where(attachment => !attachment.Deleted)
                .ToList();
            result[payload.Item.Id] = nativeAttachments.Count > 0
                ? NormalizeSecureItemNativeAttachments(payload.Item.Id, nativeAttachments).ToList()
                : payload.Attachments is { Count: > 0 }
                ? NormalizeSecureItemAttachments(payload.Item.Id, payload.Attachments).ToList()
                : NormalizeSecureItemAttachments(payload.Item.Id, DecodeSecureItemImagePaths(payload.Item)).ToList();
        }

        return result;
    }

    public async Task<byte[]?> TryReadAttachmentContentAsync(LocalMdbxDatabase database, Attachment attachment, CancellationToken cancellationToken = default)
    {
        var attachmentId = TryParseAttachmentStoragePath(attachment.StoragePath);
        if (attachmentId is null)
        {
            return null;
        }

        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        try
        {
            return await vault.ReadAttachmentContentAsync(attachmentId, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public async Task SoftDeletePasswordAsync(LocalMdbxDatabase database, PasswordEntry entry, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entry.MdbxFolderId))
        {
            return;
        }

        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var record = await FindEntryAsync(vault, entry.MdbxFolderId!, PasswordEntryTypes, includeDeleted: true, cancellationToken);
        if (record is not null && !record.Deleted)
        {
            await vault.DeleteEntryAsync(record.ProjectId, entry.MdbxFolderId!, cancellationToken);
        }
    }

    public async Task RestorePasswordAsync(LocalMdbxDatabase database, PasswordEntry entry, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entry.MdbxFolderId))
        {
            return;
        }

        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var record = await FindEntryAsync(vault, entry.MdbxFolderId!, PasswordEntryTypes, includeDeleted: true, cancellationToken);
        if (record is not null)
        {
            await vault.RestoreEntryAsync(record.ProjectId, entry.MdbxFolderId!, cancellationToken);
        }
    }

    public Task SoftDeletePasswordEntriesAsync(LocalMdbxDatabase database, CancellationToken cancellationToken = default) =>
        SoftDeleteEntriesByTypesAsync(database, PasswordEntryTypes, deleteAttachments: true, cancellationToken);

    public Task<SecureItem> SaveSecureItemAsync(LocalMdbxDatabase database, SecureItem item, CancellationToken cancellationToken = default) =>
        SaveSecureItemAsync(database, item, new Dictionary<long, Category>(), cancellationToken);

    public async Task<SecureItem> SaveSecureItemAsync(LocalMdbxDatabase database, SecureItem item, IReadOnlyDictionary<long, Category> categories, CancellationToken cancellationToken = default)
    {
        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var project = await ResolveProjectAsync(vault, item.CategoryId, categories, cancellationToken);
        var entryType = ToMdbxEntryType(item.ItemType);
        var portableItem = CloneSecureItemForPayload(item);
        portableItem.ItemData = ToPortableSensitiveValue(portableItem.ItemData);
        var boundPasswordEntryId = await ResolvePasswordEntryIdAsync(vault, item.BoundPasswordId, cancellationToken);
        var payload = AndroidMdbxPayloadCodec.EncodeSecureItem(
            portableItem,
            boundPasswordEntryId,
            NormalizeSecureItemAttachments(item.Id, DecodeSecureItemImagePaths(item)).ToList());
        var record = await SaveEntryAsync(vault, project.ProjectId, item.MdbxFolderId, SecureEntryTypes, entryType, item.Title, payload, cancellationToken);
        await DeleteUnreferencedEntryAttachmentsAsync(vault, record.EntryId, DecodeSecureItemImagePaths(item), cancellationToken);

        item.MdbxDatabaseId = database.Id;
        item.MdbxFolderId = record.EntryId;
        return item;
    }

    public Task<IReadOnlyList<SecureItem>> GetSecureItemsAsync(LocalMdbxDatabase database, VaultItemType? itemType = null, bool includeDeleted = false, CancellationToken cancellationToken = default) =>
        GetSecureItemsAsync(database, [], itemType, includeDeleted, cancellationToken);

    public async Task<IReadOnlyList<SecureItem>> GetSecureItemsAsync(LocalMdbxDatabase database, IReadOnlyList<Category> categories, VaultItemType? itemType = null, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var projects = await EnsureProjectsForReadAsync(vault, cancellationToken);
        var categoryByProjectId = BuildCategoryByProjectId(categories, projects);
        var records = itemType is null
            ? await ListSecureRecordsAsync(vault, projects, includeDeleted, cancellationToken)
            : await ListSecureRecordsAsync(vault, projects, [ToMdbxEntryType(itemType.Value)], includeDeleted, cancellationToken);
        var payloads = records
            .Select(record => (Record: record, Payload: DeserializeSecureItemPayloadSnapshot(record.PayloadJson, record.Title, record.EntryType)))
            .Where(item => item.Payload is not null)
            .ToList();
        IReadOnlyDictionary<string, long> passwordIdByEntryId = payloads.Any(item => item.Payload!.BoundPasswordEntryId is { Length: > 0 })
            ? await BuildPasswordIdByEntryIdAsync(vault, projects, cancellationToken)
            : new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        return payloads
            .Select(item =>
            {
                var record = item.Record;
                var secureItem = item.Payload!.Item;
                secureItem.MdbxDatabaseId = database.Id;
                secureItem.MdbxFolderId = record.EntryId;
                if (item.Payload.BoundPasswordEntryId is { Length: > 0 } boundEntryId &&
                    passwordIdByEntryId.TryGetValue(boundEntryId, out var boundPasswordId))
                {
                    secureItem.BoundPasswordId = boundPasswordId;
                }
                if (categoryByProjectId.TryGetValue(record.ProjectId, out var categoryId))
                {
                    secureItem.CategoryId = categoryId;
                }

                secureItem.IsDeleted = record.Deleted;
                return secureItem;
            })
            .Where(item => includeDeleted || !item.IsDeleted)
            .OrderByDescending(item => item.IsFavorite)
            .ThenBy(item => item.SortOrder)
            .ThenByDescending(item => item.UpdatedAt)
            .ToList();
    }

    public async Task<SecureItem?> FindSecureItemAsync(LocalMdbxDatabase database, IReadOnlyList<Category> categories, long itemId, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        if (itemId <= 0)
        {
            return null;
        }

        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var projects = await EnsureProjectsForReadAsync(vault, cancellationToken);
        var categoryByProjectId = BuildCategoryByProjectId(categories, projects);
        var records = await ListSecureRecordsAsync(vault, projects, includeDeleted, cancellationToken);
        var matches = records
            .Select(record => (Record: record, Payload: DeserializeSecureItemPayloadSnapshot(record.PayloadJson, record.Title, record.EntryType)))
            .Where(item => item.Payload?.Item.Id == itemId)
            .OrderBy(item => item.Record.Deleted)
            .ToList();
        IReadOnlyDictionary<string, long> passwordIdByEntryId = matches.Any(item => item.Payload!.BoundPasswordEntryId is { Length: > 0 })
            ? await BuildPasswordIdByEntryIdAsync(vault, projects, cancellationToken)
            : new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in matches)
        {
            var record = item.Record;
            var secureItem = item.Payload!.Item;
            secureItem.MdbxDatabaseId = database.Id;
            secureItem.MdbxFolderId = record.EntryId;
            if (item.Payload.BoundPasswordEntryId is { Length: > 0 } boundEntryId &&
                passwordIdByEntryId.TryGetValue(boundEntryId, out var boundPasswordId))
            {
                secureItem.BoundPasswordId = boundPasswordId;
            }
            if (categoryByProjectId.TryGetValue(record.ProjectId, out var categoryId))
            {
                secureItem.CategoryId = categoryId;
            }

            secureItem.IsDeleted = record.Deleted;
            return secureItem;
        }

        return null;
    }

    public async Task SoftDeleteSecureItemAsync(LocalMdbxDatabase database, SecureItem item, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(item.MdbxFolderId))
        {
            return;
        }

        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var record = await FindEntryAsync(vault, item.MdbxFolderId!, SecureEntryTypes, includeDeleted: true, cancellationToken);
        if (record is not null && !record.Deleted)
        {
            await vault.DeleteEntryAsync(record.ProjectId, item.MdbxFolderId!, cancellationToken);
        }
    }

    public async Task RestoreSecureItemAsync(LocalMdbxDatabase database, SecureItem item, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(item.MdbxFolderId))
        {
            return;
        }

        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var record = await FindEntryAsync(vault, item.MdbxFolderId!, SecureEntryTypes, includeDeleted: true, cancellationToken);
        if (record is not null)
        {
            await vault.RestoreEntryAsync(record.ProjectId, item.MdbxFolderId!, cancellationToken);
        }
    }

    public Task SoftDeleteSecureItemEntriesAsync(LocalMdbxDatabase database, CancellationToken cancellationToken = default) =>
        SoftDeleteEntriesByTypesAsync(database, SecureEntryTypes, deleteAttachments: true, cancellationToken);

    public async Task DetachSecureItemsFromPasswordsAsync(LocalMdbxDatabase database, IReadOnlyList<Category> categories, CancellationToken cancellationToken = default)
    {
        var items = await GetSecureItemsAsync(database, categories, itemType: null, includeDeleted: false, cancellationToken);
        var categoryById = categories.ToDictionary(category => category.Id);
        foreach (var item in items.Where(item => item.BoundPasswordId is not null))
        {
            item.BoundPasswordId = null;
            await SaveSecureItemAsync(database, item, categoryById, cancellationToken);
        }
    }

    public async Task UnassignCategoryAsync(LocalMdbxDatabase database, Category category, CancellationToken cancellationToken = default)
    {
        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var projects = await EnsureProjectsForReadAsync(vault, cancellationToken);
        var categoryProject = ResolveCategoryProject(category, projects);
        if (categoryProject is null)
        {
            return;
        }

        var defaultProject = await EnsureDefaultProjectAsync(vault, cancellationToken);
        var passwordRecords = await ListPasswordRecordsAsync(vault, [categoryProject], includeDeleted: true, cancellationToken);
        foreach (var record in passwordRecords)
        {
            var payload = DeserializePasswordPayload(record.PayloadJson, record.Title);
            if (payload is null)
            {
                continue;
            }

            payload.Entry.CategoryId = null;
            payload.Entry.MdbxDatabaseId = database.Id;
            payload.Entry.MdbxFolderId = record.EntryId;
            var entryType = ToMdbxEntryType(payload.Entry);
            var payloadJson = AndroidMdbxPayloadCodec.EncodePassword(
                ClonePasswordEntryForPayload(payload.Entry),
                payload.CustomFields ?? [],
                payload.BoundNoteEntryId,
                payload.PasswordHistory,
                payload.Attachments);
            if (!string.Equals(record.ProjectId, defaultProject.ProjectId, StringComparison.OrdinalIgnoreCase))
            {
                await vault.MoveEntryAsync(record.ProjectId, record.EntryId, defaultProject.ProjectId, cancellationToken);
            }

            await vault.UpdateEntryAsync(defaultProject.ProjectId, record.EntryId, entryType, payload.Entry.Title, payloadJson, cancellationToken);
        }

        foreach (var record in await ListSecureRecordsAsync(vault, [categoryProject], includeDeleted: true, cancellationToken))
        {
            var securePayload = DeserializeSecureItemPayloadSnapshot(record.PayloadJson, record.Title, record.EntryType);
            if (securePayload is null)
            {
                continue;
            }

            var item = securePayload.Item;
            item.CategoryId = null;
            item.MdbxDatabaseId = database.Id;
            item.MdbxFolderId = record.EntryId;
            var entryType = ToMdbxEntryType(item.ItemType);
            var payloadJson = AndroidMdbxPayloadCodec.EncodeSecureItem(
                CloneSecureItemForPayload(item),
                securePayload.BoundPasswordEntryId,
                NormalizeSecureItemAttachments(item.Id, DecodeSecureItemImagePaths(item)).ToList());
            if (!string.Equals(record.ProjectId, defaultProject.ProjectId, StringComparison.OrdinalIgnoreCase))
            {
                await vault.MoveEntryAsync(record.ProjectId, record.EntryId, defaultProject.ProjectId, cancellationToken);
            }

            await vault.UpdateEntryAsync(defaultProject.ProjectId, record.EntryId, entryType, item.Title, payloadJson, cancellationToken);
        }
    }

    public async Task<Attachment> SaveSecureItemAttachmentAsync(LocalMdbxDatabase database, SecureItem item, Attachment attachment, byte[] content, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(item.MdbxFolderId))
        {
            throw new InvalidOperationException("Secure item must be mirrored to MDBX before saving attachments.");
        }

        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var entryRecord = await FindEntryAsync(vault, item.MdbxFolderId!, SecureEntryTypes, includeDeleted: true, cancellationToken)
            ?? throw new InvalidOperationException("Secure item MDBX entry was not found.");
        var metadata = await vault.CreateAttachmentMetadataAsync(
            entryRecord.ProjectId,
            item.MdbxFolderId,
            attachment.FileName,
            string.IsNullOrWhiteSpace(attachment.ContentType) ? null : attachment.ContentType,
            "",
            (ulong)Math.Max(0, content.LongLength),
            cancellationToken);
        var written = await vault.WriteAttachmentInlineContentAsync(metadata.AttachmentId, content, cancellationToken);

        attachment.OwnerType = "SECURE_ITEM";
        attachment.OwnerId = item.Id;
        attachment.StoragePath = ToAttachmentStoragePath(written.AttachmentId);
        attachment.Id = MdbxStableId.FromString("attachment", attachment.StoragePath);
        attachment.SizeBytes = checked((long)Math.Min(written.OriginalSize, (ulong)long.MaxValue));
        if (string.IsNullOrWhiteSpace(attachment.ContentType) && !string.IsNullOrWhiteSpace(written.MediaType))
        {
            attachment.ContentType = written.MediaType;
        }

        return attachment;
    }

    public async Task<Attachment> SavePasswordAttachmentAsync(LocalMdbxDatabase database, PasswordEntry entry, Attachment attachment, byte[] content, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entry.MdbxFolderId))
        {
            throw new InvalidOperationException("Password entry must be mirrored to MDBX before saving attachments.");
        }

        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var entryRecord = await FindEntryAsync(vault, entry.MdbxFolderId!, PasswordEntryTypes, includeDeleted: true, cancellationToken);
        var project = entryRecord is null
            ? await EnsureDefaultProjectAsync(vault, cancellationToken)
            : new MdbxNativeProjectRecord(entryRecord.ProjectId, "", Deleted: false);
        var metadata = await vault.CreateAttachmentMetadataAsync(
            project.ProjectId,
            entry.MdbxFolderId,
            attachment.FileName,
            string.IsNullOrWhiteSpace(attachment.ContentType) ? null : attachment.ContentType,
            "",
            (ulong)Math.Max(0, content.LongLength),
            cancellationToken);
        var written = await vault.WriteAttachmentInlineContentAsync(metadata.AttachmentId, content, cancellationToken);

        attachment.StoragePath = ToAttachmentStoragePath(written.AttachmentId);
        if (attachment.Id <= 0)
        {
            attachment.Id = MdbxStableId.FromString("attachment", attachment.StoragePath);
        }
        attachment.SizeBytes = checked((long)Math.Min(written.OriginalSize, (ulong)long.MaxValue));
        if (string.IsNullOrWhiteSpace(attachment.ContentType) && !string.IsNullOrWhiteSpace(written.MediaType))
        {
            attachment.ContentType = written.MediaType;
        }

        return attachment;
    }

    public async Task DeleteAttachmentAsync(LocalMdbxDatabase database, Attachment attachment, CancellationToken cancellationToken = default)
    {
        var attachmentId = TryParseAttachmentStoragePath(attachment.StoragePath);
        if (attachmentId is null)
        {
            return;
        }

        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        await vault.DeleteAttachmentAsync(attachmentId, cancellationToken);
    }

    private async Task SoftDeleteEntriesByTypesAsync(
        LocalMdbxDatabase database,
        IReadOnlyList<string> entryTypes,
        bool deleteAttachments,
        CancellationToken cancellationToken)
    {
        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var projects = await vault.ListProjectsAsync(false, cancellationToken);
        foreach (var project in projects)
        {
            foreach (var entryType in entryTypes)
            {
                var records = await vault.ListEntriesAsync(project.ProjectId, entryType, cancellationToken);
                foreach (var record in records)
                {
                    if (deleteAttachments)
                    {
                        var attachments = await vault.ListAttachmentsByEntryAsync(record.EntryId, cancellationToken);
                        foreach (var attachment in attachments)
                        {
                            await vault.DeleteAttachmentAsync(attachment.AttachmentId, cancellationToken);
                        }
                    }

                    await vault.DeleteEntryAsync(project.ProjectId, record.EntryId, cancellationToken);
                }
            }
        }
    }

    private async Task<IMdbxNativeVault> OpenAsync(LocalMdbxDatabase database, CancellationToken cancellationToken)
    {
        var path = database.WorkingCopyPath ?? database.FilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("MDBX vault path is missing.");
        }

        if (string.IsNullOrWhiteSpace(database.EncryptedPassword))
        {
            throw new InvalidOperationException("MDBX vault password is missing.");
        }

        return await nativeBridge.OpenVaultAsync(path, database.EncryptedPassword, DeviceId, cancellationToken);
    }

    private static async Task<MdbxNativeProjectRecord> EnsureDefaultProjectAsync(IMdbxNativeVault vault, CancellationToken cancellationToken)
    {
        return await EnsureProjectAsync(vault, null, DefaultProjectTitle, cancellationToken);
    }

    private static async Task<MdbxNativeProjectRecord> EnsureProjectAsync(IMdbxNativeVault vault, string? projectId, string title, CancellationToken cancellationToken)
    {
        var projects = await vault.ListProjectsAsync(false, cancellationToken);
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            var existingById = projects.FirstOrDefault(project => string.Equals(project.ProjectId, projectId, StringComparison.OrdinalIgnoreCase));
            if (existingById is not null)
            {
                return existingById;
            }
        }

        var normalizedTitle = string.IsNullOrWhiteSpace(title) ? DefaultProjectTitle : title.Trim();
        return projects.FirstOrDefault(project => string.Equals(project.Title, normalizedTitle, StringComparison.OrdinalIgnoreCase))
            ?? await vault.CreateProjectAsync(normalizedTitle, cancellationToken);
    }

    private static async Task<IReadOnlyList<MdbxNativeProjectRecord>> EnsureProjectsForReadAsync(IMdbxNativeVault vault, CancellationToken cancellationToken)
    {
        var projects = (await vault.ListProjectsAsync(false, cancellationToken)).ToList();
        if (projects.Any(project => string.Equals(project.Title, DefaultProjectTitle, StringComparison.OrdinalIgnoreCase)))
        {
            return projects;
        }

        projects.Add(await vault.CreateProjectAsync(DefaultProjectTitle, cancellationToken));
        return projects;
    }

    private static async Task<bool> HasBusinessEntriesAsync(IMdbxNativeVault vault, string projectId, CancellationToken cancellationToken)
    {
        foreach (var entryType in PasswordEntryTypes.Concat(SecureEntryTypes))
        {
            if ((await vault.ListEntriesAsync(projectId, entryType, cancellationToken)).Count > 0 ||
                (await vault.ListDeletedEntriesAsync(projectId, entryType, cancellationToken)).Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<MdbxNativeProjectRecord> ResolveProjectAsync(
        IMdbxNativeVault vault,
        long? categoryId,
        IReadOnlyDictionary<long, Category> categories,
        CancellationToken cancellationToken)
    {
        if (categoryId is { } id && categories.TryGetValue(id, out var category))
        {
            return await EnsureProjectAsync(vault, category.MdbxFolderId, category.Name, cancellationToken);
        }

        return await EnsureDefaultProjectAsync(vault, cancellationToken);
    }

    private static async Task<MdbxNativeEntryRecord> SaveEntryAsync(
        IMdbxNativeVault vault,
        string targetProjectId,
        string? entryId,
        IReadOnlyList<string> searchEntryTypes,
        string entryType,
        string title,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entryId))
        {
            return await vault.CreateEntryAsync(targetProjectId, entryType, title, payloadJson, cancellationToken);
        }

        var existing = await FindEntryAsync(vault, entryId, searchEntryTypes, includeDeleted: true, cancellationToken);
        if (existing is null)
        {
            return await vault.CreateEntryAsync(targetProjectId, entryType, title, payloadJson, cancellationToken);
        }

        if (!string.Equals(existing.ProjectId, targetProjectId, StringComparison.OrdinalIgnoreCase))
        {
            await vault.MoveEntryAsync(existing.ProjectId, entryId, targetProjectId, cancellationToken);
        }

        return await vault.UpdateEntryAsync(targetProjectId, entryId, entryType, title, payloadJson, cancellationToken);
    }

    private static async Task<MdbxNativeEntryRecord?> FindEntryAsync(
        IMdbxNativeVault vault,
        string entryId,
        IReadOnlyList<string> entryTypes,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entryId))
        {
            return null;
        }

        var projects = await EnsureProjectsForReadAsync(vault, cancellationToken);
        foreach (var project in projects)
        {
            foreach (var entryType in entryTypes)
            {
                var active = await vault.ListEntriesAsync(project.ProjectId, entryType, cancellationToken);
                var match = active.FirstOrDefault(entry => string.Equals(entry.EntryId, entryId, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    return match;
                }

                if (includeDeleted)
                {
                    var deleted = await vault.ListDeletedEntriesAsync(project.ProjectId, entryType, cancellationToken);
                    match = deleted.FirstOrDefault(entry => string.Equals(entry.EntryId, entryId, StringComparison.OrdinalIgnoreCase));
                    if (match is not null)
                    {
                        return match;
                    }
                }
            }
        }

        return null;
    }

    private static async Task DeleteUnreferencedEntryAttachmentsAsync(IMdbxNativeVault vault, string entryId, IReadOnlyList<string> referencedImagePaths, CancellationToken cancellationToken)
    {
        var referencedAttachmentIds = referencedImagePaths
            .Select(TryParseAttachmentStoragePath)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var attachment in await vault.ListAttachmentsByEntryAsync(entryId, cancellationToken))
        {
            if (!referencedAttachmentIds.Contains(attachment.AttachmentId))
            {
                await vault.DeleteAttachmentAsync(attachment.AttachmentId, cancellationToken);
            }
        }
    }

    private static async Task<IReadOnlyList<MdbxNativeEntryRecord>> ListPasswordRecordsAsync(
        IMdbxNativeVault vault,
        IReadOnlyList<MdbxNativeProjectRecord> projects,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        return await ListRecordsAsync(vault, projects, PasswordEntryTypes, includeDeleted, cancellationToken);
    }

    private static Task<IReadOnlyList<MdbxNativeEntryRecord>> ListSecureRecordsAsync(
        IMdbxNativeVault vault,
        IReadOnlyList<MdbxNativeProjectRecord> projects,
        bool includeDeleted,
        CancellationToken cancellationToken) =>
        ListSecureRecordsAsync(vault, projects, SecureEntryTypes, includeDeleted, cancellationToken);

    private static async Task<IReadOnlyList<MdbxNativeEntryRecord>> ListSecureRecordsAsync(
        IMdbxNativeVault vault,
        IReadOnlyList<MdbxNativeProjectRecord> projects,
        IReadOnlyList<string> entryTypes,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        return await ListRecordsAsync(vault, projects, entryTypes, includeDeleted, cancellationToken);
    }

    private static async Task<IReadOnlyList<MdbxNativeEntryRecord>> ListRecordsAsync(
        IMdbxNativeVault vault,
        IReadOnlyList<MdbxNativeProjectRecord> projects,
        IReadOnlyList<string> entryTypes,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        var records = new List<MdbxNativeEntryRecord>();
        foreach (var project in projects)
        {
            foreach (var entryType in entryTypes)
            {
                records.AddRange(await vault.ListEntriesAsync(project.ProjectId, entryType, cancellationToken));
                if (includeDeleted)
                {
                    records.AddRange(await vault.ListDeletedEntriesAsync(project.ProjectId, entryType, cancellationToken));
                }
            }
        }

        return records;
    }

    private async Task<string?> ResolvePasswordEntryIdAsync(
        IMdbxNativeVault vault,
        long? passwordId,
        CancellationToken cancellationToken)
    {
        if (passwordId is null or <= 0)
        {
            return null;
        }

        var projects = await EnsureProjectsForReadAsync(vault, cancellationToken);
        return (await ListPasswordRecordsAsync(vault, projects, includeDeleted: true, cancellationToken))
            .Select(record => (Record: record, Payload: DeserializePasswordPayload(record.PayloadJson, record.Title)))
            .FirstOrDefault(item => item.Payload?.Entry.Id == passwordId.Value)
            .Record?.EntryId;
    }

    private async Task<string?> ResolveSecureItemEntryIdAsync(
        IMdbxNativeVault vault,
        long? itemId,
        string entryType,
        CancellationToken cancellationToken)
    {
        if (itemId is null or <= 0)
        {
            return null;
        }

        var projects = await EnsureProjectsForReadAsync(vault, cancellationToken);
        return (await ListSecureRecordsAsync(vault, projects, [entryType], includeDeleted: true, cancellationToken))
            .Select(record => (Record: record, Payload: DeserializeSecureItemPayloadSnapshot(record.PayloadJson, record.Title, record.EntryType)))
            .FirstOrDefault(item => item.Payload?.Item.Id == itemId.Value)
            .Record?.EntryId;
    }

    private static async Task<IReadOnlyDictionary<string, long>> BuildPasswordIdByEntryIdAsync(
        IMdbxNativeVault vault,
        IReadOnlyList<MdbxNativeProjectRecord> projects,
        CancellationToken cancellationToken)
    {
        return (await ListPasswordRecordsAsync(vault, projects, includeDeleted: true, cancellationToken))
            .Select(record => (Record: record, Payload: DeserializePasswordPayload(record.PayloadJson, record.Title)))
            .Where(item => item.Payload?.Entry.Id > 0)
            .GroupBy(item => item.Record.EntryId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Payload!.Entry.Id, StringComparer.OrdinalIgnoreCase);
    }

    private string ToPortableSensitiveValue(string value)
    {
        if (string.IsNullOrEmpty(value) || cryptoService is null)
        {
            return value;
        }

        if (!cryptoService.IsUnlocked)
        {
            throw new InvalidOperationException("Vault must be unlocked before sensitive values can be written to MDBX.");
        }

        try
        {
            return cryptoService.DecryptString(value);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return value;
        }
    }

    private static MdbxNativeProjectRecord? ResolveCategoryProject(Category category, IReadOnlyList<MdbxNativeProjectRecord> projects)
    {
        if (!string.IsNullOrWhiteSpace(category.MdbxFolderId))
        {
            var project = projects.FirstOrDefault(project =>
                string.Equals(project.ProjectId, category.MdbxFolderId, StringComparison.OrdinalIgnoreCase));
            if (project is not null)
            {
                return project;
            }
        }

        if (string.IsNullOrWhiteSpace(category.Name) ||
            string.Equals(category.Name, DefaultProjectTitle, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return projects.FirstOrDefault(project =>
            string.Equals(project.Title, category.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, long> BuildCategoryByProjectId(IReadOnlyList<Category> categories, IReadOnlyList<MdbxNativeProjectRecord> projects)
    {
        var categoryByProjectId = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var projectByTitle = projects.ToDictionary(project => project.Title, project => project.ProjectId, StringComparer.OrdinalIgnoreCase);
        foreach (var category in categories)
        {
            if (!string.IsNullOrWhiteSpace(category.MdbxFolderId))
            {
                categoryByProjectId[category.MdbxFolderId] = category.Id;
            }
            else if (!string.IsNullOrWhiteSpace(category.Name) &&
                     projectByTitle.TryGetValue(category.Name, out var projectId) &&
                     !string.Equals(category.Name, DefaultProjectTitle, StringComparison.OrdinalIgnoreCase))
            {
                categoryByProjectId[projectId] = category.Id;
            }
        }

        return categoryByProjectId;
    }

    private static PasswordEntry ClonePasswordEntryForPayload(PasswordEntry entry)
    {
        var clone = JsonSerializer.Deserialize<PasswordEntry>(JsonSerializer.Serialize(entry, JsonOptions), JsonOptions) ?? new PasswordEntry();
        clone.MdbxDatabaseId = null;
        return clone;
    }

    private static SecureItem CloneSecureItemForPayload(SecureItem item)
    {
        var clone = JsonSerializer.Deserialize<SecureItem>(JsonSerializer.Serialize(item, JsonOptions), JsonOptions) ?? new SecureItem();
        clone.MdbxDatabaseId = null;
        return clone;
    }

    private static T? DeserializePayload<T>(string payloadJson)
    {
        var payload = JsonSerializer.Deserialize<MdbxPayload<T>>(payloadJson, JsonOptions);
        return payload is null ? default : payload.Data;
    }

    private static PasswordPayloadSnapshot? DeserializePasswordPayload(string payloadJson, string recordTitle = "")
    {
        try
        {
            var payload = JsonSerializer.Deserialize<MdbxPayload<MdbxPasswordPayload>>(payloadJson, JsonOptions);
            if (payload?.Data?.Entry is not null)
            {
                return new PasswordPayloadSnapshot(payload.Data.Entry, payload.Data.CustomFields, payload.Data.PasswordHistory, payload.Data.Attachments, BoundNoteEntryId: null);
            }
        }
        catch (JsonException)
        {
        }

        try
        {
            var entry = DeserializePayload<PasswordEntry>(payloadJson);
            if (entry is not null)
            {
                return new PasswordPayloadSnapshot(entry, CustomFields: null, PasswordHistory: null, Attachments: null, BoundNoteEntryId: null);
            }
        }
        catch (JsonException)
        {
        }

        var androidPayload = AndroidMdbxPayloadCodec.DecodePassword(payloadJson, recordTitle);
        return androidPayload is null
            ? null
            : new PasswordPayloadSnapshot(
                androidPayload.Entry,
                androidPayload.CustomFields.ToList(),
                androidPayload.PasswordHistory,
                androidPayload.Attachments,
                androidPayload.BoundNoteEntryId);
    }

    private static SecureItemPayloadSnapshot? DeserializeSecureItemPayloadSnapshot(
        string payloadJson,
        string recordTitle = "",
        string entryType = "")
    {
        try
        {
            var payload = JsonSerializer.Deserialize<MdbxPayload<MdbxSecureItemPayload>>(payloadJson, JsonOptions);
            if (payload?.Data?.Item is not null)
            {
                return new SecureItemPayloadSnapshot(payload.Data.Item, payload.Data.Attachments, BoundPasswordEntryId: null);
            }
        }
        catch (JsonException)
        {
        }

        try
        {
            var item = DeserializePayload<SecureItem>(payloadJson);
            if (item is not null)
            {
                return new SecureItemPayloadSnapshot(item, Attachments: null, BoundPasswordEntryId: null);
            }
        }
        catch (JsonException)
        {
        }

        var androidPayload = AndroidMdbxPayloadCodec.DecodeSecureItem(payloadJson, recordTitle, entryType);
        return androidPayload is null
            ? null
            : new SecureItemPayloadSnapshot(
                androidPayload.Item,
                androidPayload.Attachments,
                androidPayload.BoundPasswordEntryId);
    }

    private static IReadOnlyList<CustomField> NormalizeCustomFields(long entryId, IReadOnlyList<CustomField> customFields) =>
        customFields
            .Where(field => !string.IsNullOrWhiteSpace(field.Title) && !string.IsNullOrWhiteSpace(field.Value))
            .OrderBy(field => field.SortOrder)
            .ThenBy(field => field.Id)
            .Select((field, index) => new CustomField
            {
                Id = field.Id,
                EntryId = entryId,
                Title = field.Title.Trim(),
                Value = field.Value.Trim(),
                IsProtected = field.IsProtected,
                SortOrder = index
            })
            .ToList();

    private static IReadOnlyList<Attachment> NormalizeAttachments(long entryId, IReadOnlyList<Attachment> attachments) =>
        attachments
            .Where(attachment => !string.IsNullOrWhiteSpace(attachment.FileName) && !string.IsNullOrWhiteSpace(attachment.StoragePath))
            .OrderByDescending(attachment => attachment.CreatedAt)
            .ThenByDescending(attachment => attachment.Id)
            .Select(attachment => new Attachment
            {
                Id = attachment.Id,
                OwnerType = "PASSWORD",
                OwnerId = entryId,
                FileName = attachment.FileName.Trim(),
                ContentType = attachment.ContentType.Trim(),
                StoragePath = attachment.StoragePath.Trim(),
                SizeBytes = attachment.SizeBytes,
                CreatedAt = attachment.CreatedAt == default ? DateTimeOffset.UtcNow : attachment.CreatedAt,
                BitwardenVaultId = attachment.BitwardenVaultId,
                KeepassBinaryRef = attachment.KeepassBinaryRef
            })
            .ToList();

    private static IReadOnlyList<Attachment> NormalizeSecureItemAttachments(long itemId, IReadOnlyList<string> imagePaths) =>
        imagePaths
            .Where(path => TryParseAttachmentStoragePath(path) is not null)
            .Select((path, index) => new Attachment
            {
                Id = MdbxStableId.FromString("attachment", path),
                OwnerType = "SECURE_ITEM",
                OwnerId = itemId,
                FileName = $"image-{index + 1}",
                ContentType = "",
                StoragePath = path,
                SizeBytes = 0,
                CreatedAt = DateTimeOffset.UtcNow
            })
            .ToList();

    private static IReadOnlyList<Attachment> NormalizeSecureItemAttachments(long itemId, IReadOnlyList<Attachment> attachments) =>
        attachments
            .Where(attachment => TryParseAttachmentStoragePath(attachment.StoragePath.Trim()) is not null)
            .Select((attachment, index) => new Attachment
            {
                Id = attachment.Id > 0
                    ? attachment.Id
                    : MdbxStableId.FromString("attachment", attachment.StoragePath.Trim()),
                OwnerType = "SECURE_ITEM",
                OwnerId = itemId,
                FileName = string.IsNullOrWhiteSpace(attachment.FileName) ? $"image-{index + 1}" : attachment.FileName.Trim(),
                ContentType = attachment.ContentType.Trim(),
                StoragePath = attachment.StoragePath.Trim(),
                SizeBytes = attachment.SizeBytes,
                CreatedAt = attachment.CreatedAt == default ? DateTimeOffset.UtcNow : attachment.CreatedAt,
                BitwardenVaultId = attachment.BitwardenVaultId,
                KeepassBinaryRef = attachment.KeepassBinaryRef
            })
            .ToList();

    private static IReadOnlyList<Attachment> NormalizeSecureItemNativeAttachments(long itemId, IReadOnlyList<MdbxNativeAttachmentRecord> attachments) =>
        attachments
            .OrderBy(attachment => attachment.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(attachment => attachment.AttachmentId, StringComparer.OrdinalIgnoreCase)
            .Select(attachment => new Attachment
            {
                Id = MdbxStableId.FromString("attachment", ToAttachmentStoragePath(attachment.AttachmentId)),
                OwnerType = "SECURE_ITEM",
                OwnerId = itemId,
                FileName = attachment.FileName.Trim(),
                ContentType = attachment.MediaType?.Trim() ?? "",
                StoragePath = ToAttachmentStoragePath(attachment.AttachmentId),
                SizeBytes = checked((long)Math.Min(attachment.OriginalSize, (ulong)long.MaxValue)),
                CreatedAt = DateTimeOffset.UtcNow
            })
            .ToList();

    private static IReadOnlyList<PasswordHistoryEntry> NormalizePasswordHistory(long entryId, IReadOnlyList<PasswordHistoryEntry> passwordHistory) =>
        passwordHistory
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Password))
            .OrderByDescending(entry => entry.LastUsedAt)
            .ThenByDescending(entry => entry.Id)
            .Select(entry => new PasswordHistoryEntry
            {
                Id = entry.Id,
                EntryId = entryId,
                Password = entry.Password,
                LastUsedAt = entry.LastUsedAt == default ? DateTimeOffset.UtcNow : entry.LastUsedAt
            })
            .ToList();

    private static string ToMdbxEntryType(PasswordEntry entry) =>
        entry.LoginType == PasswordLoginType.SshKey ? "ssh-key" : "login";

    private static string ToMdbxEntryType(VaultItemType itemType) => itemType switch
    {
        VaultItemType.Totp => "totp",
        VaultItemType.BankCard => "card",
        VaultItemType.Document => "document-ref",
        VaultItemType.BillingAddress => "billing-address",
        VaultItemType.PaymentAccount => "payment-account",
        _ => "note"
    };

    private static IReadOnlyList<string> DecodeSecureItemImagePaths(SecureItem item) => item.ItemType switch
    {
        VaultItemType.Document => WalletItemDataCodec.DecodeDocument(item).ImagePaths,
        VaultItemType.BankCard => WalletItemDataCodec.DecodeBankCard(item).ImagePaths,
        VaultItemType.BillingAddress => WalletItemDataCodec.DecodeBillingAddress(item).ImagePaths,
        VaultItemType.PaymentAccount => WalletItemDataCodec.DecodePaymentAccount(item).ImagePaths,
        VaultItemType.Note => NoteContentCodec.DecodeImagePaths(item.ImagePaths),
        _ => WalletItemDataCodec.DecodeImagePaths(item.ImagePaths)
    };

    public static string ToAttachmentStoragePath(string attachmentId) => $"mdbx:{attachmentId}";

    public static string? TryParseAttachmentStoragePath(string storagePath)
    {
        if (storagePath.StartsWith("mdbx:", StringComparison.OrdinalIgnoreCase))
        {
            var id = storagePath["mdbx:".Length..].Trim();
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }

        return null;
    }

    private sealed record MdbxPayload<T>(string Kind, int SchemaVersion, T Data);

    private sealed class MdbxPasswordPayload
    {
        public PasswordEntry? Entry { get; init; }
        public List<CustomField>? CustomFields { get; init; }
        public List<PasswordHistoryEntry>? PasswordHistory { get; init; }
        public List<Attachment>? Attachments { get; init; }
    }

    private sealed class MdbxSecureItemPayload
    {
        public SecureItem? Item { get; init; }
        public List<Attachment>? Attachments { get; init; }
    }

    private sealed record PasswordPayloadSnapshot(
        PasswordEntry Entry,
        List<CustomField>? CustomFields,
        List<PasswordHistoryEntry>? PasswordHistory,
        List<Attachment>? Attachments,
        string? BoundNoteEntryId);

    private sealed record SecureItemPayloadSnapshot(
        SecureItem Item,
        List<Attachment>? Attachments,
        string? BoundPasswordEntryId);
}
