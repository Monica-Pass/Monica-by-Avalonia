using System.Security.Cryptography;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.Data;

public interface IVaultDataProtector
{
    PasswordEntry Protect(PasswordEntry entry);
    PasswordEntry Unprotect(PasswordEntry entry);
    CustomField Protect(CustomField field);
    CustomField Unprotect(CustomField field);
    PasswordHistoryEntry Protect(PasswordHistoryEntry entry);
    PasswordHistoryEntry Unprotect(PasswordHistoryEntry entry);
    SecureItem Protect(SecureItem item);
    SecureItem Unprotect(SecureItem item);
    LocalMdbxDatabase Protect(LocalMdbxDatabase database);
    LocalMdbxDatabase Unprotect(LocalMdbxDatabase database);
    OperationLog Protect(OperationLog log);
    OperationLog Unprotect(OperationLog log);
}

public sealed class NoopVaultDataProtector : IVaultDataProtector
{
    public static NoopVaultDataProtector Instance { get; } = new();

    private NoopVaultDataProtector()
    {
    }

    public PasswordEntry Protect(PasswordEntry entry) => entry;
    public PasswordEntry Unprotect(PasswordEntry entry) => entry;
    public CustomField Protect(CustomField field) => field;
    public CustomField Unprotect(CustomField field) => field;
    public PasswordHistoryEntry Protect(PasswordHistoryEntry entry) => entry;
    public PasswordHistoryEntry Unprotect(PasswordHistoryEntry entry) => entry;
    public SecureItem Protect(SecureItem item) => item;
    public SecureItem Unprotect(SecureItem item) => item;
    public LocalMdbxDatabase Protect(LocalMdbxDatabase database) => database;
    public LocalMdbxDatabase Unprotect(LocalMdbxDatabase database) => database;
    public OperationLog Protect(OperationLog log) => log;
    public OperationLog Unprotect(OperationLog log) => log;
}

public sealed class VaultDataProtector(ICryptoService cryptoService) : IVaultDataProtector
{
    private const string ProtectedPrefix = "vault:v1:";

    public PasswordEntry Protect(PasswordEntry entry)
    {
        var protectedEntry = Clone(entry);
        protectedEntry.Title = ProtectText(entry.Title);
        protectedEntry.Website = ProtectText(entry.Website);
        protectedEntry.Username = ProtectText(entry.Username);
        protectedEntry.Password = ProtectText(entry.Password, allowLegacyCipher: true);
        protectedEntry.Notes = ProtectText(entry.Notes);
        protectedEntry.AppPackageName = ProtectText(entry.AppPackageName);
        protectedEntry.AppName = ProtectText(entry.AppName);
        protectedEntry.Email = ProtectText(entry.Email);
        protectedEntry.Phone = ProtectText(entry.Phone);
        protectedEntry.AddressLine = ProtectText(entry.AddressLine);
        protectedEntry.City = ProtectText(entry.City);
        protectedEntry.State = ProtectText(entry.State);
        protectedEntry.ZipCode = ProtectText(entry.ZipCode);
        protectedEntry.Country = ProtectText(entry.Country);
        protectedEntry.CreditCardNumber = ProtectText(entry.CreditCardNumber);
        protectedEntry.CreditCardHolder = ProtectText(entry.CreditCardHolder);
        protectedEntry.CreditCardExpiry = ProtectText(entry.CreditCardExpiry);
        protectedEntry.CreditCardCvv = ProtectText(entry.CreditCardCvv);
        protectedEntry.AuthenticatorKey = ProtectText(entry.AuthenticatorKey);
        protectedEntry.PasskeyBindings = ProtectText(entry.PasskeyBindings);
        protectedEntry.SshKeyData = ProtectText(entry.SshKeyData);
        protectedEntry.SsoProvider = ProtectText(entry.SsoProvider);
        protectedEntry.WifiMetadata = ProtectText(entry.WifiMetadata);
        protectedEntry.CustomIconValue = ProtectNullableText(entry.CustomIconValue);
        return protectedEntry;
    }

