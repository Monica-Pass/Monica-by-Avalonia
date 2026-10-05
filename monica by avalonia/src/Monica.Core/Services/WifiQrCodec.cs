using System.Text;
using Monica.Core.Models;

namespace Monica.Core.Services;

public sealed record WifiQrImport(WifiNetworkData Network, string Password);

public static class WifiQrCodec
{
    public const int MaximumPayloadCharacters = 4096;

    public static string? Build(WifiNetworkData network, string password)
    {
        if (network.Ssid.Length > MaximumPayloadCharacters || password.Length > MaximumPayloadCharacters
            || !network.SupportsConnectionQr || string.IsNullOrWhiteSpace(network.Ssid)
            || (!network.IsOpen && string.IsNullOrEmpty(password)))
        {
            return null;
        }

        var auth = network.IsOpen ? "nopass" : network.Security == nameof(WifiSecurity.WEP) ? "WEP" : "WPA";
        var payload = $"WIFI:T:{auth};S:{Escape(network.Ssid)};"
            + (network.IsOpen ? "" : $"P:{Escape(password)};")
            + (network.HiddenNetwork ? "H:true;" : "") + ";";
        return payload.Length <= MaximumPayloadCharacters ? payload : null;
    }

    public static WifiQrImport? Parse(string? raw)
    {
        var payload = raw?.Trim();
        if (payload is null || payload.Length > MaximumPayloadCharacters
            || !payload.StartsWith("WIFI:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var token = new StringBuilder();
        for (var index = 5; index <= payload.Length; index++)
        {
            if (index < payload.Length && payload[index] == '\\')
            {
                if (++index == payload.Length)
                {
                    return null;
                }

                token.Append(payload[index]);
            }
            else if (index == payload.Length || payload[index] == ';')
            {
                var text = token.ToString();
                var separator = text.IndexOf(':');
                if (separator > 0 && !fields.TryAdd(text[..separator], text[(separator + 1)..]))
                {
                    return null;
                }

                token.Clear();
            }
            else
            {
                token.Append(payload[index]);
            }
        }

        if (!fields.TryGetValue("S", out var ssid) || string.IsNullOrWhiteSpace(ssid))
        {
            return null;
        }

        var security = fields.GetValueOrDefault("T", "WPA").ToUpperInvariant() switch
        {
            "NOPASS" or "" => nameof(WifiSecurity.NONE),
            "WEP" => nameof(WifiSecurity.WEP),
            "WPA" or "WPA2" => nameof(WifiSecurity.WPA2_WPA3),
            "WPA3" or "SAE" => nameof(WifiSecurity.WPA3),
            _ => null
        };
        if (security is null)
        {
            return null;
        }

        return new(new(ssid, security,
            string.Equals(fields.GetValueOrDefault("H"), "true", StringComparison.OrdinalIgnoreCase)),
            security == nameof(WifiSecurity.NONE) ? "" : fields.GetValueOrDefault("P", ""));
    }

    private static string Escape(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is '\\' or ';' or ',' or ':' or '"')
            {
                result.Append('\\');
            }

            result.Append(character);
        }

        return result.ToString();
    }
}
