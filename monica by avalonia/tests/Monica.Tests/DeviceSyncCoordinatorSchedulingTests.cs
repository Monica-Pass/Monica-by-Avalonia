using System.Text;
using System.Text.Json;
using Monica.App.Services;
using Monica.Core.Services;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed class DeviceSyncCoordinatorSchedulingTests
{
    [Theory]
    [InlineData(DeviceSyncMode.Bidirectional, 1)]
    [InlineData(DeviceSyncMode.SendOnly, 0)]
    [InlineData(DeviceSyncMode.ReceiveOnly, 1)]
    public async Task Direction_controls_source_export_and_destination_apply(DeviceSyncMode mode, int applies)
    {
        await using var fixture = await Fixture.CreateAsync(mode: mode);
        fixture.Data.FailExports = mode == DeviceSyncMode.ReceiveOnly;
        await fixture.CompleteRoundAsync();
        Assert.Equal(mode, fixture.Sync.State.Mode);
        Assert.Equal(mode.ToWire(), fixture.Network.LastStarted.GetProperty("device").GetProperty("sync_mode").GetString());
        Assert.Equal(applies, fixture.Data.Applies);
        Assert.NotNull(Assert.Single(fixture.Sync.State.Peers).LastSyncedAt);
        if (mode == DeviceSyncMode.ReceiveOnly)
        {
            Assert.Equal(0, fixture.Data.Revisions);
            Assert.Equal(0, fixture.Data.Prepares);
            Assert.Equal(JsonValueKind.Null, fixture.Network.LastStarted.GetProperty("outgoing_directory").ValueKind);
            Assert.Equal("", fixture.Network.LastStarted.GetProperty("content_key").GetString());
        }
        else Assert.Equal(1, fixture.Data.Prepares);
    }

    [Theory]
    [InlineData(DeviceSyncMode.SendOnly, true)]
    [InlineData(DeviceSyncMode.Bidirectional, false)]
    public async Task A_round_with_no_incoming_contents_does_not_wait_for_the_edit_gate(DeviceSyncMode mode, bool received)
    {
        await using var fixture = await Fixture.CreateAsync(mode: mode);
        fixture.Network.ReceiveContent = received;
        fixture.Sync.CanApply = () => false;
        await fixture.CompleteRoundAsync();
        Assert.Equal("completed", fixture.Sync.State.Phase);
        Assert.Equal(0, fixture.Data.Applies);
    }

    [Fact]
    public async Task Changing_direction_stops_the_old_listener_and_keeps_pairing_and_frequency()
    {
        await using var fixture = await Fixture.CreateAsync(host: true);
        await fixture.Sync.TickAsync();
        var revisions = fixture.Data.Revisions;
        var peer = Assert.Single(fixture.Sync.State.Peers);
        await fixture.Sync.SetModeAsync(DeviceSyncMode.ReceiveOnly);
        Assert.Equal(1, fixture.Network.Cancellations);
        Assert.Equal(DeviceSyncMode.ReceiveOnly, fixture.Sync.State.Mode);
        Assert.Equal(peer.DeviceId, Assert.Single(fixture.Sync.State.Peers).DeviceId);
        Assert.Equal(300, fixture.Sync.State.IntervalSeconds);
        fixture.Data.FailExports = true;
        await fixture.CompleteRoundAsync();
        Assert.Equal(2, fixture.Network.Listeners);
        Assert.Equal(revisions, fixture.Data.Revisions);
    }

    [Fact]
    public async Task Protected_preferences_default_old_records_to_bidirectional_and_keep_direction_on_reload()
    {
        var protector = new FixtureProtector();
        var path = Path.Combine(TestTempPaths.CreateDirectoryPath(), "peers.protected");
        var original = new DeviceSyncPreferences
        {
            DeviceId = "a",
            Peers = [
            new DeviceSyncPeer { DeviceId = "z", Name = "Peer", PairId = "pair", TrustedKey = new string('A',43) }]
        };
        var legacy = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(original))!.AsObject();
        legacy.Remove("Mode");
        await File.WriteAllTextAsync(path, await protector.ProtectAsync(legacy.ToJsonString()));
        var store = new DeviceSyncPreferenceStore(protector, path);
        var loaded = await store.LoadAsync();
        Assert.Equal(DeviceSyncMode.Bidirectional, loaded.Mode);
        foreach (var mode in Enum.GetValues<DeviceSyncMode>())
        {
            loaded.Mode = mode;
            await store.SaveAsync(loaded);
            var reloaded = await store.LoadAsync();
            Assert.Equal(mode, reloaded.Mode);
            Assert.Equal(original.ReplicaKey, reloaded.ReplicaKey);
            Assert.Equal(original.Peers[0].TrustedKey, Assert.Single(reloaded.Peers).TrustedKey);
        }
    }

    [Fact]
    public async Task Preparation_failure_clears_completed_progress_and_keeps_existing_pairing()
    {
        await using var fixture = await Fixture.CreateAsync(host: true);
        fixture.Data.ReportPreparation = true;
        fixture.Data.FailPrepare = true;
        var failure = new TaskCompletionSource<DeviceSyncState>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Sync.Changed += (_, state) => { if (state.Error is not null) failure.TrySetResult(state); };
        fixture.Sync.Start();
        var state = await failure.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("invalid_passkey_material", state.Error);
        Assert.Equal("preparing", state.FailureStage);
        Assert.Equal("failed", state.Phase);
        Assert.Null(state.Progress);
        Assert.Equal("preparation_failed", Assert.Single(state.Peers).Status);
        Assert.Equal(0, fixture.Network.Listeners);
        fixture.Data.FailPrepare = false;
        fixture.Time.Advance(10);
        await fixture.Sync.TickAsync();
        Assert.Null(fixture.Sync.State.Error);
        Assert.Null(fixture.Sync.State.Progress);
        Assert.Single(fixture.Sync.State.Peers);
        Assert.Equal(1, fixture.Network.Listeners);
    }

    [Fact]
    public async Task Prepared_listener_waits_for_first_sync_without_leaving_a_full_progress_bar()
    {
        await using var fixture = await Fixture.CreateAsync(host: true);
        fixture.Data.ReportPreparation = true;
        var waiting = new TaskCompletionSource<DeviceSyncState>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Sync.Changed += (_, state) => { if (fixture.Network.Listeners > 0) waiting.TrySetResult(state); };
        fixture.Sync.Start();
        var state = await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(state.Error);
        Assert.Equal("idle", state.Phase);
        Assert.Null(state.Progress);
        Assert.Equal("first_sync_pending", Assert.Single(state.Peers).Status);
        Assert.Null(Assert.Single(state.Peers).LastSyncedAt);
    }

    [Fact]
    public async Task Interval_changes_wait_for_the_deadline_but_manual_sync_and_reconnect_catch_up()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.CompleteRoundAsync();
        Assert.Equal(1, fixture.Network.Connections);
        fixture.Data.Revision = "local-2";
        fixture.Time.Advance(299);
        await fixture.Sync.TickAsync();
        Assert.Equal(1, fixture.Network.Connections);
        Assert.Equal("scheduled", Assert.Single(fixture.Sync.State.Peers).Status);
        fixture.Time.Advance(1);
        await fixture.CompleteRoundAsync();
        Assert.Equal(2, fixture.Network.Connections);
        fixture.Time.Advance(1000);
        await fixture.Sync.TickAsync();
        Assert.Equal(2, fixture.Network.Connections); // No edits: no transfer.
        await fixture.Sync.SyncNowAsync();
        await fixture.CompleteRoundAsync();
        Assert.Equal(3, fixture.Network.Connections);
        fixture.Network.Offline = true;
        await fixture.Sync.TickAsync();
        fixture.Network.Offline = false;
        await fixture.CompleteRoundAsync();
        Assert.Equal(4, fixture.Network.Connections);
    }

    [Fact]
    public async Task Remote_manual_requests_and_equal_version_frequency_writers_trigger_a_transfer()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.CompleteRoundAsync();
        fixture.Network.RemoteRequest = 1;
        await fixture.CompleteRoundAsync();
        Assert.Equal(2, fixture.Network.Connections);
        fixture.Network.RemoteVersion = 1;
        fixture.Network.RemoteWriter = "z";
        fixture.Network.RemoteInterval = 900;
        await fixture.Sync.SetIntervalAsync(60); // Also version 1, writer a.
        await fixture.CompleteRoundAsync();
        Assert.Equal(900, fixture.Sync.State.IntervalSeconds);
    }

    [Fact]
    public async Task Waiting_listener_restarts_when_frequency_or_manual_request_changes()
    {
        await using var fixture = await Fixture.CreateAsync(host: true);
        await fixture.Sync.TickAsync();
        Assert.Equal(1, fixture.Network.Listeners);
        await fixture.Sync.SetIntervalAsync(60);
        await fixture.Sync.TickAsync();
        Assert.Equal(2, fixture.Network.Listeners);
        Assert.Equal(60, fixture.Network.LastStarted.GetProperty("device").GetProperty("interval_seconds").GetInt32());
        await fixture.Sync.SyncNowAsync();
        await fixture.Sync.TickAsync();
        Assert.Equal(3, fixture.Network.Listeners);
        Assert.Equal(1UL, fixture.Network.LastStarted.GetProperty("device").GetProperty("sync_request").GetUInt64());
    }

    [Fact]
    public async Task Pausing_stops_transfers_and_resuming_does_not_require_pairing_again()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.CompleteRoundAsync();
        await fixture.Sync.SetEnabledAsync(false);
        fixture.Data.Revision = "changed-while-paused";
        await fixture.Sync.TickAsync();
        Assert.Equal(1, fixture.Network.Connections);
        Assert.True(fixture.Data.Pauses > 0);
        Assert.Equal("paused", fixture.Sync.State.Phase);
        await fixture.Sync.SetEnabledAsync(true);
        await fixture.CompleteRoundAsync();
        Assert.Equal(2, fixture.Network.Connections);
        Assert.Single(fixture.Sync.State.Peers);
    }

    [Fact]
    public async Task Failed_applies_use_backoff_and_are_not_repeated_on_each_progress_tick()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Data.FailApply = true;
        await fixture.Sync.TickAsync();
        await fixture.Sync.TickAsync();
        Assert.Equal(1, fixture.Data.Applies);
        await fixture.Sync.TickAsync();
        fixture.Time.Advance(1);
        await fixture.Sync.TickAsync();
        Assert.Equal(1, fixture.Data.Applies);
        fixture.Time.Advance(1);
        await fixture.Sync.TickAsync();
        await fixture.Sync.TickAsync();
        Assert.Equal(2, fixture.Data.Applies);
    }

    [Fact]
    public async Task A_local_apply_failure_reports_its_stage_instead_of_looking_like_a_connection_failure()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Data.FailApply = true;
        await fixture.Sync.TickAsync();
        await fixture.Sync.TickAsync();
        Assert.Equal("applying", fixture.Sync.State.FailureStage);
        Assert.Equal("invalid_record", fixture.Sync.State.Error);
        Assert.Equal("replica_merge", fixture.Sync.State.FailureOperation);
        Assert.Equal(65536, fixture.Sync.State.FailureRequestBytes);
        Assert.DoesNotContain("synthetic", JsonSerializer.Serialize(fixture.Sync.State));
    }

    [Fact]
    public async Task Locking_clears_decrypted_state_without_reloading_protected_preferences_in_a_loop()
    {
        await using var fixture = await Fixture.CreateAsync(host: true);
        await fixture.Sync.TickAsync();
        fixture.Session.MarkLocked();
        await fixture.Sync.TickAsync();
        var loads = fixture.Secrets.Reads;
        await fixture.Sync.TickAsync();
        await fixture.Sync.TickAsync();
        Assert.Equal(loads, fixture.Secrets.Reads);
        Assert.True(fixture.Data.Pauses > 0);
        Assert.Empty(fixture.Sync.State.Code);
        Assert.Empty(fixture.Sync.State.Invitations!);
        Assert.Equal("locked", fixture.Sync.State.Phase);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required DeviceSyncCoordinator Sync { get; init; }
        public required FakeNetwork Network { get; init; }
        public required FakeData Data { get; init; }
        public required FakeTime Time { get; init; }
        public required VaultSessionService Session { get; init; }
        public required FixtureProtector Secrets { get; init; }

        public static async Task<Fixture> CreateAsync(bool host = false, DeviceSyncMode mode = DeviceSyncMode.Bidirectional)
        {
            var time = new FakeTime();
            var session = new VaultSessionService();
            session.MarkUnlocked();
            var protector = new FixtureProtector();
            var store = new DeviceSyncPreferenceStore(protector, Path.Combine(TestTempPaths.CreateDirectoryPath(), "peers.protected"));
            await store.SaveAsync(new DeviceSyncPreferences
            {
                DeviceId = host ? "z" : "a",
                IntervalSeconds = 300,
                Mode = mode,
                Peers = [new DeviceSyncPeer { DeviceId = host ? "a" : "z", Name = "Peer", PairId = "pair", TrustedKey = new string('A', 43) }]
            });
            var data = new FakeData();
            var network = new FakeNetwork();
            return new Fixture
            {
                Data = data,
                Network = network,
                Time = time,
                Session = session,
                Secrets = protector,
                Sync = new DeviceSyncCoordinator(store, data, session, network, time, TestTempPaths.CreateDirectoryPath())
            };
        }

        public async Task CompleteRoundAsync()
        {
            await Sync.TickAsync();
            await Sync.TickAsync();
            await Sync.TickAsync();
        }

        public async ValueTask DisposeAsync() { await Sync.DisposeAsync(); Session.Dispose(); }
    }

    private sealed class FakeData : IDeviceSyncDataProvider, IDeviceSyncProgressSource
    {
        public event Action<DeviceSyncProgress>? Progress;
        public bool ReportPreparation { get; set; }
        public bool FailPrepare { get; set; }
        public bool FailExports { get; set; }
        public int Prepares { get; private set; }
        public int Revisions { get; private set; }
        public string Revision { get; set; } = "local-1";
        public bool FailApply { get; set; }
        public int Applies { get; private set; }
        public int Pauses { get; private set; }
        public Task<string> GetRevisionAsync(CancellationToken token)
        {
            Revisions++;
            if (FailExports) throw new LanSyncException("invalid_passkey_material");
            return Task.FromResult(Revision);
        }
        public Task<DeviceSyncSnapshot> PrepareAsync(string directory, CancellationToken token)
        {
            Prepares++;
            if (FailExports) throw new LanSyncException("invalid_passkey_material");
            if (ReportPreparation) Progress?.Invoke(new("preparing", 851, 851));
            if (FailPrepare) throw new LanSyncException("invalid_passkey_material");
            Directory.CreateDirectory(directory);
            return Task.FromResult(new DeviceSyncSnapshot("monica-passwords-v1", Revision, directory, new string('A', 43)));
        }
        public Task<uint> ApplyAsync(string directory, JsonElement peer, CancellationToken token)
        {
            Applies++;
            if (FailApply) throw new LanSyncException("invalid_record", "replica_merge", 65536);
            return Task.FromResult(0U);
        }
        public Task PauseAsync() { Pauses++; return Task.CompletedTask; }
    }

    private sealed class FakeTime : TimeProvider
    {
        private long _seconds;
        public void Advance(long seconds) => _seconds += seconds;
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => _seconds;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddSeconds(_seconds);
    }

    private sealed class FakeNetwork : IDeviceSyncTransport
    {
        private readonly Dictionary<ulong, string> _phases = [];
        private ulong _next;
        public int Connections { get; private set; }
        public int Listeners { get; private set; }
        public int Cancellations { get; private set; }
        public bool ReceiveContent { get; set; } = true;
        public bool Offline { get; set; }
        public ulong RemoteRequest { get; set; }
        public ulong RemoteVersion { get; set; }
        public string RemoteWriter { get; set; } = "";
        public int RemoteInterval { get; set; } = 300;
        public JsonElement LastStarted { get; private set; }

        public JsonElement Request(object request)
        {
            var input = JsonSerializer.SerializeToElement(request);
            switch (input.GetProperty("operation").GetString())
            {
                case "discover":
                    return JsonSerializer.SerializeToElement(new
                    {
                        devices = Offline ? Array.Empty<object>() : [
                        new { device_id = "z", session = "pair", trusted = true, address = "127.0.0.1", port = 1234,
                            revision = $"remote-1:{RemoteVersion}:{RemoteWriter}:{RemoteRequest}" }
                    ]
                    });
                case "start":
                case "connect":
                    LastStarted = input;
                    var connecting = input.GetProperty("operation").GetString() == "connect";
                    if (connecting) Connections++; else Listeners++;
                    var handle = ++_next;
                    _phases[handle] = connecting ? "received" : "waiting";
                    return JsonSerializer.SerializeToElement(new { handle });
                case "progress":
                    return JsonSerializer.SerializeToElement(new
                    {
                        phase = _phases[input.GetProperty("handle").GetUInt64()],
                        peer = new
                        {
                            device_id = "z",
                            name = "Peer",
                            content_type = "vault",
                            vault_content_version = 2,
                            revision = "remote-1",
                            interval_seconds = RemoteInterval,
                            settings_version = RemoteVersion,
                            settings_writer = RemoteWriter,
                            sync_request = RemoteRequest,
                            receive_content = ReceiveContent,
                            pairing = new { content_key = new string('A', 43) }
                        }
                    });
                case "acknowledge":
                    _phases[input.GetProperty("handle").GetUInt64()] = input.GetProperty("success").GetBoolean() ? "completed" : "failed";
                    break;
                case "cancel":
                    Cancellations++;
                    _phases[input.GetProperty("handle").GetUInt64()] = "cancelled";
                    break;
                case "release":
                    _phases.Remove(input.GetProperty("handle").GetUInt64());
                    break;
            }
            return JsonSerializer.SerializeToElement(new { });
        }
    }

    private sealed class FixtureProtector : ISecretProtector
    {
        public int Reads { get; private set; }
        public PlatformIntegrationCapability Capability => new(PlatformFeatureKeys.SecretProtection, PlatformFeatureStatus.Available, "Fixture");
        public Task<string> ProtectAsync(string value, CancellationToken token = default) => Task.FromResult(Convert.ToBase64String(Encoding.UTF8.GetBytes(value)));
        public Task<string> UnprotectAsync(string value, CancellationToken token = default)
        {
            Reads++;
            return Task.FromResult(Encoding.UTF8.GetString(Convert.FromBase64String(value)));
        }
    }
}
