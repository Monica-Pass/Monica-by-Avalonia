using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Monica.Core.Passkeys;

/// <summary>
/// WebAuthn RP ID normalization and loose-equivalence matcher, mirroring Monica for Android.
///
/// Matching stays conservative: case and a trailing dot are removed, an internationalised domain is
/// folded to its ASCII form, and there is no cross-domain aliasing of any kind.
/// </summary>
public static class PasskeyRpId
{
    public static string? Normalize(string? rpId)
    {
        var trimmed = (rpId?.Trim() ?? string.Empty).TrimEnd('.');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        var lower = trimmed.ToLowerInvariant();
        try
        {
            return new IdnMapping { UseStd3AsciiRules = true }.GetAscii(lower);
        }
        catch (ArgumentException)
        {
            return lower;
        }
    }

    public static bool IsEquivalent(string? left, string? right)
    {
        var normalizedLeft = Normalize(left);
        var normalizedRight = Normalize(right);
        return normalizedLeft is not null && normalizedRight is not null && normalizedLeft == normalizedRight;
    }

    /// <summary>
    /// The relying-party hash is taken over the exact identifier the caller supplied, the same way the
    /// Android authenticator hashes the rpId straight out of the request. Normalization is deliberately
    /// kept out of the hash so a credential registered under one spelling verifies under that spelling.
    /// </summary>
    public static byte[] Hash(string rpId) => SHA256.HashData(Encoding.UTF8.GetBytes(rpId));
}
