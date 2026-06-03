using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CsvHelper;
using CsvHelper.Configuration;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.Core.ImportExport;

public interface IImportExportService
{
    string ExportJson(
        IEnumerable<PasswordEntry> passwords,
        IEnumerable<SecureItem> secureItems,
        IEnumerable<Category>? categories = null,
        IReadOnlyDictionary<long, IReadOnlyList<CustomField>>? passwordCustomFields = null,
        IReadOnlyDictionary<long, IReadOnlyList<PasswordHistoryEntry>>? passwordHistory = null,
        IReadOnlyDictionary<long, IReadOnlyList<PasswordAttachmentExport>>? passwordAttachments = null,
        IReadOnlyDictionary<long, IReadOnlyList<SecureItemAttachmentExport>>? secureItemAttachments = null);
    MonicaExportPackage ImportJson(string json);
    string ExportPasswordCsv(IEnumerable<PasswordEntry> passwords);
    string ExportTotpCsv(IEnumerable<SecureItem> secureItems);
    string ExportNoteCsv(IEnumerable<SecureItem> secureItems);
    string ExportWalletCsv(IEnumerable<SecureItem> secureItems);
    string ExportAegisJson(IEnumerable<SecureItem> secureItems);
    IReadOnlyList<SecureItem> ImportTotpCsv(string csv);
    IReadOnlyList<SecureItem> ImportNoteCsv(string csv);
    IReadOnlyList<SecureItem> ImportAegisJson(string json);
    IReadOnlyList<PasswordEntry> ImportPasswordCsv(string csv);
}

public sealed record MonicaExportPackage(
    int SchemaVersion,
    IReadOnlyList<PasswordEntry> Passwords,
    IReadOnlyList<SecureItem> SecureItems,
    IReadOnlyList<Category> Categories,
    IReadOnlyList<PasswordCustomFieldExportGroup> PasswordCustomFields,
    IReadOnlyList<PasswordHistoryExportGroup> PasswordHistory,
    IReadOnlyList<PasswordAttachmentExportGroup> PasswordAttachments,
    IReadOnlyList<SecureItemAttachmentExportGroup> SecureItemAttachments);

public sealed record PasswordCustomFieldExportGroup(long PasswordId, IReadOnlyList<CustomField> Fields);

public sealed record PasswordHistoryExportGroup(long PasswordId, IReadOnlyList<PasswordHistoryEntry> Entries);

public sealed record PasswordAttachmentExportGroup(long PasswordId, IReadOnlyList<PasswordAttachmentExport> Attachments);

public sealed record PasswordAttachmentExport(Attachment Metadata, string ContentBase64);

public sealed record SecureItemAttachmentExportGroup(long SecureItemId, IReadOnlyList<SecureItemAttachmentExport> Attachments);

public sealed record SecureItemAttachmentExport(Attachment Metadata, string ContentBase64);

