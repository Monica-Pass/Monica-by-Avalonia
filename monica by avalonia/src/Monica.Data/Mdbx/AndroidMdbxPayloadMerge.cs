using System.Text;
using System.Text.Json;

namespace Monica.Data.Mdbx;

/// <summary>Which flat payload shape a record carries, i.e. which key set this client is allowed to rewrite.</summary>
public enum AndroidMdbxPayloadFamily
{
    Password,
    SecureItem
}

/// <summary>
/// Rebuilds a payload so an edit stays lossless for the other client. The desktop encodes the fields it
/// models; everything else has to come back off the record it just read, because MDBX holds one payload
/// document per object and writing a fresh document deletes whatever the reader skipped.
/// </summary>
/// <remarks>
/// Rules, in order of how they were chosen:
/// <list type="bullet">
/// <item>Every key the new payload carries is written from the new payload. A key this client emits can
/// never be held back by an older value, otherwise an edit would silently not apply.</item>
/// <item>Keys the new payload does not carry are kept verbatim unless this client owns them. Ownership is
/// what makes clearing possible: an absent owned key means "the user removed this", an absent foreign key
/// means "the other client added it and I cannot model it".</item>
/// <item>Keys are matched on their snake_case form because both spellings appear in real payloads
/// (<c>password_plain</c> and <c>passwordPlain</c>) and are the same field.</item>
/// <item>Values are copied as raw JSON text, so a nested object, an array with a <see langword="null"/>
/// element, <c>false</c>, an empty string and a big integer survive without passing through a model that
/// would round or normalize them.</item>
/// </list>
/// An unchanged <c>custom_fields</c> sequence is copied whole, including foreign metadata. Once the
/// modeled sequence changes, use the new array: without stable item ids, guessing which row owns
/// metadata could attach it to the wrong item.
/// </remarks>
public static class AndroidMdbxPayloadMerge
{
    /// <summary>
    /// Fields the desktop writes for a password, plus the legacy <c>password</c> alias it no longer emits.
    /// Mirrors <see cref="AndroidMdbxPayloadCodec.EncodePassword"/>.
    /// </summary>
    private static readonly HashSet<string> PasswordOwnedKeys =
    [
        "kind", "monica_entry_id", "room_id", "website", "username", "app_package_name", "app_name",
        "password_plain", "password", "notes", "sort_order", "category_id", "mdbx_folder_id",
        "bound_note_room_id", "bound_note_entry_id", "login_type", "ssh_key_data", "authenticator_key",
        "passkey_bindings", "custom_fields", "bitwarden_mode", "keepass_mode", "deleted_at",
        "password_history", "attachments", "is_archived", "archived_at"
    ];

    /// <summary>
    /// Fields the desktop writes for a secure item. Mirrors
    /// <see cref="AndroidMdbxPayloadCodec.EncodeSecureItem"/>.
    /// </summary>
    private static readonly HashSet<string> SecureItemOwnedKeys =
    [
        "kind", "monica_entry_id", "room_id", "notes", "sort_order", "item_data", "image_paths",
        "category_id", "mdbx_folder_id", "bound_password_entry_id", "bitwarden_mode", "keepass_mode",
        "deleted_at", "attachments"
    ];

    /// <summary>
    /// Owned keys whose absence means "leave the stored value alone" rather than "clear it".
    /// <see cref="AndroidMdbxPayloadCodec"/> omits <c>ssh_key_data</c> when the desktop holds no key, and
    /// Android reads an absent value the same way, so writing it back keeps the other client's key instead
    /// of clearing a field neither client can express as a clear.
    /// </summary>
    private static readonly HashSet<string> InheritOnAbsence = ["ssh_key_data"];

    public static string PreserveForeignFields(
        string? storedPayloadJson,
        string newPayloadJson,
        AndroidMdbxPayloadFamily family)
    {
        if (string.IsNullOrWhiteSpace(storedPayloadJson))
        {
            return newPayloadJson;
        }

        var owned = family == AndroidMdbxPayloadFamily.Password ? PasswordOwnedKeys : SecureItemOwnedKeys;
        try
        {
            using var stored = JsonDocument.Parse(storedPayloadJson);
            using var @new = JsonDocument.Parse(newPayloadJson);
            if (stored.RootElement.ValueKind != JsonValueKind.Object ||
                @new.RootElement.ValueKind != JsonValueKind.Object)
            {
                return newPayloadJson;
            }

            var written = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in @new.RootElement.EnumerateObject())
            {
                written.Add(Canonical(property.Name));
            }

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var property in stored.RootElement.EnumerateObject())
                {
                    var name = Canonical(property.Name);
                    if (written.Contains(name) ||
                        (owned.Contains(name) && !InheritOnAbsence.Contains(name)))
                    {
                        continue;
                    }

                    WriteRaw(writer, property.Name, property.Value);
                }

                foreach (var property in @new.RootElement.EnumerateObject())
                {
                    var value = property.Value;
                    if (family == AndroidMdbxPayloadFamily.Password &&
                        Canonical(property.Name) == "custom_fields" &&
                        TryGetUniqueProperty(stored.RootElement, "custom_fields", out var original) &&
                        CustomFieldsUnchanged(original, value))
                    {
                        value = original;
                    }

                    WriteRaw(writer, property.Name, value);
                }

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            // A stored payload this client cannot parse is not a licence to guess at it: write the new
            // document, which is what happened before merging existed.
            return newPayloadJson;
        }
    }

    private static readonly HashSet<string> CustomFieldKeys =
        ["title", "value", "is_protected", "sort_order"];

    private static bool CustomFieldsUnchanged(JsonElement stored, JsonElement updated)
    {
        if (stored.ValueKind != JsonValueKind.Array || updated.ValueKind != JsonValueKind.Array ||
            stored.GetArrayLength() != updated.GetArrayLength())
        {
            return false;
        }

        for (var index = 0; index < stored.GetArrayLength(); index++)
        {
            var before = stored[index];
            var after = updated[index];
            if (before.ValueKind != JsonValueKind.Object || after.ValueKind != JsonValueKind.Object ||
                after.EnumerateObject().Any(property => !CustomFieldKeys.Contains(Canonical(property.Name))))
            {
                return false;
            }

            foreach (var key in CustomFieldKeys)
            {
                if (!TryGetUniqueProperty(before, key, out var oldValue) ||
                    !TryGetUniqueProperty(after, key, out var newValue) ||
                    !JsonElement.DeepEquals(oldValue, newValue))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TryGetUniqueProperty(JsonElement owner, string name, out JsonElement value)
    {
        value = default;
        var found = false;
        foreach (var property in owner.EnumerateObject())
        {
            if (Canonical(property.Name) != name)
            {
                continue;
            }

            if (found)
            {
                return false;
            }

            value = property.Value;
            found = true;
        }

        return found;
    }

    private static void WriteRaw(Utf8JsonWriter writer, string name, JsonElement value)
    {
        writer.WritePropertyName(name);
        value.WriteTo(writer);
    }

    private static string Canonical(string name)
    {
        var builder = new StringBuilder(name.Length + 4);
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (char.IsUpper(character))
            {
                if (builder.Length > 0 && builder[^1] != '_')
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
