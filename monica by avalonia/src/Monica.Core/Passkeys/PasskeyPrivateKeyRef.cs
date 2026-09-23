using System.Security.Cryptography;
using System.Text;

namespace Monica.Core.Passkeys;

/// <summary>
/// Reference scheme for passkey private-key material, mirroring Monica for Android: the credential row
/// keeps a stable non-secret reference and the key itself lives in a separate protected store.
///
/// Old rows written before this split carry raw PKCS#8 (or a platform keystore alias) in the same
/// column, so every consumer has to accept a reference and bare material alike.
/// </summary>
public static class PasskeyPrivateKeyRef
{
    public const string ReferencePrefix = "monica-passkey-key-ref-v1:";
    public const string StorageKeyPrefix = "passkey_private_key_v1_";

    public static bool IsProtectedReference(string? value) =>
        (value ?? string.Empty).Trim().StartsWith(ReferencePrefix, StringComparison.Ordinal);

    public static string? StorageKeyFrom(string? reference)
    {
        var trimmed = reference?.Trim();
        return IsProtectedReference(trimmed) ? trimmed![ReferencePrefix.Length..] : null;
    }

    public static string ToReference(string storageKey) => ReferencePrefix + storageKey;

    /// <summary>
    /// The storage key covers the key material's digest as well as the credential identity, so two
    /// registrations for the same account never overwrite each other's private key.
    /// </summary>
    public static string StorageKeyFor(string credentialId, string rpId, string userId, string privateKeyPkcs8Base64)
    {
        var digestSource = string.Join('|', credentialId, rpId, userId, ShortSha(privateKeyPkcs8Base64));
        return StorageKeyPrefix + ShortSha(digestSource, 16);
    }

    private static string ShortSha(string value, int bytes = 12)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(digest.AsSpan(0, bytes)).ToLowerInvariant();
    }
}
