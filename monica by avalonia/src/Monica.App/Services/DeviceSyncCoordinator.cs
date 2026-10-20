using System.Text.Json;
using Monica.Core.Services;
using Monica.Platform.Services;

namespace Monica.App.Services;

public sealed record DeviceSyncSnapshot(string VaultId, string Revision, string Directory, string ContentKey)
{
    public override string ToString() => $"DeviceSyncSnapshot({VaultId})";
}

public interface IDeviceSyncDataProvider
{
    Task<string> GetRevisionAsync(CancellationToken token);
    Task<DeviceSyncSnapshot> PrepareAsync(string directory, CancellationToken token);
    Task<uint> ApplyAsync(string directory, JsonElement peer, CancellationToken token);
    Task PauseAsync();
}

public sealed record DeviceSyncProgress(string Stage, long Completed, long Total, long Bytes = 0, long TotalBytes = 0);
public interface IDeviceSyncProgressSource { event Action<DeviceSyncProgress>? Progress; }

public interface IDeviceSyncTransport
{
    JsonElement Request(object request);
}

public sealed class NativeDeviceSyncTransport : IDeviceSyncTransport
{
    public JsonElement Request(object request) => LanSyncNative.Request(request);
}

public sealed record DeviceSyncPeerState(string DeviceId, string Name, string Status, DateTimeOffset? LastSyncedAt);
public sealed record DeviceSyncState(bool Enabled, int IntervalSeconds, IReadOnlyList<DeviceSyncPeerState> Peers,
    string Phase = "idle", string Code = "", IReadOnlyList<string>? Invitations = null, string? Error = null, bool Pairing = false,
    string FailureStage = "", int ConnectionAttempts = 0, string FailureKind = "",
    string FailureOperation = "", int FailureRequestBytes = 0, DeviceSyncProgress? Progress = null,
    DeviceSyncMode Mode = DeviceSyncMode.Bidirectional);