    public PasswordEntry Unprotect(PasswordEntry entry)
    {
        var unprotectedEntry = Clone(entry);
        unprotectedEntry.Title = UnprotectText(entry.Title);
        unprotectedEntry.Website = UnprotectText(entry.Website);
        unprotectedEntry.Username = UnprotectText(entry.Username);
        unprotectedEntry.Password = UnprotectText(entry.Password, allowLegacyCipher: true);
        unprotectedEntry.Notes = UnprotectText(entry.Notes);
        unprotectedEntry.AppPackageName = UnprotectText(entry.AppPackageName);
        unprotectedEntry.AppName = UnprotectText(entry.AppName);
        unprotectedEntry.Email = UnprotectText(entry.Email);
        unprotectedEntry.Phone = UnprotectText(entry.Phone);
        unprotectedEntry.AddressLine = UnprotectText(entry.AddressLine);
        unprotectedEntry.City = UnprotectText(entry.City);
        unprotectedEntry.State = UnprotectText(entry.State);
        unprotectedEntry.ZipCode = UnprotectText(entry.ZipCode);
        unprotectedEntry.Country = UnprotectText(entry.Country);
        unprotectedEntry.CreditCardNumber = UnprotectText(entry.CreditCardNumber);
        unprotectedEntry.CreditCardHolder = UnprotectText(entry.CreditCardHolder);
        unprotectedEntry.CreditCardExpiry = UnprotectText(entry.CreditCardExpiry);
        unprotectedEntry.CreditCardCvv = UnprotectText(entry.CreditCardCvv);
        unprotectedEntry.AuthenticatorKey = UnprotectText(entry.AuthenticatorKey);
        unprotectedEntry.PasskeyBindings = UnprotectText(entry.PasskeyBindings);
        unprotectedEntry.SshKeyData = UnprotectText(entry.SshKeyData);
        unprotectedEntry.SsoProvider = UnprotectText(entry.SsoProvider);
        unprotectedEntry.WifiMetadata = UnprotectText(entry.WifiMetadata);
        unprotectedEntry.CustomIconValue = UnprotectNullableText(entry.CustomIconValue);
        return unprotectedEntry;
    }

    public CustomField Protect(CustomField field) => new()
    {
        Id = field.Id,
        EntryId = field.EntryId,
        Title = ProtectText(field.Title),
        Value = ProtectText(field.Value, allowLegacyCipher: field.IsProtected),
        IsProtected = field.IsProtected,
        SortOrder = field.SortOrder
    };

    public CustomField Unprotect(CustomField field) => new()
    {
        Id = field.Id,
        EntryId = field.EntryId,
        Title = UnprotectText(field.Title),
        Value = UnprotectText(field.Value, allowLegacyCipher: field.IsProtected),
        IsProtected = field.IsProtected,
        SortOrder = field.SortOrder
    };

    public PasswordHistoryEntry Protect(PasswordHistoryEntry entry) => new()
    {
        Id = entry.Id,
        EntryId = entry.EntryId,
        Password = ProtectText(entry.Password, allowLegacyCipher: true),
        LastUsedAt = entry.LastUsedAt
    };

    public PasswordHistoryEntry Unprotect(PasswordHistoryEntry entry) => new()
    {
        Id = entry.Id,
        EntryId = entry.EntryId,
        Password = UnprotectText(entry.Password, allowLegacyCipher: true),
        LastUsedAt = entry.LastUsedAt
    };

    public SecureItem Protect(SecureItem item)
    {
        var protectedItem = Clone(item);
        protectedItem.Title = ProtectText(item.Title);
        protectedItem.Notes = ProtectText(item.Notes);
        protectedItem.ItemData = ProtectText(item.ItemData);
        protectedItem.ImagePaths = ProtectText(item.ImagePaths);
        return protectedItem;
    }

    public SecureItem Unprotect(SecureItem item)
    {
        var unprotectedItem = Clone(item);
        unprotectedItem.Title = UnprotectText(item.Title);
        unprotectedItem.Notes = UnprotectText(item.Notes);
        unprotectedItem.ItemData = UnprotectText(item.ItemData);
        unprotectedItem.ImagePaths = UnprotectText(item.ImagePaths);
        return unprotectedItem;
    }

