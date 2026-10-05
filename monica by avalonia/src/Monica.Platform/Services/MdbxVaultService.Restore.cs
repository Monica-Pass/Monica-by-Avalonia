using Monica.Core.Models;
using Monica.Data.Mdbx;

namespace Monica.Platform.Services;

public sealed partial class MdbxVaultService
{
    public async Task<MdbxSnapshotRestoreResult> RestoreSnapshotAsync(LocalMdbxDatabase database,
        string incomingPath, Func<CancellationToken, Task> commitMetadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commitMetadata);
        var target = SnapshotSourcePath(database);
        var incoming = Path.GetFullPath(incomingPath);
        if (SameSnapshotPath(target, incoming)) throw new MdbxSnapshotException("validation-failed");
        var scratch = CreateSnapshotScratch(Path.GetDirectoryName(target)!);
        var preserveScratch = false;
        try
        {
            var candidate = Path.Combine(scratch, "incoming.mdbx");
            var info = await PrepareSnapshotAsync(database, incoming, candidate, scratch, cancellationToken);
            using var replacementLease = await RequireReplacementCoordinator().AcquireFileReplacementAsync(target, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            RejectExternalSnapshotFiles(target);
            RequireNoLiveSnapshotSidecars(target);
            var exists = File.Exists(target);
            if (!exists && (database.IsOfflineAvailable || database.LastSyncedAt is not null))
            {
                // Metadata currently has no persistent vault identity. A previously bound source
                // must not silently accept a different vault just because its cache disappeared.
                throw new MdbxSnapshotException("identity-unavailable");
            }
            if (exists && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
            {
                throw new MdbxSnapshotException("vault-busy");
            }

            // On Windows this permits our atomic rename but denies an external writable handle.
            // The store gate covers its own readers/writers; unexpected WAL/SHM is never deleted.
            using var originalGuard = exists ? OpenSnapshotReplacementGuard(target) : null;
            string? recovery = null;
            if (exists)
            {
                // The closed store has checkpointed the main file and no sidecars remain. Copy it
                // under the read guard before native reads: SQLite can create empty WAL/SHM even
                // when opening a WAL-mode database read-only, which must not touch the target.
                var recoveryInput = Path.Combine(scratch, "current-input.mdbx");
                File.Copy(target, recoveryInput, overwrite: false);
                var recoveryCandidate = Path.Combine(scratch, "current.mdbx");
                var current = await PrepareSnapshotAsync(database, recoveryInput, recoveryCandidate, scratch, cancellationToken);
                if (current.VaultId != info.VaultId) throw new MdbxSnapshotException("mismatched-vault");
                RequireNoLiveSnapshotSidecars(target);
                var currentHash = await HashSnapshotAsync(recoveryCandidate, cancellationToken);
                var incomingHash = await HashSnapshotAsync(candidate, cancellationToken);
                if (System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(currentHash, incomingHash))
                {
                    await commitMetadata(cancellationToken);
                    return new MdbxSnapshotRestoreResult(null);
                }

                recovery = BuildSnapshotRecoveryPath(target);
                File.Move(recoveryCandidate, recovery, overwrite: false);
            }

            var rollback = Path.Combine(scratch, "original.mdbx");
            var installed = false;
            FileStream? installedGuard = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                // ReplaceFileW opens the replacement with write access. Guard the published file
                // immediately afterwards, rather than obstructing publication with a candidate guard.
                if (exists) File.Replace(candidate, target, rollback);
                else File.Move(candidate, target, overwrite: false);
                installed = true;
                installedGuard = OpenSnapshotReplacementGuard(target);
                await commitMetadata(cancellationToken);
                // Once metadata has committed, later cancellation cannot turn success into a rollback.
                return new MdbxSnapshotRestoreResult(recovery);
            }
            catch
            {
                // ReplaceFileW can move the original into its backup before reporting failure.
                // Recover that original even when publication never returned successfully.
                if (installed || File.Exists(rollback))
                {
                    try
                    {
                        installedGuard?.Dispose();
                        originalGuard?.Dispose();
                        if (exists && File.Exists(target)) File.Replace(rollback, target, null);
                        else if (exists) File.Move(rollback, target, overwrite: false);
                        else File.Delete(target);
                    }
                    catch (Exception)
                    {
                        // Keep the original rollback file and validated recovery if rollback is obstructed.
                        preserveScratch = true;
                        throw new MdbxSnapshotException("rollback-failed");
                    }
                }

                throw;
            }
            finally
            {
                installedGuard?.Dispose();
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (MdbxSnapshotException) { throw; }
        catch (IOException) { throw new MdbxSnapshotException("vault-busy"); }
        catch (UnauthorizedAccessException) { throw new MdbxSnapshotException("vault-busy"); }
        finally
        {
            if (!preserveScratch) CleanupSnapshotScratch(scratch);
        }
    }

    private static FileStream OpenSnapshotReplacementGuard(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);

    private static void RequireNoLiveSnapshotSidecars(string path)
    {
        foreach (var artifact in new[] { path + "-wal", path + "-shm", path + "-journal" })
        {
            try { _ = File.GetAttributes(artifact); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            throw new MdbxSnapshotException("vault-busy");
        }
    }

    private static string BuildSnapshotRecoveryPath(string path) =>
        Path.Combine(Path.GetDirectoryName(path)!,
            $"{Path.GetFileNameWithoutExtension(path)}.local-conflict-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}{Path.GetExtension(path)}");
}
