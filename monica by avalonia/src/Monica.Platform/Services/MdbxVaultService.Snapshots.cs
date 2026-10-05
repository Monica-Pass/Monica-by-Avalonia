using System.Security.Cryptography;
using Monica.Core.Models;
using Monica.Data.Mdbx;

namespace Monica.Platform.Services;

public sealed partial class MdbxVaultService
{
    public async Task<Stream> OpenSnapshotStreamAsync(LocalMdbxDatabase database, CancellationToken cancellationToken = default)
    {
        var source = SnapshotSourcePath(database);
        var scratch = CreateSnapshotScratch(Path.GetTempPath());
        try
        {
            var path = Path.Combine(scratch, "snapshot.mdbx");
            await PrepareSnapshotAsync(database, source, path, scratch, cancellationToken);
            var hash = await HashSnapshotAsync(path, cancellationToken);
            return new MdbxOwnedSnapshotStream(path, scratch, source, hash);
        }
        catch
        {
            CleanupSnapshotScratch(scratch);
            throw;
        }
    }

    public async Task CreateSnapshotAsync(LocalMdbxDatabase database, string destination, CancellationToken cancellationToken = default)
    {
        var target = Path.GetFullPath(destination);
        RequireSnapshotDestinationAbsent(target);
        var scratch = CreateSnapshotScratch(Path.GetDirectoryName(target)!);
        try
        {
            var path = Path.Combine(scratch, "snapshot.mdbx");
            await PrepareSnapshotAsync(database, SnapshotSourcePath(database), path, scratch, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(path, target, overwrite: false);
        }
        finally
        {
            CleanupSnapshotScratch(scratch);
        }
    }

    public async Task CommitSnapshotUploadAsync(LocalMdbxDatabase database, Stream snapshot,
        Func<bool, CancellationToken, Task> commitMetadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commitMetadata);
        var source = SnapshotSourcePath(database);
        if (snapshot is not MdbxOwnedSnapshotStream owned || !SameSnapshotPath(source, owned.SourcePath))
        {
            throw new MdbxSnapshotException("validation-failed");
        }

        using var lease = await RequireReplacementCoordinator().AcquireSnapshotCommitAsync(source, cancellationToken);
        var scratch = CreateSnapshotScratch(Path.GetTempPath());
        try
        {
            var current = Path.Combine(scratch, "current.mdbx");
            await CreatePortableSnapshotCopyAsync(source, current, scratch, cancellationToken);
            var hash = await HashSnapshotAsync(current, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await commitMetadata(CryptographicOperations.FixedTimeEquals(hash, owned.ContentHash), cancellationToken);
        }
        finally
        {
            CleanupSnapshotScratch(scratch);
        }
    }

    private async Task<MdbxNativeBackupInfo> PrepareSnapshotAsync(LocalMdbxDatabase database,
        string source, string destination, string scratch, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(database.EncryptedPassword))
        {
            throw new MdbxSnapshotException("credential-or-integrity");
        }

        RejectExternalSnapshotFiles(source);
        var bridge = RequireSnapshotBridge();
        var info = await CreatePortableSnapshotCopyAsync(source, destination, scratch, cancellationToken);
        await RequireCurrentSnapshotFormatAsync(destination, cancellationToken);
        // Unlock/health checks may migrate or create sidecars, so they run on a second disposable copy.
        // The source and the candidate subsequently published/uploaded remain unopened and unchanged.
        var validationPath = Path.Combine(scratch, "validation-" + Guid.NewGuid().ToString("N") + ".mdbx");
        await bridge.CreatePortableBackupAsync(destination, validationPath, cancellationToken);
        var validation = await bridge.ValidateSnapshotAsync(validationPath, database.EncryptedPassword, cancellationToken);
        if (validation.VaultId != info.VaultId)
        {
            throw new MdbxSnapshotException("mismatched-vault");
        }

        if (validation.HasExternalBlobReferences)
        {
            throw new MdbxSnapshotException("external-blobs");
        }

        RejectExternalSnapshotFiles(source);
        cancellationToken.ThrowIfCancellationRequested();
        return info;
    }

    private async Task<MdbxNativeBackupInfo> CreatePortableSnapshotCopyAsync(
        string source, string destination, string scratch, CancellationToken cancellationToken)
    {
        var bridge = RequireSnapshotBridge();
        if (File.Exists(source + "-wal") || File.Exists(source + "-shm") || File.Exists(source + "-journal"))
        {
            return await bridge.CreatePortableBackupAsync(source, destination, cancellationToken);
        }

        // Opening a checkpointed WAL-mode file read-only can still create empty WAL/SHM. With no
        // sidecars and no writable handles, freeze the main file first and open only our owned copy.
        var input = Path.Combine(scratch, "source-input-" + Guid.NewGuid().ToString("N") + ".mdbx");
        try
        {
            using var guard = OpenSnapshotReplacementGuard(source);
            RequireNoLiveSnapshotSidecars(source);
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(source, input, overwrite: false);
            RequireNoLiveSnapshotSidecars(source);
        }
        catch (IOException) { throw new MdbxSnapshotException("vault-busy"); }
        catch (UnauthorizedAccessException) { throw new MdbxSnapshotException("vault-busy"); }
        return await bridge.CreatePortableBackupAsync(input, destination, cancellationToken);
    }

    private async Task RequireCurrentSnapshotFormatAsync(string path, CancellationToken cancellationToken)
    {
        var header = await _nativeBridge.InspectMigrationAsync(path, cancellationToken);
        if (header is null || !header.Initialized || header.UnknownCriticalExtensions || header.RequiresUpgrade ||
            header.FormatVersion != _nativeBridge.WritableStorageFormat ||
            header.FormatVersion != header.TargetFormatVersion || header.SchemaVersion != header.TargetSchemaVersion)
        {
            throw new MdbxSnapshotException("unsupported-format");
        }
    }

    private IMdbxNativeSnapshotBridge RequireSnapshotBridge() =>
        _nativeBridge is IMdbxNativeSnapshotBridge bridge && _nativeBridge.IsAvailable
            ? bridge
            : throw new MdbxSnapshotException("native-unavailable");

    private IMdbxVaultFileReplacementCoordinator RequireReplacementCoordinator() =>
        _replacementCoordinator ?? throw new MdbxSnapshotException("coordination-unavailable");

    private static string SnapshotSourcePath(LocalMdbxDatabase database) =>
        Path.GetFullPath(database.WorkingCopyPath ?? database.FilePath);

    private static bool SameSnapshotPath(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string CreateSnapshotScratch(string parent)
    {
        Directory.CreateDirectory(parent);
        var directory = Path.Combine(Path.GetFullPath(parent), ".monica-snapshot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void CleanupSnapshotScratch(string directory)
    {
        if (!Path.GetFileName(directory).StartsWith(".monica-snapshot-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid snapshot scratch directory.");
        }

        try { Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void RequireSnapshotDestinationAbsent(string path)
    {
        foreach (var artifact in new[] { path, path + "-wal", path + "-shm", path + "-journal", path + ".blobs" })
        {
            try { _ = File.GetAttributes(artifact); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            throw new MdbxSnapshotException("destination-exists");
        }
    }

    private static void RejectExternalSnapshotFiles(string path)
    {
        var directory = path + ".blobs";
        if (File.Exists(directory) || Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
        {
            throw new MdbxSnapshotException("external-blobs");
        }
    }

    private static async Task<byte[]> HashSnapshotAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await SHA256.HashDataAsync(stream, cancellationToken);
    }
}