    public LocalMdbxDatabase Protect(LocalMdbxDatabase database)
    {
        var protectedDatabase = Clone(database);
        protectedDatabase.EncryptedPassword = ProtectNullableText(database.EncryptedPassword, allowLegacyCipher: true);
        protectedDatabase.KeyFileUri = ProtectNullableText(database.KeyFileUri);
        return protectedDatabase;
    }

    public LocalMdbxDatabase Unprotect(LocalMdbxDatabase database)
    {
        var unprotectedDatabase = Clone(database);
        unprotectedDatabase.EncryptedPassword = UnprotectNullableText(database.EncryptedPassword, allowLegacyCipher: true);
        unprotectedDatabase.KeyFileUri = UnprotectNullableText(database.KeyFileUri);
        return unprotectedDatabase;
    }

    public OperationLog Protect(OperationLog log) => new()
    {
        Id = log.Id,
        ItemType = log.ItemType,
        ItemId = log.ItemId,
        ItemTitle = ProtectText(log.ItemTitle),
        OperationType = log.OperationType,
        ChangesJson = ProtectText(log.ChangesJson),
        DeviceId = log.DeviceId,
        DeviceName = log.DeviceName,
        Timestamp = log.Timestamp,
        IsReverted = log.IsReverted
    };

    public OperationLog Unprotect(OperationLog log) => new()
    {
        Id = log.Id,
        ItemType = log.ItemType,
        ItemId = log.ItemId,
        ItemTitle = UnprotectText(log.ItemTitle),
        OperationType = log.OperationType,
        ChangesJson = UnprotectText(log.ChangesJson),
        DeviceId = log.DeviceId,
        DeviceName = log.DeviceName,
        Timestamp = log.Timestamp,
        IsReverted = log.IsReverted
    };

    private string ProtectText(string value, bool allowLegacyCipher = false)
    {
        if (string.IsNullOrEmpty(value) || IsProtected(value))
        {
            return value;
        }

        if (allowLegacyCipher && LooksLikeLegacyCipher(value))
        {
            return value;
        }

        EnsureUnlocked();
        return ProtectedPrefix + cryptoService.EncryptString(value);
    }

    private string UnprotectText(string value, bool allowLegacyCipher = false)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (IsProtected(value))
        {
            EnsureUnlocked();
            return cryptoService.DecryptString(value[ProtectedPrefix.Length..]);
        }

