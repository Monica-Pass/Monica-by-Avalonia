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

    /// <param name="folderId">
    /// Native folder (project) object id the entry lives in. Android reads <c>mdbx_folder_id</c> as
    /// the entry's parent folder, so the entry's own record id must never be written there.
    /// </param>
    public static string EncodePassword(
        PasswordEntry entry,
        IReadOnlyList<CustomField> customFields,
        string? folderId,
        string? boundNoteEntryId = null,
        IReadOnlyList<PasswordHistoryEntry>? passwordHistory = null,
        IReadOnlyList<Attachment>? attachments = null)
    {
        var fieldsToWrite = customFields.ToList();
        if (entry.LoginType == PasswordLoginType.ApiKey &&
            !fieldsToWrite.Any(ApiKeyEntryFields.IsMarker))
        {
            fieldsToWrite = fieldsToWrite
                .Where(field => !ApiKeyEntryFields.Owns(field.Title))
                .Append(new CustomField
                {
                    EntryId = entry.Id,
                    Title = ApiKeyEntryFields.Marker,
                    Value = ApiKeyEntryFields.Type,
                    SortOrder = fieldsToWrite.Count
                })
                .ToList();
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            // Key order mirrors Monica Android's passwordMutation so both clients emit the same document.
            writer.WriteStartObject();
            writer.WriteString("kind", "password");
            WriteOptionalString(writer, "monica_entry_id", entry.ReplicaGroupId);
            writer.WriteNumber("room_id", entry.Id);
            writer.WriteString("website", entry.Website ?? "");
            writer.WriteString("username", entry.Username ?? "");
            writer.WriteString("app_package_name", entry.AppPackageName ?? "");
            writer.WriteString("app_name", entry.AppName ?? "");
            writer.WriteString("password_plain", entry.Password ?? "");
            writer.WriteString("notes", entry.Notes ?? "");
            writer.WriteNumber("sort_order", entry.SortOrder);
            WriteOptionalNumber(writer, "category_id", entry.CategoryId);
            WriteOptionalString(writer, "mdbx_folder_id", NormalizeMdbxFolderId(folderId));
            WriteOptionalNumber(writer, "bound_note_room_id", entry.BoundNoteId);
            WriteOptionalString(writer, "bound_note_entry_id", boundNoteEntryId);
            writer.WriteString("login_type", ToAndroidLoginType(entry.LoginType));
            // An empty desktop value must not be emitted as an explicit "": Android treats a
            // present-but-empty ssh_key_data as a clear, and legacy Android payloads carry the
            // key only in Room, so omission is the only non-destructive choice.
            WriteOptionalString(writer, "ssh_key_data", string.IsNullOrEmpty(entry.SshKeyData) ? null : entry.SshKeyData);
            writer.WriteString("authenticator_key", entry.AuthenticatorKey ?? "");
            writer.WriteString("passkey_bindings", entry.PasskeyBindings ?? "");
            writer.WritePropertyName("custom_fields");
            writer.WriteStartArray();
            foreach (var field in fieldsToWrite
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
            WriteOptionalUnixMilliseconds(writer, "deleted_at", entry.DeletedAt);
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

            // Android keeps archive state in Room only, so there is no canonical archived
            // field to reuse; without this an archived entry reappears after a reload.
            if (entry.IsArchived || entry.ArchivedAt is not null)
            {
                writer.WriteBoolean("is_archived", entry.IsArchived);
                WriteOptionalUnixMilliseconds(writer, "archived_at", entry.ArchivedAt);
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
                Password = GetPasswordPlain(root),
                Notes = GetString(root, "notes"),
                SortOrder = checked((int)(GetInt64(root, "sort_order", "sortOrder") ?? 0)),
                CategoryId = GetInt64(root, "category_id", "categoryId"),
                MdbxFolderId = NormalizeMdbxFolderId(GetNullableString(root, "mdbx_folder_id", "mdbxFolderId")),
                ReplicaGroupId = NormalizeOptionalText(GetNullableString(root, "monica_entry_id", "monicaEntryId")),
                DeletedAt = GetDateTimeOffset(root, "deleted_at", "deletedAt"),
                IsArchived = GetBoolean(root, "is_archived", "isArchived"),
                ArchivedAt = GetDateTimeOffset(root, "archived_at", "archivedAt"),
                BoundNoteId = GetInt64(root, "bound_note_room_id", "boundNoteRoomId"),
                LoginType = ParseLoginType(GetString(root, "login_type", "loginType")),
                SshKeyData = GetMdbxSshKeyData(root),
                AuthenticatorKey = GetString(root, "authenticator_key", "authenticatorKey"),
                PasskeyBindings = GetString(root, "passkey_bindings", "passkeyBindings")
            };

            var customFields = DecodeCustomFields(root, entryId);
            // Android API-key records identify themselves twice: the login_type discriminator is
            // canonical, while older records may carry only the marker custom field. Treat the
            // marker as a compatibility fallback so those records do not reopen as passwords.
            if (entry.LoginType == PasswordLoginType.Password &&
                customFields.Any(ApiKeyEntryFields.IsMarker))
            {
                entry.LoginType = PasswordLoginType.ApiKey;
            }
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

    /// <param name="folderId">
    /// Native folder (project) object id the item lives in; see the note on <see cref="EncodePassword"/>.
    /// </param>
    public static string EncodeSecureItem(
        SecureItem item,
        string? folderId,
        string? boundPasswordEntryId = null,
        IReadOnlyList<Attachment>? attachments = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            // Key order mirrors Monica Android's secureItemMutation.
            writer.WriteStartObject();
            writer.WriteString("kind", ToAndroidSecureItemKind(item.ItemType));
            WriteOptionalString(writer, "monica_entry_id", item.ReplicaGroupId);
            writer.WriteNumber("room_id", item.Id);
            writer.WriteString("notes", item.Notes ?? "");
            writer.WriteNumber("sort_order", item.SortOrder);
            writer.WriteString("item_data", item.ItemData ?? "");
            writer.WriteString("image_paths", item.ImagePaths ?? "[]");
            WriteOptionalNumber(writer, "category_id", item.CategoryId);
            WriteOptionalString(writer, "mdbx_folder_id", NormalizeMdbxFolderId(folderId));
            WriteOptionalString(writer, "bound_password_entry_id", boundPasswordEntryId);
            writer.WriteBoolean("bitwarden_mode", item.BitwardenVaultId is not null);
            writer.WriteBoolean("keepass_mode", item.KeepassDatabaseId is not null);
            WriteOptionalUnixMilliseconds(writer, "deleted_at", item.DeletedAt);
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
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !TryGetString(root, out var kind, "kind"))
            {
                return null;
            }

            var itemType = FromAndroidSecureItemKind(kind) ?? FromMdbxEntryType(entryType);
            if (itemType is null)
            {
                return null;
            }

            var item = new SecureItem
            {
                Id = GetInt64(root, "room_id", "roomId") ?? 0,
                ItemType = itemType.Value,
                Title = recordTitle ?? "",
                Notes = GetString(root, "notes"),
                SortOrder = checked((int)(GetInt64(root, "sort_order", "sortOrder") ?? 0)),
                ItemData = GetString(root, "item_data", "itemData"),
                ImagePaths = GetPreferredString(root, "image_paths", "imagePaths", "[]"),
                CategoryId = GetInt64(root, "category_id", "categoryId"),
                MdbxFolderId = NormalizeMdbxFolderId(GetNullableString(root, "mdbx_folder_id", "mdbxFolderId")),
                ReplicaGroupId = NormalizeOptionalText(GetNullableString(root, "monica_entry_id", "monicaEntryId")),
                DeletedAt = GetDateTimeOffset(root, "deleted_at", "deletedAt")
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
        "APIKEY" or "API_KEY" => PasswordLoginType.ApiKey,
        "SSO" => PasswordLoginType.Sso,
        "WIFI" => PasswordLoginType.Wifi,
        "SSH_KEY" or "SSH-KEY" => PasswordLoginType.SshKey,
        "BARCODE" => PasswordLoginType.Barcode,
        _ => PasswordLoginType.Password
    };

    private static string ToAndroidLoginType(PasswordLoginType value) => value switch
    {
        PasswordLoginType.ApiKey => "API_KEY",
        PasswordLoginType.Sso => "SSO",
        PasswordLoginType.Wifi => "WIFI",
        PasswordLoginType.SshKey => "SSH_KEY",
        PasswordLoginType.Barcode => "BARCODE",
        _ => "PASSWORD"
    };

    private static string ToAndroidSecureItemKind(VaultItemType itemType) => itemType switch
    {
        VaultItemType.Totp => "totp",
        VaultItemType.BankCard => "bank_card",
        VaultItemType.Document => "document",
        VaultItemType.BillingAddress => "billing_address",
        VaultItemType.PaymentAccount => "payment_account",
        _ => "note"
    };

    private static VaultItemType? FromAndroidSecureItemKind(string kind) => kind.Trim().ToLowerInvariant() switch
    {
        "note" => VaultItemType.Note,
        "totp" => VaultItemType.Totp,
        "bank_card" or "card" => VaultItemType.BankCard,
        "document" or "document_ref" or "document-ref" => VaultItemType.Document,
        "billing_address" or "billing-address" => VaultItemType.BillingAddress,
        "payment_account" or "payment-account" => VaultItemType.PaymentAccount,
        _ => null
    };

    private static VaultItemType? FromMdbxEntryType(string entryType) => entryType.Trim().ToLowerInvariant() switch
    {
        "note" => VaultItemType.Note,
        "totp" => VaultItemType.Totp,
        "card" => VaultItemType.BankCard,
        "document-ref" => VaultItemType.Document,
        "billing-address" => VaultItemType.BillingAddress,
        "payment-account" => VaultItemType.PaymentAccount,
        _ => null
    };

    private static string? NormalizeMdbxFolderId(string? value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value, "root", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// An explicit password_plain is the current value even when it is empty; the legacy password field
    /// only applies to payloads written before that field existed. Treating a blank read as missing would
    /// hand back a password the user cleared.
    /// </summary>
    private static string GetPasswordPlain(JsonElement root) =>
        GetNullableString(root, "password_plain", "passwordPlain") ??
        GetNullableString(root, "password") ??
        "";

    /// <summary>
    /// Mirrors Android's readMdbxSshKeyData: an absent key means "leave the local value alone",
    /// an explicit empty string clears it, and object-valued producers are kept as raw JSON text.
    /// </summary>
    private static string GetMdbxSshKeyData(JsonElement root, string existing = "")
    {
        if (!TryGetProperty(root, out var value, "ssh_key_data", "sshKeyData") || value.ValueKind == JsonValueKind.Null)
        {
            return existing;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Object or JsonValueKind.Array => value.GetRawText(),
            _ => existing
        };
    }

    private static void WriteOptionalString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(propertyName, value);
        }
    }

    private static void WriteOptionalNumber(Utf8JsonWriter writer, string propertyName, long? value)
    {
        if (value is not null)
        {
            writer.WriteNumber(propertyName, value.Value);
        }
    }

    private static void WriteOptionalUnixMilliseconds(Utf8JsonWriter writer, string propertyName, DateTimeOffset? value)
    {
        if (value is not null)
        {
            writer.WriteNumber(propertyName, value.Value.ToUnixTimeMilliseconds());
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

    private static DateTimeOffset? GetDateTimeOffset(JsonElement element, params string[] names)
    {
        var milliseconds = GetInt64(element, names);
        if (milliseconds is not null)
        {
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds.Value);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        var value = GetNullableString(element, names);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
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
