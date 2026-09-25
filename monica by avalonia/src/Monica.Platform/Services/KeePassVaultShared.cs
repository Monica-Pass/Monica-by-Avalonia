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

    public static KeePassVaultException WriteFailed() => new(
        KeePassVaultError.WriteFailed,
        "The KeePass database could not be written in a state that verifies.");

    public static KeePassVaultException ConcurrentChange() => new(
        KeePassVaultError.ConcurrentChange,
        "The KeePass file changed on disk after it was opened. Reload it before saving.");

    public static KeePassVaultException NoSourceFile() => new(
        KeePassVaultError.NoSourceFile,
        "This KeePass session was not opened from a file, so it cannot save in place.");
}

internal static class KeePassVaultText
{
    /// <summary>
    /// Field names Android writes a TOTP seed under. The first is the one Monica writes back.
    /// </summary>
    public static IReadOnlyList<string> TotpFieldNames { get; } = ["otp", "TOTP Seed"];

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

    /// <summary>
    /// A custom field name as it can safely reach the XML layer. Control characters have no
    /// representation in the document format other clients parse, so an unusable name is dropped
    /// rather than written.
    /// </summary>
    public static string NormalizeFieldName(string? value)
    {
        var name = value?.Trim() ?? "";
        return name.Length is 0 or > 128 || name.Any(char.IsControl) ? "" : name;
    }

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
