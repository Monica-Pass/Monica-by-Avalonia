using System.Text.Json;
using System.Text.Json.Nodes;

namespace Monica.Core.Models;

public enum WifiSecurity
{
    NONE,
    WEP,
    WPA_WPA2,
    WPA2_WPA3,
    WPA3,
    WPA2_ENTERPRISE,
    WPA3_ENTERPRISE
}

public sealed record WifiNetworkData(string Ssid, string Security, bool HiddenNetwork)
{
    public bool IsOpen => Security == nameof(WifiSecurity.NONE);
    public bool SupportsConnectionQr => Security is nameof(WifiSecurity.NONE) or nameof(WifiSecurity.WEP)
        or nameof(WifiSecurity.WPA_WPA2) or nameof(WifiSecurity.WPA2_WPA3) or nameof(WifiSecurity.WPA3);

    public static bool TryRead(string metadata, string fallbackSsid, out WifiNetworkData network)
    {
        network = new(fallbackSsid, nameof(WifiSecurity.WPA2_WPA3), false);
        try
        {
            var fields = ReadObject(metadata);
            foreach (var key in new[] { "ssid", "security", "hiddenNetwork" })
            {
                if (fields.ContainsKey(key) && fields[key] is null)
                {
                    return false;
                }
            }

            var ssid = fields["ssid"]?.GetValue<string>();
            network = new(
                string.IsNullOrWhiteSpace(ssid) ? fallbackSsid : ssid,
                fields["security"]?.GetValue<string>() ?? nameof(WifiSecurity.WPA2_WPA3),
                fields["hiddenNetwork"]?.GetValue<bool>() ?? false);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            return false;
        }
    }

    // Update only the fields owned by the common editor. Android enterprise, proxy, IP,
    // MAC settings and future extension fields remain in the original JSON graph.
    public string ApplyTo(string metadata)
    {
        var fields = ReadObject(metadata);
        fields["ssid"] = Ssid;
        fields["security"] = Security;
        fields["hiddenNetwork"] = HiddenNetwork;
        return fields.ToJsonString();
    }

    private static JsonObject ReadObject(string metadata) => string.IsNullOrWhiteSpace(metadata)
        ? new JsonObject()
        : JsonNode.Parse(metadata) as JsonObject ?? throw new JsonException("Invalid Wi-Fi metadata.");
}
