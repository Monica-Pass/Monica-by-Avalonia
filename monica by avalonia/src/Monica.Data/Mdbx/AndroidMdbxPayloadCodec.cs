using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Monica.Core.Models;

namespace Monica.Data.Mdbx;

/// <summary>
/// Encodes and decodes the flat business payload used by Monica Android.
/// Record metadata such as title, entry type, deletion state, and object ID
/// remains owned by the surrounding MDBX entry record.
/// </summary>
public static class AndroidMdbxPayloadCodec
{
    private static readonly JsonSerializerOptions ExtensionJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string EncodePassword(
        PasswordEntry entry,
        IReadOnlyList<CustomField> customFields,
        string? boundNoteEntryId = null,
        IReadOnlyList<PasswordHistoryEntry>? passwordHistory = null,
        IReadOnlyList<Attachment>? attachments = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", "password");
            writer.WriteNumber("room_id", entry.Id);
            writer.WriteString("website", entry.Website ?? "");
            writer.WriteString("username", entry.Username ?? "");
            writer.WriteString("app_package_name", entry.AppPackageName ?? "");
            writer.WriteString("app_name", entry.AppName ?? "");
            writer.WriteString("password_plain", entry.Password ?? "");
            writer.WriteString("notes", entry.Notes ?? "");
            WriteNullableNumber(writer, "category_id", entry.CategoryId);
            WriteMdbxFolderId(writer, entry.MdbxFolderId);
            WriteNullableNumber(writer, "bound_note_room_id", entry.BoundNoteId);
            WriteNullableString(writer, "bound_note_entry_id", boundNoteEntryId);
            writer.WriteString("login_type", ToAndroidLoginType(entry.LoginType));
            writer.WriteString("authenticator_key", entry.AuthenticatorKey ?? "");
            writer.WriteString("passkey_bindings", entry.PasskeyBindings ?? "");
            writer.WritePropertyName("custom_fields");
            writer.WriteStartArray();
            foreach (var field in customFields
                         .Where(field => !string.IsNullOrWhiteSpace(field.Title))
                         .OrderBy(field => field.SortOrder)
                         .ThenBy(field => field.Id))
            {
                writer.WriteStartObject();
                writer.WriteString("title", field.Title.Trim());
                writer.WriteString("value", field.Value ?? "");
                writer.WriteBoolean("is_protected", field.IsProtected);
                writer.WriteNumber("sort_order", field.SortOrder);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteBoolean("bitwarden_mode", entry.BitwardenVaultId is not null);
            writer.WriteBoolean("keepass_mode", entry.KeepassDatabaseId is not null);

            // Avalonia-only compatibility extensions stay flat and are ignored by Android.
            // They preserve current desktop history/attachment behavior until those features
            // move to canonical MDBX-native records in the next storage milestone.
            if (passwordHistory is not null)
            {
                writer.WritePropertyName("password_history");
                JsonSerializer.Serialize(writer, passwordHistory, ExtensionJsonOptions);
            }

            if (attachments is not null)
            {
                writer.WritePropertyName("attachments");
                JsonSerializer.Serialize(writer, attachments, ExtensionJsonOptions);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static AndroidMdbxPasswordPayload? DecodePassword(string payloadJson, string recordTitle)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetString(root, out var kind, "kind") ||
                !string.Equals(kind, "password", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var entryId = GetInt64(root, "room_id", "roomId") ?? 0;
            var entry = new PasswordEntry
            {
                Id = entryId,
                Title = recordTitle ?? "",
                Website = GetString(root, "website"),
                Username = GetString(root, "username"),
                AppPackageName = GetString(root, "app_package_name", "appPackageName"),
                AppName = GetString(root, "app_name", "appName"),
                Password = GetPreferredString(root, "password_plain", "password"),
                Notes = GetString(root, "notes"),
                CategoryId = GetInt64(root, "category_id", "categoryId"),
                MdbxFolderId = NormalizeMdbxFolderId(GetNullableString(root, "mdbx_folder_id", "mdbxFolderId")),
                BoundNoteId = GetInt64(root, "bound_note_room_id", "boundNoteRoomId"),
                LoginType = ParseLoginType(GetString(root, "login_type", "loginType")),
                AuthenticatorKey = GetString(root, "authenticator_key", "authenticatorKey"),
                PasskeyBindings = GetString(root, "passkey_bindings", "passkeyBindings")
            };

            var customFields = DecodeCustomFields(root, entryId);
            var passwordHistory = DeserializeExtensionList<PasswordHistoryEntry>(root, "password_history", "passwordHistory");
            var attachments = DeserializeExtensionList<Attachment>(root, "attachments");
            return new AndroidMdbxPasswordPayload(
                entry,
                customFields,
                GetNullableString(root, "bound_note_entry_id", "boundNoteEntryId"),
                passwordHistory,
                attachments);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string EncodeSecureItem(
        SecureItem item,
        string? boundPasswordEntryId = null,
        IReadOnlyList<Attachment>? attachments = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", ToAndroidSecureItemKind(item.ItemType));
            writer.WriteNumber("room_id", item.Id);
            writer.WriteString("notes", item.Notes ?? "");
            writer.WriteString("item_data", item.ItemData ?? "");
            writer.WriteString("image_paths", item.ImagePaths ?? "[]");
            WriteNullableNumber(writer, "category_id", item.CategoryId);
            WriteMdbxFolderId(writer, item.MdbxFolderId);
            WriteNullableString(writer, "bound_password_entry_id", boundPasswordEntryId);
            writer.WriteBoolean("bitwarden_mode", item.BitwardenVaultId is not null);
            writer.WriteBoolean("keepass_mode", item.KeepassDatabaseId is not null);
            if (attachments is not null)
            {
                writer.WritePropertyName("attachments");
                JsonSerializer.Serialize(writer, attachments, ExtensionJsonOptions);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static AndroidMdbxSecureItemPayload? DecodeSecureItem(
        string payloadJson,
        string recordTitle,
        string entryType)
    {
        var itemType = FromMdbxEntryType(entryType);
        if (itemType is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !TryGetString(root, out _, "kind"))
            {
                return null;
            }

            var item = new SecureItem
            {
                Id = GetInt64(root, "room_id", "roomId") ?? 0,
                ItemType = itemType.Value,
                Title = recordTitle ?? "",
                Notes = GetString(root, "notes"),
                ItemData = GetString(root, "item_data", "itemData"),
                ImagePaths = GetPreferredString(root, "image_paths", "imagePaths", "[]"),
                CategoryId = GetInt64(root, "category_id", "categoryId"),
                MdbxFolderId = NormalizeMdbxFolderId(GetNullableString(root, "mdbx_folder_id", "mdbxFolderId"))
            };

            return new AndroidMdbxSecureItemPayload(
                item,
                GetNullableString(root, "bound_password_entry_id", "boundPasswordEntryId"),
                DeserializeExtensionList<Attachment>(root, "attachments"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<CustomField> DecodeCustomFields(JsonElement root, long entryId)
    {
        if (!TryGetProperty(root, out var fields, "custom_fields", "customFields") || fields.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<CustomField>();
        var index = 0;
        foreach (var element in fields.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                index++;
                continue;
            }

            var title = GetPreferredString(element, "title", "label").Trim();
            if (title.Length == 0)
            {
                index++;
                continue;
            }

            result.Add(new CustomField
            {
                EntryId = entryId,
                Title = title,
                Value = GetString(element, "value"),
                IsProtected = GetBoolean(element, "is_protected", "isProtected"),
                SortOrder = checked((int)(GetInt64(element, "sort_order", "sortOrder") ?? index))
            });
            index++;
        }

        return result;
    }

    private static List<T>? DeserializeExtensionList<T>(JsonElement root, params string[] names)
    {
        if (!TryGetProperty(root, out var element, names) || element.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return JsonSerializer.Deserialize<List<T>>(element.GetRawText(), ExtensionJsonOptions);
    }

    private static PasswordLoginType ParseLoginType(string value) => value.Trim().ToUpperInvariant() switch
    {
        "SSO" => PasswordLoginType.Sso,
        "WIFI" => PasswordLoginType.Wifi,
        "SSH_KEY" or "SSH-KEY" => PasswordLoginType.SshKey,
        _ => PasswordLoginType.Password
    };

    private static string ToAndroidLoginType(PasswordLoginType value) => value switch
    {
        PasswordLoginType.Sso => "SSO",
        PasswordLoginType.Wifi => "WIFI",
        PasswordLoginType.SshKey => "SSH_KEY",
        _ => "PASSWORD"
    };

    private static string ToAndroidSecureItemKind(VaultItemType itemType) => itemType switch
    {
        VaultItemType.Totp => "totp",
        VaultItemType.BankCard => "bank_card",
        VaultItemType.Document => "document",
        _ => "note"
    };

    private static VaultItemType? FromMdbxEntryType(string entryType) => entryType.Trim().ToLowerInvariant() switch
    {
        "note" => VaultItemType.Note,
        "totp" => VaultItemType.Totp,
        "card" => VaultItemType.BankCard,
        "document-ref" => VaultItemType.Document,
        _ => null
    };

    private static string? NormalizeMdbxFolderId(string? value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value, "root", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();

    private static void WriteMdbxFolderId(Utf8JsonWriter writer, string? value) =>
        WriteNullableString(writer, "mdbx_folder_id", NormalizeMdbxFolderId(value));

    private static void WriteNullableString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
        }
        else
        {
            writer.WriteString(propertyName, value);
        }
    }

    private static void WriteNullableNumber(Utf8JsonWriter writer, string propertyName, long? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
        }
        else
        {
            writer.WriteNumber(propertyName, value.Value);
        }
    }

    private static string GetPreferredString(JsonElement element, string primary, string fallback, string defaultValue = "")
    {
        var primaryValue = GetNullableString(element, primary);
        if (!string.IsNullOrEmpty(primaryValue))
        {
            return primaryValue;
        }

        return GetNullableString(element, fallback) ?? defaultValue;
    }

    private static string GetString(JsonElement element, params string[] names) =>
        GetNullableString(element, names) ?? "";

    private static string? GetNullableString(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static bool TryGetString(JsonElement element, out string value, params string[] names)
    {
        value = GetNullableString(element, names) ?? "";
        return value.Length > 0;
    }

    private static long? GetInt64(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return long.TryParse(value.ToString(), out number) ? number : null;
    }

    private static bool GetBoolean(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
            JsonValueKind.Number => value.TryGetInt32(out var number) && number != 0,
            _ => false
        };
    }

    private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }
}

public sealed record AndroidMdbxPasswordPayload(
    PasswordEntry Entry,
    IReadOnlyList<CustomField> CustomFields,
    string? BoundNoteEntryId,
    List<PasswordHistoryEntry>? PasswordHistory = null,
    List<Attachment>? Attachments = null);

public sealed record AndroidMdbxSecureItemPayload(
    SecureItem Item,
    string? BoundPasswordEntryId,
    List<Attachment>? Attachments = null);
