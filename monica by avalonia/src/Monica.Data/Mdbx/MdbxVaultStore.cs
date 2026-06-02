using System.Text.Json;
using System.Text.Json.Serialization;
using Monica.Core.Models;

namespace Monica.Data.Mdbx;

public interface IMdbxVaultStore
{
    bool IsAvailable { get; }
    Task<PasswordEntry> SavePasswordAsync(LocalMdbxDatabase database, PasswordEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PasswordEntry>> GetPasswordsAsync(LocalMdbxDatabase database, bool includeDeleted = false, bool includeArchived = false, CancellationToken cancellationToken = default);
    Task SoftDeletePasswordAsync(LocalMdbxDatabase database, PasswordEntry entry, CancellationToken cancellationToken = default);
    Task RestorePasswordAsync(LocalMdbxDatabase database, PasswordEntry entry, CancellationToken cancellationToken = default);
    Task<SecureItem> SaveSecureItemAsync(LocalMdbxDatabase database, SecureItem item, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SecureItem>> GetSecureItemsAsync(LocalMdbxDatabase database, VaultItemType? itemType = null, bool includeDeleted = false, CancellationToken cancellationToken = default);
    Task SoftDeleteSecureItemAsync(LocalMdbxDatabase database, SecureItem item, CancellationToken cancellationToken = default);
    Task RestoreSecureItemAsync(LocalMdbxDatabase database, SecureItem item, CancellationToken cancellationToken = default);
}

public sealed class MdbxVaultStore(IMdbxNativeBridge nativeBridge) : IMdbxVaultStore
{
    private const string DefaultProjectTitle = "Monica";
    private const string DeviceId = "monica-avalonia";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        IgnoreReadOnlyProperties = true
    };

    public bool IsAvailable => nativeBridge.IsAvailable;

    public async Task<PasswordEntry> SavePasswordAsync(LocalMdbxDatabase database, PasswordEntry entry, CancellationToken cancellationToken = default)
    {
        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var project = await EnsureDefaultProjectAsync(vault, cancellationToken);
        var entryType = ToMdbxEntryType(entry);
        var payload = SerializePayload("password", entry);
        var record = string.IsNullOrWhiteSpace(entry.MdbxFolderId)
            ? await vault.CreateEntryAsync(project.ProjectId, entryType, entry.Title, payload, cancellationToken)
            : await vault.UpdateEntryAsync(project.ProjectId, entry.MdbxFolderId!, entryType, entry.Title, payload, cancellationToken);

        entry.MdbxDatabaseId = database.Id;
        entry.MdbxFolderId = record.EntryId;
        return entry;
    }

    public async Task<IReadOnlyList<PasswordEntry>> GetPasswordsAsync(LocalMdbxDatabase database, bool includeDeleted = false, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var project = await EnsureDefaultProjectAsync(vault, cancellationToken);
        var records = new List<MdbxNativeEntryRecord>();
        foreach (var entryType in new[] { "login", "ssh-key" })
        {
            records.AddRange(await vault.ListEntriesAsync(project.ProjectId, entryType, cancellationToken));
            if (includeDeleted)
            {
                records.AddRange(await vault.ListDeletedEntriesAsync(project.ProjectId, entryType, cancellationToken));
            }
        }

        return records
            .Select(record => (Record: record, Entry: DeserializePayload<PasswordEntry>(record.PayloadJson)))
            .Where(item => item.Entry is not null)
            .Select(item =>
            {
                var record = item.Record;
                var entry = item.Entry!;
                entry.MdbxDatabaseId = database.Id;
                entry.MdbxFolderId = record.EntryId;
                entry.IsDeleted = record.Deleted;
                return entry;
            })
            .Where(entry => includeDeleted || !entry.IsDeleted)
            .Where(entry => includeArchived || !entry.IsArchived)
            .OrderByDescending(entry => entry.IsFavorite)
            .ThenBy(entry => entry.SortOrder)
            .ThenByDescending(entry => entry.UpdatedAt)
            .ToList();
    }

    public async Task SoftDeletePasswordAsync(LocalMdbxDatabase database, PasswordEntry entry, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entry.MdbxFolderId))
        {
            return;
        }

        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var project = await EnsureDefaultProjectAsync(vault, cancellationToken);
        await vault.DeleteEntryAsync(project.ProjectId, entry.MdbxFolderId!, cancellationToken);
    }

    public async Task RestorePasswordAsync(LocalMdbxDatabase database, PasswordEntry entry, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entry.MdbxFolderId))
        {
            return;
        }

        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var project = await EnsureDefaultProjectAsync(vault, cancellationToken);
        await vault.RestoreEntryAsync(project.ProjectId, entry.MdbxFolderId!, cancellationToken);
    }

    public async Task<SecureItem> SaveSecureItemAsync(LocalMdbxDatabase database, SecureItem item, CancellationToken cancellationToken = default)
    {
        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var project = await EnsureDefaultProjectAsync(vault, cancellationToken);
        var entryType = ToMdbxEntryType(item.ItemType);
        var payload = SerializePayload("secure-item", item);
        var record = string.IsNullOrWhiteSpace(item.MdbxFolderId)
            ? await vault.CreateEntryAsync(project.ProjectId, entryType, item.Title, payload, cancellationToken)
            : await vault.UpdateEntryAsync(project.ProjectId, item.MdbxFolderId!, entryType, item.Title, payload, cancellationToken);

        item.MdbxDatabaseId = database.Id;
        item.MdbxFolderId = record.EntryId;
        return item;
    }

    public async Task<IReadOnlyList<SecureItem>> GetSecureItemsAsync(LocalMdbxDatabase database, VaultItemType? itemType = null, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var project = await EnsureDefaultProjectAsync(vault, cancellationToken);
        var entryTypes = itemType is null
            ? new[] { "note", "totp", "card", "document-ref" }
            : [ToMdbxEntryType(itemType.Value)];
        var records = new List<MdbxNativeEntryRecord>();
        foreach (var entryType in entryTypes)
        {
            records.AddRange(await vault.ListEntriesAsync(project.ProjectId, entryType, cancellationToken));
            if (includeDeleted)
            {
                records.AddRange(await vault.ListDeletedEntriesAsync(project.ProjectId, entryType, cancellationToken));
            }
        }

        return records
            .Select(record => (Record: record, Item: DeserializePayload<SecureItem>(record.PayloadJson)))
            .Where(item => item.Item is not null)
            .Select(item =>
            {
                var record = item.Record;
                var secureItem = item.Item!;
                secureItem.MdbxDatabaseId = database.Id;
                secureItem.MdbxFolderId = record.EntryId;
                secureItem.IsDeleted = record.Deleted;
                return secureItem;
            })
            .Where(item => includeDeleted || !item.IsDeleted)
            .OrderByDescending(item => item.IsFavorite)
            .ThenBy(item => item.SortOrder)
            .ThenByDescending(item => item.UpdatedAt)
            .ToList();
    }

    public async Task SoftDeleteSecureItemAsync(LocalMdbxDatabase database, SecureItem item, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(item.MdbxFolderId))
        {
            return;
        }

        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var project = await EnsureDefaultProjectAsync(vault, cancellationToken);
        await vault.DeleteEntryAsync(project.ProjectId, item.MdbxFolderId!, cancellationToken);
    }

    public async Task RestoreSecureItemAsync(LocalMdbxDatabase database, SecureItem item, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(item.MdbxFolderId))
        {
            return;
        }

        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var project = await EnsureDefaultProjectAsync(vault, cancellationToken);
        await vault.RestoreEntryAsync(project.ProjectId, item.MdbxFolderId!, cancellationToken);
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
        var projects = await vault.ListProjectsAsync(false, cancellationToken);
        return projects.FirstOrDefault(project => string.Equals(project.Title, DefaultProjectTitle, StringComparison.OrdinalIgnoreCase))
            ?? await vault.CreateProjectAsync(DefaultProjectTitle, cancellationToken);
    }

    private static string SerializePayload<T>(string kind, T data) =>
        JsonSerializer.Serialize(new MdbxPayload<T>(kind, 1, data), JsonOptions);

    private static T? DeserializePayload<T>(string payloadJson)
    {
        var payload = JsonSerializer.Deserialize<MdbxPayload<T>>(payloadJson, JsonOptions);
        return payload is null ? default : payload.Data;
    }

    private static string ToMdbxEntryType(PasswordEntry entry) =>
        entry.LoginType == PasswordLoginType.SshKey ? "ssh-key" : "login";

    private static string ToMdbxEntryType(VaultItemType itemType) => itemType switch
    {
        VaultItemType.Totp => "totp",
        VaultItemType.BankCard => "card",
        VaultItemType.Document => "document-ref",
        _ => "note"
    };

    private sealed record MdbxPayload<T>(string Kind, int SchemaVersion, T Data);
}