public sealed class ImportExportService : IImportExportService
{
    private static readonly string[] PasswordCsvHeaders =
    [
        "title",
        "website",
        "username",
        "password",
        "notes",
        "authenticatorKey",
        "appName",
        "appPackageName",
        "email",
        "phone",
        "loginType",
        "ssoProvider",
        "passkeyBindings",
        "wifiMetadata",
        "sshKeyData"
    ];
    private static readonly string[] SecureItemCsvHeaders =
    [
        "ID",
        "Type",
        "Title",
        "Data",
        "Notes",
        "IsFavorite",
        "ImagePaths",
        "CreatedAt",
        "UpdatedAt"
    ];

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
        var package = JsonSerializer.Deserialize(json, MonicaJsonContext.Default.MonicaExportDtoPackage);
        return package is null
            ? new MonicaExportPackage(68, [], [], [], [], [], [], [])
            : new MonicaExportPackage(
                package.SchemaVersion,
                package.Passwords.Select(item => item.ToModel()).ToList(),
                package.SecureItems.Select(item => item.ToModel()).ToList(),
                (package.Categories ?? []).Select(item => item.ToModel()).ToList(),
                (package.PasswordCustomFields ?? []).Select(item => item.ToModel()).ToList(),
                (package.PasswordHistory ?? []).Select(item => item.ToModel()).ToList(),
                (package.PasswordAttachments ?? []).Select(item => item.ToModel()).ToList(),
                (package.SecureItemAttachments ?? []).Select(item => item.ToModel()).ToList());
    }

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

    public string ExportPasswordCsv(IEnumerable<PasswordEntry> passwords)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        using var csv = new CsvWriter(writer, CreateCsvConfiguration());

        foreach (var header in PasswordCsvHeaders)
        {
            csv.WriteField(header);
        }

        csv.NextRecord();

        foreach (var password in passwords)
        {
            csv.WriteField(password.Title);
            csv.WriteField(password.Website);
            csv.WriteField(password.Username);
            csv.WriteField(password.Password);
            csv.WriteField(password.Notes);
            csv.WriteField(password.AuthenticatorKey);
            csv.WriteField(password.AppName);
            csv.WriteField(password.AppPackageName);
            csv.WriteField(password.Email);
            csv.WriteField(password.Phone);
            csv.WriteField(password.LoginType.ToString());
            csv.WriteField(password.SsoProvider);
            csv.WriteField(password.PasskeyBindings);
            csv.WriteField(password.WifiMetadata);
            csv.WriteField(password.SshKeyData);
            csv.NextRecord();
        }

        return writer.ToString();
    }

    public string ExportTotpCsv(IEnumerable<SecureItem> secureItems)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        using var csv = new CsvWriter(writer, CreateCsvConfiguration());

        foreach (var header in SecureItemCsvHeaders)
        {
            csv.WriteField(header);
        }

        csv.NextRecord();

        foreach (var item in secureItems.Where(item => item.ItemType == VaultItemType.Totp))
        {
            var data = TotpDataResolver.ParseStoredItemData(item.ItemData, item.Title, item.Notes);
            if (data is null || string.IsNullOrWhiteSpace(data.Secret))
            {
                continue;
            }

            csv.WriteField(item.Id);
            csv.WriteField("TOTP");
            csv.WriteField(item.Title);
            csv.WriteField(TotpDataResolver.ToItemData(data));
            csv.WriteField(item.Notes);
            csv.WriteField(item.IsFavorite.ToString());
            csv.WriteField("");
            csv.WriteField(item.CreatedAt.ToUnixTimeMilliseconds());
            csv.WriteField(item.UpdatedAt.ToUnixTimeMilliseconds());
            csv.NextRecord();
        }

        return writer.ToString();
    }

    public string ExportNoteCsv(IEnumerable<SecureItem> secureItems)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        using var csv = new CsvWriter(writer, CreateCsvConfiguration());

        foreach (var header in SecureItemCsvHeaders)
        {
            csv.WriteField(header);
        }

        csv.NextRecord();

        foreach (var item in secureItems.Where(item => item.ItemType == VaultItemType.Note))
        {
            var decoded = NoteContentCodec.DecodeFromItem(item);
            var payload = NoteContentCodec.BuildSavePayload(
                item.Title,
                decoded.Content,
                string.Join(",", decoded.Tags),
                decoded.IsMarkdown,
                FilterPortableImagePaths(NoteContentCodec.DecodeImagePaths(item.ImagePaths)));

            csv.WriteField(item.Id);
            csv.WriteField("NOTE");
            csv.WriteField(payload.Title);
            csv.WriteField(payload.ItemData);
            csv.WriteField(payload.NotesCache);
            csv.WriteField(item.IsFavorite.ToString());
            csv.WriteField(payload.ImagePaths);
            csv.WriteField(item.CreatedAt.ToUnixTimeMilliseconds());
            csv.WriteField(item.UpdatedAt.ToUnixTimeMilliseconds());
            csv.NextRecord();
        }

        return writer.ToString();
    }

    public string ExportWalletCsv(IEnumerable<SecureItem> secureItems)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        using var csv = new CsvWriter(writer, CreateCsvConfiguration());

        foreach (var header in SecureItemCsvHeaders)
        {
            csv.WriteField(header);
        }

        csv.NextRecord();

        foreach (var item in secureItems.Where(item => item.ItemType is VaultItemType.BankCard or VaultItemType.Document))
        {
            var (type, data, imagePaths) = CreateWalletCsvPayload(item);
            csv.WriteField(item.Id);
            csv.WriteField(type);
            csv.WriteField(item.Title);
            csv.WriteField(data);
            csv.WriteField(item.Notes);
            csv.WriteField(item.IsFavorite.ToString());
            csv.WriteField(imagePaths);
            csv.WriteField(item.CreatedAt.ToUnixTimeMilliseconds());
            csv.WriteField(item.UpdatedAt.ToUnixTimeMilliseconds());
            csv.NextRecord();
        }

        return writer.ToString();
    }

    public string ExportAegisJson(IEnumerable<SecureItem> secureItems)
    {
        var entries = secureItems
            .Where(item => item.ItemType == VaultItemType.Totp)
            .Select(CreateAegisEntry)
            .OfType<AegisEntryDto>()
            .ToList();
        var package = new AegisExportPackageDto(
            1,
            new AegisHeaderDto([], new AegisHeaderParamsDto("", "")),
            new AegisDatabaseDto(3, entries));

        return JsonSerializer.Serialize(package, MonicaJsonContext.Default.AegisExportPackageDto);
    }

    public IReadOnlyList<SecureItem> ImportAegisJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("db", out var db))
        {
            throw new InvalidOperationException("Invalid Aegis JSON payload.");
        }

        if (db.ValueKind == JsonValueKind.String)
        {
            throw new NotSupportedException("Encrypted Aegis JSON imports are not supported yet.");
        }

        if (db.ValueKind != JsonValueKind.Object || !db.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Invalid Aegis database payload.");
        }

        var imported = new List<SecureItem>();
        foreach (var entry in entries.EnumerateArray())
        {
            var item = CreateSecureItemFromAegisEntry(entry);
            if (item is not null)
            {
                imported.Add(item);
            }
        }

        return imported;
    }

    public IReadOnlyList<SecureItem> ImportTotpCsv(string csvText)
    {
        using var reader = new StringReader(csvText);
        using var csv = new CsvReader(reader, CreateCsvConfiguration());
        var items = new List<SecureItem>();

        if (!csv.Read())
        {
            return items;
        }

        csv.ReadHeader();

        while (csv.Read())
        {
            var type = ReadField(csv, "type", "itemType", "item_type");
            if (!string.Equals(type, "TOTP", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var title = ReadField(csv, "title", "name");
            var dataText = ReadField(csv, "data", "itemData", "item_data");
            var data = TotpDataResolver.ParseStoredItemData(dataText, title);
            if (data is null || string.IsNullOrWhiteSpace(data.Secret))
            {
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            items.Add(new SecureItem
            {
                ItemType = VaultItemType.Totp,
                Title = FirstNonEmpty(title, data.AccountName, data.Issuer, "Authenticator"),
                Notes = ReadField(csv, "notes", "note"),
                IsFavorite = ParseBoolean(ReadField(csv, "isFavorite", "favorite")),
                CreatedAt = ParseUnixMilliseconds(ReadField(csv, "createdAt", "created_at")) ?? now,
                UpdatedAt = ParseUnixMilliseconds(ReadField(csv, "updatedAt", "updated_at")) ?? now,
                ItemData = TotpDataResolver.ToItemData(data)
            });
        }

        return items;
    }

    public IReadOnlyList<SecureItem> ImportNoteCsv(string csvText)
    {
        using var reader = new StringReader(csvText);
        using var csv = new CsvReader(reader, CreateCsvConfiguration());
        var items = new List<SecureItem>();

        if (!csv.Read())
        {
            return items;
        }

        csv.ReadHeader();

        while (csv.Read())
        {
            var type = ReadField(csv, "type", "itemType", "item_type");
            if (!string.Equals(type, "NOTE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var title = ReadField(csv, "title", "name");
            var dataText = ReadField(csv, "data", "itemData", "item_data");
            var notes = ReadField(csv, "notes", "note");
            var decoded = NoteContentCodec.Decode(dataText, notes);
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(decoded.Content))
            {
                continue;
            }

            var payload = NoteContentCodec.BuildSavePayload(
                title,
                decoded.Content,
                string.Join(",", decoded.Tags),
                decoded.IsMarkdown,
                NoteContentCodec.DecodeImagePaths(ReadField(csv, "imagePaths", "image_paths")));
            var now = DateTimeOffset.UtcNow;
            items.Add(new SecureItem
            {
                ItemType = VaultItemType.Note,
                Title = payload.Title,
                Notes = payload.NotesCache,
                ImagePaths = payload.ImagePaths,
                IsFavorite = ParseBoolean(ReadField(csv, "isFavorite", "favorite")),
                CreatedAt = ParseUnixMilliseconds(ReadField(csv, "createdAt", "created_at")) ?? now,
                UpdatedAt = ParseUnixMilliseconds(ReadField(csv, "updatedAt", "updated_at")) ?? now,
                ItemData = payload.ItemData
            });
        }

        return items;
    }

    public IReadOnlyList<PasswordEntry> ImportPasswordCsv(string csvText)
    {
        using var reader = new StringReader(csvText);
        using var csv = new CsvReader(reader, CreateCsvConfiguration());
        var passwords = new List<PasswordEntry>();

        if (!csv.Read())
        {
            return passwords;
        }

        csv.ReadHeader();

        while (csv.Read())
        {
            var title = ReadField(csv, "title", "name", "folder/name", "login_title");
            var website = ReadField(csv, "website", "url", "uri", "login_uri", "login_uri_1");
            var username = ReadField(csv, "username", "login_username", "user", "email");
            var password = ReadField(csv, "password", "login_password");
            var notes = ReadField(csv, "notes", "note");
            var entry = new PasswordEntry
            {
                Title = string.IsNullOrWhiteSpace(title) ? InferTitle(website, username) : title,
                Website = website,
                Username = username,
                Password = password,
                Notes = notes,
                AuthenticatorKey = ReadField(csv, "authenticatorKey", "totp", "login_totp", "otp", "otp_secret"),
                AppName = ReadField(csv, "appName", "app_name"),
                AppPackageName = ReadField(csv, "appPackageName", "app_package_name"),
                Email = ReadField(csv, "email"),
                Phone = ReadField(csv, "phone"),
                LoginType = ParseLoginType(ReadField(csv, "loginType", "login_type", "type")),
                SsoProvider = ReadField(csv, "ssoProvider", "sso_provider"),
                PasskeyBindings = ReadField(csv, "passkeyBindings", "passkey_bindings", "passkey"),
                WifiMetadata = ReadField(csv, "wifiMetadata", "wifi_metadata"),
                SshKeyData = ReadField(csv, "sshKeyData", "ssh_key_data", "ssh_key"),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                BitwardenLocalModified = true
            };

            passwords.Add(entry);
        }

        return passwords;
    }

    private static CsvConfiguration CreateCsvConfiguration()
    {
        return new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            BadDataFound = null,
            HeaderValidated = null,
            IgnoreBlankLines = true,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim
        };
    }

    private static string ReadField(CsvReader csv, params string[] names)
    {
        if (csv.HeaderRecord is not { Length: > 0 } headers)
        {
            return "";
        }

        foreach (var name in names)
        {
            var header = headers.FirstOrDefault(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
            if (header is null)
            {
                continue;
            }

            try
            {
                return csv.GetField(header) ?? "";
            }
            catch
            {
                return "";
            }
        }

        return "";
    }

    private static string InferTitle(string website, string username)
    {
        if (Uri.TryCreate(website, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            return uri.Host;
        }

        if (!string.IsNullOrWhiteSpace(website))
        {
            return website;
        }

        return string.IsNullOrWhiteSpace(username) ? "Imported password" : username;
    }

    private static PasswordLoginType ParseLoginType(string value)
    {
        return Enum.TryParse<PasswordLoginType>(value, ignoreCase: true, out var loginType)
            ? loginType
            : PasswordLoginType.Password;
    }

    private static bool ParseBoolean(string value) =>
        bool.TryParse(value, out var result)
            ? result
            : value is "1" or "yes" or "YES";

    private static DateTimeOffset? ParseUnixMilliseconds(string value) =>
        long.TryParse(value, CultureInfo.InvariantCulture, out var milliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : null;

    private static AegisEntryDto? CreateAegisEntry(SecureItem item)
    {
        var data = TotpDataResolver.ParseStoredItemData(item.ItemData, item.Title);
        if (data is null || string.IsNullOrWhiteSpace(data.Secret))
        {
            return null;
        }

        var normalized = TotpDataResolver.Normalize(data);
        if (!string.Equals(normalized.OtpType, "TOTP", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(normalized.OtpType, "STEAM", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = FirstNonEmpty(normalized.AccountName, item.Title, normalized.Issuer, "Authenticator");
        var issuer = string.IsNullOrWhiteSpace(normalized.Issuer) ? item.Title.Trim() : normalized.Issuer;
        var note = string.IsNullOrWhiteSpace(item.Notes) ? null : item.Notes.Trim();
        var entryType = string.Equals(normalized.OtpType, "STEAM", StringComparison.OrdinalIgnoreCase) ? "steam" : "totp";

        return new AegisEntryDto(
            entryType,
            Guid.NewGuid().ToString(),
            name,
            issuer,
            note,
            new AegisEntryInfoDto(
                normalized.Secret,
                normalized.Algorithm,
                normalized.Digits,
                normalized.Period));
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.Select(value => value.Trim()).First(value => !string.IsNullOrWhiteSpace(value));

    private static SecureItem? CreateSecureItemFromAegisEntry(JsonElement entry)
    {
        if (!entry.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var secret = ReadJsonString(info, "secret");
        if (string.IsNullOrWhiteSpace(secret))
        {
            return null;
        }

        var type = ReadJsonString(entry, "type");
        var otpType = type.Trim().ToLowerInvariant() switch
        {
            "steam" => "STEAM",
            "totp" or "" => "TOTP",
            _ => ""
        };
        if (otpType.Length == 0)
        {
            return null;
        }

        var name = ReadJsonString(entry, "name");
        var issuer = ReadJsonString(entry, "issuer");
        var note = ReadJsonString(entry, "note");
        var data = TotpDataResolver.Normalize(new TotpData(
            secret,
            issuer,
            name,
            ReadJsonInt(info, "period") ?? 30,
            ReadJsonInt(info, "digits") ?? (otpType == "STEAM" ? 5 : 6),
            ReadJsonString(info, "algo", "SHA1"),
            otpType));
        var now = DateTimeOffset.UtcNow;

        return new SecureItem
        {
            ItemType = VaultItemType.Totp,
            Title = FirstNonEmpty(name, issuer, "Authenticator"),
            Notes = note,
            ItemData = TotpDataResolver.ToItemData(data),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static string ReadJsonString(JsonElement element, string name, string fallback = "") =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static int? ReadJsonInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : null;

    private static (string Type, string Data, string ImagePaths) CreateWalletCsvPayload(SecureItem item)
    {
        if (item.ItemType == VaultItemType.BankCard)
        {
            var data = WalletItemDataCodec.DecodeBankCard(item);
            var imagePaths = WalletItemDataCodec.EncodeImagePaths(FilterPortableImagePaths(data.ImagePaths));
            data.ImagePaths.Clear();
            return ("BANK_CARD", WalletItemDataCodec.EncodeBankCard(data), imagePaths);
        }

        var document = WalletItemDataCodec.DecodeDocument(item);
        var documentImagePaths = WalletItemDataCodec.EncodeImagePaths(FilterPortableImagePaths(document.ImagePaths));
        document.ImagePaths.Clear();
        return ("DOCUMENT", WalletItemDataCodec.EncodeDocument(document), documentImagePaths);
    }

    private static IReadOnlyList<string> FilterPortableImagePaths(IEnumerable<string> imagePaths) =>
        imagePaths
            .Where(path => !path.StartsWith("mdbx:", StringComparison.OrdinalIgnoreCase))
            .ToList();
}

internal sealed record MonicaExportDtoPackage(
    int SchemaVersion,
    IReadOnlyList<PasswordEntryDto> Passwords,
    IReadOnlyList<SecureItemDto> SecureItems,
    IReadOnlyList<CategoryDto>? Categories = null,
    IReadOnlyList<PasswordCustomFieldGroupDto>? PasswordCustomFields = null,
    IReadOnlyList<PasswordHistoryGroupDto>? PasswordHistory = null,
    IReadOnlyList<PasswordAttachmentGroupDto>? PasswordAttachments = null,
    IReadOnlyList<SecureItemAttachmentGroupDto>? SecureItemAttachments = null);

internal sealed record AegisExportPackageDto(
    int Version,
    AegisHeaderDto Header,
    AegisDatabaseDto Db);

internal sealed record AegisHeaderDto(
    IReadOnlyList<AegisHeaderSlotDto> Slots,
    AegisHeaderParamsDto Params);

internal sealed class AegisHeaderSlotDto
{
}

internal sealed record AegisHeaderParamsDto(string Nonce, string Tag);

internal sealed record AegisDatabaseDto(int Version, IReadOnlyList<AegisEntryDto> Entries);

internal sealed record AegisEntryDto(
    string Type,
    string Uuid,
    string Name,
    string Issuer,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Note,
    AegisEntryInfoDto Info);

internal sealed record AegisEntryInfoDto(
    string Secret,
    string Algo,
    int Digits,
    int Period);

internal sealed class CategoryDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public long? MdbxDatabaseId { get; set; }
    public string? MdbxFolderId { get; set; }

    public static CategoryDto FromModel(Category source)
    {
        return new CategoryDto
        {
            Id = source.Id,
            Name = source.Name,
            SortOrder = source.SortOrder,
            MdbxDatabaseId = source.MdbxDatabaseId,
            MdbxFolderId = source.MdbxFolderId
        };
    }

    public Category ToModel()
    {
        return new Category
        {
            Id = Id,
            Name = Name,
            SortOrder = SortOrder,
            MdbxDatabaseId = MdbxDatabaseId,
            MdbxFolderId = MdbxFolderId
        };
    }
}

internal sealed class PasswordCustomFieldGroupDto
{
    public long PasswordId { get; set; }
    public List<CustomFieldDto> Fields { get; set; } = [];

    public static PasswordCustomFieldGroupDto FromModel(long passwordId, IReadOnlyList<CustomField> fields)
    {
        return new PasswordCustomFieldGroupDto
        {
            PasswordId = passwordId,
            Fields = fields.Select(CustomFieldDto.FromModel).ToList()
        };
    }

    public PasswordCustomFieldExportGroup ToModel()
    {
        return new PasswordCustomFieldExportGroup(
            PasswordId,
            Fields.Select(item => item.ToModel()).ToList());
    }
}

internal sealed class CustomFieldDto
{
    public long Id { get; set; }
    public long EntryId { get; set; }
    public string Title { get; set; } = "";
    public string Value { get; set; } = "";
    public bool IsProtected { get; set; }
    public int SortOrder { get; set; }

    public static CustomFieldDto FromModel(CustomField source)
    {
        return new CustomFieldDto
        {
            Id = source.Id,
            EntryId = source.EntryId,
            Title = source.Title,
            Value = source.Value,
            IsProtected = source.IsProtected,
            SortOrder = source.SortOrder
        };
    }

    public CustomField ToModel()
    {
        return new CustomField
        {
            Id = Id,
            EntryId = EntryId,
            Title = Title,
            Value = Value,
            IsProtected = IsProtected,
            SortOrder = SortOrder
        };
    }
}

internal sealed class PasswordHistoryGroupDto
{
    public long PasswordId { get; set; }
    public List<PasswordHistoryEntryDto> Entries { get; set; } = [];

    public static PasswordHistoryGroupDto FromModel(long passwordId, IReadOnlyList<PasswordHistoryEntry> entries)
    {
        return new PasswordHistoryGroupDto
        {
            PasswordId = passwordId,
            Entries = entries.Select(PasswordHistoryEntryDto.FromModel).ToList()
        };
    }

    public PasswordHistoryExportGroup ToModel()
    {
        return new PasswordHistoryExportGroup(
            PasswordId,
            Entries.Select(item => item.ToModel()).ToList());
    }
}

internal sealed class PasswordHistoryEntryDto
{
    public long Id { get; set; }
    public long EntryId { get; set; }
    public string Password { get; set; } = "";
    public DateTimeOffset LastUsedAt { get; set; } = DateTimeOffset.UtcNow;

    public static PasswordHistoryEntryDto FromModel(PasswordHistoryEntry source)
    {
        return new PasswordHistoryEntryDto
        {
            Id = source.Id,
            EntryId = source.EntryId,
            Password = source.Password,
            LastUsedAt = source.LastUsedAt
        };
    }

    public PasswordHistoryEntry ToModel()
    {
        return new PasswordHistoryEntry
        {
            Id = Id,
            EntryId = EntryId,
            Password = Password,
            LastUsedAt = LastUsedAt
        };
    }
}

internal sealed class PasswordAttachmentGroupDto
{
    public long PasswordId { get; set; }
    public List<PasswordAttachmentDto> Attachments { get; set; } = [];

    public static PasswordAttachmentGroupDto FromModel(long passwordId, IReadOnlyList<PasswordAttachmentExport> attachments)
    {
        return new PasswordAttachmentGroupDto
        {
            PasswordId = passwordId,
            Attachments = attachments.Select(PasswordAttachmentDto.FromModel).ToList()
        };
    }

    public PasswordAttachmentExportGroup ToModel()
    {
        return new PasswordAttachmentExportGroup(
            PasswordId,
            Attachments.Select(item => item.ToModel()).ToList());
    }
}

internal sealed class PasswordAttachmentDto
{
    public AttachmentDto Metadata { get; set; } = new();
    public string ContentBase64 { get; set; } = "";

    public static PasswordAttachmentDto FromModel(PasswordAttachmentExport source)
    {
        return new PasswordAttachmentDto
        {
            Metadata = AttachmentDto.FromModel(source.Metadata, includeStoragePath: false),
            ContentBase64 = source.ContentBase64
        };
    }

    public PasswordAttachmentExport ToModel()
    {
        return new PasswordAttachmentExport(Metadata.ToModel(), ContentBase64);
    }
}

internal sealed class SecureItemAttachmentGroupDto
{
    public long SecureItemId { get; set; }
    public List<SecureItemAttachmentDto> Attachments { get; set; } = [];

    public static SecureItemAttachmentGroupDto FromModel(long secureItemId, IReadOnlyList<SecureItemAttachmentExport> attachments)
    {
        return new SecureItemAttachmentGroupDto
        {
            SecureItemId = secureItemId,
            Attachments = attachments.Select(SecureItemAttachmentDto.FromModel).ToList()
        };
    }

    public SecureItemAttachmentExportGroup ToModel()
    {
        return new SecureItemAttachmentExportGroup(
            SecureItemId,
            Attachments.Select(item => item.ToModel()).ToList());
    }
}

internal sealed class SecureItemAttachmentDto
{
    public AttachmentDto Metadata { get; set; } = new();
    public string ContentBase64 { get; set; } = "";

    public static SecureItemAttachmentDto FromModel(SecureItemAttachmentExport source)
    {
        return new SecureItemAttachmentDto
        {
            Metadata = AttachmentDto.FromModel(source.Metadata, includeStoragePath: false),
            ContentBase64 = source.ContentBase64
        };
    }

    public SecureItemAttachmentExport ToModel()
    {
        return new SecureItemAttachmentExport(Metadata.ToModel(), ContentBase64);
    }
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

    public static AttachmentDto FromModel(Attachment source, bool includeStoragePath = true)
    {
        return new AttachmentDto
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
    }

    public Attachment ToModel()
    {
        return new Attachment
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
}

internal sealed class PasswordEntryDto
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Website { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool IsFavorite { get; set; }
    public int SortOrder { get; set; }
    public bool IsGroupCover { get; set; }
    public string AppPackageName { get; set; } = "";
    public string AppName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string AddressLine { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string ZipCode { get; set; } = "";
    public string Country { get; set; } = "";
    public string CreditCardNumber { get; set; } = "";
    public string CreditCardHolder { get; set; } = "";
    public string CreditCardExpiry { get; set; } = "";
    public string CreditCardCvv { get; set; } = "";
    public long? CategoryId { get; set; }
    public long? BoundNoteId { get; set; }
    public long? KeepassDatabaseId { get; set; }
    public string? KeepassGroupPath { get; set; }
    public string? KeepassEntryUuid { get; set; }
    public string? KeepassGroupUuid { get; set; }
    public long? MdbxDatabaseId { get; set; }
    public string? MdbxFolderId { get; set; }
    public string AuthenticatorKey { get; set; } = "";
    public string PasskeyBindings { get; set; } = "";
    public string SshKeyData { get; set; } = "";
    public PasswordLoginType LoginType { get; set; } = PasswordLoginType.Password;
    public string SsoProvider { get; set; } = "";
    public long? SsoRefEntryId { get; set; }
    public string WifiMetadata { get; set; } = "";
    public string CustomIconType { get; set; } = "NONE";
    public string? CustomIconValue { get; set; }
    public long CustomIconUpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public string? ReplicaGroupId { get; set; }
    public long? BitwardenVaultId { get; set; }
    public string? BitwardenCipherId { get; set; }
    public string? BitwardenFolderId { get; set; }
    public string? BitwardenRevisionDate { get; set; }
    public int BitwardenCipherType { get; set; } = 1;
    public bool BitwardenLocalModified { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public static PasswordEntryDto FromModel(PasswordEntry source)
    {
        return new PasswordEntryDto
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

    public PasswordEntry ToModel()
    {
        return new PasswordEntry
        {
            Id = Id,
            Title = Title,
            Website = Website,
            Username = Username,
            Password = Password,
            Notes = Notes,
            IsFavorite = IsFavorite,
            SortOrder = SortOrder,
            IsGroupCover = IsGroupCover,
            AppPackageName = AppPackageName,
            AppName = AppName,
            Email = Email,
            Phone = Phone,
            AddressLine = AddressLine,
            City = City,
            State = State,
            ZipCode = ZipCode,
            Country = Country,
            CreditCardNumber = CreditCardNumber,
            CreditCardHolder = CreditCardHolder,
            CreditCardExpiry = CreditCardExpiry,
            CreditCardCvv = CreditCardCvv,
            CategoryId = CategoryId,
            BoundNoteId = BoundNoteId,
            KeepassDatabaseId = KeepassDatabaseId,
            KeepassGroupPath = KeepassGroupPath,
            KeepassEntryUuid = KeepassEntryUuid,
            KeepassGroupUuid = KeepassGroupUuid,
            MdbxDatabaseId = MdbxDatabaseId,
            MdbxFolderId = MdbxFolderId,
            AuthenticatorKey = AuthenticatorKey,
            PasskeyBindings = PasskeyBindings,
            SshKeyData = SshKeyData,
            LoginType = LoginType,
            SsoProvider = SsoProvider,
            SsoRefEntryId = SsoRefEntryId,
            WifiMetadata = WifiMetadata,
            CustomIconType = CustomIconType,
            CustomIconValue = CustomIconValue,
            CustomIconUpdatedAt = CustomIconUpdatedAt,
            IsDeleted = IsDeleted,
            DeletedAt = DeletedAt,
            IsArchived = IsArchived,
            ArchivedAt = ArchivedAt,
            ReplicaGroupId = ReplicaGroupId,
            BitwardenVaultId = BitwardenVaultId,
            BitwardenCipherId = BitwardenCipherId,
            BitwardenFolderId = BitwardenFolderId,
            BitwardenRevisionDate = BitwardenRevisionDate,
            BitwardenCipherType = BitwardenCipherType,
            BitwardenLocalModified = BitwardenLocalModified,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };
    }
}

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

    public static SecureItemDto FromModel(SecureItem source)
    {
        return new SecureItemDto
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

    public SecureItem ToModel()
    {
        return new SecureItem
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
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(MonicaExportDtoPackage))]
[JsonSerializable(typeof(AegisExportPackageDto))]
[JsonSerializable(typeof(MonicaExportPackage))]
[JsonSerializable(typeof(PasswordEntry))]
[JsonSerializable(typeof(SecureItem))]
[JsonSerializable(typeof(Category))]
internal sealed partial class MonicaJsonContext : JsonSerializerContext;
