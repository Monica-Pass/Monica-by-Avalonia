using System.Text.Json;
using System.Text.Json.Nodes;

namespace Monica.Data.Mdbx;

/// <summary>Portable login content. Device-local IDs, linked accounts and hardware keys stay local.</summary>
public static class DevicePasswordShape
{
    public static readonly string[] StringFields = ["website", "username", "password_plain", "notes", "app_package_name", "app_name",
        "authenticator_key", "ssh_key_data", "wifi_metadata", "email", "phone", "address_line", "city", "state", "zip_code",
        "country", "credit_card_number_plain", "credit_card_holder", "credit_card_expiry", "credit_card_cvv_plain", "sso_provider"];

    public static JsonObject Normalize(string payload, string title, string folder, string type = "login")
    {
        var source = JsonNode.Parse(payload)?.AsObject() ?? throw new InvalidDataException("invalid_password_payload");
        var result = new JsonObject { ["title"] = title, ["folder"] = folder, ["entry_type"] = type };
        foreach (var field in StringFields)
        {
            var words = field.Split('_');
            var camel = words[0] + string.Concat(words.Skip(1).Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
            var value = source[field] ?? source[camel];
            if (value is null && field.EndsWith("_plain", StringComparison.Ordinal))
                value = source[field[..^6]] ?? source[camel[..^5]];
            if (value is null && field == "credit_card_cvv_plain") value = source["creditCardCVV"];
            result[field] = value switch
            {
                null => "",
                JsonObject wifi when field == "wifi_metadata" => wifi.ToJsonString(),
                _ when value.GetValueKind() == JsonValueKind.String => value.GetValue<string>(),
                _ => throw new InvalidDataException("invalid_password_payload")
            };
        }
        result["login_type"] = source["login_type"]?.GetValue<string>() ?? "PASSWORD";
        result["is_archived"] = source["is_archived"]?.GetValue<bool>() ?? false;
        result["archived_at"] = result["is_archived"]!.GetValue<bool>() ? source["archived_at"]?.DeepClone() : null;
        var fields = source["custom_fields"] as JsonArray ?? source["customFields"] as JsonArray ?? [];
        result["custom_fields"] = new JsonArray(fields.OrderBy(field => (field?["sort_order"] ?? field?["sortOrder"])?.GetValue<int>() ?? 0)
            .Select((field, index) => (JsonNode)new JsonObject
            {
                ["title"] = field?["title"]?.GetValue<string>() ?? throw new InvalidDataException("invalid_password_payload"),
                ["value"] = field["value"]?.GetValue<string>() ?? "",
                ["is_protected"] = (field["is_protected"] ?? field["isProtected"])?.GetValue<bool>() ?? false,
                ["sort_order"] = index
            }).ToArray());
        var extensions = source.DeepClone().AsObject();
        foreach (var key in DeviceVaultShape.LocalFields.Concat(StringFields).Concat(new[] { "custom_fields", "customFields", "password", "passwordPlain", "password_history", "attachments" })) extensions.Remove(key);
        result["extensions"] = extensions;
        if (source["password_history"] is { } history) result["password_history"] = history.DeepClone();
        Validate(result);
        return result;
    }

    public static void Validate(JsonObject data)
    {
        static void Invalid() => throw new InvalidDataException("invalid_password_payload");
        if (System.Text.Encoding.UTF8.GetByteCount(data.ToJsonString()) > 1024 * 1024) Invalid();
        foreach (var field in StringFields.Concat(new[] { "title", "folder", "entry_type", "login_type" }))
            if (data[field]?.GetValueKind() != JsonValueKind.String) Invalid();
        if (data["entry_type"]!.GetValue<string>() is not ("login" or "ssh-key") || data["title"]!.GetValue<string>().Length > 4096 || data["folder"]!.GetValue<string>().Length > 4096) Invalid();
        if (data["is_archived"]?.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False)) Invalid();
        if (data["archived_at"] is { } timestamp && (timestamp is not JsonValue number || !number.TryGetValue<long>(out var millis) || millis is < 0 or > 253402300799999)) Invalid();
        if (data["custom_fields"] is not JsonArray fields || fields.Count > 1024) throw new InvalidDataException("invalid_password_payload");
        foreach (var field in fields)
            if (field is not JsonObject item || item["title"]?.GetValueKind() != JsonValueKind.String || item["value"]?.GetValueKind() != JsonValueKind.String ||
                item["is_protected"]?.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False)) Invalid();
    }

    public static JsonObject Apply(JsonObject data, string? existing, string id, string? folder)
    {
        Validate(data);
        var payload = existing is null ? new JsonObject() : JsonNode.Parse(existing)!.AsObject();
        if (data["extensions"] is JsonObject extensions)
            foreach (var pair in extensions) if (!DeviceVaultShape.LocalFields.Contains(pair.Key)) payload[pair.Key] = pair.Value?.DeepClone();
        if (data["password_history"] is { } history) payload["password_history"] = history.DeepClone();
        foreach (var field in StringFields)
        {
            payload[field] = data[field]?.DeepClone() ?? JsonValue.Create("");
            var words = field.Split('_');
            var camel = words[0] + string.Concat(words.Skip(1).Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
            if (camel != field) payload.Remove(camel);
            if (field.EndsWith("_plain", StringComparison.Ordinal)) { payload.Remove(field[..^6]); payload.Remove(camel[..^5]); }
        }
        foreach (var alias in new[] { "customFields", "loginType", "isArchived", "archivedAt", "creditCardCVV" }) payload.Remove(alias);
        foreach (var field in new[] { "login_type", "is_archived", "archived_at", "custom_fields" }) payload[field] = data[field]?.DeepClone();
        payload["kind"] = "password";
        payload["monica_password_encoding"] = "plaintext-v1";
        payload["monica_entry_id"] = id;
        payload["mdbx_folder_id"] = folder;
        payload["room_id"] ??= JsonValue.Create(MdbxStableId.FromString("password", id));
        return payload;
    }
}

