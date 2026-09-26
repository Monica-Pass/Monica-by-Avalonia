using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Serialization;

namespace Monica.Platform.Services;

/// <summary>
/// Writes a decoded database back out. <see cref="KdbxFile.Save"/> is not thread safe process-wide -
/// parallel saves have produced databases that reject the key that created them - so payloads are
/// built under a gate and re-opened before a caller is allowed to publish them.
/// </summary>
internal static class KeePassVaultWrite
{
    public const uint Kdbx31 = 0x0003_0001;
    public const uint Kdbx40 = 0x0004_0000;
    public const uint Kdbx41 = 0x0004_0001;

    private static readonly object SaveGate = new();
    private const int MaximumWriteAttempts = 5;

    private static readonly PropertyInfo? ForceVersionProperty = typeof(KdbxFile).GetProperty(
        "ForceVersion",
        BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// Reads the KDBX version out of the header bytes. The writer downgrades a 4.1 database to 4.0
    /// unless the version is pinned, and the version that was loaded is only reachable through
    /// internal state, so it is taken from the payload the caller already holds.
    /// </summary>
    public static uint? ReadFormatVersion(ReadOnlySpan<byte> content)
    {
        if (content.Length < 12)
        {
            return null;
        }

        var minor = BinaryPrimitives.ReadUInt16LittleEndian(content[8..]);
        var major = BinaryPrimitives.ReadUInt16LittleEndian(content[10..]);
        if (major is < 3 or > 4)
        {
            return null;
        }

        return ((uint)major << 16) | minor;
    }

    public static bool IsKnownFormatVersion(uint version) =>
        version is Kdbx31 or Kdbx40 or Kdbx41;

    /// <summary>
    /// Serializes the model and proves the payload unlocks with the same key while carrying the same
    /// structure. A failed proof is retried because the underlying writer is known to produce
    /// unreadable output under load, and a payload that never proves out is an error rather than a
    /// write.
    /// </summary>
    public static byte[] BuildVerifiedPayload(
        PwDatabase database,
        CompositeKey key,
        uint? formatVersion)
    {
        for (var attempt = 0; attempt < MaximumWriteAttempts; attempt++)
        {
            var payload = SaveOnce(database, formatVersion);
            if (Matches(payload, key, database, formatVersion))
            {
                return payload;
            }
        }

        throw KeePassVaultFaults.WriteFailed();
    }

    /// <summary>
    /// Structural summary of a tree: uuids, field names, value lengths and history depth. Titles and
    /// secrets never enter it, so it is safe to compare in a test failure message.
    /// </summary>
    public static string Fingerprint(PwGroup root)
    {
        var lines = new List<string>();
        Collect(root, lines);
        lines.Sort(StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))));
    }

    public static int CountEntries(PwGroup group)
    {
        var count = group.Entries.Count();
        foreach (var child in group.Groups)
        {
            count += CountEntries(child);
        }

        return count;
    }

    /// <summary>
    /// Replaces a file in one step: the payload lands in a temporary beside the target, is flushed to
    /// disk, and only then becomes the target. A vault that vanished half-way through a write is the
    /// failure this path exists to make impossible.
    /// </summary>
    public static async Task WriteAtomicAsync(string target, byte[] payload, CancellationToken cancellationToken)
    {
        var temporary = target + ".monica-tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? ".");
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 64 * 1024,
                             FileOptions.WriteThrough))
            {
                await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
                // Flush(true) is the fsync that makes the temporary file durable before the rename
                // replaces the vault with it.
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, target, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    public static string NormalizePath(string path)
    {
        var trimmed = path?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            throw KeePassVaultFaults.InvalidFile();
        }

        return Path.GetFullPath(trimmed);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover temporary file is untidy; the failed write is the fact the user needs.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static byte[] SaveOnce(PwDatabase database, uint? formatVersion)
    {
        lock (SaveGate)
        {
            using var stream = new MemoryStream();
            var file = new KdbxFile(database);
            if (formatVersion is { } version && IsKnownFormatVersion(version))
            {
                ForceVersionProperty?.SetValue(file, version);
            }

            file.Save(stream, database.RootGroup, KdbxFormat.Default, null);
            return stream.ToArray();
        }
    }

    private static bool Matches(byte[] payload, CompositeKey key, PwDatabase source, uint? formatVersion)
    {
        try
        {
            var probe = new PwDatabase();
            probe.MasterKey = key;
            new KdbxFile(probe).Load(new MemoryStream(payload, writable: false), KdbxFormat.Default, null);
            try
            {
                return probe.RootGroup is { } root
                    && CountEntries(root) == CountEntries(source.RootGroup)
                    && Fingerprint(root) == Fingerprint(source.RootGroup)
                    && SameUuid(probe.DataCipherUuid, source.DataCipherUuid)
                    && SameUuid(probe.KdfParameters.KdfUuid, source.KdfParameters.KdfUuid)
                    && probe.Compression == source.Compression
                    && (formatVersion is null || ReadFormatVersion(payload) == formatVersion);
            }
            finally
            {
                probe.Close();
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool SameUuid(PwUuid left, PwUuid right) =>
        left.ToHexString() == right.ToHexString();

    private static void Collect(PwGroup group, List<string> lines)
    {
        lines.Add($"G:{group.Uuid.ToHexString()}|{group.Name}");
        foreach (var entry in group.Entries)
        {
            lines.Add($"E:{entry.Uuid.ToHexString()}|{group.Uuid.ToHexString()}|{(int)entry.IconId}");
            foreach (var field in entry.Strings)
            {
                lines.Add(
                    $"S:{entry.Uuid.ToHexString()}|{field.Key}|{field.Value.Length}" +
                    (field.Value.IsProtected ? "|p" : ""));
            }

            foreach (var binary in entry.Binaries)
            {
                lines.Add($"B:{entry.Uuid.ToHexString()}|{binary.Key}|{binary.Value.Length}");
            }

            foreach (var history in entry.History)
            {
                lines.Add($"H:{entry.Uuid.ToHexString()}|{history.LastModificationTime.Ticks}");
            }
        }

        foreach (var child in group.Groups)
        {
            Collect(child, lines);
        }
    }
}
