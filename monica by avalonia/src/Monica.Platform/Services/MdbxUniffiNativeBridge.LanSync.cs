using Monica.Data.Mdbx;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Monica.Mdbx.Ffi;

namespace Monica.Platform.Services;

public sealed partial class MdbxUniffiNativeBridge
{
    private sealed partial class MdbxUniffiNativeVault : IMdbxNativeLanSync, IMdbxNativeDevicePasswordAccess
    {
        public Task<JsonArray> ReadDeviceSyncRelationsAsync(string id, CancellationToken token) => RunBlockingNativeAsync(() =>
            new JsonArray(vault.ListObjectRelationsFrom(id, null).Where(r => !r.Deleted).Select(r => (JsonNode)new JsonObject
            {
                ["target"] = r.TargetObjectId,
                ["kind"] = r.RelationKind,
                ["payload"] = r.PayloadJson,
                ["version"] = r.PayloadSchemaVersion
            }).OrderBy(n => n.ToJsonString(), StringComparer.Ordinal).ToArray()), token);

        public Task ResolveDeviceSyncRelationsAsync(CancellationToken token, IReadOnlyDictionary<string, string>? targets = null) => RunBlockingNativeAsync(() =>
        {
            string? cursor = null;
            do
            {
                var collections = vault.ListCollectionSummaries(CollectionPageSize, cursor); cursor = collections.NextCursor;
                foreach (var collection in collections.Items)
                {
                    string? objectCursor = null;
                    do
                    {
                        var objects = vault.ListObjectSummaries(collection.CollectionId, null, 200, objectCursor); objectCursor = objects.NextCursor;
                        foreach (var item in objects.Items)
                        {
                            var record = vault.GetObject(item.CollectionId, item.ObjectId);
                            if (record is null) continue;
                            var payload = JsonNode.Parse(record.PayloadJson)!.AsObject();
                            if (payload["device_sync_pending_relations"] is not JsonArray desired) continue;
                            var commands = new List<MdbxWriteCommand>();
                            foreach (var relation in vault.ListObjectRelationsFrom(item.ObjectId, null).Where(r => !r.Deleted))
                                commands.Add(new MdbxWriteCommand.DeleteObjectRelation(relation.RelationId));
                            foreach (var relation in desired.OfType<JsonObject>())
                            {
                                var target = relation["target"]!.GetValue<string>();
                                target = targets?.GetValueOrDefault(target, target) ?? target;
                                if (vault.GetObjectSummary(target) is null) throw new InvalidDataException("reference_unavailable");
                                commands.Add(new MdbxWriteCommand.CreateObjectRelation(Guid.NewGuid().ToString(), item.ObjectId, target,
                                    relation["kind"]!.GetValue<string>(), relation["payload"]!.GetValue<string>(), JsonSerializer.Deserialize<uint>(relation["version"]!.ToJsonString())));
                            }
                            payload.Remove("device_sync_pending_relations");
                            if (item.Deleted) commands.Insert(0, new MdbxWriteCommand.RestoreEntry(item.ObjectId, item.CollectionId));
                            commands.Add(new MdbxWriteCommand.UpdateEntry(item.ObjectId, item.CollectionId, item.ObjectTypeId, item.Title, payload.ToJsonString()));
                            if (item.Deleted) commands.Add(new MdbxWriteCommand.DeleteEntry(item.ObjectId, item.CollectionId));
                            vault.ExecuteWriteOperation(Guid.NewGuid().ToString(), "device-vault-relations", commands.ToArray());
                        }
                    } while (objectCursor is not null);
                }
            } while (cursor is not null);
        }, token);
        public Task<string> GetDeviceSyncGuardAsync(string id, CancellationToken token) => RunBlockingNativeAsync(() => DeviceGuard(id), token);
        private string DeviceGuard(string id)
        {
            var current = vault.GetObjectSummary(id);
            if (current is null) return "";
            return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                current.HeadCommitId,
                attachments = vault.ListAttachments(current.CollectionId, id).OrderBy(a => a.AttachmentId).Select(a => new { a.AttachmentId, a.ContentHash, a.Deleted, a.FileName, a.MediaType }),
                labels = vault.ListObjectLabelAssignments(id).Where(a => !a.Deleted).OrderBy(a => a.AssignmentId).Select(a => new { a.AssignmentId, a.LabelId, Head = vault.GetObjectLabelSummary(a.LabelId)?.HeadCommitId }),
                relations = vault.ListObjectRelationsFrom(id, null).OrderBy(r => r.RelationId)
            })));
        }
        public Task<JsonArray> ReadDeviceSyncLabelsAsync(string id, CancellationToken token) => RunBlockingNativeAsync(() =>
        {
            var result = new JsonArray();
            foreach (var assignment in vault.ListObjectLabelAssignments(id).Where(a => !a.Deleted))
            {
                var label = vault.RevealObjectLabelWithLimits(assignment.LabelId, new MdbxObjectMetadataDisclosureLimits(1024 * 1024)).Label
                    ?? throw new InvalidDataException("unsupported_vault_content");
                if (!label.Deleted) result.Add(new JsonObject { ["name"] = label.Name, ["payload"] = label.PayloadJson, ["version"] = label.PayloadSchemaVersion });
            }
            return new JsonArray(result.OrderBy(n => n!.ToJsonString(), StringComparer.Ordinal).Select(n => n!.DeepClone()).ToArray());
        }, token);

        public Task<MdbxNativeObjectSummary?> CommitDeviceSyncObjectAsync(DeviceVaultWritePlan plan, CancellationToken token) =>
            RunBlockingNativeAsync<MdbxNativeObjectSummary?>(() =>
            {
                var current = vault.GetObjectSummary(plan.Id);
                if ((current is null ? null : ToObjectSummary(current)) != plan.Previous) return null;
                if (plan.ExpectedGuard is not null && DeviceGuard(plan.Id) != plan.ExpectedGuard) return null;
                var commands = new List<MdbxWriteCommand>();
                if (current is null) commands.Add(new MdbxWriteCommand.CreateEntry(plan.Id, plan.ProjectId, plan.Type, plan.Title, plan.Payload));
                else
                {
                    if (current.Deleted) commands.Add(new MdbxWriteCommand.RestoreEntry(plan.Id, current.CollectionId));
                    if (current.CollectionId != plan.ProjectId) commands.Add(new MdbxWriteCommand.MoveEntry(plan.Id, current.CollectionId, plan.ProjectId));
                    commands.Add(new MdbxWriteCommand.UpdateEntry(plan.Id, plan.ProjectId, plan.Type, plan.Title, plan.Payload));
                }
                if (plan.Labels is { } labels)
                {
                    foreach (var assignment in current is null ? [] : vault.ListObjectLabelAssignments(plan.Id).Where(a => !a.Deleted).ToArray())
                        commands.Add(new MdbxWriteCommand.RemoveObjectLabelAssignment(assignment.AssignmentId));
                    foreach (var label in labels.OfType<JsonObject>())
                    {
                        var labelId = Guid.NewGuid().ToString();
                        commands.Add(new MdbxWriteCommand.CreateObjectLabel(labelId, plan.ProjectId, label["name"]!.GetValue<string>(), label["payload"]!.GetValue<string>(), JsonSerializer.Deserialize<uint>(label["version"]!.ToJsonString())));
                        commands.Add(new MdbxWriteCommand.AssignObjectLabel(Guid.NewGuid().ToString(), plan.Id, labelId));
                    }
                }
                if (plan.Deleted) commands.Add(new MdbxWriteCommand.DeleteEntry(plan.Id, plan.ProjectId));
                // The engine's composite inline blobs exceed its 16 MiB delta limit even for
                // ordinary images. Stage authenticated external blobs, then atomically publish
                // the business payload, labels and removals. The lease excludes other app edits.
                var staged = new List<string>(); var placeholder = false;
                try
                {
                    if (current is null && plan.Attachments.Count > 0)
                    {
                        vault.ExecuteWriteOperation(Guid.NewGuid().ToString(), "device-vault-stage", [new MdbxWriteCommand.CreateEntry(plan.Id, plan.ProjectId, plan.Type, plan.Title,
                            new JsonObject { ["device_sync_staging"] = true, ["device_sync_record_id"] = plan.Id }.ToJsonString())]);
                        placeholder = true;
                        commands[0] = new MdbxWriteCommand.UpdateEntry(plan.Id, plan.ProjectId, plan.Type, plan.Title, plan.Payload);
                    }
                    foreach (var attachment in plan.Attachments)
                    {
                        vault.CreateAttachmentWithExternalContent(Guid.NewGuid().ToString(), new MdbxAttachmentCreateRequest(attachment.Id, current?.CollectionId ?? plan.ProjectId, plan.Id,
                            attachment.FileName, attachment.MediaType), attachment.Content, MdbxFfi.DefaultAttachmentContentLimits());
                        staged.Add(attachment.Id);
                    }
                    var removals = plan.RemovedAttachments.Select(id => (MdbxAttachmentBatchCommand)new MdbxAttachmentBatchCommand.Delete(id)).ToArray();
                    if (removals.Length == 0) vault.ExecuteWriteOperation(Guid.NewGuid().ToString(), "device-vault-apply", commands.ToArray());
                    else vault.ExecuteCompositeWriteOperation(Guid.NewGuid().ToString(), "device-vault-apply", commands.ToArray(), removals);
                }
                catch
                {
                    foreach (var id in staged) vault.DeleteAttachment(id);
                    if (placeholder) vault.DeleteEntry(plan.ProjectId, plan.Id);
                    throw;
                }
                return vault.GetObjectSummary(plan.Id) is { } after ? ToObjectSummary(after) : null;
            }, token);
        public Task EnsureDeviceSyncFoldersAsync(JsonArray path, CancellationToken token) => RunBlockingNativeAsync(() => EnsureFolders(path), token);

        private string FolderGuard(string id) => vault.GetCollectionSummary(id) is { } current
            ? Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                current.HeadCommitId,
                tags = vault.ListObjectLabels(id).Where(t => !t.Deleted && JsonNode.Parse(t.PayloadJson) is JsonObject data &&
                    data["kind"]?.GetValue<string>() is "monica-project-tag" or "monica-device-folder-title-v2").OrderBy(t => t.LabelId).Select(t => new
                    {
                        t.LabelId,
                        t.Name,
                        t.PayloadJson,
                        t.PayloadSchemaVersion
                    })
            }))) : "";

        private void EnsureFolders(JsonArray path, string? updateId = null)
        {
            var commands = FolderCommands(path, updateId);
            if (commands.Count > 0) vault.ExecuteWriteOperation(Guid.NewGuid().ToString(), "device-vault-folders", commands.ToArray());
        }

        private List<MdbxWriteCommand> FolderCommands(JsonArray path, string? updateId = null)
        {
            var commands = new List<MdbxWriteCommand>();
            var occupied = new Dictionary<(string Parent, string Title), string>(); string? cursor = null;
            var root = MdbxAndroidRoot.ProjectIdFor(vault.Info().VaultId);
            do
            {
                var page = vault.ListCollectionSummaries(CollectionPageSize, cursor); cursor = page.NextCursor;
                foreach (var folder in page.Items.Where(f => !f.Deleted)) occupied[(folder.GroupId ?? root, folder.Title.ToUpperInvariant())] = folder.CollectionId;
            } while (cursor is not null);
            if (path.Count > 128) throw new InvalidDataException("invalid_folder");
            if (vault.GetCollectionSummary(root) is null) commands.Add(new MdbxWriteCommand.CreateProject(root, MdbxAndroidRoot.Title));
            var seen = new HashSet<string>();
            foreach (var node in path.OfType<JsonObject>())
            {
                var id = node["id"]!.GetValue<string>(); var title = node["title"]!.GetValue<string>();
                var parent = node["parent"]?.GetValue<string>() ?? root;
                if (!Guid.TryParse(id, out _) || !Guid.TryParse(parent, out _) || title.Length > 4096 || id == parent || !seen.Add(id)) throw new InvalidDataException("invalid_folder");
                var current = vault.GetCollectionSummary(id);
                var logicalTitle = title;
                if (occupied.TryGetValue((parent, title.ToUpperInvariant()), out var other) && other != id)
                {
                    title = title.Length <= 4080 ? title + " [" + id[..8] + "]" : title[..4080] + " [" + id[..8] + "]";
                    if (occupied.TryGetValue((parent, title.ToUpperInvariant()), out var collision) && collision != id) title = logicalTitle[..Math.Min(4050, logicalTitle.Length)] + " [" + id + "]";
                }
                if (current is null || id == updateId) occupied[(parent, title.ToUpperInvariant())] = id;
                if (current is null) commands.Add(new MdbxWriteCommand.CreateProjectWithParent(id, title, parent));
                else if (id == updateId)
                {
                    if (current.Deleted) commands.Add(new MdbxWriteCommand.RestoreProject(id, parent));
                    if (current.Title != title) commands.Add(new MdbxWriteCommand.RenameProject(id, title));
                    if (current.GroupId != parent) commands.Add(new MdbxWriteCommand.MoveProject(id, parent));
                }
                if ((current is null || id == updateId) && title != logicalTitle)
                {
                    var metadata = new JsonObject { ["kind"] = "monica-device-folder-title-v2", ["logical_title"] = logicalTitle, ["local_title"] = title }.ToJsonString();
                    var label = current is null ? null : vault.ListObjectLabels(id).FirstOrDefault(l => !l.Deleted && JsonNode.Parse(l.PayloadJson)?["kind"]?.GetValue<string>() == "monica-device-folder-title-v2");
                    if (label is null) commands.Add(new MdbxWriteCommand.CreateObjectLabel(Guid.NewGuid().ToString(), id, "monica-device-folder-title-v2", metadata, 1));
                    else if (label.PayloadJson != metadata) commands.Add(new MdbxWriteCommand.UpdateObjectLabel(label.LabelId, label.Name, metadata, 1));
                }
            }
            return commands;
        }

        public Task<string?> ApplyDeviceSyncFolderAsync(string id, string? expectedGuard, bool newBinding, bool deleted, JsonObject data, CancellationToken token) =>
            RunBlockingNativeAsync<string?>(() =>
            {
                var current = vault.GetCollectionSummary(id);
                if (!newBinding && FolderGuard(id) != (expectedGuard ?? "")) return null;
                if (deleted && data["trashed"]?.GetValue<bool>() != true)
                {
                    if (current is { Deleted: false }) Execute(new MdbxWriteCommand.DeleteProject(id));
                    return FolderGuard(id);
                }
                var path = data["payload"]!["path"]!.DeepClone().AsArray();
                if (path.Count == 0) throw new InvalidDataException("invalid_folder");
                path[^1]!["id"] = id; path[^1]!["title"] = data["title"]!.DeepClone();
                var commands = FolderCommands(path, updateId: id);
                if (data["payload"]!["tags"] is JsonArray tags)
                {
                    var existing = ProjectTagRecords(id);
                    var desired = tags.OfType<JsonObject>().Select(t => t["name"]!.GetValue<string>()).ToHashSet();
                    foreach (var tag in existing.Where(t => !desired.Contains(t.Name))) commands.Add(new MdbxWriteCommand.DeleteObjectLabel(tag.LabelId));
                    foreach (var tag in tags.OfType<JsonObject>())
                    {
                        var old = existing.FirstOrDefault(t => t.Name == tag["name"]!.GetValue<string>());
                        if (old is null) commands.Add(new MdbxWriteCommand.CreateObjectLabel(Guid.NewGuid().ToString(), id, tag["name"]!.GetValue<string>(), tag["payload"]!.GetValue<string>(), JsonSerializer.Deserialize<uint>(tag["version"]!.ToJsonString())));
                        else if (old.PayloadJson != tag["payload"]!.GetValue<string>() || old.PayloadSchemaVersion != JsonSerializer.Deserialize<uint>(tag["version"]!.ToJsonString())) commands.Add(new MdbxWriteCommand.UpdateObjectLabel(old.LabelId, old.Name, tag["payload"]!.GetValue<string>(), JsonSerializer.Deserialize<uint>(tag["version"]!.ToJsonString())));
                    }
                }
                if (deleted) commands.Add(new MdbxWriteCommand.DeleteProject(id));
                if (commands.Count > 0) vault.ExecuteWriteOperation(Guid.NewGuid().ToString(), "device-vault-folder", commands.ToArray());
                return FolderGuard(id);
            }, token);
        public Task<MdbxNativeEntryRecord?> ReadDeviceSyncObjectAsync(MdbxNativeObjectSummary summary, CancellationToken token) =>
            RunBlockingNativeAsync<MdbxNativeEntryRecord?>(() =>
            {
                if (!DeviceVaultShape.Supports(summary.EntryType) || summary.PayloadSchemaVersion != 1)
                    throw new InvalidDataException("unsupported_vault_content");
                var before = vault.GetObjectSummary(summary.EntryId);
                if (before is null || ToObjectSummary(before) != summary) return null;
                var entry = vault.GetObject(summary.ProjectId, summary.EntryId);
                var after = vault.GetObjectSummary(summary.EntryId);
                if (entry is null || after is null || ToObjectSummary(after) != summary) return null;
                return new(entry.ObjectId, entry.CollectionId, entry.ObjectTypeId, entry.Title, entry.PayloadJson, entry.Deleted, entry.PayloadSchemaVersion);
            }, token);
        public Task<MdbxNativeObjectSummary?> GetDeviceSyncSummaryAsync(string id, CancellationToken token) =>
            RunBlockingNativeAsync(() => vault.GetObjectSummary(id) is { } summary ? ToObjectSummary(summary) : null, token);
        public Task<IReadOnlyList<MdbxNativeProjectRecord>> ListDeviceSyncProjectsAsync(CancellationToken token) =>
            RunBlockingNativeAsync<IReadOnlyList<MdbxNativeProjectRecord>>(() =>
            {
                var projects = new List<MdbxNativeProjectRecord>();
                string? cursor = null;
                do
                {
                    var page = vault.ListCollectionSummaries(CollectionPageSize, cursor);
                    projects.AddRange(page.Items.Select(item => ToProject(item) with { Guard = FolderGuard(item.CollectionId), Title = LogicalFolderTitle(item.CollectionId, item.Title) }));
                    cursor = page.NextCursor;
                } while (cursor is not null);
                return projects;
            }, token);

        private MdbxObjectLabelRecord[] ProjectTagRecords(string id) => vault.ListObjectLabels(id).Where(label => !label.Deleted &&
            JsonNode.Parse(label.PayloadJson)?["kind"]?.GetValue<string>() == "monica-project-tag").ToArray();
        private string LogicalFolderTitle(string id, string title)
        {
            foreach (var label in vault.ListObjectLabels(id).Where(l => !l.Deleted))
            {
                if (JsonNode.Parse(label.PayloadJson) is JsonObject data && data["kind"]?.GetValue<string>() == "monica-device-folder-title-v2" && data["local_title"]?.GetValue<string>() == title)
                    return data["logical_title"]!.GetValue<string>();
            }
            return title;
        }
        public Task<JsonArray> ReadDeviceSyncProjectTagsAsync(string id, CancellationToken token) => RunBlockingNativeAsync(() =>
            new JsonArray(ProjectTagRecords(id).OrderBy(t => t.Name, StringComparer.Ordinal).Select(t => (JsonNode)new JsonObject
            {
                ["name"] = t.Name,
                ["payload"] = t.PayloadJson,
                ["version"] = t.PayloadSchemaVersion
            }).ToArray()), token);

        public Task<MdbxNativeEntryRecord> CreateDeviceSyncEntryAsync(string id, string project, string type, string title, string payload, CancellationToken token) =>
            RunBlockingNativeAsync(() =>
            {
                if (vault.GetObjectSummary(id) is not null) throw new InvalidOperationException("password_identity_collision");
                Execute(new MdbxWriteCommand.CreateEntry(id, project, type, title, payload));
                return new MdbxNativeEntryRecord(id, project, type, title, payload, false);
            }, token);
        private string LanRevision() => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            vault.ListBranches().OrderBy(branch => branch.BranchName, StringComparer.Ordinal)
                .Select(branch => new { branch.BranchName, branch.HeadCommitId }))));

        public Task<string> GetLanRevisionAsync(CancellationToken cancellationToken) =>
            RunBlockingNativeAsync(LanRevision, cancellationToken);

        public Task<MdbxLanBundle> ExportLanBundleAsync(string destination, CancellationToken cancellationToken) =>
            RunBlockingNativeAsync(() =>
            {
                var references = vault.ListExternalBlobReferences(null, 1);
                if (references.RawReferenceCount > 0 || references.UniqueReferenceCount > 0 ||
                    references.Items.Length > 0 || vault.DiagnosticsSummary().ExternalAttachmentCount > 0)
                    throw new LanSyncException("external_blobs");
                var result = vault.ExportManualSyncBundle(destination);
                return new MdbxLanBundle(result.VaultId, result.CommitCount, result.FileSizeBytes, LanRevision());
            }, cancellationToken);

        public Task<MdbxLanMerge> ApplyLanBundleAsync(string source, CancellationToken cancellationToken) =>
            RunBlockingNativeAsync(() =>
            {
                var result = vault.ApplyManualSyncBundle(source);
                if (result.MissingParentCount != 0) throw new InvalidOperationException("The sync bundle has missing dependencies.");
                return new MdbxLanMerge(result.AppliedCommits, result.SkippedCommits, result.ConflictCount);
            }, cancellationToken);
    }
}
