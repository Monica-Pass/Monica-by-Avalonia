using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Monica.Core.Passkeys;

/// <summary>
/// Credential-id compatibility layer, mirroring Monica for Android: a credential id may arrive as
/// Base64URL (the WebAuthn wire format) or as UUID text (what a 16-byte id looks like when another
/// vault renders it), and both spellings have to compare equal.
/// </summary>
public static class PasskeyCredentialId
{
    private const int UuidByteLength = 16;

    public static byte[] NewRandom()
    {
        var bytes = new byte[UuidByteLength];
        RandomNumberGenerator.Fill(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return bytes;
    }

    /// <summary>Comparison form: a 16-byte id collapses to canonical UUID text, anything else to Base64URL.</summary>
    public static string? Normalize(string? credentialId)
    {
        var raw = credentialId?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        if (TryReadUuidText(raw, out var uuidBytes))
        {
            return ToUuidText(uuidBytes);
        }

        if (!PasskeyBase64Url.TryDecode(raw, out var decoded))
        {
            return raw;
        }

        return decoded.Length == UuidByteLength ? ToUuidText(decoded) : PasskeyBase64Url.Encode(decoded);
    }

    /// <summary>The id/rawId string a WebAuthn response carries: always Base64URL without padding.</summary>
    public static string? ToWebAuthnId(string? credentialId)
    {
        var raw = credentialId?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        if (TryReadUuidText(raw, out var uuidBytes))
        {
            return PasskeyBase64Url.Encode(uuidBytes);
        }

        return PasskeyBase64Url.TryDecode(raw, out var decoded)
            ? PasskeyBase64Url.Encode(decoded)
            : raw;
    }

    public static string ToUuidText(byte[] bytes)
    {
        if (bytes.Length != UuidByteLength)
        {
            throw new ArgumentException("A UUID is 16 bytes.", nameof(bytes));
        }

        // Guid.Parse of the hex run keeps the bytes big-endian; Guid.ToByteArray would byte-swap the
        // first three fields and produce an id no other WebAuthn implementation recognises.
        return Guid.Parse(Convert.ToHexString(bytes)).ToString("D");
    }

    private static bool TryReadUuidText(string value, [NotNullWhen(true)] out byte[]? bytes)
    {
        bytes = null;
        if (!Guid.TryParseExact(value, "D", out var guid))
        {
            return false;
        }

        var hex = guid.ToString("N");
        var parsed = new byte[UuidByteLength];
        for (var index = 0; index < parsed.Length; index++)
        {
            parsed[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
        }

        bytes = parsed;
        return true;
    }
}
