using Monica.Core.ImportExport;
using Monica.Core.Models;

namespace Monica.App.Services.VaultOperations;

public static class ImportExportHelpers
{
    public static bool TryDecodeAttachmentContent(string contentBase64, out byte[] content)
    {
        try
        {
            content = Convert.FromBase64String(contentBase64);
            return true;
        }
        catch (FormatException)
        {
            content = [];
            return false;
        }
    }

    public static SecureItem CloneSecureItemForExport(SecureItem source, bool includeCategory = true, bool includeImages = true)
    {
        var clone = CloneSecureItem(source);
        if (!includeCategory)
        {
            clone.CategoryId = null;
        }

        if (!includeImages)
        {
            StripSecureItemImages(clone);
        }

        clone.MdbxDatabaseId = null;
        clone.MdbxFolderId = null;
        return clone;
    }

    public static SecureItem CloneSecureItemForImport(
        SecureItem source,
        IReadOnlyDictionary<long, long> passwordIdMap,
        IReadOnlyDictionary<long, long>? categoryIdMap = null)
    {
        var clone = CloneSecureItem(source);
        clone.Id = 0;
        clone.MdbxDatabaseId = null;
        clone.MdbxFolderId = null;
        if (clone.BoundPasswordId is { } boundPasswordId)
        {
            clone.BoundPasswordId = passwordIdMap.TryGetValue(boundPasswordId, out var importedPasswordId)
                ? importedPasswordId
                : null;
        }

        if (clone.CategoryId is { } categoryId)
        {
            clone.CategoryId = categoryIdMap?.TryGetValue(categoryId, out var importedCategoryId) == true
                ? importedCategoryId
                : null;
        }

        clone.IsDeleted = false;
        clone.DeletedAt = null;
        clone.BitwardenLocalModified = true;
        clone.SyncStatus = clone.BitwardenVaultId is null ? SyncStatus.None : SyncStatus.Pending;
        return clone;
    }

    public static SecureItem CloneSecureItem(SecureItem source) => source.CreateDetachedCopy();

    public static Category CloneCategory(Category source)
    {
        return new Category
        {
            Id = source.Id,
            Name = source.Name,
            SortOrder = source.SortOrder
        };
    }

    public static void StripSecureItemImages(SecureItem item)
    {
        item.ImagePaths = "[]";
        if (item.ItemType == VaultItemType.Note)
        {
            var note = NoteContentCodec.DecodeFromItem(item);
            item.ItemData = NoteContentCodec.BuildSavePayload(
                item.Title,
                note.Content,
                string.Join(",", note.Tags),
                note.IsMarkdown,
                []).ItemData;
            return;
        }

        if (item.ItemType == VaultItemType.Document)
        {
            var data = WalletItemDataCodec.DecodeDocument(item);
            data.ImagePaths.Clear();
            item.ItemData = WalletItemDataCodec.EncodeDocument(data);
            return;
        }

        if (item.ItemType == VaultItemType.BankCard)
        {
            var data = WalletItemDataCodec.DecodeBankCard(item);
            data.ImagePaths.Clear();
            item.ItemData = WalletItemDataCodec.EncodeBankCard(data);
            return;
        }

        if (item.ItemType == VaultItemType.BillingAddress)
        {
            var data = WalletItemDataCodec.DecodeBillingAddress(item);
            data.ImagePaths.Clear();
            item.ItemData = WalletItemDataCodec.EncodeBillingAddress(data);
            return;
        }

        if (item.ItemType == VaultItemType.PaymentAccount)
        {
            var data = WalletItemDataCodec.DecodePaymentAccount(item);
            data.ImagePaths.Clear();
            item.ItemData = WalletItemDataCodec.EncodePaymentAccount(data);
        }
    }

    public static IReadOnlyList<string> DecodeSecureItemImagePaths(SecureItem item) => item.ItemType switch
    {
        VaultItemType.Document => WalletItemDataCodec.DecodeDocument(item).ImagePaths,
        VaultItemType.BankCard => WalletItemDataCodec.DecodeBankCard(item).ImagePaths,
        VaultItemType.BillingAddress => WalletItemDataCodec.DecodeBillingAddress(item).ImagePaths,
        VaultItemType.PaymentAccount => WalletItemDataCodec.DecodePaymentAccount(item).ImagePaths,
        VaultItemType.Note => NoteContentCodec.DecodeImagePaths(item.ImagePaths),
        _ => WalletItemDataCodec.DecodeImagePaths(item.ImagePaths)
    };

    public static Attachment CreateSecureItemImageAttachmentForExport(SecureItem item, string imagePath, int index)
    {
        return new Attachment
        {
            Id = 0,
            OwnerType = "SECURE_ITEM",
            OwnerId = item.Id,
            FileName = ResolveSecureItemImageFileName(item, imagePath, index),
            ContentType = InferAttachmentContentType(imagePath),
            StoragePath = imagePath,
            SizeBytes = 0,
            CreatedAt = item.UpdatedAt == default ? DateTimeOffset.UtcNow : item.UpdatedAt
        };
    }

    public static string ResolveSecureItemImageFileName(SecureItem item, string imagePath, int index)
    {
        var fileName = Path.GetFileName(imagePath.Replace('\\', Path.DirectorySeparatorChar));
        if (!string.IsNullOrWhiteSpace(fileName) && !imagePath.StartsWith("mdbx:", StringComparison.OrdinalIgnoreCase))
        {
            return fileName;
        }

        var prefix = item.ItemType switch
        {
            VaultItemType.BankCard => "card-image",
            VaultItemType.Document => "document-image",
            VaultItemType.BillingAddress => "address-image",
            VaultItemType.PaymentAccount => "payment-image",
            VaultItemType.Note => "note-image",
            _ => "secure-item-image"
        };
        return $"{prefix}-{index + 1}";
    }

    public static string InferAttachmentContentType(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
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

    public static void ApplySecureItemImagePaths(SecureItem item, IReadOnlyList<string> imagePaths)
    {
        item.ImagePaths = WalletItemDataCodec.EncodeImagePaths(imagePaths);
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
            return;
        }

        if (item.ItemType == VaultItemType.BillingAddress)
        {
            var data = WalletItemDataCodec.DecodeBillingAddress(item);
            data.ImagePaths = imagePaths.ToList();
            item.ItemData = WalletItemDataCodec.EncodeBillingAddress(data);
            return;
        }

        if (item.ItemType == VaultItemType.PaymentAccount)
        {
            var data = WalletItemDataCodec.DecodePaymentAccount(item);
            data.ImagePaths = imagePaths.ToList();
            item.ItemData = WalletItemDataCodec.EncodePaymentAccount(data);
        }
    }

    public static Attachment CloneAttachmentForExport(Attachment attachment)
    {
        return new Attachment
        {
            Id = attachment.Id,
            OwnerType = attachment.OwnerType,
            OwnerId = attachment.OwnerId,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            StoragePath = attachment.StoragePath,
            SizeBytes = attachment.SizeBytes,
            CreatedAt = attachment.CreatedAt
        };
    }

    public static PasswordEntry ClonePasswordForExport(PasswordEntry source, bool includeCategory = true)
    {
        var clone = source.CreateDetachedCopy();
        if (!includeCategory)
        {
            clone.CategoryId = null;
        }

        clone.MdbxDatabaseId = null;
        clone.MdbxFolderId = null;
        return clone;
    }
}
