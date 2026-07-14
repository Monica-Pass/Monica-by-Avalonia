using System.Text.Json;
using System.Text.Json.Serialization;
using Monica.Core.Models;

namespace Monica.Core.ImportExport;

internal sealed record MonicaExportDtoPackage(
    int SchemaVersion,
    IReadOnlyList<PasswordEntryDto> Passwords,
    IReadOnlyList<SecureItemDto> SecureItems,
    IReadOnlyList<CategoryDto>? Categories = null,
    IReadOnlyList<PasswordCustomFieldGroupDto>? PasswordCustomFields = null,
    IReadOnlyList<PasswordHistoryGroupDto>? PasswordHistory = null,
    IReadOnlyList<PasswordAttachmentGroupDto>? PasswordAttachments = null,
    IReadOnlyList<SecureItemAttachmentGroupDto>? SecureItemAttachments = null);

internal sealed record AegisExportPackageDto(int Version, AegisHeaderDto Header, AegisDatabaseDto Db);

internal sealed record AegisHeaderDto(IReadOnlyList<AegisHeaderSlotDto> Slots, AegisHeaderParamsDto Params);

internal sealed class AegisHeaderSlotDto;

internal sealed record AegisHeaderParamsDto(string Nonce, string Tag);

internal sealed record AegisDatabaseDto(int Version, IReadOnlyList<AegisEntryDto> Entries);

internal sealed record AegisEntryDto(
    string Type,
    string Uuid,
    string Name,
    string Issuer,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Note,
    AegisEntryInfoDto Info);

internal sealed record AegisEntryInfoDto(string Secret, string Algo, int Digits, int Period);

internal sealed class CategoryDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public long? MdbxDatabaseId { get; set; }
    public string? MdbxFolderId { get; set; }

    public static CategoryDto FromModel(Category source) =>
        new()
        {
            Id = source.Id,
            Name = source.Name,
            SortOrder = source.SortOrder,
            MdbxDatabaseId = source.MdbxDatabaseId,
            MdbxFolderId = source.MdbxFolderId
        };

    public Category ToModel() =>
        new()
        {
            Id = Id,
            Name = Name,
            SortOrder = SortOrder,
            MdbxDatabaseId = MdbxDatabaseId,
            MdbxFolderId = MdbxFolderId
        };
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(MonicaExportDtoPackage))]
[JsonSerializable(typeof(AegisExportPackageDto))]
[JsonSerializable(typeof(MonicaExportPackage))]
[JsonSerializable(typeof(PasswordEntry))]
[JsonSerializable(typeof(SecureItem))]
[JsonSerializable(typeof(Category))]
internal sealed partial class MonicaJsonContext : JsonSerializerContext;
