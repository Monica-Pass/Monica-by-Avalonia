using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Monica.Data;
using Monica.Platform.Services;

namespace Monica.App.Services;

[JsonConverter(typeof(JsonStringEnumConverter<DeviceSyncMode>))]
public enum DeviceSyncMode { Bidirectional, SendOnly, ReceiveOnly }

public static class DeviceSyncModeNames
{
    public static string ToWire(this DeviceSyncMode mode) => mode switch
    {
        DeviceSyncMode.Bidirectional => "bidirectional",
        DeviceSyncMode.SendOnly => "send_only",
        DeviceSyncMode.ReceiveOnly => "receive_only",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}

public sealed class DeviceSyncPeer
{
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string PairId { get; set; } = "";
    public string TrustedKey { get; set; } = "";
    public string Address { get; set; } = "";
    public int Port { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public string LastSentRevision { get; set; } = "";
    public string LastReceivedRevision { get; set; } = "";
    public ulong LastReceivedRequest { get; set; }
    public override string ToString() => $"DeviceSyncPeer({Name})";
}

public sealed class DeviceSyncPreferences
{
    public int Version { get; set; } = 1;
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public string ReplicaKey { get; set; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; }
    public ulong SettingsVersion { get; set; }
    public string SettingsWriter { get; set; } = "";
    public ulong SyncRequest { get; set; }
    public List<DeviceSyncPeer> Peers { get; set; } = [];
    public DeviceSyncMode Mode { get; set; } = DeviceSyncMode.Bidirectional;

    public void SetInterval(int seconds)
    {
        if (seconds != 0 && seconds is not (>= 60 and <= 86400)) throw new ArgumentOutOfRangeException(nameof(seconds));
        IntervalSeconds = seconds;
        if (SettingsVersion >= long.MaxValue) throw new InvalidOperationException("device_sync_settings_limit");
        SettingsVersion++;
        SettingsWriter = DeviceId;
    }

    public bool AcceptInterval(int seconds, ulong version, string writer)
    {
        if ((seconds != 0 && seconds is not (>= 60 and <= 86400)) || writer.Length > 64 || version > long.MaxValue) return false;
        if (version < SettingsVersion || (version == SettingsVersion && string.CompareOrdinal(writer, SettingsWriter) <= 0)) return false;
        IntervalSeconds = seconds;
        SettingsVersion = version;
        SettingsWriter = writer;
        return true;
    }
}

public static class DeviceSyncCadence
{
    public static bool IsDue(int intervalSeconds, TimeSpan elapsed, bool changed, bool force = false) =>
        force || (changed && elapsed >= TimeSpan.FromSeconds(intervalSeconds == 0 ? 2 : intervalSeconds));
}

/// <summary>
/// Pairing keys stay outside synchronized vault data, protected by the OS credential service. Where no
/// such service is wired up (Linux and macOS today) they are never written to disk: they live in this
/// process only, so pairing works for the session instead of failing on every save.
/// </summary>
public sealed class DeviceSyncPreferenceStore(ISecretProtector protector, string? path = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path = path ?? MonicaAppDataPaths.GetPath("device-sync/peers.protected");
    private string? _sessionOnlyJson;

    /// <summary>False when pairings only last until the app exits.</summary>
    public bool PersistsAcrossRestarts => protector.Capability.IsUsable;

    public async Task<DeviceSyncPreferences> LoadAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (!PersistsAcrossRestarts)
            {
                return _sessionOnlyJson is null ? new DeviceSyncPreferences() : Parse(_sessionOnlyJson);
            }

            if (!File.Exists(_path)) return new DeviceSyncPreferences();
            var protectedText = await File.ReadAllTextAsync(_path, token);
            var plain = await protector.UnprotectAsync(protectedText, token);
            return Parse(plain);
        }
        finally { _gate.Release(); }
    }

    private static DeviceSyncPreferences Parse(string plain)
    {
        var settings = JsonSerializer.Deserialize<DeviceSyncPreferences>(plain) ?? throw new InvalidDataException("device_sync_preferences_invalid");
        if (settings.Version != 1 || settings.Peers.Count > 8 || settings.DeviceId.Length > 64 || !Enum.IsDefined(settings.Mode) ||
            (settings.IntervalSeconds != 0 && settings.IntervalSeconds is not (>= 60 and <= 86400)))
            throw new InvalidDataException("device_sync_preferences_invalid");
        foreach (var peer in settings.Peers)
        {
            if (peer.DeviceId.Length is < 1 or > 64 || peer.PairId.Length is < 1 or > 64 ||
                LanPairingInvitation.Parse($"monica-lan:1:127.0.0.1:1:{peer.TrustedKey}") is null)
                throw new InvalidDataException("device_sync_preferences_invalid");
        }
        return settings;
    }

    public async Task SaveAsync(DeviceSyncPreferences settings, CancellationToken token = default)
    {
        if (!PersistsAcrossRestarts)
        {
            var json = JsonSerializer.Serialize(settings);
            await _gate.WaitAsync(token);
            try { _sessionOnlyJson = json; }
            finally { _gate.Release(); }
            return;
        }

        var protectedText = await protector.ProtectAsync(JsonSerializer.Serialize(settings), token);
        await _gate.WaitAsync(token);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllTextAsync(temporary, protectedText, token);
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); _gate.Release(); }
    }
}
