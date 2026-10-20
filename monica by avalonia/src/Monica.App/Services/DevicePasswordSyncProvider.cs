using System.Text.Json;
using System.Text.Json.Nodes;
using Monica.Core.Models;
using Monica.Data;
using Monica.Data.Mdbx;
using Monica.Data.Repositories;
using Monica.Data.Passkeys;
using Monica.Data.Services;
using System.Security.Cryptography;
using System.Text;
using Monica.Platform.Services;

namespace Monica.App.Services;

public sealed class DevicePasswordSyncProvider(
    IMonicaRepository repository,
    MdbxVaultStore vaults,
    DeviceSyncPreferenceStore preferences, string? replicaPath = null, IPasskeyStore? passkeys = null,
    IAttachmentContentStore? attachmentContent = null) : IDeviceSyncDataProvider, IDeviceSyncProgressSource
{
    public event Action<DeviceSyncProgress>? Progress;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ulong? _replica;
    private string _nodeId = "";
    private string? _sourceRevision;
    private IReadOnlyList<DeviceVaultAsset> _assets = [];
    private DeviceAssetTransfer? _preparedAssets;
    private readonly HashSet<string> _verifiedKeys = [];
    private readonly Dictionary<string, long> _nativeKeyRows = [];

    private async Task EnsureReplicaAsync(CancellationToken token)
    {
        if (_replica is not null) return;
        var settings = await preferences.LoadAsync(token);
        var result = await Task.Run(() => LanSyncNative.Request(new
        {
            operation = "replica_open",
            device_id = settings.DeviceId,
            key = settings.ReplicaKey,
            path = replicaPath ?? MonicaAppDataPaths.GetPath("device-sync/passwords.replica")
        }), token);
        _replica = result.GetProperty("handle").GetUInt64();
        _nodeId = settings.DeviceId;
    }

    private async Task<LocalMdbxDatabase> PrimaryAsync(CancellationToken token)
    {
        var databases = await repository.GetMdbxDatabasesAsync(token);
        return databases.FirstOrDefault(database => database.IsDefault) ?? databases.FirstOrDefault()
            ?? throw new LanSyncException("passwords_not_ready");
    }

    private async Task ReconcileAsync(CancellationToken token, bool force = false)
    {
        await EnsureReplicaAsync(token);
        var databases = (await repository.GetMdbxDatabasesAsync(token)).ToArray();
        if (databases.Length == 0) throw new LanSyncException("passwords_not_ready");
        var keyEntries = passkeys is null ? [] : (await passkeys.ListAllAsync(token)).Where(k => k.BitwardenVaultId is null && k.KeepassDatabaseId is null).ToArray();
        var revisions = new List<string>();
        foreach (var source in databases) revisions.Add($"{source.Id}:" + await vaults.GetLanRevisionAsync(source, token));
        var revision = string.Join(";", revisions) + ":" + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(keyEntries)));
        if (!force && revision == _sourceRevision) return;
        Progress?.Invoke(new("preparing", 0, 0));
        await PrepareNativePasskeyDeletesAsync(token);
        var passwords = new List<DevicePasswordSnapshot>();
        foreach (var source in databases) passwords.AddRange(await vaults.ReadDevicePasswordsAsync(source, token));
        await ProjectNativePasskeysAsync(token, passwords);
        if (passkeys is not null) keyEntries = (await passkeys.ListAllAsync(token)).Where(k => k.BitwardenVaultId is null && k.KeepassDatabaseId is null).ToArray();
        for (var index = 0; index < passwords.Count; index++) if (passwords[index].KeyProjectionPending)
        {
            var item = passwords[index]; var database = databases.First(d => item.Scope == $"mdbx:{d.Id}");
            passwords[index] = item with { Guard = await vaults.DeviceSyncGuardAsync(database, item.LocalId.Split(':', 3)[2], token), KeyProjectionPending = false };
        }
        var passwordRows = keyEntries.Length == 0 ? [] : await repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true, cancellationToken: token);
        var categories = keyEntries.Length == 0 ? [] : await repository.GetCategoriesAsync(token);
        var keys = new List<DevicePasswordSnapshot>();
        foreach (var entry in keyEntries)
        {
            // MDBX-backed credential rows are a projection of the same native object.
            if (passwords.FirstOrDefault(p => p.Data["entry_type"]?.GetValue<string>() == "passkey" &&
                p.Data["payload"]?["rp_id"]?.GetValue<string>() == entry.RpId &&
                Monica.Core.Passkeys.PasskeyCredentialId.Normalize(p.Data["payload"]?["credential_id"]?.GetValue<string>() ?? "") == Monica.Core.Passkeys.PasskeyCredentialId.Normalize(entry.CredentialId)) is { } nativeKey)
            {
                _nativeKeyRows[nativeKey.LocalId] = entry.Id;
                continue;
            }
            var data = DevicePasskeyShape.Export(entry, await passkeys!.ResolvePrivateKeyAsync(entry, token), verify: false);
            data["payload"]!["bound_password_entry_id"] = passwordRows.FirstOrDefault(p => p.Id == entry.BoundPasswordId)?.ReplicaGroupId;
            if (entry.CategoryId is null) data["folder_path"] = new JsonArray();
            var category = categories.FirstOrDefault(c => c.Id == entry.CategoryId);
            data["folder"] = category?.Name ?? "";
            var saved = LanSyncNative.Request(new { operation = "replica_local", handle = _replica, local_ids = new[] { $"passkey:{entry.Id}" }, include_data = true }).GetProperty("items");
            if (saved.GetArrayLength() > 0 && saved[0].GetProperty("data").ValueKind == JsonValueKind.Object)
            {
                var oldData = JsonNode.Parse(saved[0].GetProperty("data").GetRawText())!.AsObject();
                var oldPayload = oldData["payload"]!.AsObject();
                DevicePasskeyShape.ValidateIdentity(entry, oldPayload);
                var eligibility = entry.BackupEligible ?? oldPayload["backup_eligible"]?.GetValue<bool>();
                var state = entry.BackupState ?? oldPayload["backup_state"]?.GetValue<bool>();
                if (eligibility != entry.BackupEligible || state != entry.BackupState)
                {
                    entry.BackupEligible = eligibility; entry.BackupState = state;
                    await passkeys.SaveAsync(entry, entry.PrivateKeyAlias, token);
                    data["payload"]!["backup_eligible"] = eligibility; data["payload"]!["backup_state"] = state;
                }
                data = MergePasskeyData(oldData, data);
            }
            if (passwordRows.FirstOrDefault(p => p.Id == entry.BoundPasswordId)?.ReplicaGroupId is { Length: > 0 } bound)
                data["payload"]!["bound_password_entry_id"] = bound;
            if (category?.MdbxFolderId is { } folderId && passwords.FirstOrDefault(p => p.GlobalId == folderId && p.Data["entry_type"]?.GetValue<string>() == "folder") is { } folder)
                data["folder_path"] = folder.Data["payload"]!["path"]!.DeepClone();
            VerifyKeyOnce(data);
            keys.Add(new($"passkey:{entry.Id}", DevicePasskeyShape.Identity(entry), "passkeys", KeyGuard(entry), false, data));
        }
        foreach (var snapshot in passwords.Where(p => p.Data["entry_type"]?.GetValue<string>() == "passkey" && (!p.Deleted || p.Data["trashed"]?.GetValue<bool>() == true)))
        {
            await NormalizeNativePasskeyAsync(snapshot.Data, token);
            if (!snapshot.KeyProjectionPending && _nativeKeyRows.TryGetValue(snapshot.LocalId, out var rowId) && keyEntries.FirstOrDefault(k => k.Id == rowId) is { } row)
            {
                var current = DevicePasskeyShape.Export(row, await passkeys!.ResolvePrivateKeyAsync(row, token));
                current["folder"] = snapshot.Data["folder"]?.DeepClone();
                current["folder_path"] = snapshot.Data["folder_path"]?.DeepClone();
                if (passwordRows.FirstOrDefault(p => p.Id == row.BoundPasswordId)?.ReplicaGroupId is { } bound) current["payload"]!["bound_password_entry_id"] = bound;
                else current["payload"]!["bound_password_entry_id"] = null;
                var merged = MergePasskeyData(snapshot.Data, current);
                snapshot.Data.Clear(); foreach (var pair in merged) snapshot.Data[pair.Key] = pair.Value?.DeepClone();
            }
        }
        for (var index = 0; index < passwords.Count; index++) if (_nativeKeyRows.TryGetValue(passwords[index].LocalId, out var rowId) && keyEntries.FirstOrDefault(k => k.Id == rowId) is { } row)
            passwords[index] = passwords[index] with { Guard = passwords[index].Guard + ":" + KeyGuard(row) };
        passwords.AddRange(keys);
        var allAssets = new List<DeviceVaultAsset>();
        foreach (var snapshot in passwords)
        {
            foreach (var asset in snapshot.Assets ?? [])
            {
                if (asset.Legacy is null) { allAssets.Add(asset); continue; }
                var bytes = await (attachmentContent ?? throw new InvalidDataException("asset_unavailable")).TryReadAttachmentContentAsync(asset.Legacy, token)
                    ?? throw new InvalidDataException("asset_unavailable");
                try
                {
                    if (bytes.Length > 64 * 1024 * 1024) throw new InvalidDataException("asset_limit");
                    var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                    snapshot.Data["assets"]!.AsArray().Add(new JsonObject
                    {
                        ["id"] = hash,
                        ["attachment_id"] = asset.Legacy.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["file_name"] = asset.Legacy.FileName,
                        ["media_type"] = asset.Legacy.ContentType,
                        ["size"] = bytes.LongLength
                    });
                    DeviceVaultShape.RemapAssetPath(snapshot.Data, asset.Legacy.StoragePath, "device-asset:" + hash);
                    allAssets.Add(asset with { Id = hash, SizeBytes = bytes.LongLength });
                }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
        }
        _assets = allAssets.DistinctBy(a => a.Id).ToArray();
        foreach (var batch in passwords.Where(p => p.LocalId.StartsWith("mdbx:", StringComparison.Ordinal)).Chunk(256))
            LanSyncNative.Request(new
            {
                operation = "replica_rebind",
                handle = _replica,
                items = batch.Select(p => new
                {
                    old_local_id = p.LocalId.Split(':', 3)[2],
                    new_local_id = p.LocalId,
                    scope = p.Scope
                })
            });
        NormalizeNativeReferences(passwords, $"mdbx:{databases.FirstOrDefault(d => d.IsDefault)?.Id ?? databases[0].Id}");
        await Task.Run(() =>
        {
            LanSyncNative.Request(new { operation = "replica_scan_begin", handle = _replica, scopes = databases.Select(d => $"mdbx:{d.Id}").Concat(passkeys is null ? [] : new[] { "passkeys" }) });
            var completed = 0;
            foreach (var batch in ScanBatches(passwords))
            {
                token.ThrowIfCancellationRequested();
                LanSyncNative.Request(new
                {
                    operation = "replica_scan_batch",
                    handle = _replica,
                    entries = batch.Select(item => new
                    {
                        local_id = item.LocalId,
                        global_id = item.GlobalId,
                        scope = item.Scope,
                        guard = item.Guard,
                        deleted = item.Deleted,
                        data = item.Data
                    })
                });
                completed += batch.Count;
                Progress?.Invoke(new("preparing", completed, passwords.Count));
            }
            LanSyncNative.Request(new { operation = "replica_scan_end", handle = _replica });
        }, token);
        // Keep the marker read BEFORE the snapshot; a later edit must trigger another scan.
        _sourceRevision = revision;
    }
    private async Task<byte[]> ReadAssetSourceAsync(DeviceVaultAsset asset, IReadOnlyList<LocalMdbxDatabase> databases, CancellationToken token)
    {
        var bytes = asset.Legacy is null ? await vaults.ReadDeviceSyncAssetAsync(databases.First(d => d.Id == asset.DatabaseId), asset, token) :
            await (attachmentContent ?? throw new InvalidDataException("asset_unavailable")).TryReadAttachmentContentAsync(asset.Legacy, token) ?? throw new InvalidDataException("asset_unavailable");
        if (bytes.LongLength != asset.SizeBytes || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(asset.Id, StringComparison.OrdinalIgnoreCase))
        { CryptographicOperations.ZeroMemory(bytes); throw new InvalidDataException("invalid_asset"); }
        return bytes;
    }

    private void NormalizeNativeReferences(List<DevicePasswordSnapshot> snapshots, string primaryScope)
    {
        var known = new Dictionary<string, string>();
        foreach (var batch in snapshots.Chunk(256)) foreach (var item in LanSyncNative.Request(new
        {
            operation = "replica_local",
            handle = _replica,
            local_ids = batch.Select(p => p.LocalId)
        }).GetProperty("items").EnumerateArray())
            known[item.GetProperty("local_id").GetString()!] = item.GetProperty("global_id").GetString()!;
        var copied = snapshots.Where(p => p.Scope.StartsWith("mdbx:", StringComparison.Ordinal)).GroupBy(p => p.GlobalId)
            .Where(g => g.Select(p => p.Scope).Distinct().Count() > 1).Select(g => g.Key).ToHashSet();
        var maps = new Dictionary<string, Dictionary<string, string>>();
        for (var index = 0; index < snapshots.Count; index++)
        {
            var item = snapshots[index];
            if (!item.LocalId.StartsWith("mdbx:", StringComparison.Ordinal)) continue;
            var global = known.GetValueOrDefault(item.LocalId) ?? item.GlobalId;
            if (!known.ContainsKey(item.LocalId) && copied.Contains(global))
            {
                var hex = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"device-object:{_nodeId}:{item.LocalId}")));
                global = $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..32]}";
            }
            snapshots[index] = item with { GlobalId = global };
            if (!maps.TryGetValue(item.Scope, out var map)) maps[item.Scope] = map = [];
            map[item.LocalId[(item.Scope.Length + 1)..].Replace("folder:", "", StringComparison.Ordinal)] = global;
        }
        foreach (var item in snapshots) if (maps.TryGetValue(item.Scope == "passkeys" ? primaryScope : item.Scope, out var map))
            DeviceVaultShape.RemapReferences(item.Data, id => map.GetValueOrDefault(id, id));
        var aliases = new Dictionary<string, string>();
        foreach (var batch in snapshots.SelectMany(p => DeviceVaultShape.ReferenceIds(p.Data)).Distinct().Chunk(256)) foreach (var item in LanSyncNative.Request(new
        {
            operation = "replica_resolve",
            handle = _replica,
            ids = batch
        }).GetProperty("items").EnumerateArray())
            aliases[item.GetProperty("id").GetString()!] = item.GetProperty("global_id").GetString()!;
        foreach (var item in snapshots) DeviceVaultShape.RemapReferences(item.Data, id => aliases.GetValueOrDefault(id, id));
    }

    private JsonObject ResolveIncomingReferences(JsonObject data, string? scope)
    {
        var result = data.DeepClone().AsObject(); var map = new Dictionary<string, string>();
        foreach (var batch in DeviceVaultShape.ReferenceIds(result).Chunk(256)) foreach (var item in LanSyncNative.Request(new
        {
            operation = "replica_resolve",
            handle = _replica,
            ids = batch,
            scope
        }).GetProperty("items").EnumerateArray())
        {
            var global = item.GetProperty("global_id").GetString()!;
            var local = item.GetProperty("local_id").GetString();
            map[item.GetProperty("id").GetString()!] = scope is not null && local?.StartsWith(scope + ":", StringComparison.Ordinal) == true
                ? local[(scope.Length + 1)..].Replace("folder:", "", StringComparison.Ordinal) : global;
        }
        DeviceVaultShape.RemapReferences(result, id => map.GetValueOrDefault(id, id));
        return result;
    }

    private void VerifyKeyOnce(JsonObject data)
    {
        var p = data["payload"]!.AsObject();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(p["public_key"]!.GetValue<string>() + ":" + p["public_key_algorithm"] + ":" + p["private_key_alias"])));
        if (_verifiedKeys.Contains(hash)) return;
        DevicePasskeyShape.Verify(data); _verifiedKeys.Add(hash);
    }
    private static string KeyGuard(PasskeyEntry entry) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(entry)));
    private static JsonObject MergePasskeyData(JsonObject previous, JsonObject current)
    {
        var result = previous.DeepClone().AsObject();
        var payload = result["payload"] as JsonObject ?? new JsonObject();
        foreach (var pair in current["payload"]!.AsObject())
        {
            if (pair.Key is "backup_eligible" or "backup_state" && pair.Value is null && payload[pair.Key] is not null) continue;
            payload[pair.Key] = pair.Value?.DeepClone();
        }
        result["payload"] = payload;
        foreach (var pair in current.Where(p => p.Key != "payload")) if (pair.Key is not ("labels" or "relations" or "assets" or "trashed") || !result.ContainsKey(pair.Key))
            result[pair.Key] = pair.Value?.DeepClone();
        return result;
    }
    private async Task NormalizeNativePasskeyAsync(JsonObject data, CancellationToken token)
    {
        var p = data["payload"]!.AsObject();
        var key = p["private_key_alias"]?.GetValue<string>() ?? "";
        if (key.StartsWith(Monica.Core.Passkeys.PasskeyPrivateKeyRef.ReferencePrefix, StringComparison.Ordinal))
        {
            var row = passkeys is null ? null : await passkeys.FindAsync(p["credential_id"]!.GetValue<string>(), p["rp_id"]!.GetValue<string>(), token);
            key = row is null ? "" : await passkeys!.ResolvePrivateKeyAsync(row, token) ?? "";
        }
        else if (key.Length > 0)
        {
            try { if (Convert.FromBase64String(key).FirstOrDefault() != 0x30) key = ""; } catch (FormatException) { key = ""; }
        }
        p["private_key_alias"] = key; p["key_status"] = key.Length == 0 ? "reference" : "portable";
        var entry = DevicePasskeyShape.Import(data, 0);
        p["public_key"] = Convert.ToBase64String(Monica.Core.Passkeys.PasskeyKeyMaterialGenerator.BuildCosePublicKey(entry.PublicKeyAlgorithm, entry.PublicKey));
        VerifyKeyOnce(data);
    }

    private static IEnumerable<IReadOnlyList<DevicePasswordSnapshot>> ScanBatches(IReadOnlyList<DevicePasswordSnapshot> passwords)
    {
        var batch = new List<DevicePasswordSnapshot>();
        var bytes = 0;
        foreach (var password in passwords)
        {
            var size = System.Text.Encoding.UTF8.GetByteCount(password.Data.ToJsonString()) + 1024;
            if (batch.Count > 0 && (batch.Count == 256 || bytes + size > 4 * 1024 * 1024))
            { yield return batch; batch = []; bytes = 0; }
            batch.Add(password); bytes += size;
        }
        if (batch.Count > 0) yield return batch;
    }

    public async Task<string> GetRevisionAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            await ReconcileAsync(token);
            return RevisionStamp(LanSyncNative.Request(new { operation = "replica_revision", handle = _replica }));
        }
        finally { _gate.Release(); }
    }

    public async Task<DeviceSyncSnapshot> PrepareAsync(string directory, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            await ReconcileAsync(token);
            Directory.CreateDirectory(directory);
            var snapshot = await Task.Run(() => LanSyncNative.Request(new
            {
                operation = "replica_export",
                handle = _replica,
                path = Path.Combine(directory, "changes.mdbxsync")
            }), token);
            var contentKey = snapshot.GetProperty("content_key").GetString()!;
            var assets = new DeviceAssetTransfer(directory, contentKey);
            var databases = await repository.GetMdbxDatabasesAsync(token);
            var total = _assets.Sum(a => a.SizeBytes); long completed = 0;
            foreach (var source in _assets)
            {
                var bytes = await ReadAssetSourceAsync(source, databases, token);
                try { assets.Append(source.Id, bytes, token, count => Progress?.Invoke(new("preparing", 0, 0, completed + count, total))); }
                finally { CryptographicOperations.ZeroMemory(bytes); }
                completed += source.SizeBytes;
            }
            _preparedAssets = assets;
            return new("monica-passwords-v1", RevisionStamp(snapshot), directory, contentKey);
        }
        finally { _gate.Release(); }
    }

    public async Task<uint> ApplyAsync(string directory, JsonElement peer, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        var touched = new HashSet<long>();
        string? localAssetCache = null;
        try
        {
            await ReconcileAsync(token);
            var key = peer.GetProperty("pairing").GetProperty("content_key").GetString();
            var assets = new DeviceAssetTransfer(directory, key!);
            var fallback = _preparedAssets;
            if (_assets.Count > 0 && (fallback is null || _assets.Any(a => !fallback.Contains(a.Id))))
            {
                localAssetCache = Path.Combine(directory, "local-assets-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(localAssetCache);
                var localKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                fallback = new DeviceAssetTransfer(localAssetCache, localKey);
                var sources = await repository.GetMdbxDatabasesAsync(token);
                foreach (var source in _assets)
                {
                    var bytes = await ReadAssetSourceAsync(source, sources, token);
                    try { fallback.Append(source.Id, bytes, token); } finally { CryptographicOperations.ZeroMemory(bytes); }
                }
            }
            Task<byte[]> ReadAsset(JsonObject descriptor, CancellationToken cancellation) => assets.Contains(descriptor["id"]!.GetValue<string>())
                ? assets.ReadAsync(descriptor, cancellation)
                : (fallback?.Contains(descriptor["id"]!.GetValue<string>()) == true ? fallback.ReadAsync(descriptor, cancellation) : throw new InvalidDataException("asset_unavailable"));
            await Task.Run(() => LanSyncNative.Request(new
            {
                operation = "replica_merge",
                handle = _replica,
                path = Path.Combine(directory, "changes.mdbxsync"),
                content_key = key
            }), token);
            uint changed = 0;
            var retries = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var page = LanSyncNative.Request(new { operation = "replica_pending", handle = _replica, dependency_order = true });
                var pending = page.GetProperty("items").EnumerateArray().ToArray();
                var total = changed + page.GetProperty("total").GetInt64();
                if (pending.Length == 0) break;
                var acknowledgments = new List<object>();
                var raced = false;
                foreach (var item in pending)
                {
                    var record = item.GetProperty("record");
                    var id = record.GetProperty("id").GetString()!;
                    var localId = item.GetProperty("local_id").GetString();
                    var scope = item.GetProperty("scope").GetString();
                    LocalMdbxDatabase database;
                    if (scope is not null && scope.StartsWith("mdbx:", StringComparison.Ordinal) && long.TryParse(scope.AsSpan(5), out var databaseId))
                        database = (await repository.GetMdbxDatabasesAsync(token)).FirstOrDefault(row => row.Id == databaseId)
                            ?? throw new LanSyncException("password_source_unavailable");
                    else database = await PrimaryAsync(token);
                    var payload = JsonNode.Parse(record.GetProperty("data").GetRawText())!.AsObject();
                    var deleted = record.GetProperty("deleted").GetBoolean();
                    if ((!deleted || payload["trashed"]?.GetValue<bool>() == true) && payload["entry_type"]?.GetValue<string>() == "passkey") DevicePasskeyShape.Verify(payload);
                    var localPasskey = localId?.StartsWith("passkey:", StringComparison.Ordinal) == true;
                    var materialized = ResolveIncomingReferences(payload, localPasskey ? null : $"mdbx:{database.Id}");
                    var expected = item.GetProperty("expected_guard").GetString();
                    if (!localPasskey && localId is not null && expected?.Contains(':') == true && _nativeKeyRows.TryGetValue(localId, out var projectedId))
                    {
                        var row = await passkeys!.GetAsync(projectedId, token);
                        if (row is null || KeyGuard(row) != expected.Split(':', 2)[1]) { raced = true; break; }
                        expected = expected.Split(':', 2)[0];
                    }
                    var applied = localPasskey
                        ? await ApplyPasskeyAsync(id, localId, item.GetProperty("expected_guard").GetString(), deleted, materialized, token)
                        : await vaults.TryApplyDevicePasswordAsync(database, id, localId, expected, deleted, materialized, token, ReadAsset);
                    if (applied is null) { raced = true; break; }
                    acknowledgments.Add(new
                    {
                        record_id = id,
                        clock = record.GetProperty("clock").Clone(),
                        local_id = applied.LocalId,
                        scope = applied.Scope,
                        guard = applied.Guard,
                        data = localPasskey ? applied.Data : payload
                    });
                    touched.Add(database.Id);
                    changed++;
                    Progress?.Invoke(new("applying", changed, total));
                }
                if (acknowledgments.Count > 0)
                    await Task.Run(() => LanSyncNative.Request(new { operation = "replica_applied_batch", handle = _replica, items = acknowledgments }), token);
                if (raced)
                {
                    if (++retries > 4) throw new LanSyncException("passwords_changing");
                    await ReconcileAsync(token, force: true);
                }
            }
            foreach (var database in await repository.GetMdbxDatabasesAsync(token))
            {
                var targets = await EnsureNativePasskeyTargetsAsync(database, token);
                await vaults.ResolveDeviceSyncRelationsAsync(database, token, targets);
                touched.Add(database.Id);
            }
            if (repository is IMdbxLanSyncMutationObserver observer)
                foreach (var database in (await repository.GetMdbxDatabasesAsync(token)).Where(d => touched.Contains(d.Id))) await observer.OnLanSyncAppliedAsync(database);
            await ProjectNativePasskeysAsync(token);
            await RepairPasskeyBindingsAsync(token);
            _sourceRevision = null;
            return changed;
        }
        finally
        {
            try
            {
                if (localAssetCache is not null && Directory.Exists(localAssetCache)) Directory.Delete(localAssetCache, true);
                if (repository is IMdbxLanSyncMutationObserver observer)
                    foreach (var database in (await repository.GetMdbxDatabasesAsync(CancellationToken.None)).Where(database => touched.Contains(database.Id)))
                        await observer.OnLanSyncAppliedAsync(database);
            }
            finally { _gate.Release(); }
        }
    }

    internal async Task<DevicePasswordSnapshot?> ApplyPasskeyAsync(string globalId, string? localId, string? expected, bool deleted, JsonObject data, CancellationToken token)
    {
        var store = passkeys ?? throw new LanSyncException("passkeys_not_ready");
        var id = localId is not null && localId.StartsWith("passkey:", StringComparison.Ordinal) ? long.Parse(localId.AsSpan(8)) : 0;
        var previous = id > 0 ? await store.GetAsync(id, token) : null;
        if (previous is not null)
        {
            if (KeyGuard(previous) != expected) return null;
        }
        else if (!string.IsNullOrEmpty(expected)) return null;
        if (deleted)
        {
            if (previous is not null) await store.DeleteAsync(id, token);
            return new($"passkey:{id}", globalId, "passkeys", "", true, new JsonObject());
        }
        var entry = DevicePasskeyShape.Import(data, id);
        if (previous is not null) DevicePasskeyShape.PreserveExisting(entry, previous, data["payload"]!.AsObject());
        var duplicate = await store.FindAsync(entry.CredentialId, entry.RpId, token);
        if (duplicate is not null && duplicate.Id != id)
        {
            var current = DevicePasskeyShape.Export(duplicate, await store.ResolvePrivateKeyAsync(duplicate, token));
            if (!JsonNode.DeepEquals(current, data)) throw new LanSyncException("passkey_conflict");
            return new($"passkey:{duplicate.Id}", globalId, "passkeys", KeyGuard(duplicate), false, current);
        }
        id = await store.SaveAsync(entry, entry.PrivateKeyAlias, token);
        var saved = await store.GetAsync(id, token) ?? throw new LanSyncException("device_sync_write_failed");
        var actual = DevicePasskeyShape.Export(saved, await store.ResolvePrivateKeyAsync(saved, token));
        actual["folder"] = data["folder"]?.DeepClone();
        actual["folder_path"] = data["folder_path"]?.DeepClone();
        return new($"passkey:{id}", globalId, "passkeys", KeyGuard(saved), false, MergePasskeyData(data, actual));
    }

    private async Task RepairPasskeyBindingsAsync(CancellationToken token)
    {
        if (passkeys is null) return;
        var entries = (await passkeys.ListAllAsync(token)).Where(k => k.BitwardenVaultId is null && k.KeepassDatabaseId is null).ToArray();
        if (entries.Length == 0) return;
        var passwords = await repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true, cancellationToken: token);
        var categories = await repository.GetCategoriesAsync(token);
        foreach (var batch in entries.Chunk(256))
        {
            var saved = LanSyncNative.Request(new { operation = "replica_local", handle = _replica, local_ids = batch.Select(k => $"passkey:{k.Id}"), include_data = true }).GetProperty("items");
            foreach (var item in saved.EnumerateArray())
            {
                var entry = batch.First(k => $"passkey:{k.Id}" == item.GetProperty("local_id").GetString());
                var primary = await PrimaryAsync(token);
                var data = ResolveIncomingReferences(JsonNode.Parse(item.GetProperty("data").GetRawText())!.AsObject(), $"mdbx:{primary.Id}");
                var payload = data["payload"]!.AsObject();
                var bound = payload.ContainsKey("bound_password_entry_id")
                    ? passwords.FirstOrDefault(p => p.ReplicaGroupId == payload["bound_password_entry_id"]?.GetValue<string>())?.Id : entry.BoundPasswordId;
                var path = data["folder_path"] as JsonArray;
                var folderId = path?.LastOrDefault()?["id"]?.GetValue<string>();
                var category = folderId is null ? null : categories.FirstOrDefault(c => c.MdbxFolderId == folderId);
                var categoryId = path is null ? entry.CategoryId : category?.Id;
                if (entry.BoundPasswordId == bound && entry.CategoryId == categoryId) continue;
                entry.BoundPasswordId = bound; entry.CategoryId = categoryId;
                entry.MdbxFolderId = category?.MdbxFolderId;
                await passkeys.SaveAsync(entry, cancellationToken: token);
            }
        }
    }

    private async Task PrepareNativePasskeyDeletesAsync(CancellationToken token)
    {
        if (passkeys is null) return;
        var databases = await repository.GetMdbxDatabasesAsync(token);
        foreach (var link in await passkeys.ListNativeProjectionsAsync(token))
        {
            _nativeKeyRows[$"mdbx:{link.DatabaseId}:{link.ObjectId}"] = link.RecordId;
            if (await passkeys.GetAsync(link.RecordId, token) is not null) continue;
            var database = databases.FirstOrDefault(d => d.Id == link.DatabaseId);
            if (database is null) continue;
            await vaults.DeleteDeviceSyncPasskeyProjectionAsync(database, link.ObjectId, token);
            await passkeys.RemoveNativeProjectionAsync(link.DatabaseId, link.ObjectId, token);
        }
    }

    internal async Task ProjectNativePasskeysAsync(CancellationToken token, IReadOnlyList<DevicePasswordSnapshot>? supplied = null)
    {
        if (passkeys is null) return;
        var all = supplied ?? await ReadNativePasskeysAsync(token);
        if (!all.Any(p => p.Data["entry_type"]?.GetValue<string>() == "passkey" || _nativeKeyRows.ContainsKey(p.LocalId))) return;
        var passwords = await repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true, cancellationToken: token);
        var categories = await repository.GetCategoriesAsync(token);
        var databases = await repository.GetMdbxDatabasesAsync(token);
        foreach (var snapshot in all)
        {
            var database = databases.First(d => snapshot.Scope == $"mdbx:{d.Id}");
            var key = snapshot.Data["entry_type"]?.GetValue<string>() == "passkey";
            _nativeKeyRows.TryGetValue(snapshot.LocalId, out var rowId);
            if (!key && rowId == 0) continue;
            var row = rowId == 0 ? null : await passkeys.GetAsync(rowId, token);
            if (key && snapshot.Data["payload"]?["credential_id"] is { } credential) row ??= await passkeys.FindAsync(credential.GetValue<string>(), snapshot.Data["payload"]!["rp_id"]?.GetValue<string>(), token);
            var originalGuard = row is null ? null : KeyGuard(row);
            var payload = snapshot.Data["payload"] as JsonObject;
            var hydrateFlags = row is not null && payload is not null &&
                ((row.BackupEligible is null && payload["backup_eligible"] is not null) ||
                 (row.BackupState is null && payload["backup_state"] is not null));
            if (!hydrateFlags && !snapshot.KeyProjectionPending && row is not null && !snapshot.Deleted && LanSyncNative.Request(new
            {
                operation = "replica_local",
                handle = _replica,
                local_ids = new[] { snapshot.LocalId }
            }).GetProperty("items").GetArrayLength() > 0) continue;
            var physical = snapshot.LocalId.Split(':', 3)[2];
            if (snapshot.Deleted)
            {
                if (row is not null) await passkeys.DeleteAsync(row.Id, token); await vaults.CompleteDeviceSyncKeyProjectionAsync(database, physical, token);
                await passkeys.RemoveNativeProjectionAsync(database.Id, physical, token); continue;
            }
            await NormalizeNativePasskeyAsync(snapshot.Data, token);
            var entry = DevicePasskeyShape.Import(snapshot.Data, row?.Id ?? 0);
            if (row is not null)
            {
                DevicePasskeyShape.ValidateIdentity(row, snapshot.Data["payload"]!.AsObject());
                // Existing native payload is authoritative for a projection made before these columns existed.
                if (row.MdbxDatabaseId == database.Id)
                {
                    row.BackupEligible ??= entry.BackupEligible;
                    row.BackupState ??= entry.BackupState;
                }
                DevicePasskeyShape.PreserveExisting(entry, row, snapshot.Data["payload"]!.AsObject());
            }
            var material = entry.PrivateKeyAlias;
            entry.MdbxDatabaseId = database.Id;
            entry.MdbxFolderId = snapshot.Data["folder_path"]?.AsArray().LastOrDefault()?["id"]?.GetValue<string>();
            entry.CategoryId = categories.FirstOrDefault(c => c.MdbxFolderId == entry.MdbxFolderId)?.Id;
            entry.BoundPasswordId = passwords.FirstOrDefault(p => p.ReplicaGroupId == snapshot.Data["payload"]?["bound_password_entry_id"]?.GetValue<string>())?.Id;
            if (row is not null)
            {
                entry.UseCount = Math.Max(row.UseCount, entry.UseCount); entry.LastUsedAt = row.LastUsedAt > entry.LastUsedAt ? row.LastUsedAt : entry.LastUsedAt;
                if (material.Length == 0 && row.PublicKey == entry.PublicKey) { entry.PrivateKeyAlias = row.PrivateKeyAlias; material = row.PrivateKeyAlias; }
            }
            if (row is not null && await passkeys.ResolvePrivateKeyAsync(row, token) == material)
            {
                entry.PrivateKeyAlias = row.PrivateKeyAlias;
                if (KeyGuard(entry) == originalGuard)
                {
                    await vaults.CompleteDeviceSyncKeyProjectionAsync(database, physical, token);
                    await passkeys.SaveNativeProjectionAsync(row.Id, database.Id, physical, token); continue;
                }
            }
            var id = await passkeys.SaveAsync(entry, material, token);
            _nativeKeyRows[snapshot.LocalId] = id;
            await passkeys.SaveNativeProjectionAsync(id, database.Id, physical, token);
            await vaults.CompleteDeviceSyncKeyProjectionAsync(database, physical, token);
        }
    }
    private async Task<IReadOnlyList<DevicePasswordSnapshot>> ReadNativePasskeysAsync(CancellationToken token)
    {
        var result = new List<DevicePasswordSnapshot>();
        foreach (var database in await repository.GetMdbxDatabasesAsync(token)) result.AddRange(await vaults.ReadDevicePasswordsAsync(database, token));
        return result;
    }

    private async Task<IReadOnlyDictionary<string, string>> EnsureNativePasskeyTargetsAsync(LocalMdbxDatabase database, CancellationToken token)
    {
        var targets = new Dictionary<string, string>(); var scope = $"mdbx:{database.Id}";
        foreach (var batch in (await vaults.DeviceSyncRelationTargetsAsync(database, token)).Chunk(256))
            foreach (var item in LanSyncNative.Request(new { operation = "replica_resolve", handle = _replica, ids = batch, scope, include_data = true }).GetProperty("items").EnumerateArray())
            {
                var global = item.GetProperty("global_id").GetString()!; var local = item.GetProperty("local_id").GetString();
                var target = local?.StartsWith(scope + ":", StringComparison.Ordinal) == true ? local[(scope.Length + 1)..] : global;
                targets[item.GetProperty("id").GetString()!] = target;
                if (await vaults.DeviceSyncObjectExistsAsync(database, target, token)) continue;
                if (!item.TryGetProperty("data", out var data) || item.GetProperty("deleted").GetBoolean()) throw new InvalidDataException("reference_unavailable");
                var payload = JsonNode.Parse(data.GetRawText())!.AsObject();
                if (payload["entry_type"]?.GetValue<string>() != "passkey") throw new InvalidDataException("reference_unavailable");
                DevicePasskeyShape.Verify(payload);
                if (await vaults.TryApplyDevicePasswordAsync(database, global, null, null, false, ResolveIncomingReferences(payload, scope), token) is null)
                    throw new InvalidDataException("passwords_changing");
            }
        return targets;
    }

    private static string RevisionStamp(JsonElement result) => result.GetProperty("revision").GetString()! +
        (result.TryGetProperty("pending", out var pending) && pending.GetBoolean() ? "~pending" : "");

    public async Task PauseAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_replica is { } handle) LanSyncNative.Request(new { operation = "replica_close", handle });
            _replica = null; _sourceRevision = null; _preparedAssets = null; _verifiedKeys.Clear();
        }
        finally { _gate.Release(); }
    }
}