public sealed record DeviceVaultAsset(string Id, string NativeId, long DatabaseId, long SizeBytes, Monica.Core.Models.Attachment? Legacy = null);
public sealed record DevicePasswordSnapshot(string LocalId, string GlobalId, string Scope, string Guard, bool Deleted, JsonObject Data, IReadOnlyList<DeviceVaultAsset>? Assets = null, bool KeyProjectionPending = false)
{
    public override string ToString() => $"DevicePasswordSnapshot({LocalId})";
}

public interface IMdbxDevicePasswordStore
{
    Task<IReadOnlyList<DevicePasswordSnapshot>> ReadDevicePasswordsAsync(Monica.Core.Models.LocalMdbxDatabase database, CancellationToken token);
    Task<DevicePasswordSnapshot?> TryApplyDevicePasswordAsync(Monica.Core.Models.LocalMdbxDatabase database, string globalId,
        string? localId, string? expectedGuard, bool deleted, JsonObject data, CancellationToken token,
        Func<JsonObject, CancellationToken, Task<byte[]>>? assetReader = null);
}

public interface IMdbxNativeDevicePasswordAccess
{
    Task<System.Text.Json.Nodes.JsonArray> ReadDeviceSyncLabelsAsync(string id, CancellationToken token);
    Task<JsonArray> ReadDeviceSyncProjectTagsAsync(string id, CancellationToken token);
    Task<JsonArray> ReadDeviceSyncRelationsAsync(string id, CancellationToken token);
    Task ResolveDeviceSyncRelationsAsync(CancellationToken token, IReadOnlyDictionary<string, string>? targets = null);
    Task<IReadOnlyList<DevicePasswordSnapshot>> ReadDeviceSyncRootMetadataAsync(CancellationToken token);
    Task<string?> ApplyDeviceSyncRootMetadataAsync(string owner, string? expected, bool newBinding, bool deleted, JsonObject data, CancellationToken token);
    Task<string> GetDeviceSyncGuardAsync(string id, CancellationToken token);
    Task<MdbxNativeObjectSummary?> CommitDeviceSyncObjectAsync(DeviceVaultWritePlan plan, CancellationToken token);
    Task<string?> ApplyDeviceSyncFolderAsync(string id, string? expectedGuard, bool newBinding, bool deleted, JsonObject data, CancellationToken token);
    Task EnsureDeviceSyncFoldersAsync(JsonArray path, CancellationToken token);
    Task<MdbxNativeEntryRecord?> ReadDeviceSyncObjectAsync(MdbxNativeObjectSummary summary, CancellationToken token);
    Task<MdbxNativeObjectSummary?> GetDeviceSyncSummaryAsync(string id, CancellationToken token);
    Task<IReadOnlyList<MdbxNativeProjectRecord>> ListDeviceSyncProjectsAsync(CancellationToken token);
    Task<MdbxNativeEntryRecord> CreateDeviceSyncEntryAsync(string id, string project, string type, string title, string payload, CancellationToken token);
}

public sealed record DeviceVaultAttachmentWrite(string Id, string FileName, string? MediaType, byte[] Content);
public sealed record DeviceVaultWritePlan(string Id, string ProjectId, string Type, string Title, string Payload,
    MdbxNativeObjectSummary? Previous, bool Deleted, IReadOnlyList<DeviceVaultAttachmentWrite> Attachments,
    IReadOnlyList<string> RemovedAttachments, JsonArray? Labels = null, string? ExpectedGuard = null);
