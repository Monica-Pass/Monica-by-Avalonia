using Monica.Core.Models;

namespace Monica.Core.ImportExport;

internal sealed class SecureItemDto
{
    public long Id { get; set; }
    public VaultItemType ItemType { get; set; }
    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool IsFavorite { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string ItemData { get; set; } = "{}";
    public string ImagePaths { get; set; } = "[]";
    public long? BoundPasswordId { get; set; }
    public long? CategoryId { get; set; }
    public long? KeepassDatabaseId { get; set; }
    public string? KeepassGroupPath { get; set; }
    public string? KeepassEntryUuid { get; set; }
    public string? KeepassGroupUuid { get; set; }
    public long? MdbxDatabaseId { get; set; }
    public string? MdbxFolderId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? ReplicaGroupId { get; set; }
    public long? BitwardenVaultId { get; set; }
    public string? BitwardenCipherId { get; set; }
    public string? BitwardenFolderId { get; set; }
    public string? BitwardenRevisionDate { get; set; }
    public bool BitwardenLocalModified { get; set; }
    public SyncStatus SyncStatus { get; set; } = SyncStatus.None;

    public static SecureItemDto FromModel(SecureItem source) =>
        new()
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

    public SecureItem ToModel() =>
        new()
        {
            Id = Id,
            ItemType = ItemType,
            Title = Title,
            Notes = Notes,
            IsFavorite = IsFavorite,
            SortOrder = SortOrder,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            ItemData = ItemData,
            ImagePaths = ImagePaths,
            BoundPasswordId = BoundPasswordId,
            CategoryId = CategoryId,
            KeepassDatabaseId = KeepassDatabaseId,
            KeepassGroupPath = KeepassGroupPath,
            KeepassEntryUuid = KeepassEntryUuid,
            KeepassGroupUuid = KeepassGroupUuid,
            MdbxDatabaseId = MdbxDatabaseId,
            MdbxFolderId = MdbxFolderId,
            IsDeleted = IsDeleted,
            DeletedAt = DeletedAt,
            ReplicaGroupId = ReplicaGroupId,
            BitwardenVaultId = BitwardenVaultId,
            BitwardenCipherId = BitwardenCipherId,
            BitwardenFolderId = BitwardenFolderId,
            BitwardenRevisionDate = BitwardenRevisionDate,
            BitwardenLocalModified = BitwardenLocalModified,
            SyncStatus = SyncStatus
        };
}
