using System.Security.Cryptography;
using System.Text;

namespace Monica.Data.Mdbx;

/// <summary>
/// Android materializes one hidden root project per vault and uses it as the write target for every
/// entry that has no folder. Its id is derived from the vault id so both clients can recompute it
/// without storing it. The engine rejects a write whose target project does not exist, so a vault
/// without this project cannot receive Android's folder-less entries at all.
/// </summary>
public static class MdbxAndroidRoot
{
    public const string Title = ".monica-root";

    public static bool IsRootTitle(string? title) =>
        string.Equals(title?.Trim(), Title, StringComparison.OrdinalIgnoreCase);

    public static string ProjectIdFor(string vaultId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultId);
        return NameUuidFromBytes(Encoding.UTF8.GetBytes($"monica-root:{vaultId}"));
    }

    /// <summary>
    /// Reproduces java.util.UUID.nameUUIDFromBytes: MD5 over the bytes with the version-3 and
    /// RFC-4122 variant nibbles stamped in, printed big-endian. Guid's byte-array constructor would
    /// byte-swap the first three groups, so the text is assembled from the digest directly.
    /// </summary>
    private static string NameUuidFromBytes(byte[] bytes)
    {
        var digest = MD5.HashData(bytes);
        digest[6] = (byte)((digest[6] & 0x0F) | 0x30);
        digest[8] = (byte)((digest[8] & 0x3F) | 0x80);
        var hex = Convert.ToHexString(digest).ToLowerInvariant();
        return $"{hex[..8]}-{hex.Substring(8, 4)}-{hex.Substring(12, 4)}-{hex.Substring(16, 4)}-{hex.Substring(20, 12)}";
    }
}