        return allowLegacyCipher ? TryUnprotectLegacyCipher(value) : value;
    }

    private string? ProtectNullableText(string? value, bool allowLegacyCipher = false) =>
        value is null ? null : ProtectText(value, allowLegacyCipher);

    private string? UnprotectNullableText(string? value, bool allowLegacyCipher = false) =>
        value is null ? null : UnprotectText(value, allowLegacyCipher);

    private bool LooksLikeLegacyCipher(string value)
    {
        if (!cryptoService.IsUnlocked)
        {
            return false;
        }

        try
        {
            _ = cryptoService.DecryptString(value);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    private string TryUnprotectLegacyCipher(string value)
    {
        if (!cryptoService.IsUnlocked)
        {
            return value;
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

    private static bool IsProtected(string value) => value.StartsWith(ProtectedPrefix, StringComparison.Ordinal);

    private void EnsureUnlocked()
    {
        if (!cryptoService.IsUnlocked)
        {
            throw new InvalidOperationException("Vault must be unlocked before protected data can be persisted or read.");
        }
    }

    private static PasswordEntry Clone(PasswordEntry source) => new()
    {
        Id = source.Id,
        Title = source.Title,
        Website = source.Website,
        Username = source.Username,
        Password = source.Password,
        Notes = source.Notes,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt,
        IsFavorite = source.IsFavorite,
        SortOrder = source.SortOrder,
        IsGroupCover = source.IsGroupCover,
        AppPackageName = source.AppPackageName,
        AppName = source.AppName,
        Email = source.Email,
        Phone = source.Phone,
        AddressLine = source.AddressLine,
        City = source.City,
        State = source.State,
        ZipCode = source.ZipCode,
        Country = source.Country,
        CreditCardNumber = source.CreditCardNumber,
        CreditCardHolder = source.CreditCardHolder,
        CreditCardExpiry = source.CreditCardExpiry,
        CreditCardCvv = source.CreditCardCvv,
        CategoryId = source.CategoryId,
        BoundNoteId = source.BoundNoteId,
        KeepassDatabaseId = source.KeepassDatabaseId,
        KeepassGroupPath = source.KeepassGroupPath,
        KeepassEntryUuid = source.KeepassEntryUuid,
        KeepassGroupUuid = source.KeepassGroupUuid,
        MdbxDatabaseId = source.MdbxDatabaseId,
        MdbxFolderId = source.MdbxFolderId,
        AuthenticatorKey = source.AuthenticatorKey,
        PasskeyBindings = source.PasskeyBindings,
        SshKeyData = source.SshKeyData,
        LoginType = source.LoginType,
        SsoProvider = source.SsoProvider,
        SsoRefEntryId = source.SsoRefEntryId,
        WifiMetadata = source.WifiMetadata,
        CustomIconType = source.CustomIconType,
        CustomIconValue = source.CustomIconValue,
        CustomIconUpdatedAt = source.CustomIconUpdatedAt,
        IsDeleted = source.IsDeleted,
        DeletedAt = source.DeletedAt,
        IsArchived = source.IsArchived,
        ArchivedAt = source.ArchivedAt,
        ReplicaGroupId = source.ReplicaGroupId,
        BitwardenVaultId = source.BitwardenVaultId,
        BitwardenCipherId = source.BitwardenCipherId,
        BitwardenFolderId = source.BitwardenFolderId,
        BitwardenRevisionDate = source.BitwardenRevisionDate,
        BitwardenCipherType = source.BitwardenCipherType,
        BitwardenLocalModified = source.BitwardenLocalModified
    };

    private static SecureItem Clone(SecureItem source) => new()
    {
        Id = source.Id,
        ItemType = source.ItemType,
        Title = source.Title,
        Notes = source.Notes,
        IsFavorite = source.IsFavorite,
        SortOrder = source.SortOrder,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt,
        ItemData = source.ItemData,
        ImagePaths = source.ImagePaths,
        BoundPasswordId = source.BoundPasswordId,
        CategoryId = source.CategoryId,
        KeepassDatabaseId = source.KeepassDatabaseId,
        KeepassGroupPath = source.KeepassGroupPath,
        KeepassEntryUuid = source.KeepassEntryUuid,
        KeepassGroupUuid = source.KeepassGroupUuid,
        MdbxDatabaseId = source.MdbxDatabaseId,
        MdbxFolderId = source.MdbxFolderId,
        IsDeleted = source.IsDeleted,
        DeletedAt = source.DeletedAt,
        ReplicaGroupId = source.ReplicaGroupId,
        BitwardenVaultId = source.BitwardenVaultId,
        BitwardenCipherId = source.BitwardenCipherId,
        BitwardenFolderId = source.BitwardenFolderId,
        BitwardenRevisionDate = source.BitwardenRevisionDate,
        BitwardenLocalModified = source.BitwardenLocalModified,
        SyncStatus = source.SyncStatus
    };

    private static LocalMdbxDatabase Clone(LocalMdbxDatabase source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        FilePath = source.FilePath,
        StorageLocation = source.StorageLocation,
        SourceType = source.SourceType,
        SourceId = source.SourceId,
        TigaMode = source.TigaMode,
        EncryptedPassword = source.EncryptedPassword,
        UnlockMethod = source.UnlockMethod,
        KdfProfile = source.KdfProfile,
        KeyFileName = source.KeyFileName,
        KeyFileUri = source.KeyFileUri,
        KeyFileFingerprint = source.KeyFileFingerprint,
        Description = source.Description,
        CreatedAt = source.CreatedAt,
        LastAccessedAt = source.LastAccessedAt,
        LastSyncedAt = source.LastSyncedAt,
        IsDefault = source.IsDefault,
        ProjectCount = source.ProjectCount,
        SortOrder = source.SortOrder,
        WorkingCopyPath = source.WorkingCopyPath,
        CacheCopyPath = source.CacheCopyPath,
        IsOfflineAvailable = source.IsOfflineAvailable,
        LastSyncStatus = source.LastSyncStatus,
        LastSyncError = source.LastSyncError
    };
}
