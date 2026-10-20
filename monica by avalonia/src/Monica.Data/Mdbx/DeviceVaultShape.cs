using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Monica.Core.Passkeys;

namespace Monica.Data.Mdbx;

/// <summary>The portable business payload, without identifiers owned by the source device.</summary>
public static class DeviceVaultShape
{
    public static bool Supports(string type) => type is "login" or "ssh-key" or "totp" or "note" or "card"
        or "document-ref" or "identity" or "billing-address" or "payment-account" or "passkey" or "api-token" or "steam-mafile" or "folder" or "vault-metadata";

    public static readonly string[] LocalFields = ["room_id", "roomId", "id", "monica_entry_id", "category_id", "categoryId",
        "mdbx_folder_id", "mdbxFolderId", "mdbxDatabaseId", "mdbx_database_id", "bitwarden_mode", "keepass_mode",
        "bitwarden_vault_id", "bitwarden_cipher_id", "bitwarden_folder_id", "bitwarden_revision_date", "bitwarden_local_modified",
        "bitwardenVaultId", "bitwardenCipherId", "keepassDatabaseId", "keepassGroupPath", "storagePath", "device_sync_assets", "device_sync_images", "monica_password_encoding", "device_sync_pending_relations", "device_sync_key_projection"];

    public static JsonObject Normalize(string payload, string title, string folder, string type)
    {
        if (type is "login" or "ssh-key") return DevicePasswordShape.Normalize(payload, title, folder, type);
        var source = JsonNode.Parse(payload)?.AsObject() ?? throw new InvalidDataException("invalid_vault_payload");
        if (source["data"] is JsonObject wrapped)
        {
            source = (wrapped["item"] as JsonObject ?? wrapped).DeepClone().AsObject();
            source["kind"] = type switch
            {
                "card" => "bank_card",
                "document-ref" or "identity" => "document",
                "billing-address" => "billing_address",
                "payment-account" => "payment_account",
                _ => type
            };
            foreach (var (camel, snake) in new[] { ("itemData", "item_data"), ("imagePaths", "image_paths"),
                ("sortOrder", "sort_order"), ("isFavorite", "is_favorite") })
                if (source[camel] is { } value) { source[snake] = value.DeepClone(); source.Remove(camel); }
            foreach (var (camel, snake) in new[] { ("createdAt", "created_at"), ("updatedAt", "updated_at") })
                if (source[camel] is JsonValue date && date.TryGetValue<string>(out var stamp)) { source[snake] = DateTimeOffset.Parse(stamp, System.Globalization.CultureInfo.InvariantCulture).ToUnixTimeMilliseconds(); source.Remove(camel); }
        }
        foreach (var key in LocalFields) source.Remove(key);
        source.Remove("attachments");
        var result = new JsonObject { ["title"] = title, ["folder"] = folder, ["entry_type"] = type, ["payload"] = source };
        Validate(result);
        return result;
    }

    public static void Validate(JsonObject data)
    {
        var password = data["entry_type"]?.GetValue<string>() is "login" or "ssh-key";
        if (password) DevicePasswordShape.Validate(data);
        else if (System.Text.Encoding.UTF8.GetByteCount(data.ToJsonString()) > 1024 * 1024 ||
            data["entry_type"]?.GetValueKind() != JsonValueKind.String || !Supports(data["entry_type"]!.GetValue<string>()) ||
            data["title"]?.GetValueKind() != JsonValueKind.String || data["title"]!.GetValue<string>().Length > 4096 ||
            data["folder"]?.GetValueKind() != JsonValueKind.String || data["folder"]!.GetValue<string>().Length > 4096 ||
            data["payload"] is not JsonObject)
            throw new InvalidDataException("invalid_vault_payload");
        if (!password)
        {
            var payload = data["payload"]!.AsObject();
            var type = data["entry_type"]!.GetValue<string>();
            if (payload.Count == 0) throw new InvalidDataException("invalid_vault_payload");
            if (type is "note" or "totp" or "card" or "document-ref" or "billing-address" or "payment-account") Text(payload, "item_data");
            if (type == "passkey")
            {
                foreach (var field in new[] { "credential_id", "rp_id", "user_id", "public_key", "private_key_alias", "key_status" }) Text(payload, field);
                if (string.IsNullOrWhiteSpace(payload["credential_id"]!.GetValue<string>()) || string.IsNullOrWhiteSpace(payload["rp_id"]!.GetValue<string>()) ||
                    payload["public_key"]!.GetValue<string>().Length == 0 || payload["public_key_algorithm"] is not JsonValue algorithm || !algorithm.TryGetValue<int>(out _) ||
                    payload["key_status"]!.GetValue<string>() is not ("portable" or "reference")) throw new InvalidDataException("invalid_passkey_material");
                var eligibility = BackupFlag(payload, "backup_eligible");
                var state = BackupFlag(payload, "backup_state");
                if (eligibility == false && state == true) throw new InvalidDataException("invalid_passkey_material");
            }
            if (type is "folder" or "vault-metadata")
            {
                if (type == "folder") ValidatePath(payload["path"]);
                else if (payload["tags"] is not JsonArray) throw new InvalidDataException("invalid_vault_payload");
                foreach (var tag in Objects(payload, "tags"))
                {
                    Text(tag, "name"); Text(tag, "payload");
                    if (!uint.TryParse(tag["version"]?.ToJsonString(), out _)) throw new InvalidDataException("invalid_vault_payload");
                }
            }
        }
        if (data.ContainsKey("folder_path")) ValidatePath(data["folder_path"], allowEmpty: true);
        foreach (var asset in Objects(data, "assets"))
        {
            Text(asset, "id"); Text(asset, "file_name"); Text(asset, "media_type");
            var id = asset["id"]!.GetValue<string>();
            if (id.Length != 64 || !id.All(Uri.IsHexDigit) || asset["size"] is not JsonValue size || !size.TryGetValue<long>(out var bytes) ||
                bytes < 0 || bytes > 64L * 1024 * 1024) throw new InvalidDataException("invalid_asset");
        }
        foreach (var name in new[] { "labels", "relations" }) foreach (var item in Objects(data, name))
        {
            Text(item, name == "labels" ? "name" : "target"); Text(item, "payload");
            if (name == "relations") Text(item, "kind");
            if (!uint.TryParse(item["version"]?.ToJsonString(), out _)) throw new InvalidDataException("invalid_vault_payload");
        }
        foreach (var item in Objects(data, "password_history")) Text(item, "password");
    }

