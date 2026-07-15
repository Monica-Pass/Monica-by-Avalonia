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
        clone.Password = ReadPasswordSecretOrThrow(source.Password);
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
        clone.Password = ProtectPassword(ReadPasswordSecretOrThrow(source.Password));
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
    private static PasswordEntry ClonePassword(PasswordEntry source) => source.CreateDetachedCopy();

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
            Password = ReadPasswordSecretOrThrow(source.Password),
            LastUsedAt = source.LastUsedAt
        };
    }

    private PasswordHistoryEntry ClonePasswordHistoryForImport(PasswordHistoryEntry source, long importedPasswordId)
    {
        return new PasswordHistoryEntry
        {
            Id = 0,
            EntryId = importedPasswordId,
            Password = ProtectPassword(ReadPasswordSecretOrThrow(source.Password)),
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
