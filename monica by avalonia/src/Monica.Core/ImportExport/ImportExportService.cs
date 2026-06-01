using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CsvHelper;
using CsvHelper.Configuration;
using Monica.Core.Models;

namespace Monica.Core.ImportExport;

public interface IImportExportService
{
    string ExportJson(IEnumerable<PasswordEntry> passwords, IEnumerable<SecureItem> secureItems, IEnumerable<Category>? categories = null);
    MonicaExportPackage ImportJson(string json);
    string ExportPasswordCsv(IEnumerable<PasswordEntry> passwords);
    IReadOnlyList<PasswordEntry> ImportPasswordCsv(string csv);
}

public sealed record MonicaExportPackage(
    int SchemaVersion,
    IReadOnlyList<PasswordEntry> Passwords,
    IReadOnlyList<SecureItem> SecureItems,
    IReadOnlyList<Category> Categories);

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

    public string ExportJson(IEnumerable<PasswordEntry> passwords, IEnumerable<SecureItem> secureItems, IEnumerable<Category>? categories = null)
    {
        var package = new MonicaExportDtoPackage(
            68,
            passwords.Select(PasswordEntryDto.FromModel).ToList(),
            secureItems.Select(SecureItemDto.FromModel).ToList(),
            (categories ?? []).Select(CategoryDto.FromModel).ToList());
        return JsonSerializer.Serialize(package, MonicaJsonContext.Default.MonicaExportDtoPackage);
    }

    public MonicaExportPackage ImportJson(string json)
    {
        var package = JsonSerializer.Deserialize(json, MonicaJsonContext.Default.MonicaExportDtoPackage);
        return package is null
            ? new MonicaExportPackage(68, [], [], [])
            : new MonicaExportPackage(
                package.SchemaVersion,
                package.Passwords.Select(item => item.ToModel()).ToList(),
                package.SecureItems.Select(item => item.ToModel()).ToList(),
                (package.Categories ?? []).Select(item => item.ToModel()).ToList());
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
}

internal sealed record MonicaExportDtoPackage(
    int SchemaVersion,
    IReadOnlyList<PasswordEntryDto> Passwords,
    IReadOnlyList<SecureItemDto> SecureItems,
    IReadOnlyList<CategoryDto>? Categories = null);

internal sealed class CategoryDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public long? MdbxDatabaseId { get; set; }

    public static CategoryDto FromModel(Category source)
    {
        return new CategoryDto
        {
            Id = source.Id,
            Name = source.Name,
            SortOrder = source.SortOrder,
            MdbxDatabaseId = source.MdbxDatabaseId
        };
    }

    public Category ToModel()
    {
        return new Category
        {
            Id = Id,
            Name = Name,
            SortOrder = SortOrder,
            MdbxDatabaseId = MdbxDatabaseId
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
[JsonSerializable(typeof(MonicaExportPackage))]
[JsonSerializable(typeof(PasswordEntry))]
[JsonSerializable(typeof(SecureItem))]
[JsonSerializable(typeof(Category))]
internal sealed partial class MonicaJsonContext : JsonSerializerContext;
