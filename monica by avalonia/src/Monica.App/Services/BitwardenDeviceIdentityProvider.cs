using Monica.Data;

namespace Monica.App.Services;

public interface IBitwardenDeviceIdentityProvider
{
    string DeviceIdentifier { get; }
    string DeviceName { get; }
}

public sealed class BitwardenDeviceIdentityProvider : IBitwardenDeviceIdentityProvider
{
    private const string IdentifierFileName = "bitwarden-device-id";
    private readonly Lazy<string> _deviceIdentifier = new(LoadOrCreateIdentifier);

    public string DeviceIdentifier => _deviceIdentifier.Value;

    public string DeviceName
    {
        get
        {
            var machineName = string.IsNullOrWhiteSpace(Environment.MachineName)
                ? "Windows device"
                : Environment.MachineName.Trim();
            return $"{machineName} (Monica desktop)";
        }
    }

    private static string LoadOrCreateIdentifier()
    {
        var identityDirectory = MonicaAppDataPaths.GetPath("identity");
        Directory.CreateDirectory(identityDirectory);
        var path = Path.Combine(identityDirectory, IdentifierFileName);
        if (TryReadIdentifier(path, out var existing))
        {
            return existing;
        }

        var identifier = Guid.NewGuid().ToString("N");
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, identifier);
            File.Move(temporaryPath, path, overwrite: true);
            return identifier;
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch
            {
                // A stale non-secret temporary identifier is harmless and can be retried later.
            }
        }
    }

    private static bool TryReadIdentifier(string path, out string identifier)
    {
        identifier = "";
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var value = File.ReadAllText(path).Trim();
            if (!Guid.TryParseExact(value, "N", out var parsed))
            {
                return false;
            }

            identifier = parsed.ToString("N");
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
