using System.Security.Cryptography;

namespace Monica.App.Services;

/// <summary>Enrollment exchanges trust before opening the user's password sources.</summary>
internal static class DevicePairingSnapshot
{
    // This reserved revision is shared with Android. The encrypted empty snapshot is
    // still readable by earlier protocol-2 clients that also merge during enrollment.
    public const string Revision = "pairing-only-v1";

    public static DeviceSyncSnapshot Create(IDeviceSyncTransport transport, string directory, string deviceId)
    {
        Directory.CreateDirectory(directory);
        var journal = Path.Combine(directory, ".pairing.replica");
        var key = RandomNumberGenerator.GetBytes(32);
        ulong? handle = null;
        try
        {
            handle = transport.Request(new
            {
                operation = "replica_open",
                path = journal,
                device_id = deviceId,
                key = Convert.ToBase64String(key).TrimEnd('=').Replace('+', '-').Replace('/', '_')
            }).GetProperty("handle").GetUInt64();
            var exported = transport.Request(new
            {
                operation = "replica_export",
                handle = handle.Value,
                path = Path.Combine(directory, "changes.mdbxsync")
            });
            return new DeviceSyncSnapshot("monica-passwords-v1", Revision, directory,
                exported.GetProperty("content_key").GetString()!);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            try { if (handle is not null) transport.Request(new { operation = "replica_close", handle = handle.Value }); }
            finally { if (File.Exists(journal)) File.Delete(journal); }
        }
    }
}
