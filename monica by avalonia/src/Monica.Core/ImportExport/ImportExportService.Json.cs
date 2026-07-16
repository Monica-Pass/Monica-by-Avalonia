using System.Text.Json;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.Core.ImportExport;

public sealed partial class ImportExportService : IImportExportService
{
    private const int MaximumMonicaJsonCharacters = 64 * 1024 * 1024;

    public string ExportJson(
        IEnumerable<PasswordEntry> passwords,
        IEnumerable<SecureItem> secureItems,
        IEnumerable<Category>? categories = null,
        IReadOnlyDictionary<long, IReadOnlyList<CustomField>>? passwordCustomFields = null,
        IReadOnlyDictionary<long, IReadOnlyList<PasswordHistoryEntry>>? passwordHistory = null,
        IReadOnlyDictionary<long, IReadOnlyList<PasswordAttachmentExport>>? passwordAttachments = null,
        IReadOnlyDictionary<long, IReadOnlyList<SecureItemAttachmentExport>>? secureItemAttachments = null)
    {
        var passwordList = passwords.ToList();
        var secureItemList = secureItems.ToList();
        var exportedPasswordIds = passwordList
            .Select(item => item.Id)
            .Where(id => id > 0)
            .ToHashSet();
        var exportedSecureItemIds = secureItemList
            .Select(item => item.Id)
            .Where(id => id > 0)
            .ToHashSet();
        var package = new MonicaExportDtoPackage(
            71,
            passwordList.Select(ToPortableDto).ToList(),
            secureItemList.Select(ToPortableDto).ToList(),
            (categories ?? []).Select(ToPortableDto).ToList(),
            ToCustomFieldGroupDtos(passwordCustomFields, exportedPasswordIds),
            ToPasswordHistoryGroupDtos(passwordHistory, exportedPasswordIds),
            ToPasswordAttachmentGroupDtos(passwordAttachments, exportedPasswordIds),
            ToSecureItemAttachmentGroupDtos(secureItemAttachments, exportedSecureItemIds));
        return JsonSerializer.Serialize(package, MonicaJsonContext.Default.MonicaExportDtoPackage);
    }

