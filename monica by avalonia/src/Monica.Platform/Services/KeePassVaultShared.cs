using System.Buffers.Binary;
using System.Security.Cryptography;
using KeePassLib;
using KeePassLib.Serialization;

namespace Monica.Platform.Services;

internal static class KeePassVaultLimits
{
    public const int MaximumFileBytes = 256 * 1024 * 1024;
    public const int MaximumEntryCount = 100_000;
    public const int MaximumGroupCount = 50_000;
    public const int MaximumAttachmentBytes = 64 * 1024 * 1024;
    public const long MaximumTotalAttachmentBytes = 256L * 1024 * 1024;

    public static void EnsureFileSize(long length)
    {
        if (length <= 0)
        {
            throw KeePassVaultFaults.InvalidFile();
        }

        if (length > MaximumFileBytes)
        {
            throw KeePassVaultFaults.ResourceLimitExceeded();
        }
    }

    public static long AddAttachmentBytes(ulong length, long total)
    {
        if (length > MaximumAttachmentBytes || total > MaximumTotalAttachmentBytes - (long)length)
        {
            throw KeePassVaultFaults.ResourceLimitExceeded();
        }

        return total + (long)length;
    }
}

internal static class KeePassVaultFaults
{
    public static KeePassVaultException InvalidFile() => new(
        KeePassVaultError.InvalidCredentialsOrFile,
        "The KeePass database could not be unlocked. Check the password and file integrity.");

    public static KeePassVaultException UnsupportedFormat() => new(
        KeePassVaultError.UnsupportedFormat,
        "This KeePass database format is not supported.");

    public static KeePassVaultException ResourceLimitExceeded() => new(
        KeePassVaultError.ResourceLimitExceeded,
        "The KeePass database exceeds the safe opening limits.");
}

internal static class KeePassVaultText
{
    private static readonly string[] TotpFieldNames = ["otp", "TOTP Seed"];

    public static string ReadTotp(PwEntry entry)
    {
        foreach (var name in TotpFieldNames)
        {
            var value = entry.Strings.ReadSafe(name).Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "";
    }

    public static bool IsCustomField(string key) =>
        !PwDefs.IsStandardField(key) && !TotpFieldNames.Contains(key, StringComparer.OrdinalIgnoreCase);

    public static string NormalizeDisplayText(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    public static string NormalizeFileName(string? fileName)
    {
        var normalized = Path.GetFileName(fileName?.Trim());
        return string.IsNullOrWhiteSpace(normalized) ? "database.kdbx" : normalized;
    }

    public static DateTimeOffset ToDateTimeOffset(DateTime value)
    {
        if (value == default)
        {
            return DateTimeOffset.UtcNow;
        }

        return value.Kind switch
        {
            DateTimeKind.Utc => new DateTimeOffset(value),
            DateTimeKind.Local => new DateTimeOffset(value).ToUniversalTime(),
            _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
        };
    }

    public static long CreateDatabaseId(byte[] rootUuid)
    {
        var hash = SHA256.HashData(rootUuid);
        var id = BinaryPrimitives.ReadInt64LittleEndian(hash) & long.MaxValue;
        return id == 0 ? 1 : id;
    }

    public static string GroupPathOf(PwGroup group, PwGroup root)
    {
        if (ReferenceEquals(group, root))
        {
            return "";
        }

        var segments = new List<string>();
        for (PwGroup? cursor = group; cursor is not null && !ReferenceEquals(cursor, root); cursor = cursor.ParentGroup)
        {
            segments.Add(NormalizeDisplayText(cursor.Name, "Untitled group"));
        }

        segments.Reverse();
        return string.Join('/', segments);
    }
}
