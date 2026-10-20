using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Monica.Data.Mdbx;
using Monica.Mdbx.Ffi;

namespace Monica.Platform.Services;

public sealed partial class MdbxUniffiNativeBridge
{
    private sealed partial class MdbxUniffiNativeVault
    {
        private const string RootMetadataKind = "monica-device-root-tags-v2";
        private (MdbxObjectLabelRecord Label, JsonObject Content)[] RootMetadata(string root) =>
            vault.GetCollectionSummary(root) is null ? [] : vault.ListObjectLabels(root).Where(l => !l.Deleted)
                .Select(l => (Label: l, Content: JsonNode.Parse(l.PayloadJson) as JsonObject))
                .Where(l => l.Content?["kind"]?.GetValue<string>() == RootMetadataKind).Select(l => (l.Label, l.Content!)).ToArray();

        private MdbxObjectLabelRecord[] RootTags(string root, string owner, (MdbxObjectLabelRecord Label, JsonObject Content)[] metadata)
        {
            if (vault.GetCollectionSummary(root) is null) return [];
            var ids = metadata.Where(m => owner == root ? m.Content["owner"]!.GetValue<string>() != root : m.Content["owner"]!.GetValue<string>() == owner)
                .SelectMany(m => m.Content["tag_ids"]!.AsArray().Select(n => n!.GetValue<string>())).ToHashSet();
            return ProjectTagRecords(root).Where(t => owner == root ? !ids.Contains(t.LabelId) : ids.Contains(t.LabelId)).ToArray();
        }
        private string RootMetadataGuard(string root, string owner, (MdbxObjectLabelRecord Label, JsonObject Content)[] metadata)
        {
            var marker = metadata.FirstOrDefault(m => m.Content["owner"]!.GetValue<string>() == owner);
            if (owner != root && marker.Label is null) return "";
            return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                marker = marker.Label?.PayloadJson,
                tags = RootTags(root, owner, metadata).OrderBy(t => t.LabelId).Select(t => new { t.LabelId, t.Name, t.PayloadJson, t.PayloadSchemaVersion })
            })));
        }
        public Task<IReadOnlyList<DevicePasswordSnapshot>> ReadDeviceSyncRootMetadataAsync(CancellationToken token) =>
            RunBlockingNativeAsync<IReadOnlyList<DevicePasswordSnapshot>>(() =>
            {
                var root = MdbxAndroidRoot.ProjectIdFor(vault.Info().VaultId);
                if (vault.GetCollectionSummary(root) is null) return [];
                var metadata = RootMetadata(root);
                var owners = metadata.Select(m => m.Content["owner"]!.GetValue<string>()).Append(root).Distinct();
                return owners.Select(owner =>
                {
                    var marker = metadata.FirstOrDefault(m => m.Content["owner"]!.GetValue<string>() == owner);
                    var tags = RootTags(root, owner, metadata);
                    var deleted = marker.Content?["deleted"]?.GetValue<bool>() == true && tags.Length == 0;
                    var data = deleted ? new JsonObject() : marker.Content?["data"] is JsonObject saved && saved["payload"] is JsonObject ? saved.DeepClone().AsObject() : new JsonObject
                    {
                        ["title"] = "Vault metadata",
                        ["folder"] = "",
                        ["entry_type"] = "vault-metadata",
                        ["payload"] = new JsonObject()
                    };
                    if (!deleted) data["payload"]!["tags"] = new JsonArray(tags.OrderBy(t => t.Name, StringComparer.Ordinal).Select(t => (JsonNode)new JsonObject
                    {
                        ["name"] = t.Name,
                        ["payload"] = t.PayloadJson,
                        ["version"] = t.PayloadSchemaVersion
                    }).ToArray());
                    return new DevicePasswordSnapshot("metadata:" + owner, owner, "", RootMetadataGuard(root, owner, metadata), deleted, data);
                }).ToArray();
            }, token);

        public Task<string?> ApplyDeviceSyncRootMetadataAsync(string owner, string? expected, bool newBinding, bool deleted, JsonObject data, CancellationToken token) =>
            RunBlockingNativeAsync<string?>(() =>
            {
                var root = MdbxAndroidRoot.ProjectIdFor(vault.Info().VaultId); var metadata = RootMetadata(root);
                if (!newBinding && RootMetadataGuard(root, owner, metadata) != (expected ?? "")) return null;
                var tags = deleted ? new JsonArray() : data["payload"]!["tags"]!.AsArray();
                var existing = RootTags(root, owner, metadata); var keep = new List<string>(); var commands = new List<MdbxWriteCommand>();
                if (vault.GetCollectionSummary(root) is null) commands.Add(new MdbxWriteCommand.CreateProject(root, MdbxAndroidRoot.Title));
                foreach (var tag in tags.OfType<JsonObject>())
                {
                    var old = existing.FirstOrDefault(t => !keep.Contains(t.LabelId) && t.Name == tag["name"]!.GetValue<string>());
                    var id = old?.LabelId ?? Guid.NewGuid().ToString(); keep.Add(id);
                    var version = JsonSerializer.Deserialize<uint>(tag["version"]!.ToJsonString());
                    if (old is null) commands.Add(new MdbxWriteCommand.CreateObjectLabel(id, root, tag["name"]!.GetValue<string>(), tag["payload"]!.GetValue<string>(), version));
                    else if (old.PayloadJson != tag["payload"]!.GetValue<string>() || old.PayloadSchemaVersion != version)
                        commands.Add(new MdbxWriteCommand.UpdateObjectLabel(id, old.Name, tag["payload"]!.GetValue<string>(), version));
                }
                foreach (var tag in existing.Where(t => !keep.Contains(t.LabelId))) commands.Add(new MdbxWriteCommand.DeleteObjectLabel(tag.LabelId));
                var content = new JsonObject
                {
                    ["kind"] = RootMetadataKind,
                    ["owner"] = owner,
                    ["deleted"] = deleted,
                    ["data"] = data.DeepClone(),
                    ["tag_ids"] = new JsonArray(keep.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray())
                };
                var marker = metadata.FirstOrDefault(m => m.Content["owner"]!.GetValue<string>() == owner);
                if (marker.Label is null) commands.Add(new MdbxWriteCommand.CreateObjectLabel(Guid.NewGuid().ToString(), root, RootMetadataKind, content.ToJsonString(), 1));
                else if (marker.Label.PayloadJson != content.ToJsonString()) commands.Add(new MdbxWriteCommand.UpdateObjectLabel(marker.Label.LabelId, RootMetadataKind, content.ToJsonString(), 1));
                if (commands.Count > 0) vault.ExecuteWriteOperation(Guid.NewGuid().ToString(), "device-root-metadata", commands.ToArray());
                return RootMetadataGuard(root, owner, RootMetadata(root));
            }, token);
    }
}