    private static void Text(JsonObject item, string name)
    {
        if (item[name]?.GetValueKind() != JsonValueKind.String) throw new InvalidDataException("invalid_vault_payload");
    }
    private static IEnumerable<JsonObject> Objects(JsonObject data, string name)
    {
        if (!data.ContainsKey(name)) yield break;
        if (data[name] is not JsonArray list || list.Count > 8192) throw new InvalidDataException("invalid_vault_payload");
        foreach (var node in list) yield return node as JsonObject ?? throw new InvalidDataException("invalid_vault_payload");
    }
    private static void ValidatePath(JsonNode? node, bool allowEmpty = false)
    {
        if (node is not JsonArray path || path.Count > 128 || (!allowEmpty && path.Count == 0)) throw new InvalidDataException("invalid_folder");
        var seen = new HashSet<string>();
        foreach (var item in path)
        {
            if (item is not JsonObject folder) throw new InvalidDataException("invalid_folder");
            Text(folder, "id"); Text(folder, "title");
            var id = folder["id"]!.GetValue<string>();
            if (!Guid.TryParse(id, out _) || !seen.Add(id) || folder["title"]!.GetValue<string>().Length > 4096 ||
                (folder["parent"] is { } parent && (!Guid.TryParse(parent.GetValue<string>(), out _) || parent.GetValue<string>() == id)))
                throw new InvalidDataException("invalid_folder");
        }
    }

    public static void RemapAssetPath(JsonObject data, string from, string to)
    {
        var note = data["entry_type"]?.GetValue<string>() == "note" || data["kind"]?.GetValue<string>() == "note";
        void Visit(JsonNode node, string? key = null)
        {
            if (node is JsonObject map) foreach (var name in map.Select(p => p.Key).ToArray())
            {
                if (map[name] is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    if (name is "image_paths" or "imagePaths" or "photoPaths")
                    {
                        if (text == from) map[name] = to;
                        else if (text.StartsWith('[')) { var paths = JsonNode.Parse(text)!; Visit(paths, name); map[name] = paths.ToJsonString(); }
                    }
                    else if (name is "storagePath" or "storage_path" or "localPath" or "local_path" or "imagePath" or "custom_icon_value" or "customIconValue") { if (text == from) map[name] = to; }
                    else if (name == "item_data" && text.StartsWith('{')) { var payload = JsonNode.Parse(text)!; Visit(payload); map[name] = payload.ToJsonString(); }
                    else if (note && name == "content") map[name] = System.Text.RegularExpressions.Regex.Replace(text,
                        @"(!\[[^\]]*\]\()([^\s)]+)(\))", match => match.Groups[2].Value == from ? match.Groups[1].Value + to + match.Groups[3].Value : match.Value);
                }
                else if (map[name] is { } child) Visit(child, name);
            }
            else if (node is JsonArray list) for (var i = 0; i < list.Count; i++)
            {
                if (key is "image_paths" or "imagePaths" or "photoPaths" && list[i] is JsonValue value && value.TryGetValue<string>(out var path)) { if (path == from) list[i] = to; }
                else if (list[i] is { } child) Visit(child, key);
            }
        }
        Visit(data);
    }

    public static IReadOnlyList<string> ReferenceIds(JsonObject data)
    {
        var result = new HashSet<string>();
        RemapReferences(data, id => { result.Add(id); return id; });
        return result.ToArray();
    }
    public static void RemapReferences(JsonObject data, Func<string, string> map)
    {
        void Visit(JsonNode node, string? key = null)
        {
            if (node is JsonObject item) foreach (var name in item.Select(p => p.Key).ToArray())
            {
                if ((name is "bound_password_entry_id" or "bound_note_entry_id" or "sso_ref_entry_id" ||
                    (key is "folder_path" or "path" && name is "id" or "parent") || (key == "relations" && name == "target")) &&
                    item[name] is JsonValue value && value.TryGetValue<string>(out var id) && id.Length > 0) item[name] = map(id);
                else if (item[name] is { } child) Visit(child, name);
            }
            else if (node is JsonArray list) foreach (var child in list) if (child is not null) Visit(child, key);
        }
        Visit(data);
    }

