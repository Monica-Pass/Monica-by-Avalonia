using System.Buffers.Binary;
using System.Security.Cryptography;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Serialization;

namespace Monica.Platform.Services;

public sealed class KeePassVaultService : IKeePassVaultService
{
    private const int MaximumFileBytes = 256 * 1024 * 1024;
    private const int MaximumEntryCount = 100_000;
    private const int MaximumGroupCount = 50_000;
    private const int MaximumAttachmentBytes = 64 * 1024 * 1024;
    private const long MaximumTotalAttachmentBytes = 256L * 1024 * 1024;
    private static readonly HashSet<string> TotpFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "otp",
        "TOTP Seed"
    };

    public async Task<KeePassVaultSummary> InspectAsync(
        string path,
        string? password,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return new KeePassVaultSummary(path, false, "KDBX file not found.", 0, 0);
        }

        var file = new FileInfo(path);
        EnsureFileSize(file.Length);
        var content = await File.ReadAllBytesAsync(path, cancellationToken);
        var snapshot = await ReadAsync(content, file.Name, password, cancellationToken);
        return new KeePassVaultSummary(
            path,
            true,
            "KeePass database is ready to import.",
            snapshot.Groups.Count,
            snapshot.Entries.Count);
    }

    public Task<KeePassVaultSnapshot> ReadAsync(
        ReadOnlyMemory<byte> content,
        string fileName,
        string? password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureFileSize(content.Length);
        var safeFileName = NormalizeFileName(fileName);
        return Task.Run(
            () => ReadCore(content, safeFileName, password, cancellationToken),
            cancellationToken);
    }

    private static KeePassVaultSnapshot ReadCore(
        ReadOnlyMemory<byte> content,
        string fileName,
        string? password,
        CancellationToken cancellationToken)
    {
        var database = new PwDatabase();
        try
        {
            var key = new CompositeKey();
            if (password is not null)
            {
                key.AddUserKey(new KcpPassword(password));
            }

            database.MasterKey = key;
            using var stream = new MemoryStream(content.ToArray(), writable: false);
            new KdbxFile(database).Load(stream, KdbxFormat.Default, null);
            cancellationToken.ThrowIfCancellationRequested();
            return CreateSnapshot(database, fileName, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (KeePassVaultException)
        {
            throw;
        }
        catch (OldFormatException)
        {
            throw CreateSafeException(
                KeePassVaultError.UnsupportedFormat,
                "This KeePass database format is not supported.");
        }
        catch (InvalidCompositeKeyException)
        {
            throw CreateInvalidFileException();
        }
        catch (Exception)
        {
            throw CreateInvalidFileException();
        }
        finally
        {
            if (database.IsOpen)
            {
                database.Close();
            }
        }
    }

    private static KeePassVaultSnapshot CreateSnapshot(
        PwDatabase database,
        string fileName,
        CancellationToken cancellationToken)
    {
        var root = database.RootGroup ?? throw CreateInvalidFileException();
        var groups = new List<KeePassGroupSnapshot>();
        var entries = new List<KeePassEntrySnapshot>();
        long totalAttachmentBytes = 0;

        TraverseGroup(
            root,
            parentPath: "",
            isRoot: true,
            groups,
            entries,
            ref totalAttachmentBytes,
            cancellationToken);

        var rootUuid = root.Uuid.ToHexString();
        var databaseName = string.IsNullOrWhiteSpace(database.Name)
            ? Path.GetFileNameWithoutExtension(fileName)
            : database.Name.Trim();
        return new KeePassVaultSnapshot(
            CreateDatabaseId(root.Uuid.UuidBytes),
            databaseName,
            fileName,
            rootUuid,
            groups,
            entries);
    }

    private static void TraverseGroup(
        PwGroup group,
        string parentPath,
        bool isRoot,
        ICollection<KeePassGroupSnapshot> groups,
        ICollection<KeePassEntrySnapshot> entries,
        ref long totalAttachmentBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var name = NormalizeDisplayText(group.Name, "Untitled group");
        var path = isRoot ? "" : CombineGroupPath(parentPath, name);
        if (!isRoot)
        {
            if (groups.Count >= MaximumGroupCount)
            {
                throw CreateResourceLimitException();
            }

            groups.Add(new KeePassGroupSnapshot(
                name,
                path,
                group.Uuid.ToHexString(),
                group.ParentGroup?.Uuid.ToHexString()));
        }

        foreach (var entry in group.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entries.Count >= MaximumEntryCount)
            {
                throw CreateResourceLimitException();
            }

            entries.Add(CreateEntry(entry, path, ref totalAttachmentBytes));
        }

        foreach (var child in group.Groups)
        {
            TraverseGroup(
                child,
                path,
                isRoot: false,
                groups,
                entries,
                ref totalAttachmentBytes,
                cancellationToken);
        }
    }

    private static KeePassEntrySnapshot CreateEntry(
        PwEntry entry,
        string groupPath,
        ref long totalAttachmentBytes)
    {
        var customFields = entry.Strings
            .Where(item => !PwDefs.IsStandardField(item.Key) && !TotpFieldNames.Contains(item.Key))
            .Select(item => new KeePassCustomFieldSnapshot(
                item.Key,
                item.Value.ReadString(),
                item.Value.IsProtected))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var attachments = new List<KeePassAttachmentSnapshot>();
        foreach (var binary in entry.Binaries)
        {
            var length = checked((long)binary.Value.Length);
            if (length > MaximumAttachmentBytes ||
                totalAttachmentBytes > MaximumTotalAttachmentBytes - length)
            {
                throw CreateResourceLimitException();
            }

            totalAttachmentBytes += length;
            attachments.Add(new KeePassAttachmentSnapshot(
                binary.Key,
                binary.Key,
                binary.Value.ReadData()));
        }

        return new KeePassEntrySnapshot(
            entry.Strings.ReadSafe(PwDefs.TitleField),
            entry.Strings.ReadSafe(PwDefs.UserNameField),
            entry.Strings.ReadSafe(PwDefs.PasswordField),
            entry.Strings.ReadSafe(PwDefs.UrlField),
            entry.Strings.ReadSafe(PwDefs.NotesField),
            ReadTotp(entry),
            groupPath,
            entry.Uuid.ToHexString(),
            entry.ParentGroup?.Uuid.ToHexString() ?? "",
            ToDateTimeOffset(entry.CreationTime),
            ToDateTimeOffset(entry.LastModificationTime),
            customFields,
            attachments);
    }

    private static string ReadTotp(PwEntry entry)
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

    private static long CreateDatabaseId(byte[] rootUuid)
    {
        var hash = SHA256.HashData(rootUuid);
        var id = BinaryPrimitives.ReadInt64LittleEndian(hash) & long.MaxValue;
        return id == 0 ? 1 : id;
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value)
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

    private static string CombineGroupPath(string parentPath, string name) =>
        string.IsNullOrWhiteSpace(parentPath) ? name : $"{parentPath}/{name}";

    private static string NormalizeDisplayText(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string NormalizeFileName(string? fileName)
    {
        var normalized = Path.GetFileName(fileName?.Trim());
        return string.IsNullOrWhiteSpace(normalized) ? "database.kdbx" : normalized;
    }

    private static void EnsureFileSize(long length)
    {
        if (length <= 0)
        {
            throw CreateInvalidFileException();
        }

        if (length > MaximumFileBytes)
        {
            throw CreateResourceLimitException();
        }
    }

    private static KeePassVaultException CreateInvalidFileException() =>
        CreateSafeException(
            KeePassVaultError.InvalidCredentialsOrFile,
            "The KeePass database could not be unlocked. Check the password and file integrity.");

    private static KeePassVaultException CreateResourceLimitException() =>
        CreateSafeException(
            KeePassVaultError.ResourceLimitExceeded,
            "The KeePass database exceeds the safe import limits.");

    private static KeePassVaultException CreateSafeException(KeePassVaultError error, string message) =>
        new(error, message);
}
