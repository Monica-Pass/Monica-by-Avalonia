using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Monica.App;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private PasswordEntry ClonePasswordForExport(PasswordEntry source, bool includeCategory = true)
    {
        var clone = ClonePassword(source);
        clone.Password = UnprotectPassword(source.Password);
        if (!includeCategory)
        {
            clone.CategoryId = null;
        }

        clone.MdbxDatabaseId = null;
        clone.MdbxFolderId = null;
        return clone;
    }

    private PasswordEntry ClonePasswordForImport(PasswordEntry source, IReadOnlyDictionary<long, long>? categoryIdMap = null)
    {
        var clone = ClonePassword(source);
        clone.Id = 0;
        clone.Password = ProtectPassword(UnprotectPassword(source.Password));
        clone.MdbxDatabaseId = null;
        clone.MdbxFolderId = null;
        if (clone.CategoryId is { } categoryId)
        {
            clone.CategoryId = categoryIdMap?.TryGetValue(categoryId, out var importedCategoryId) == true
                ? importedCategoryId
                : null;
        }

        clone.IsDeleted = false;
        clone.DeletedAt = null;
        clone.IsArchived = false;
        clone.ArchivedAt = null;
        clone.BitwardenLocalModified = true;
        return clone;
    }
    private static PasswordEntry ClonePassword(PasswordEntry source)
    {
        return new PasswordEntry
        {
            Id = source.Id,
            Title = source.Title,
            Website = source.Website,
            Username = source.Username,
            Password = source.Password,
            Notes = source.Notes,
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
            BitwardenLocalModified = source.BitwardenLocalModified,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt
        };
    }

    private static CustomField CloneCustomFieldForImport(CustomField source, long importedPasswordId)
    {
        return new CustomField
        {
            Id = 0,
            EntryId = importedPasswordId,
            Title = source.Title,
            Value = source.Value,
            IsProtected = source.IsProtected,
            SortOrder = source.SortOrder
        };
    }

    private PasswordHistoryEntry ClonePasswordHistoryForExport(PasswordHistoryEntry source)
    {
        return new PasswordHistoryEntry
        {
            Id = source.Id,
            EntryId = source.EntryId,
            Password = UnprotectPassword(source.Password),
            LastUsedAt = source.LastUsedAt
        };
    }

    private PasswordHistoryEntry ClonePasswordHistoryForImport(PasswordHistoryEntry source, long importedPasswordId)
    {
        return new PasswordHistoryEntry
        {
            Id = 0,
            EntryId = importedPasswordId,
            Password = ProtectPassword(UnprotectPassword(source.Password)),
            LastUsedAt = source.LastUsedAt
        };
    }

    private static Attachment CloneAttachmentForExport(Attachment source)
    {
        return new Attachment
        {
            Id = source.Id,
            OwnerType = source.OwnerType,
            OwnerId = source.OwnerId,
            FileName = source.FileName,
            ContentType = source.ContentType,
            StoragePath = source.StoragePath,
            SizeBytes = source.SizeBytes,
            CreatedAt = source.CreatedAt,
            BitwardenVaultId = source.BitwardenVaultId,
            KeepassBinaryRef = source.KeepassBinaryRef
        };
    }

    private static Attachment CloneAttachmentForImport(Attachment source, long importedPasswordId)
    {
        return new Attachment
        {
            Id = 0,
            OwnerType = "PASSWORD",
            OwnerId = importedPasswordId,
            FileName = source.FileName,
            ContentType = source.ContentType,
            StoragePath = "",
            SizeBytes = source.SizeBytes,
            CreatedAt = source.CreatedAt == default ? DateTimeOffset.UtcNow : source.CreatedAt,
            BitwardenVaultId = source.BitwardenVaultId,
            KeepassBinaryRef = source.KeepassBinaryRef
        };
    }

}