    public static JsonObject Apply(JsonObject data, string? existing, string id, string? folder)
    {
        Validate(data);
        if (data["entry_type"]!.GetValue<string>() is "login" or "ssh-key") return DevicePasswordShape.Apply(data, existing, id, folder);
        var payload = data["payload"]!.DeepClone().AsObject();
        if (data["entry_type"]!.GetValue<string>() == "passkey" && existing is not null)
            PreservePasskeyKey(payload, JsonNode.Parse(existing)!.AsObject());
        foreach (var key in LocalFields) payload.Remove(key);
        payload["monica_entry_id"] = id;
        payload["room_id"] = MdbxStableId.FromString(data["entry_type"]!.GetValue<string>(), id);
        payload["mdbx_folder_id"] = folder;
        return payload;
    }

    private static void PreservePasskeyKey(JsonObject incoming, JsonObject previous)
    {
        string Text(JsonObject value, string field) => value[field]?.GetValue<string>() ?? "";
        var oldKey = Text(previous, "private_key_alias");
        var oldPublic = Text(previous, "public_key");
        if (oldKey.Length == 0 && oldPublic.Length == 0) return;
        var algorithm = incoming["public_key_algorithm"]!.GetValue<int>();
        if (previous["public_key_algorithm"] is JsonValue number && number.TryGetValue<int>(out var oldAlgorithm) && oldAlgorithm != algorithm)
            throw new InvalidDataException("passkey_conflict");
        var credential = Text(previous, "credential_id");
        var rp = Text(previous, "rp_id");
        if ((credential.Length > 0 && PasskeyCredentialId.Normalize(credential) != PasskeyCredentialId.Normalize(Text(incoming, "credential_id"))) ||
            (rp.Length > 0 && !rp.Equals(Text(incoming, "rp_id"), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("passkey_conflict");
        try
        {
            byte[] Canonical(string encoded)
            {
                var bytes = Convert.FromBase64String(encoded);
                if (PasskeyKeyMaterialGenerator.TryParseCosePublicKey(bytes, out var cose, out var consumed) && consumed == bytes.Length)
                {
                    if (cose!.Algorithm != algorithm) throw new InvalidDataException("passkey_conflict");
                    encoded = cose.ToSubjectPublicKeyInfoBase64();
                }
                return PasskeyKeyMaterialGenerator.BuildCosePublicKey(algorithm, encoded);
            }
            if (oldPublic.Length > 0)
            {
                if (!CryptographicOperations.FixedTimeEquals(Canonical(oldPublic), Canonical(Text(incoming, "public_key"))))
                    throw new InvalidDataException("passkey_conflict");
            }
            else
            {
                var publicBytes = Canonical(Text(incoming, "public_key"));
                if (!PasskeyKeyMaterialGenerator.TryParseCosePublicKey(publicBytes, out var parsed, out _))
                    throw new InvalidDataException("passkey_conflict");
                var challenge = RandomNumberGenerator.GetBytes(32);
                var signature = PasskeyKeyMaterialGenerator.Sign(algorithm, oldKey, challenge);
                if (!PasskeyKeyMaterialGenerator.Verify(algorithm, parsed!.ToSubjectPublicKeyInfoBase64(), challenge, signature))
                    throw new InvalidDataException("passkey_conflict");
            }
        }
        catch (Exception error) when (error is CryptographicException or FormatException or ArgumentException or NotSupportedException)
        {
            throw new InvalidDataException("passkey_conflict");
        }
        PreserveBackupFlags(incoming, previous);
        // A metadata-only reference cannot erase the local private key or claim a new backup.
        if (Text(incoming, "private_key_alias").Length == 0 && oldKey.Length > 0)
            incoming["private_key_alias"] = oldKey;
    }

    private static void PreserveBackupFlags(JsonObject incoming, JsonObject previous)
    {
        var oldEligibility = BackupFlag(previous, "backup_eligible");
        var eligibility = BackupFlag(incoming, "backup_eligible");
        if (eligibility is not null && eligibility != (oldEligibility ?? true)) throw new InvalidDataException("passkey_conflict");
        eligibility ??= oldEligibility;
        var state = BackupFlag(incoming, "backup_state") ?? BackupFlag(previous, "backup_state");
        if (eligibility == false && state == true) throw new InvalidDataException("invalid_passkey_material");
        if (eligibility is not null) incoming["backup_eligible"] = eligibility.Value;
        if (state is not null) incoming["backup_state"] = state.Value;
    }

    private static bool? BackupFlag(JsonObject value, string name)
    {
        if (value[name] is null) return null;
        if (value[name] is JsonValue flag && flag.TryGetValue<bool>(out var result)) return result;
        throw new InvalidDataException("invalid_passkey_material");
    }
}
