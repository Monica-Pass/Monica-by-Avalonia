using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{

    private static IReadOnlyList<string> DecodeSecureItemImagePaths(SecureItem item) => item.ItemType switch
    {
        VaultItemType.Document => WalletItemDataCodec.DecodeDocument(item).ImagePaths,
        VaultItemType.BankCard => WalletItemDataCodec.DecodeBankCard(item).ImagePaths,
        VaultItemType.BillingAddress => WalletItemDataCodec.DecodeBillingAddress(item).ImagePaths,
        VaultItemType.PaymentAccount => WalletItemDataCodec.DecodePaymentAccount(item).ImagePaths,
        VaultItemType.Note => NoteContentCodec.DecodeImagePaths(item.ImagePaths),
        _ => WalletItemDataCodec.DecodeImagePaths(item.ImagePaths)
    };

    private static Attachment CreateSecureItemImageAttachmentForExport(SecureItem item, string imagePath, int index)
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

    private static string ResolveSecureItemImageFileName(SecureItem item, string imagePath, int index)
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

    private static string InferAttachmentContentType(string path)
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

    private static void ApplySecureItemImagePaths(SecureItem item, IReadOnlyList<string> imagePaths)
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
}
