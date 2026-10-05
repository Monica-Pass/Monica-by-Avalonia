using System.Runtime.InteropServices;
using Monica.Data.Mdbx;
using Monica.Mdbx.Ffi;

namespace Monica.Platform.Services;

public sealed partial class MdbxUniffiNativeBridge
{
    private const string SnapshotValidationDeviceId = "monica-avalonia-snapshot-validation";

    public Task<MdbxNativeBackupInfo> CreatePortableBackupAsync(
        string sourcePath,
        string destination,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSnapshotNativeAvailable();
            string ownedDestination;
            try
            {
                ownedDestination = Path.GetFullPath(destination);
                // The engine also checks these artifacts and uses persist_noclobber. A successful
                // return is the ownership boundary: on any native failure we must not delete a file
                // another writer might have placed here after this preflight.
                EnsureSnapshotDestinationAbsent(ownedDestination);
            }
            catch (Exception exception) when (exception is not MdbxSnapshotException)
            {
                throw new MdbxSnapshotException("validation-failed");
            }

            MdbxBackupInfo backup;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                // This entry point opens the source read-only and merges its committed WAL pages
                // into a verified single file. It does not migrate, unlock, or reveal source payload.
                backup = MdbxFfi.CreatePortableBackup(sourcePath, ownedDestination);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw SnapshotNativeFailure(exception, "validation-failed");
            }

            // Native online backup is synchronous and cannot be interrupted. Wait for its result
            // before honoring cancellation, and remove only a target this invocation created.
            if (cancellationToken.IsCancellationRequested)
            {
                DeleteOwnedSnapshotAfterCancellation(ownedDestination);
                cancellationToken.ThrowIfCancellationRequested();
            }

            return new MdbxNativeBackupInfo(
                backup.VaultId, backup.FormatVersion, backup.SchemaVersion, backup.FileSizeBytes);
        }, cancellationToken);

    public Task<MdbxNativeSnapshotValidation> ValidateSnapshotAsync(
        string stagingPath,
        string password,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSnapshotNativeAvailable();
            try
            {
                // OpenVault performs migration and unlock bookkeeping. Only the caller's copied,
                // disposable staging file may be passed here; the original source must stay intact.
                using var vault = MdbxFfi.OpenVault(stagingPath, password, SnapshotValidationDeviceId);
                cancellationToken.ThrowIfCancellationRequested();
                var health = vault.HealthCheck();
                cancellationToken.ThrowIfCancellationRequested();
                // Rust's Healthy permits Info/Warning and rejects Error/Critical. Keep the same
                // policy, with an explicit severity check in case a runtime returns inconsistent data.
                if (!health.Healthy || health.Issues.Any(issue =>
                        issue.Severity is MdbxHealthIssueSeverity.Error or MdbxHealthIssueSeverity.Critical))
                {
                    throw new MdbxSnapshotException("credential-or-integrity");
                }

                var info = vault.Info();
                var diagnostics = vault.DiagnosticsSummary();
                cancellationToken.ThrowIfCancellationRequested();
                // The native inventory includes current/deleted attachment chunks and retained
                // snapshots, even when the backing blob files are missing. The page size limits
                // returned items, not inventory coverage. Orphan blobs and arbitrary object-version
                // payloads are excluded; callers must also reject an accompanying nonempty sidecar.
                var references = vault.ListExternalBlobReferences(null, 1);
                cancellationToken.ThrowIfCancellationRequested();
                var hasExternalReferences = diagnostics.ExternalAttachmentCount != 0 ||
                    references.RawReferenceCount != 0 || references.UniqueReferenceCount != 0 ||
                    references.Items.Length != 0;
                return new MdbxNativeSnapshotValidation(info.VaultId, info.DeviceId, hasExternalReferences);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (MdbxSnapshotException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw SnapshotNativeFailure(exception, "credential-or-integrity");
            }
        }, cancellationToken);

    private void EnsureSnapshotNativeAvailable()
    {
        if (!IsAvailable)
        {
            throw new MdbxSnapshotException("native-unavailable");
        }
    }

    private static void EnsureSnapshotDestinationAbsent(string path)
    {
        foreach (var artifact in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
        {
            try
            {
                // GetAttributes also detects directories and reparse points; File.Exists would
                // otherwise treat a directory or some unavailable artifacts as an absent target.
                _ = File.GetAttributes(artifact);
            }
            catch (FileNotFoundException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            throw new MdbxSnapshotException("validation-failed");
        }
    }

    private static void DeleteOwnedSnapshotAfterCancellation(string path)
    {
        try
        {
            // Portable backup never publishes SQLite sidecars. Do not remove unrelated artifacts
            // another connection might create, or any target after a failed/no-clobber native call.
            File.Delete(path);
        }
        catch (IOException)
        {
            // The owning service also cleans up its dedicated staging area in its finally block.
        }
        catch (UnauthorizedAccessException)
        {
            // Preserve cancellation without exposing a potentially sensitive filesystem message.
        }
    }

    private static MdbxSnapshotException SnapshotNativeFailure(Exception exception, string reasonCode) =>
        new(exception is DllNotFoundException or SEHException or EntryPointNotFoundException or
            BadImageFormatException or TypeInitializationException ? "native-unavailable" : reasonCode);
}
