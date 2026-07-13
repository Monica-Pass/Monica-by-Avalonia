using Monica.Core.ImportExport;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static bool TryDecodeAttachmentContent(string contentBase64, out byte[] content)
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

    private async Task<IReadOnlyList<string>> ImportSecureItemAttachmentsAsync(SecureItem item, IReadOnlyList<SecureItemAttachmentExport> attachments)
    {
        if (attachments.Count == 0)
        {
            return [];
        }

        var restoredPaths = new List<string>();
        foreach (var source in attachments)
        {
            if (!TryDecodeAttachmentContent(source.ContentBase64, out var content))
            {
                continue;
            }

            var draft = await _passwordAttachmentFileService.StoreAttachmentAsync(
                source.Metadata.FileName,
                content,
                source.Metadata.ContentType);
            restoredPaths.Add(draft.StoragePath);
        }

        return restoredPaths;
    }

    private static SecureItem CloneSecureItemForExport(SecureItem source, bool includeCategory = true, bool includeImages = true)
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

    private static SecureItem CloneSecureItemForImport(
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

    private static SecureItem CloneSecureItem(SecureItem source)
    {
        return new SecureItem
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
    }

    private static Category CloneCategory(Category source)
    {
        return new Category
        {
            Id = source.Id,
            Name = source.Name,
            SortOrder = source.SortOrder
        };
    }

    private static void StripSecureItemImages(SecureItem item)
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
        }
    }




















}
