using System.Diagnostics.CodeAnalysis;

namespace Monica.Core.Passkeys;

/// <summary>
/// Base64URL without padding, which is what WebAuthn transports use for every byte field.
/// Decoding accepts padded or URL-safe input because relying parties differ on both.
/// </summary>
public static class PasskeyBase64Url
{
    public static string Encode(ReadOnlySpan<byte> payload) =>
        Convert.ToBase64String(payload).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Encode(string payload) =>
        Encode(System.Text.Encoding.UTF8.GetBytes(payload));

    public static bool TryDecode(string? value, [NotNullWhen(true)] out byte[]? payload)
    {
        payload = null;
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        var normalised = trimmed.Replace('-', '+').Replace('_', '/');
        normalised = normalised.PadRight(normalised.Length + (4 - normalised.Length % 4) % 4, '=');
        try
        {
            payload = Convert.FromBase64String(normalised);
            return true;
        }
        catch (FormatException)
        {
            payload = null;
            return false;
        }
    }
}
