using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.Core.ImportExport;

public sealed partial class ImportExportService
{
    private const int MaximumImportCsvCharacters = 64 * 1024 * 1024;

    public IReadOnlyList<SecureItem> ImportTotpCsv(string csvText)
    {
        EnsureCsvWithinResourceLimit(csvText);
        using var reader = new StringReader(csvText);
        using var csv = new CsvReader(reader, CreateCsvConfiguration());
        var items = new List<SecureItem>();
        if (!csv.Read())
        {
            return items;
        }

        csv.ReadHeader();
        EnsureRecognizedHeader(
            csv.HeaderRecord,
            "type", "itemType", "item_type", "title", "name", "data", "itemData", "item_data", "notes", "note");
        while (csv.Read())
        {
            var type = ReadField(csv, "type", "itemType", "item_type");
            if (!string.Equals(type, "TOTP", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var title = ReadField(csv, "title", "name");
            var data = TotpDataResolver.ParseStoredItemData(ReadField(csv, "data", "itemData", "item_data"), title);
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
        EnsureCsvWithinResourceLimit(csvText);
        using var reader = new StringReader(csvText);
        using var csv = new CsvReader(reader, CreateCsvConfiguration());
        var items = new List<SecureItem>();
        if (!csv.Read())
        {
            return items;
        }

        csv.ReadHeader();
        EnsureRecognizedHeader(
            csv.HeaderRecord,
            "type", "itemType", "item_type", "title", "name", "data", "itemData", "item_data", "notes", "note");
        while (csv.Read())
        {
            var type = ReadField(csv, "type", "itemType", "item_type");
            if (!string.Equals(type, "NOTE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var title = ReadField(csv, "title", "name");
            var decoded = NoteContentCodec.Decode(ReadField(csv, "data", "itemData", "item_data"), ReadField(csv, "notes", "note"));
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
        EnsureCsvWithinResourceLimit(csvText);
        using var reader = new StringReader(csvText);
        using var csv = new CsvReader(reader, CreateCsvConfiguration());
        var passwords = new List<PasswordEntry>();
        if (!csv.Read())
        {
            return passwords;
        }

        csv.ReadHeader();
        EnsureRecognizedHeader(
            csv.HeaderRecord,
            "title", "name", "folder/name", "login_title", "website", "url", "uri", "login_uri", "login_uri_1",
            "username", "login_username", "user", "email", "password", "login_password");
        while (csv.Read())
        {
            var title = ReadField(csv, "title", "name", "folder/name", "login_title");
            var website = ReadField(csv, "website", "url", "uri", "login_uri", "login_uri_1");
            var username = ReadField(csv, "username", "login_username", "user", "email");
            passwords.Add(new PasswordEntry
            {
                Title = string.IsNullOrWhiteSpace(title) ? InferTitle(website, username) : title,
                Website = website,
                Username = username,
                Password = ReadField(csv, "password", "login_password"),
                Notes = ReadField(csv, "notes", "note"),
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
            });
        }

        return passwords;
    }

    private static void EnsureCsvWithinResourceLimit(string csvText)
    {
        if (csvText.Length > MaximumImportCsvCharacters)
        {
            throw new CsvImportException(
                CsvImportError.ResourceLimitExceeded,
                "The CSV import exceeds the safe size limit.");
        }
    }

    private static void EnsureRecognizedHeader(string[]? headers, params string[] recognizedNames)
    {
        if (headers is { Length: > 0 } && headers.Any(
                header => recognizedNames.Contains(header, StringComparer.OrdinalIgnoreCase)))
        {
            return;
        }

        throw new CsvImportException(CsvImportError.InvalidFormat, "The CSV import headers are not recognized.");
    }

    private static CsvConfiguration CreateCsvConfiguration() =>
        new(CultureInfo.InvariantCulture)
        {
            BadDataFound = null,
            HeaderValidated = null,
            IgnoreBlankLines = true,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim
        };

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

    private static PasswordLoginType ParseLoginType(string value) =>
        Enum.TryParse<PasswordLoginType>(value, ignoreCase: true, out var loginType)
            ? loginType
            : PasswordLoginType.Password;

    private static bool ParseBoolean(string value) =>
        bool.TryParse(value, out var result) ? result : value is "1" or "yes" or "YES";

    private static DateTimeOffset? ParseUnixMilliseconds(string value) =>
        long.TryParse(value, CultureInfo.InvariantCulture, out var milliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : null;
}