    public MonicaExportPackage ImportJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw InvalidMonicaJsonFormat();
        }

        if (json.Length > MaximumMonicaJsonCharacters)
        {
            throw new MonicaJsonImportException(
                MonicaJsonImportError.ResourceLimitExceeded,
                "The Monica JSON import exceeds the safe size limit.");
        }

        try
        {
            var package = JsonSerializer.Deserialize(json, MonicaJsonContext.Default.MonicaExportDtoPackage);
            if (package is null || package.Passwords is null || package.SecureItems is null)
            {
                throw InvalidMonicaJsonFormat();
            }

            return new MonicaExportPackage(
                package.SchemaVersion,
                package.Passwords.Select(item => item.ToModel()).ToList(),
                package.SecureItems.Select(item => item.ToModel()).ToList(),
                (package.Categories ?? []).Select(item => item.ToModel()).ToList(),
                (package.PasswordCustomFields ?? []).Select(item => item.ToModel()).ToList(),
                (package.PasswordHistory ?? []).Select(item => item.ToModel()).ToList(),
                (package.PasswordAttachments ?? []).Select(item => item.ToModel()).ToList(),
                (package.SecureItemAttachments ?? []).Select(item => item.ToModel()).ToList());
        }
        catch (JsonException)
        {
            throw InvalidMonicaJsonFormat();
        }
        catch (NotSupportedException)
        {
            throw InvalidMonicaJsonFormat();
        }
    }

    private static MonicaJsonImportException InvalidMonicaJsonFormat() =>
        new(MonicaJsonImportError.InvalidFormat, "The Monica JSON import format is invalid.");

    private static IReadOnlyList<PasswordCustomFieldGroupDto> ToCustomFieldGroupDtos(
        IReadOnlyDictionary<long, IReadOnlyList<CustomField>>? customFields,
        IReadOnlySet<long> exportedPasswordIds)
    {
        if (customFields is null || exportedPasswordIds.Count == 0)
        {
            return [];
        }

        return customFields
            .Where(item => exportedPasswordIds.Contains(item.Key) && item.Value.Count > 0)
            .OrderBy(item => item.Key)
            .Select(item => PasswordCustomFieldGroupDto.FromModel(item.Key, item.Value))
            .ToList();
    }

    private static IReadOnlyList<PasswordHistoryGroupDto> ToPasswordHistoryGroupDtos(
        IReadOnlyDictionary<long, IReadOnlyList<PasswordHistoryEntry>>? passwordHistory,
        IReadOnlySet<long> exportedPasswordIds)
    {
        if (passwordHistory is null || exportedPasswordIds.Count == 0)
        {
            return [];
        }

        return passwordHistory
            .Where(item => exportedPasswordIds.Contains(item.Key) && item.Value.Count > 0)
            .OrderBy(item => item.Key)
            .Select(item => PasswordHistoryGroupDto.FromModel(item.Key, item.Value))
            .ToList();
    }

    private static IReadOnlyList<PasswordAttachmentGroupDto> ToPasswordAttachmentGroupDtos(
        IReadOnlyDictionary<long, IReadOnlyList<PasswordAttachmentExport>>? passwordAttachments,
        IReadOnlySet<long> exportedPasswordIds)
    {
        if (passwordAttachments is null || exportedPasswordIds.Count == 0)
        {
            return [];
        }

        return passwordAttachments
            .Where(item => exportedPasswordIds.Contains(item.Key) && item.Value.Count > 0)
            .OrderBy(item => item.Key)
            .Select(item => PasswordAttachmentGroupDto.FromModel(item.Key, item.Value))
            .ToList();
    }

    private static IReadOnlyList<SecureItemAttachmentGroupDto> ToSecureItemAttachmentGroupDtos(
        IReadOnlyDictionary<long, IReadOnlyList<SecureItemAttachmentExport>>? secureItemAttachments,
        IReadOnlySet<long> exportedSecureItemIds)
    {
        if (secureItemAttachments is null || exportedSecureItemIds.Count == 0)
        {
            return [];
        }

        return secureItemAttachments
            .Where(item => exportedSecureItemIds.Contains(item.Key) && item.Value.Count > 0)
            .OrderBy(item => item.Key)
            .Select(item => SecureItemAttachmentGroupDto.FromModel(item.Key, item.Value))
            .ToList();
    }

    private static PasswordEntryDto ToPortableDto(PasswordEntry source)
    {
        var dto = PasswordEntryDto.FromModel(source);
        dto.MdbxDatabaseId = null;
        dto.MdbxFolderId = null;
        return dto;
    }

    private static SecureItemDto ToPortableDto(SecureItem source)
    {
        var dto = SecureItemDto.FromModel(source);
        dto.MdbxDatabaseId = null;
        dto.MdbxFolderId = null;
        StripSecureItemImages(dto);
        return dto;
    }

    private static CategoryDto ToPortableDto(Category source)
    {
        var dto = CategoryDto.FromModel(source);
        dto.MdbxDatabaseId = null;
        dto.MdbxFolderId = null;
        return dto;
    }

    private static void StripSecureItemImages(SecureItemDto dto)
    {
        var item = dto.ToModel();
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
        }
        else if (item.ItemType == VaultItemType.Document)
        {
            var data = WalletItemDataCodec.DecodeDocument(item);
            data.ImagePaths.Clear();
            item.ItemData = WalletItemDataCodec.EncodeDocument(data);
        }
        else if (item.ItemType == VaultItemType.BankCard)
        {
            var data = WalletItemDataCodec.DecodeBankCard(item);
            data.ImagePaths.Clear();
            item.ItemData = WalletItemDataCodec.EncodeBankCard(data);
        }

        dto.ItemData = item.ItemData;
        dto.ImagePaths = item.ImagePaths;
    }
}