/// <summary>Owns connections for the app session, independently of navigation.</summary>
public sealed class DeviceSyncCoordinator(
    DeviceSyncPreferenceStore store,
    IDeviceSyncDataProvider data,
    IVaultSessionService session,
    IDeviceSyncTransport? transport = null,
    TimeProvider? timeProvider = null,
    string? transferRoot = null) : IDisposable, IAsyncDisposable
{
    private readonly IDeviceSyncTransport _transport = transport ?? new NativeDeviceSyncTransport();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Transfer> _transfers = [];
    private readonly Dictionary<ulong, Transfer> _retiring = [];
    private readonly Dictionary<string, long> _lastAttempts = [];
    private readonly Dictionary<string, (int Count, DateTimeOffset After)> _retry = [];
    private readonly Dictionary<string, string> _statuses = [];
    private readonly HashSet<string> _force = [];
    private readonly string _root = transferRoot ?? Monica.Data.MonicaAppDataPaths.GetPath("device-sync/transfers/" + Guid.NewGuid().ToString("N"));
    private readonly HashSet<string> _snapshots = [];
    private DeviceSyncPreferences? _preferences;
    private DeviceSyncSnapshot? _snapshot;
    private Task? _runner;
    private bool _pairing;
    private bool _paused = true;
    private string _phase = "idle";
    private string _code = "";
    private List<string> _invitations = [];
    private string? _error;
    private string _failureStage = "";
    private int _connectionAttempts;
    private string _failureKind = "";
    private string _failureOperation = "";
    private int _failureRequestBytes;
    private DateTimeOffset _loadAfter;
    private DeviceSyncProgress? _progress;
    private bool _progressAttached;
    private const string ReceiveOnlyRevision = "receive-only-v1";

    public event EventHandler<DeviceSyncState>? Changed;
    public event EventHandler? PasswordsChanged;
    public Func<bool> CanApply { get; set; } = () => true;
    public DeviceSyncState State { get; private set; } = new(true, 0, []);

    private sealed class Transfer(ulong handle, string root, DeviceSyncSnapshot snapshot, bool enrollment, string advertisement)
    {
        public ulong Handle { get; } = handle;
        public string Root { get; } = root;
        public DeviceSyncSnapshot Snapshot { get; } = snapshot;
        public bool Enrollment { get; } = enrollment;
        public string Advertisement { get; } = advertisement;
        public bool ApplyAttempted { get; set; }
        public JsonElement Peer { get; set; }
        public string Stage { get; set; } = "connecting";
        public int Attempts { get; set; }
        public string RemoteAddress { get; set; } = "";
        public CancellationTokenRegistration LockCancellation { get; set; }
    }

    public void Start()
    {
        if (!_progressAttached && data is IDeviceSyncProgressSource source)
        {
            source.Progress += UpdateProgress;
            _progressAttached = true;
        }
        _runner ??= Task.Run(RunAsync);
    }

    private void UpdateProgress(DeviceSyncProgress progress) { _progress = progress; _phase = progress.Stage; Publish(); }

    private void Publish()
    {
        var peers = _preferences?.Peers.Select(peer =>
        {
            var status = _statuses.GetValueOrDefault(peer.DeviceId, "offline");
            if (_error is not null && _failureStage == "preparing") status = "preparation_failed";
            else if (peer.LastSyncedAt is null && status is "offline" or "waiting") status = "first_sync_pending";
            return new DeviceSyncPeerState(peer.DeviceId, peer.Name, status, peer.LastSyncedAt);
        }).ToArray() ?? State.Peers;
        var next = new DeviceSyncState(_preferences?.Enabled ?? State.Enabled, _preferences?.IntervalSeconds ?? State.IntervalSeconds,
            peers, _phase, _code, _invitations.ToArray(), _error, _pairing, _failureStage, _connectionAttempts, _failureKind,
            _failureOperation, _failureRequestBytes, _progress, _preferences?.Mode ?? State.Mode);
        if (State.Enabled == next.Enabled && State.IntervalSeconds == next.IntervalSeconds && State.Phase == next.Phase &&
            State.Code == next.Code && State.Error == next.Error && State.Pairing == next.Pairing && State.Peers.SequenceEqual(next.Peers) &&
            (State.Invitations ?? []).SequenceEqual(next.Invitations ?? []) && State.FailureStage == next.FailureStage &&
            State.ConnectionAttempts == next.ConnectionAttempts && State.FailureKind == next.FailureKind &&
            State.FailureOperation == next.FailureOperation && State.FailureRequestBytes == next.FailureRequestBytes && State.Progress == next.Progress && State.Mode == next.Mode) return;
        State = next;
        Changed?.Invoke(this, State);
    }

    private async Task EnsurePreferencesAsync(CancellationToken token)
    {
        if (_preferences is not null) return;
        if (!session.IsUnlocked) throw new OperationCanceledException(token);
        _preferences = await store.LoadAsync(token);
        if (_phase == "locked") _phase = "idle";
        foreach (var peer in _preferences.Peers) _force.Add(peer.DeviceId);
    }

    public async Task SetIntervalAsync(int seconds)
    {
        await _gate.WaitAsync(_lifetime.Token);
        try
        {
            await EnsurePreferencesAsync(_lifetime.Token);
            _preferences!.SetInterval(seconds);
            foreach (var peer in _preferences.Peers) _force.Add(peer.DeviceId);
            await store.SaveAsync(_preferences, _lifetime.Token);
            Publish();
        }
        finally { _gate.Release(); }
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        await _gate.WaitAsync(_lifetime.Token);
        try
        {
            await EnsurePreferencesAsync(_lifetime.Token);
            _preferences!.Enabled = enabled;
            await store.SaveAsync(_preferences, _lifetime.Token);
            if (!enabled) await PauseCoreAsync(locked: false);
            else
            {
                _phase = "idle";
                foreach (var peer in _preferences.Peers) _force.Add(peer.DeviceId);
            }
            Publish();
        }
        finally { _gate.Release(); }
    }

    public async Task ForgetAsync(string deviceId)
    {
        await _gate.WaitAsync(_lifetime.Token);
        try
        {
            await EnsurePreferencesAsync(_lifetime.Token);
            if (_transfers.Remove(deviceId, out var transfer)) await StopAsync(transfer);
            _preferences!.Peers.RemoveAll(peer => peer.DeviceId == deviceId);
            _force.Remove(deviceId); _retry.Remove(deviceId); _statuses.Remove(deviceId);
            await store.SaveAsync(_preferences, _lifetime.Token);
            CleanSnapshots();
            Publish();
        }
        finally { _gate.Release(); }
    }

    public async Task SetModeAsync(DeviceSyncMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        await _gate.WaitAsync(_lifetime.Token);
        try
        {
            await EnsurePreferencesAsync(_lifetime.Token);
            if (_preferences!.Mode == mode) return;
            if (_pairing) throw new LanSyncException("device_sync_pairing_busy");
            if (_preferences.SyncRequest >= long.MaxValue) throw new LanSyncException("device_sync_settings_limit");
            foreach (var transfer in _transfers.Values.ToArray()) await StopAsync(transfer);
            _transfers.Clear();
            _preferences.Mode = mode;
            _preferences.SyncRequest++;
            _snapshot = null; _progress = null; _phase = "idle"; _error = null; _loadAfter = default;
            _failureStage = ""; _failureKind = ""; _failureOperation = ""; _failureRequestBytes = 0;
            foreach (var peer in _preferences.Peers) { _force.Add(peer.DeviceId); _retry.Remove(peer.DeviceId); }
            await store.SaveAsync(_preferences, _lifetime.Token);
            CleanSnapshots();
            Publish();
        }
        finally { _gate.Release(); }
    }

    public async Task SyncNowAsync()
    {
        await _gate.WaitAsync(_lifetime.Token);
        try
        {
            await EnsurePreferencesAsync(_lifetime.Token);
            if (_preferences!.SyncRequest >= long.MaxValue) throw new LanSyncException("device_sync_settings_limit");
            _preferences.SyncRequest++;
            foreach (var peer in _preferences.Peers) { _force.Add(peer.DeviceId); _retry.Remove(peer.DeviceId); }
            await store.SaveAsync(_preferences, _lifetime.Token);
        }
        finally { _gate.Release(); }
    }

    public async Task PairAsync(string? invitation = null)
    {
        await _gate.WaitAsync(_lifetime.Token);
        try
        {
            if (_pairing || !session.IsUnlocked) return;
            _phase = "preparing"; _failureStage = ""; _failureKind = ""; _failureOperation = ""; _failureRequestBytes = 0; _connectionAttempts = 0;
            if (invitation is not null && LanPairingInvitation.Parse(invitation)?.Version != 2)
                throw new LanSyncException("unsupported_protocol");
            await EnsurePreferencesAsync(_lifetime.Token);
            if (_preferences!.Peers.Count >= 8) throw new LanSyncException("too_many_devices");
            await store.SaveAsync(_preferences, _lifetime.Token);
            foreach (var transfer in _transfers.Values) await StopAsync(transfer);
            _transfers.Clear();
            _pairing = true; _paused = false;
            _phase = "preparing"; _error = null; Publish();
            using var token = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, session.SessionCancellationToken);
            var directory = Path.Combine(_root, "out-" + Guid.NewGuid().ToString("N"));
            _snapshots.Add(directory);
            var snapshot = await Task.Run(() => DevicePairingSnapshot.Create(_transport, directory, _preferences.DeviceId));
            token.Token.ThrowIfCancellationRequested();
            _transfers["pairing"] = await StartTransferAsync(snapshot, null, invitation, true, token.Token);
        }
        catch (OperationCanceledException) { _pairing = false; _phase = "cancelled"; Publish(); }
        catch (Exception error) { RecordFailure(error, _phase); _pairing = false; _phase = "failed"; Publish(); }
        finally { _gate.Release(); }
    }

    public async Task CancelPairingAsync()
    {
        await _gate.WaitAsync(_lifetime.Token);
        try
        {
            if (_transfers.Remove("pairing", out var transfer)) await StopAsync(transfer);
            _pairing = false; _phase = "idle"; _code = ""; _invitations.Clear(); Publish();
        }
        finally { _gate.Release(); }
    }

    private string Advertisement(string revision) =>
        $"{revision}:{_preferences!.SettingsVersion}:{_preferences.SettingsWriter}:{_preferences.SyncRequest}";

    private async Task<DeviceSyncSnapshot> CurrentSnapshotAsync(CancellationToken token)
    {
        if (_failureStage == "preparing") { _error = null; _failureStage = ""; _failureKind = ""; _failureOperation = ""; _failureRequestBytes = 0; }
        try
        {
            var receiveOnly = _preferences!.Mode == DeviceSyncMode.ReceiveOnly;
            var revision = receiveOnly ? ReceiveOnlyRevision : await data.GetRevisionAsync(token);
            if (_snapshot?.Revision == revision) return _snapshot;
            var directory = Path.Combine(_root, "out-" + Guid.NewGuid().ToString("N"));
            _snapshots.Add(directory);
            if (receiveOnly)
            {
                Directory.CreateDirectory(directory);
                _snapshot = new DeviceSyncSnapshot("monica-passwords-v1", revision, directory, "");
            }
            else _snapshot = await data.PrepareAsync(directory, token);
            return _snapshot;
        }
        finally
        {
            CleanSnapshots();
            if (_progress?.Stage == "preparing") { _progress = null; if (_phase == "preparing") _phase = "idle"; }
        }
    }

    private async Task<Transfer> StartTransferAsync(DeviceSyncSnapshot snapshot, DeviceSyncPeer? peer,
        string? invitation, bool enrollment, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var directory = Path.Combine(_root, "in-" + Guid.NewGuid().ToString("N"));
        var advertisement = Advertisement(snapshot.Revision);
        JsonElement started;
        try
        {
            // Do not abandon a native handle when cancellation races creation.
            started = await Task.Run(() => _transport.Request(new
            {
                operation = invitation is null ? "start" : "connect",
                protocol = 2,
                invitation,
                name = Environment.MachineName,
                device = new
                {
                    device_id = _preferences!.DeviceId,
                    name = Environment.MachineName,
                    pair_id = peer?.PairId ?? "",
                    interval_seconds = _preferences.IntervalSeconds,
                    settings_version = _preferences.SettingsVersion,
                    settings_writer = _preferences.SettingsWriter,
                    sync_request = _preferences.SyncRequest,
                    revision = snapshot.Revision,
                    content_type = "vault",
                    vault_content_version = 2,
                    sync_mode = (enrollment ? DeviceSyncMode.Bidirectional : _preferences.Mode).ToWire()
                },
                trusted_key = invitation is null ? peer?.TrustedKey : null,
                expected_device_id = peer?.DeviceId,
                revision = advertisement,
                vault_id = snapshot.VaultId,
                content_key = snapshot.ContentKey,
                outgoing_directory = !enrollment && _preferences.Mode == DeviceSyncMode.ReceiveOnly ? null : snapshot.Directory,
                incoming_directory = directory
            }));
        }
        catch { DeleteOwned(directory); throw; }
        var transfer = new Transfer(started.GetProperty("handle").GetUInt64(), directory, snapshot, enrollment, advertisement);
        transfer.LockCancellation = session.SessionCancellationToken.Register(() =>
        {
            try { _transport.Request(new { operation = "cancel", handle = transfer.Handle }); } catch { }
        });
        if (token.IsCancellationRequested)
        {
            await StopAsync(transfer);
            token.ThrowIfCancellationRequested();
        }
        if (enrollment)
        {
            _code = started.TryGetProperty("code", out var code) ? code.GetString() ?? "" : "";
            _invitations = started.TryGetProperty("invitations", out var links) ? links.EnumerateArray().Select(item => item.GetString()!).ToList() : [];
            _phase = invitation is null ? "waiting" : "connecting"; Publish();
        }
        return transfer;
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                await TickAsync();
                await Task.Delay(500, _lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally { await StopAllAsync(); }
    }

    internal async Task TickAsync()
    {
        await _gate.WaitAsync(_lifetime.Token);
        try
        {
            foreach (var transfer in _retiring.Values.ToArray()) await StopAsync(transfer);
            CleanSnapshots();
            if (!session.IsUnlocked)
            {
                if (!_paused || _preferences is not null) await PauseCoreAsync(locked: true);
                return;
            }
            if (_time.GetUtcNow() < _loadAfter) return;
            await EnsurePreferencesAsync(_lifetime.Token);
            if (!_preferences!.Enabled && !_pairing)
            {
                if (!_paused) await PauseCoreAsync(locked: false);
                return;
            }
            _paused = false;
            using var token = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, session.SessionCancellationToken);
            foreach (var (id, transfer) in _transfers.ToArray())
            {
                try { await PollAsync(id, transfer, token.Token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception error)
                {
                    RecordFailure(error, transfer.Stage, transfer.Attempts);
                    Fail(id, transfer.Enrollment);
                    await StopAsync(transfer);
                    _transfers.Remove(id);
                }
            }
            if (!_pairing && _preferences.Peers.Count > 0 && _retiring.Count == 0)
                await MaintainConnectionsAsync(token.Token);
            CleanSnapshots();
        }
        catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested)
        { await PauseCoreAsync(locked: true); }
        catch (Exception error)
        {
            RecordFailure(error, "preparing");
            _loadAfter = _time.GetUtcNow().AddSeconds(10);
        }
        finally { Publish(); _gate.Release(); }
    }

    private async Task MaintainConnectionsAsync(CancellationToken token)
    {
        var snapshot = await CurrentSnapshotAsync(token);
        JsonElement? discovery = null;
        foreach (var peer in _preferences!.Peers.ToArray())
        {
            token.ThrowIfCancellationRequested();
            if (_transfers.ContainsKey(peer.DeviceId) || (_retry.TryGetValue(peer.DeviceId, out var retry) && retry.After > _time.GetUtcNow())) continue;
            if (string.CompareOrdinal(_preferences.DeviceId, peer.DeviceId) > 0)
            {
                _transfers[peer.DeviceId] = await StartTransferAsync(snapshot, peer, null, false, token);
                _statuses[peer.DeviceId] = "waiting";
                continue;
            }
            var knownAddresses = _preferences.Peers.Select(item => item.Address).Where(address => address.Length > 0).Distinct().Take(64).ToArray();
            discovery ??= await Task.Run(() => _transport.Request(new { operation = "discover", targets = knownAddresses }));
            token.ThrowIfCancellationRequested();
            var found = discovery.Value.GetProperty("devices").EnumerateArray().FirstOrDefault(item =>
                item.TryGetProperty("device_id", out var id) && id.GetString() == peer.DeviceId &&
                item.GetProperty("session").GetString() == peer.PairId && item.GetProperty("trusted").GetBoolean());
            if (found.ValueKind == JsonValueKind.Undefined)
            {
                _statuses[peer.DeviceId] = "offline";
                _force.Add(peer.DeviceId); // Rejoining always catches up, including interval mode.
                continue;
            }
            peer.Address = found.GetProperty("address").GetString()!;
            peer.Port = found.GetProperty("port").GetInt32();
            var parts = (found.GetProperty("revision").GetString() ?? "").Split(':', 4);
            var changed = snapshot.Revision != peer.LastSentRevision || parts[0] != peer.LastReceivedRevision;
            var policyChanged = parts.Length > 2 && ulong.TryParse(parts[1], out var version) &&
                (version > _preferences.SettingsVersion || version == _preferences.SettingsVersion && string.CompareOrdinal(parts[2], _preferences.SettingsWriter) > 0);
            var requested = parts.Length > 3 && ulong.TryParse(parts[3], out var request) && request != peer.LastReceivedRequest;
            var elapsed = _lastAttempts.TryGetValue(peer.DeviceId, out var last) ? _time.GetElapsedTime(last) : TimeSpan.MaxValue;
            if (!DeviceSyncCadence.IsDue(_preferences.IntervalSeconds, elapsed, changed, requested || policyChanged || _force.Contains(peer.DeviceId)))
            { _statuses[peer.DeviceId] = changed ? "scheduled" : "online"; continue; }
            var candidates = new[] { peer.Address }.Concat(found.TryGetProperty("addresses", out var alternatives) && alternatives.ValueKind == JsonValueKind.Array
                ? alternatives.EnumerateArray().Select(item => item.GetString() ?? "") : []).Where(item => item.Length > 0).Distinct().Take(8);
            var invitation = $"monica-lan:2:{string.Join(',', candidates)}:{peer.Port}:{peer.TrustedKey}";
            _transfers[peer.DeviceId] = await StartTransferAsync(snapshot, peer, invitation, false, token);
            _lastAttempts[peer.DeviceId] = _time.GetTimestamp();
            _statuses[peer.DeviceId] = "syncing";
        }
    }

    private async Task PollAsync(string id, Transfer transfer, CancellationToken token)
    {
        var progress = _transport.Request(new { operation = "progress", handle = transfer.Handle });
        var phase = progress.GetProperty("phase").GetString();
        if (phase is "sending" or "receiving" or "transferring" && progress.TryGetProperty("total", out var totalBytes))
            UpdateProgress(new(phase, 0, 0, progress.GetProperty("transferred").GetInt64(), totalBytes.GetInt64()));
        transfer.Stage = phase == "failed" && progress.TryGetProperty("failed_at", out var failedAt)
            ? failedAt.GetString() ?? transfer.Stage : phase ?? transfer.Stage;
        if (progress.TryGetProperty("connection_attempt", out var attempt)) transfer.Attempts = attempt.GetInt32();
        if (progress.TryGetProperty("remote_address", out var remoteAddress) && remoteAddress.ValueKind == JsonValueKind.String)
            transfer.RemoteAddress = remoteAddress.GetString() ?? "";
        if (transfer.Enrollment || phase != "waiting") _phase = phase ?? "preparing";
        else if (!_pairing) _phase = "idle";
        else if (phase != "waiting") _statuses[id] = "syncing";
        if (phase == "waiting" && !transfer.Enrollment)
        {
            var revision = _preferences!.Mode == DeviceSyncMode.ReceiveOnly ? ReceiveOnlyRevision : await data.GetRevisionAsync(token);
            if (Advertisement(revision) != transfer.Advertisement)
            { await StopAsync(transfer); _transfers.Remove(id); }
            return;
        }
        if (phase == "received" && !transfer.ApplyAttempted)
        {
            var remotePeer = progress.GetProperty("peer");
            var pairingOnly = transfer.Enrollment && remotePeer.GetProperty("revision").GetString() == DevicePairingSnapshot.Revision;
            var receiveContent = !pairingOnly && _preferences!.Mode != DeviceSyncMode.SendOnly &&
                (!remotePeer.TryGetProperty("receive_content", out var received) || received.GetBoolean());
            if (receiveContent && !CanApply()) return;
            transfer.ApplyAttempted = true; // A rejected batch must never be retried on every progress tick.
            transfer.Stage = "applying";
            transfer.Peer = remotePeer.Clone();
            try
            {
                token.ThrowIfCancellationRequested();
                var remote = transfer.Peer;
                if (remote.GetProperty("content_type").GetString() != "vault" || !remote.TryGetProperty("vault_content_version", out var contentVersion) || contentVersion.GetInt32() != 2)
                    throw new LanSyncException("unsupported_protocol");
                if (remote.GetProperty("device_id").GetString() == _preferences!.DeviceId) throw new LanSyncException("same_device");
                var changed = receiveContent ? await data.ApplyAsync(transfer.Root, remote, token) : 0;
                if (changed > 0) PasswordsChanged?.Invoke(this, EventArgs.Empty);
                _preferences.AcceptInterval(remote.GetProperty("interval_seconds").GetInt32(),
                    remote.GetProperty("settings_version").GetUInt64(), remote.GetProperty("settings_writer").GetString() ?? "");
                if (transfer.Enrollment)
                {
                    var remoteId = remote.GetProperty("device_id").GetString()!;
                    var pairing = remote.GetProperty("pairing");
                    var saved = _preferences.Peers.FirstOrDefault(item => item.DeviceId == remoteId);
                    if (saved is null)
                    {
                        saved = new DeviceSyncPeer { DeviceId = remoteId };
                        _preferences.Peers.Add(saved);
                    }
                    saved.Name = remote.GetProperty("name").GetString() ?? "Device";
                    saved.PairId = pairing.GetProperty("pair_id").GetString()!;
                    saved.TrustedKey = pairing.GetProperty("trusted_key").GetString()!;
                    saved.Address = transfer.RemoteAddress;
                    _preferences.Enabled = true;
                    _force.Add(remoteId);
                }
                // Remember trust before acknowledging, so a crash cannot strand the other device.
                transfer.Stage = "remembering";
                await store.SaveAsync(_preferences, token);
                token.ThrowIfCancellationRequested();
                _transport.Request(new { operation = "acknowledge", handle = transfer.Handle, success = true });
            }
            catch
            {
                try { _transport.Request(new { operation = "acknowledge", handle = transfer.Handle, success = false }); } catch { }
                throw;
            }
        }
        if (phase is not ("completed" or "failed" or "cancelled")) return;
        if (phase == "completed")
        {
            _phase = "completed";
            _progress = null;
            var remote = transfer.Peer;
            var remoteId = remote.GetProperty("device_id").GetString()!;
            var peer = _preferences!.Peers.FirstOrDefault(item => item.DeviceId == remoteId);
            if (peer is not null)
            {
                var contentSynced = transfer.Snapshot.Revision != DevicePairingSnapshot.Revision;
                if (contentSynced)
                {
                    peer.LastSyncedAt = _time.GetUtcNow();
                    peer.LastSentRevision = transfer.Snapshot.Revision;
                    peer.LastReceivedRevision = remote.GetProperty("revision").GetString() ?? "";
                    peer.LastReceivedRequest = remote.TryGetProperty("sync_request", out var request) ? request.GetUInt64() : 0;
                }
                if (transfer.RemoteAddress.Length > 0) peer.Address = transfer.RemoteAddress;
                _statuses[remoteId] = contentSynced ? "online" : "waiting"; _retry.Remove(remoteId);
                if (!transfer.Enrollment) _force.Remove(remoteId);
            }
            await store.SaveAsync(_preferences, token);
            _error = null;
        }
        else
        {
            Fail(id, transfer.Enrollment);
            var code = progress.TryGetProperty("error", out var error) ? error.GetString() ?? "device_sync_failed" : "device_sync_failed";
            RecordFailure(new LanSyncException(code), transfer.Stage, transfer.Attempts);
        }
        if (transfer.Enrollment) { _pairing = false; _code = ""; _invitations.Clear(); _phase = phase!; }
        await StopAsync(transfer);
        _transfers.Remove(id);
    }

    private void Fail(string id, bool enrollment)
    {
        _statuses[id] = "offline"; _force.Add(id);
        var count = Math.Min(6, _retry.GetValueOrDefault(id).Count + 1);
        _retry[id] = (count, _time.GetUtcNow().AddSeconds(Math.Min(60, 1 << count)));
        if (enrollment) { _pairing = false; _code = ""; _invitations.Clear(); _phase = "failed"; }
    }

    private async Task PauseCoreAsync(bool locked)
    {
        foreach (var transfer in _transfers.Values) await StopAsync(transfer);
        _transfers.Clear();
        await data.PauseAsync();
        _snapshot = null;
        CleanSnapshots();
        _pairing = false; _code = ""; _invitations.Clear(); _phase = locked ? "locked" : "paused";
        if (_preferences is not null)
        {
            foreach (var peer in _preferences.Peers) { _statuses[peer.DeviceId] = _phase; _force.Add(peer.DeviceId); }
            Publish();
        }
        if (locked) _preferences = null;
        _paused = true;
    }

    private async Task StopAsync(Transfer transfer)
    {
        _retiring[transfer.Handle] = transfer;
        try
        {
            _transport.Request(new { operation = "cancel", handle = transfer.Handle });
            for (var i = 0; i < 20; i++)
            {
                var phase = _transport.Request(new { operation = "progress", handle = transfer.Handle }).GetProperty("phase").GetString();
                if (phase is "completed" or "failed" or "cancelled")
                {
                    _transport.Request(new { operation = "release", handle = transfer.Handle });
                    transfer.LockCancellation.Dispose();
                    _retiring.Remove(transfer.Handle);
                    DeleteOwned(transfer.Root);
                    return;
                }
                await Task.Delay(50);
            }
        }
        catch { /* Retain ownership and retry; never delete files still used by native code. */ }
    }

    private void CleanSnapshots()
    {
        var used = _transfers.Values.Concat(_retiring.Values).Select(transfer => transfer.Snapshot.Directory).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_snapshot is not null) used.Add(_snapshot.Directory);
        foreach (var path in _snapshots.Where(path => !used.Contains(path)).ToArray())
            if (DeleteOwned(path)) _snapshots.Remove(path);
    }

    private bool DeleteOwned(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(Path.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
        try { if (Directory.Exists(full)) Directory.Delete(full, true); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private async Task StopAllAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await PauseCoreAsync(locked: true);
            foreach (var transfer in _retiring.Values.ToArray()) await StopAsync(transfer);
            CleanSnapshots();
            if (_retiring.Count == 0)
                try { if (Directory.Exists(_root) && !Directory.EnumerateFileSystemEntries(_root).Any()) Directory.Delete(_root); } catch (IOException) { }
        }
        finally { _gate.Release(); }
    }

    private void RecordFailure(Exception error, string stage, int attempts = 0)
    {
        _error = ErrorCode(error);
        _failureStage = stage;
        _failureKind = error.GetType().Name;
        _failureOperation = (error as LanSyncException)?.Operation ?? "";
        _failureRequestBytes = (error as LanSyncException)?.RequestBytes ?? 0;
        _connectionAttempts = attempts;
        _phase = "failed"; _progress = null;
    }

    private static string ErrorCode(Exception error)
    {
        if (error is LanSyncException lan) return lan.Code;
        if (error is PlatformNotSupportedException or NotSupportedException) return "device_sync_unavailable";
        // Preserve only application-owned constant reasons, never exception payloads or secrets.
        if (error.Message is "invalid_password_payload" or "password_source_unavailable" or "device_sync_read_only" or
            "invalid_password_identity" or "password_identity_collision" or "password_changed" or "device_sync_write_failed" or
            "invalid_passkey_material" or "passkey_key_unavailable" or "passkey_conflict") return error.Message;
        return "device_sync_failed";
    }

    public void Dispose() => _lifetime.Cancel();
    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (_runner is not null) await _runner; else await StopAllAsync();
    }
}
