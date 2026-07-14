using Monica.Core.Models;

namespace Monica.Core.ImportExport;

internal sealed class PasswordCustomFieldGroupDto
{
    public long PasswordId { get; set; }
    public List<CustomFieldDto> Fields { get; set; } = [];

    public static PasswordCustomFieldGroupDto FromModel(long passwordId, IReadOnlyList<CustomField> fields) =>
        new() { PasswordId = passwordId, Fields = fields.Select(CustomFieldDto.FromModel).ToList() };

    public PasswordCustomFieldExportGroup ToModel() =>
        new(PasswordId, Fields.Select(item => item.ToModel()).ToList());
}

internal sealed class CustomFieldDto
{
    public long Id { get; set; }
    public long EntryId { get; set; }
    public string Title { get; set; } = "";
    public string Value { get; set; } = "";
    public bool IsProtected { get; set; }
    public int SortOrder { get; set; }

    public static CustomFieldDto FromModel(CustomField source) =>
        new()
        {
            Id = source.Id,
            EntryId = source.EntryId,
            Title = source.Title,
            Value = source.Value,
            IsProtected = source.IsProtected,
            SortOrder = source.SortOrder
        };

    public CustomField ToModel() =>
        new()
        {
            Id = Id,
            EntryId = EntryId,
            Title = Title,
            Value = Value,
            IsProtected = IsProtected,
            SortOrder = SortOrder
        };
}

internal sealed class PasswordHistoryGroupDto
{
    public long PasswordId { get; set; }
    public List<PasswordHistoryEntryDto> Entries { get; set; } = [];

    public static PasswordHistoryGroupDto FromModel(long passwordId, IReadOnlyList<PasswordHistoryEntry> entries) =>
        new() { PasswordId = passwordId, Entries = entries.Select(PasswordHistoryEntryDto.FromModel).ToList() };

    public PasswordHistoryExportGroup ToModel() =>
        new(PasswordId, Entries.Select(item => item.ToModel()).ToList());
}

internal sealed class PasswordHistoryEntryDto
{
    public long Id { get; set; }
    public long EntryId { get; set; }
    public string Password { get; set; } = "";
    public DateTimeOffset LastUsedAt { get; set; } = DateTimeOffset.UtcNow;

    public static PasswordHistoryEntryDto FromModel(PasswordHistoryEntry source) =>
        new() { Id = source.Id, EntryId = source.EntryId, Password = source.Password, LastUsedAt = source.LastUsedAt };

    public PasswordHistoryEntry ToModel() =>
        new() { Id = Id, EntryId = EntryId, Password = Password, LastUsedAt = LastUsedAt };
}

internal sealed class PasswordAttachmentGroupDto
{
    public long PasswordId { get; set; }
    public List<PasswordAttachmentDto> Attachments { get; set; } = [];

    public static PasswordAttachmentGroupDto FromModel(long passwordId, IReadOnlyList<PasswordAttachmentExport> attachments) =>
        new() { PasswordId = passwordId, Attachments = attachments.Select(PasswordAttachmentDto.FromModel).ToList() };

    public PasswordAttachmentExportGroup ToModel() =>
        new(PasswordId, Attachments.Select(item => item.ToModel()).ToList());
}

internal sealed class PasswordAttachmentDto
{
    public AttachmentDto Metadata { get; set; } = new();
    public string ContentBase64 { get; set; } = "";

    public static PasswordAttachmentDto FromModel(PasswordAttachmentExport source) =>
        new() { Metadata = AttachmentDto.FromModel(source.Metadata, includeStoragePath: false), ContentBase64 = source.ContentBase64 };

    public PasswordAttachmentExport ToModel() => new(Metadata.ToModel(), ContentBase64);
}

internal sealed class SecureItemAttachmentGroupDto
{
    public long SecureItemId { get; set; }
    public List<SecureItemAttachmentDto> Attachments { get; set; } = [];

    public static SecureItemAttachmentGroupDto FromModel(long secureItemId, IReadOnlyList<SecureItemAttachmentExport> attachments) =>
        new() { SecureItemId = secureItemId, Attachments = attachments.Select(SecureItemAttachmentDto.FromModel).ToList() };

    public SecureItemAttachmentExportGroup ToModel() =>
        new(SecureItemId, Attachments.Select(item => item.ToModel()).ToList());
}

internal sealed class SecureItemAttachmentDto
{
    public AttachmentDto Metadata { get; set; } = new();
    public string ContentBase64 { get; set; } = "";

    public static SecureItemAttachmentDto FromModel(SecureItemAttachmentExport source) =>
        new() { Metadata = AttachmentDto.FromModel(source.Metadata, includeStoragePath: false), ContentBase64 = source.ContentBase64 };

    public SecureItemAttachmentExport ToModel() => new(Metadata.ToModel(), ContentBase64);
}

internal sealed class AttachmentDto
{
    public long Id { get; set; }
    public string OwnerType { get; set; } = "";
    public long OwnerId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string StoragePath { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public long? BitwardenVaultId { get; set; }
    public string? KeepassBinaryRef { get; set; }

    public static AttachmentDto FromModel(Attachment source, bool includeStoragePath = true) =>
        new()
        {
            Id = source.Id,
            OwnerType = source.OwnerType,
            OwnerId = source.OwnerId,
            FileName = source.FileName,
            ContentType = source.ContentType,
            StoragePath = includeStoragePath ? source.StoragePath : "",
            SizeBytes = source.SizeBytes,
            CreatedAt = source.CreatedAt,
            BitwardenVaultId = source.BitwardenVaultId,
            KeepassBinaryRef = source.KeepassBinaryRef
        };

    public Attachment ToModel() =>
        new()
        {
            Id = Id,
            OwnerType = OwnerType,
            OwnerId = OwnerId,
            FileName = FileName,
            ContentType = ContentType,
            StoragePath = StoragePath,
            SizeBytes = SizeBytes,
            CreatedAt = CreatedAt,
            BitwardenVaultId = BitwardenVaultId,
            KeepassBinaryRef = KeepassBinaryRef
        };
}
