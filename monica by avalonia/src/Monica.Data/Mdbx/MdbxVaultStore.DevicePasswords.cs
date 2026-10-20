using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Monica.Core.Models;

namespace Monica.Data.Mdbx;

public sealed partial class MdbxVaultStore : IMdbxDevicePasswordStore
{
    public async Task<IReadOnlyList<DevicePasswordSnapshot>> ReadDevicePasswordsAsync(LocalMdbxDatabase database, CancellationToken token)
    {
        using var vault = await OpenAsync(database, token);
        var access = vault as IMdbxNativeDevicePasswordAccess ?? throw new NotSupportedException("device_sync_unavailable");
        var reader = (IMdbxNativeObjectReader)vault;
        var projects = await access.ListDeviceSyncProjectsAsync(token);
        var result = new List<DevicePasswordSnapshot>();
        result.AddRange((await access.ReadDeviceSyncRootMetadataAsync(token)).Select(item => item with { LocalId = $"mdbx:{database.Id}:" + item.LocalId, Scope = $"mdbx:{database.Id}" }));
        foreach (var project in projects)
        {
            var path = FolderPath(project, projects);
            if (path.Count > 0)
                result.Add(new($"mdbx:{database.Id}:folder:" + project.ProjectId, project.ProjectId, $"mdbx:{database.Id}", project.Guard, project.Deleted,
                    new JsonObject
                    {
                        ["title"] = project.Title,
                        ["folder"] = "",
                        ["entry_type"] = "folder",
                        ["trashed"] = project.Deleted,
                        ["payload"] = new JsonObject { ["path"] = path.DeepClone(), ["tags"] = await access.ReadDeviceSyncProjectTagsAsync(project.ProjectId, token) }
                    }));
            foreach (var summary in await reader.ListObjectSummariesAsync(project.ProjectId, true, token))
            {
                if (!DeviceVaultShape.Supports(summary.EntryType) || summary.PayloadSchemaVersion != 1)
                    throw new InvalidDataException("unsupported_vault_content");
                var entry = await access.ReadDeviceSyncObjectAsync(summary, token) ?? throw new InvalidOperationException("password_changed");
                if (JsonNode.Parse(entry.PayloadJson)?["device_sync_staging"]?.GetValue<bool>() == true) continue;
                var payload = entry.EntryType is "login" or "ssh-key" ? DevicePasswordPayload(entry.PayloadJson, entry.Title) : entry.PayloadJson;
                var source = JsonNode.Parse(payload)!.AsObject();
                if (source["bitwarden_mode"]?.GetValue<bool>() == true || source["keepass_mode"]?.GetValue<bool>() == true) continue;
                var purged = entry.Deleted && source["deleted_at"] is JsonValue deletedAt && deletedAt.TryGetValue<long>(out var deletedMillis) && deletedMillis == 0;
                var data = purged ? entry.EntryType == "passkey" ? new JsonObject
                {
                    ["entry_type"] = "passkey",
                    ["payload"] = new JsonObject
                    {
                        ["credential_id"] = source["credential_id"]?.DeepClone(),
                        ["rp_id"] = source["rp_id"]?.DeepClone()
                    }
                } : new JsonObject() : DeviceVaultShape.Normalize(payload, entry.Title,
                    MdbxAndroidRoot.IsRootTitle(project.Title) ? "" : project.Title, entry.EntryType);
                var assets = new List<DeviceVaultAsset>();
                if (!purged) { data["folder_path"] = path.DeepClone(); data["trashed"] = entry.Deleted; }
                if (!purged)
                {
                    data["labels"] = await access.ReadDeviceSyncLabelsAsync(entry.EntryId, token);
                    data["relations"] = source["device_sync_pending_relations"]?.DeepClone() ?? await access.ReadDeviceSyncRelationsAsync(entry.EntryId, token);
                    var descriptors = new JsonArray();
                    var savedAssets = source["device_sync_assets"] as JsonArray;
                    foreach (var attachment in (await vault.ListAttachmentsAsync(project.ProjectId, entry.EntryId, token)).Where(a => !a.Deleted).OrderBy(a => a.ContentHash).ThenBy(a => a.FileName))
                    {
                        var hash = attachment.ContentHash.ToLowerInvariant();
                        var saved = savedAssets?.OfType<JsonObject>().FirstOrDefault(d => d["local_id"]?.GetValue<string>() == attachment.AttachmentId);
                        var descriptor = new JsonObject
                        {
                            ["id"] = hash,
                            ["attachment_id"] = saved?["attachment_id"]?.DeepClone() ?? JsonValue.Create(attachment.AttachmentId),
                            ["file_name"] = attachment.FileName,
                            ["media_type"] = attachment.MediaType ?? "application/octet-stream",
                            ["size"] = checked((long)attachment.OriginalSize)
                        };
                        if (saved?["image"] is { } image) descriptor["image"] = image.DeepClone();
                        if (saved?["icon"] is { } icon) descriptor["icon"] = icon.DeepClone();
                        descriptors.Add(descriptor);
                        DeviceVaultShape.RemapAssetPath(data, $"mdbx:{attachment.AttachmentId}", $"device-asset:{hash}");
                        assets.Add(new(hash, attachment.AttachmentId, database.Id, checked((long)attachment.OriginalSize)));
                    }
                    data["assets"] = descriptors;
                    if (source["attachments"] is JsonArray legacyAttachments)
                    {
                        foreach (var attachment in legacyAttachments.OfType<JsonObject>())
                        {
                            var legacy = System.Text.Json.JsonSerializer.Deserialize<Attachment>(attachment.ToJsonString(), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            if (legacy is null || string.IsNullOrEmpty(legacy.StoragePath) || legacy.StoragePath.StartsWith("mdbx:", StringComparison.Ordinal)) continue;
                            assets.Add(new("", "", database.Id, legacy.SizeBytes, legacy));
                        }
                    }
                }
                result.Add(new($"mdbx:{database.Id}:" + entry.EntryId, entry.EntryId, $"mdbx:{database.Id}", await access.GetDeviceSyncGuardAsync(entry.EntryId, token), entry.Deleted, data, assets,
                    source["device_sync_key_projection"]?.GetValue<bool>() == true));
            }
        }
        return result;
    }

    public async Task<DevicePasswordSnapshot?> TryApplyDevicePasswordAsync(LocalMdbxDatabase database, string globalId,
        string? localId, string? expectedGuard, bool deleted, JsonObject data, CancellationToken token,
        Func<JsonObject, CancellationToken, Task<byte[]>>? assetReader = null)
    {
        var retainedTrash = deleted && data["trashed"]?.GetValue<bool>() == true;
        if (!deleted || retainedTrash) DeviceVaultShape.Validate(data);
        if (localId?.StartsWith($"mdbx:{database.Id}:", StringComparison.Ordinal) == true) localId = localId[($"mdbx:{database.Id}:").Length..];
        using var vault = await OpenAsync(database, token);
        if (vault.IsReadOnly) throw new InvalidOperationException("device_sync_read_only");
        var reader = (IMdbxNativeObjectReader)vault;
        var access = (IMdbxNativeDevicePasswordAccess)vault;
        if (localId?.StartsWith("metadata:", StringComparison.Ordinal) == true || ((!deleted || retainedTrash) && data["entry_type"]?.GetValue<string>() == "vault-metadata"))
        {
            var owner = localId?.Substring(9) ?? globalId;
            var guard = await access.ApplyDeviceSyncRootMetadataAsync(owner, expectedGuard, localId is null, deleted, data, token);
            return guard is null ? null : new($"mdbx:{database.Id}:metadata:" + owner, globalId, $"mdbx:{database.Id}", guard, deleted, data);
        }
        if (localId?.StartsWith("folder:", StringComparison.Ordinal) == true || ((!deleted || retainedTrash) && data["entry_type"]?.GetValue<string>() == "folder"))
        {
            var folderId = localId?.Substring(7) ?? globalId;
            var guard = await access.ApplyDeviceSyncFolderAsync(folderId, expectedGuard, localId is null, deleted, data, token);
            return guard is null ? null : new($"mdbx:{database.Id}:folder:" + folderId, globalId, $"mdbx:{database.Id}", guard, deleted, data);
        }
        var projects = await access.ListDeviceSyncProjectsAsync(token);
        var id = localId ?? globalId;
        if (!Guid.TryParse(id, out _)) throw new InvalidDataException("invalid_password_identity");
        var current = await access.GetDeviceSyncSummaryAsync(id, token);
        var candidate = current is null ? null : await access.ReadDeviceSyncObjectAsync(current, token);
        var staging = candidate is not null && JsonNode.Parse(candidate.PayloadJson)?["device_sync_staging"]?.GetValue<bool>() == true &&
            JsonNode.Parse(candidate.PayloadJson)?["device_sync_record_id"]?.GetValue<string>() == id;
        if (current is not null && !DeviceVaultShape.Supports(current.EntryType)) throw new InvalidDataException("password_identity_collision");
        if (current is null && deleted && !retainedTrash) return new(id, globalId, $"mdbx:{database.Id}", "", true, data);
        var currentGuard = await access.GetDeviceSyncGuardAsync(id, token);
        if (localId is not null && (current is null ? !string.IsNullOrEmpty(expectedGuard) : currentGuard != expectedGuard)) return null;
        if (localId is null && current is not null && !staging) return null; // Reconcile a prior insertion before retrying.
        var existing = candidate;
        if (current is not null && existing is null) return null;
        var existingPayload = existing is null || staging ? null : existing.EntryType is "login" or "ssh-key" ? DevicePasswordPayload(existing.PayloadJson, existing.Title) : existing.PayloadJson;
        if (existingPayload is not null)
        {
            var source = JsonNode.Parse(existingPayload)!.AsObject();
            if (source["bitwarden_mode"]?.GetValue<bool>() == true || source["keepass_mode"]?.GetValue<bool>() == true)
                throw new InvalidOperationException("password_source_unavailable");
        }
        if (deleted && !retainedTrash)
        {
            var empty = new JsonObject
            {
                ["kind"] = current!.EntryType is "login" or "ssh-key" ? "password" : current.EntryType,
                ["password_plain"] = "",
                ["item_data"] = "",
                ["deleted_at"] = 0,
                ["monica_entry_id"] = id
            };
            if (current.EntryType == "passkey" && existingPayload is not null)
            {
                var prior = JsonNode.Parse(existingPayload)!.AsObject();
                empty["credential_id"] = prior["credential_id"]?.DeepClone(); empty["rp_id"] = prior["rp_id"]?.DeepClone();
                empty["device_sync_key_projection"] = true;
            }
            var attachments = (await vault.ListAttachmentsAsync(current.ProjectId, id, token)).Where(a => !a.Deleted).Select(a => a.AttachmentId).ToArray();
            var afterDelete = await access.CommitDeviceSyncObjectAsync(new(id, current.ProjectId, current.EntryType, current.Title, empty.ToJsonString(), current, true, [], attachments, [], currentGuard), token);
            return afterDelete is null ? null : new($"mdbx:{database.Id}:" + id, globalId, $"mdbx:{database.Id}", await access.GetDeviceSyncGuardAsync(id, token), true, new JsonObject());
        }
        var folder = data["folder"]?.GetValue<string>() ?? "";
        var root = MdbxAndroidRoot.ProjectIdFor((await vault.GetInfoAsync(token)).VaultId);
        var projectId = root;
        if (data["folder_path"] is JsonArray path && path.Count > 0)
        {
            await access.EnsureDeviceSyncFoldersAsync(path, token);
            projectId = path[^1]!["id"]!.GetValue<string>();
        }
        if (!(await access.ListDeviceSyncProjectsAsync(token)).Any(item => item.ProjectId == root)) await vault.CreateProjectWithIdentityAsync(root, MdbxAndroidRoot.Title, null, token);
        if (folder.Length > 0 && projectId == root)
        {
            var target = projects.FirstOrDefault(item => item.Title == folder);
            if (target is null)
            {
                var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"device-folder:{root}:{folder}"));
                projectId = new Guid(bytes.AsSpan(0, 16)).ToString();
                await vault.CreateProjectWithIdentityAsync(projectId, folder, root, token);
            }
            else projectId = target.ProjectId;
        }
        var title = data["title"]?.GetValue<string>() ?? "";
        var type = data["entry_type"]?.GetValue<string>() ?? "login";
        if (!DeviceVaultShape.Supports(type)) throw new InvalidDataException("invalid_password_type");
        var materialized = DeviceVaultShape.Apply(data, existingPayload, id, projectId == root ? null : projectId);
        if (type == "passkey") materialized["device_sync_key_projection"] = true;
        if (data["relations"] is JsonArray relations) materialized["device_sync_pending_relations"] = relations.DeepClone();
        var writes = new List<DeviceVaultAttachmentWrite>();
        var removedAttachments = new List<string>();
        try
        {
            if (data["assets"] is JsonArray descriptors)
            {
                var attached = current is null ? [] : (await vault.ListAttachmentsAsync(current.ProjectId, id, token)).Where(a => !a.Deleted).ToArray();
                var keep = new HashSet<string>(); var stored = new JsonArray();
                foreach (var descriptor in descriptors.OfType<JsonObject>())
                {
                    var hash = descriptor["id"]!.GetValue<string>(); var name = descriptor["file_name"]!.GetValue<string>();
                    var attachment = attached.FirstOrDefault(a => !keep.Contains(a.AttachmentId) && a.ContentHash.Equals(hash, StringComparison.OrdinalIgnoreCase) && a.FileName == name);
                    var localAssetId = attachment?.AttachmentId ?? Guid.NewGuid().ToString();
                    if (attachment is null)
                    {
                        var bytes = await (assetReader ?? throw new InvalidDataException("asset_unavailable"))(descriptor, token);
                        writes.Add(new(localAssetId, name, descriptor["media_type"]?.GetValue<string>(), bytes));
                    }
                    keep.Add(localAssetId);
                    var metadata = descriptor.DeepClone().AsObject(); metadata["local_id"] = localAssetId; stored.Add(metadata);
                    DeviceVaultShape.RemapAssetPath(materialized, $"device-asset:{hash}", ToAttachmentStoragePath(localAssetId));
                }
                materialized["device_sync_assets"] = stored;
                removedAttachments.AddRange(attached.Where(a => !keep.Contains(a.AttachmentId)).Select(a => a.AttachmentId));
            }
            token.ThrowIfCancellationRequested();
            var after = await access.CommitDeviceSyncObjectAsync(new(id, projectId, type, title, materialized.ToJsonString(), current, retainedTrash,
                writes, removedAttachments, data["labels"] as JsonArray, currentGuard), token);
            return after is null ? null : new($"mdbx:{database.Id}:" + id, globalId, $"mdbx:{database.Id}", await access.GetDeviceSyncGuardAsync(id, token), deleted, data);
        }
        finally { foreach (var write in writes) CryptographicOperations.ZeroMemory(write.Content); }
    }

    private static JsonArray FolderPath(MdbxNativeProjectRecord project, IReadOnlyList<MdbxNativeProjectRecord> projects)
    {
        var result = new List<JsonNode>(); var seen = new HashSet<string>(); var current = project;
        while (!MdbxAndroidRoot.IsRootTitle(current.Title))
        {
            if (!seen.Add(current.ProjectId) || result.Count >= 128) throw new InvalidDataException("invalid_folder");
            var parent = projects.FirstOrDefault(p => p.ProjectId == current.ParentProjectId);
            result.Add(new JsonObject
            {
                ["id"] = current.ProjectId,
                ["title"] = current.Title,
                ["parent"] = parent is null || MdbxAndroidRoot.IsRootTitle(parent.Title) ? null : current.ParentProjectId
            });
            if (parent is null) break; current = parent;
        }
        result.Reverse(); return new JsonArray(result.ToArray());
    }

    public async Task<byte[]> ReadDeviceSyncAssetAsync(LocalMdbxDatabase database, DeviceVaultAsset asset, CancellationToken token)
    {
        if (asset.SizeBytes > 64L * 1024 * 1024) throw new InvalidDataException("asset_limit");
        using var vault = await OpenAsync(database, token);
        var content = await vault.ReadAttachmentContentAsync(asset.NativeId, token);
        if (content.LongLength != asset.SizeBytes || !Convert.ToHexString(SHA256.HashData(content)).Equals(asset.Id, StringComparison.OrdinalIgnoreCase))
        { CryptographicOperations.ZeroMemory(content); throw new InvalidDataException("invalid_asset"); }
        return content;
    }

    public async Task<IReadOnlyList<string>> DeviceSyncRelationTargetsAsync(LocalMdbxDatabase database, CancellationToken token)
    {
        var snapshots = await ReadDevicePasswordsAsync(database, token);
        return snapshots.SelectMany(p => p.Data["relations"]?.AsArray().OfType<JsonObject>().Select(r => r["target"]!.GetValue<string>()) ?? []).Distinct().ToArray();
    }
    public async Task<bool> DeviceSyncObjectExistsAsync(LocalMdbxDatabase database, string id, CancellationToken token)
    {
        using var vault = await OpenAsync(database, token);
        return await ((IMdbxNativeDevicePasswordAccess)vault).GetDeviceSyncSummaryAsync(id, token) is not null;
    }
    public async Task<string> DeviceSyncGuardAsync(LocalMdbxDatabase database, string id, CancellationToken token)
    {
        using var vault = await OpenAsync(database, token);
        return await ((IMdbxNativeDevicePasswordAccess)vault).GetDeviceSyncGuardAsync(id, token);
    }
    public async Task DeleteDeviceSyncPasskeyProjectionAsync(LocalMdbxDatabase database, string id, CancellationToken token)
    {
        using var vault = await OpenAsync(database, token); var access = (IMdbxNativeDevicePasswordAccess)vault;
        var current = await access.GetDeviceSyncSummaryAsync(id, token);
        if (current is null || current.Deleted) return;
        if (current.EntryType != "passkey") throw new InvalidDataException("password_identity_collision");
        var record = await access.ReadDeviceSyncObjectAsync(current, token) ?? throw new InvalidDataException("passwords_changing");
        var old = JsonNode.Parse(record.PayloadJson)!.AsObject();
        var empty = new JsonObject { ["kind"] = "passkey", ["credential_id"] = old["credential_id"]?.DeepClone(), ["rp_id"] = old["rp_id"]?.DeepClone(), ["deleted_at"] = 0 };
        var removed = (await vault.ListAttachmentsAsync(current.ProjectId, id, token)).Where(a => !a.Deleted).Select(a => a.AttachmentId).ToArray();
        if (await access.CommitDeviceSyncObjectAsync(new(id, current.ProjectId, "passkey", current.Title, empty.ToJsonString(), current, true, [], removed, [], await access.GetDeviceSyncGuardAsync(id, token)), token) is null)
            throw new InvalidDataException("passwords_changing");
    }
    public async Task CompleteDeviceSyncKeyProjectionAsync(LocalMdbxDatabase database, string id, CancellationToken token)
    {
        using var vault = await OpenAsync(database, token); var access = (IMdbxNativeDevicePasswordAccess)vault;
        var summary = await access.GetDeviceSyncSummaryAsync(id, token) ?? throw new InvalidDataException("passwords_changing");
        var record = await access.ReadDeviceSyncObjectAsync(summary, token) ?? throw new InvalidDataException("passwords_changing");
        var payload = JsonNode.Parse(record.PayloadJson)!.AsObject(); if (!payload.Remove("device_sync_key_projection")) return;
        var guard = await access.GetDeviceSyncGuardAsync(id, token);
        if (await access.CommitDeviceSyncObjectAsync(new(id, summary.ProjectId, "passkey", summary.Title, payload.ToJsonString(), summary, summary.Deleted, [], [], null, guard), token) is null)
            throw new InvalidDataException("passwords_changing");
    }
    public async Task ResolveDeviceSyncRelationsAsync(LocalMdbxDatabase database, CancellationToken token, IReadOnlyDictionary<string, string>? targets = null)
    {
        using var vault = await OpenAsync(database, token);
        await ((IMdbxNativeDevicePasswordAccess)vault).ResolveDeviceSyncRelationsAsync(token, targets);
    }

    private static string DevicePasswordPayload(string payload, string title)
    {
        var document = JsonNode.Parse(payload)!.AsObject();
        var wrapped = document["data"] is JsonObject content && (content["entry"] is JsonObject || content["id"] is not null);
        if (!wrapped && document["kind"]?.GetValue<string>() == "password")
        {
            if (!document.ContainsKey("password_plain") && !document.ContainsKey("passwordPlain") && !document.ContainsKey("password"))
                throw new InvalidDataException("invalid_password_payload");
            return payload;
        }
        if (!wrapped || document["data"] is not JsonObject original) throw new InvalidDataException("invalid_password_payload");
        var entry = original["entry"] as JsonObject ?? original;
        var password = entry["password_plain"] ?? entry["passwordPlain"] ?? entry["password"];
        if (password?.GetValueKind() != System.Text.Json.JsonValueKind.String) throw new InvalidDataException("invalid_password_payload");
        // Read the envelope directly: model deserialization can reject a newer extension
        // such as a Wi-Fi object and then mistake the entire envelope for an empty login.
        var flat = entry.DeepClone().AsObject();
        flat["kind"] = "password";
        flat["password_plain"] = password.DeepClone();
        flat["room_id"] = entry["id"]?.DeepClone();
        flat["is_archived"] = (entry["isArchived"] ?? entry["is_archived"])?.DeepClone() ?? JsonValue.Create(false);
        flat["bitwarden_mode"] = entry["bitwardenVaultId"] is not null;
        flat["keepass_mode"] = entry["keepassDatabaseId"] is not null;
        flat["custom_fields"] = (original["customFields"] ?? entry["customFields"] ?? entry["custom_fields"])?.DeepClone() ?? new JsonArray();
        var login = entry["loginType"] ?? entry["login_type"];
        flat["login_type"] = login is JsonValue number && number.TryGetValue<int>(out var kind)
            ? kind switch { 0 => "PASSWORD", 1 => "API_KEY", 2 => "SSO", 3 => "WIFI", 4 => "SSH_KEY", 5 => "BARCODE", _ => throw new InvalidDataException("invalid_password_payload") }
            : (login?.GetValue<string>() ?? "PASSWORD").ToUpperInvariant() switch { "APIKEY" => "API_KEY", "SSHKEY" => "SSH_KEY", var value => value };
        var archived = entry["archivedAt"] ?? entry["archived_at"];
        flat["archived_at"] = archived?.GetValueKind() == System.Text.Json.JsonValueKind.String
            ? JsonValue.Create(DateTimeOffset.Parse(archived.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture).ToUnixTimeMilliseconds())
            : archived?.DeepClone();
        flat["sort_order"] = entry["sortOrder"]?.DeepClone() ?? JsonValue.Create(0);
        flat["bound_note_room_id"] = entry["boundNoteId"]?.DeepClone();
        flat["passkey_bindings"] = entry["passkeyBindings"]?.DeepClone();
        if (original["passwordHistory"] is { } history) flat["password_history"] = history.DeepClone();
        if (original["attachments"] is { } attachments) flat["attachments"] = attachments.DeepClone();
        return flat.ToJsonString();
    }
}
