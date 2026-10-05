using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private CancellationTokenSource CreateMdbxTransferCancellation(CancellationToken cancellationToken) =>
        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _vaultSessionService.SessionCancellationToken);

    // Snapshot restore and upload commit invoke these callbacks while the native gate is held. Save
    // metadata here, then load the workspace or read the vault after the service call returns.
    private Task CommitMdbxSyncedMetadataAsync(
        LocalMdbxDatabase database,
        string workingCopyPath,
        RemoteFileVersion version,
        CancellationToken cancellationToken) =>
        CommitMdbxRemoteVersionMetadataAsync(database, workingCopyPath, version, SyncStatus.Synced, cancellationToken);

    private Task CommitMdbxUploadedMetadataAsync(
        LocalMdbxDatabase database,
        string workingCopyPath,
        RemoteFileVersion version,
        bool current,
        CancellationToken cancellationToken) =>
        CommitMdbxRemoteVersionMetadataAsync(database, workingCopyPath, version,
            current ? SyncStatus.Synced : SyncStatus.PendingUpload, cancellationToken);

    private Task CommitMdbxRemoteVersionMetadataAsync(
        LocalMdbxDatabase database,
        string workingCopyPath,
        RemoteFileVersion version,
        SyncStatus status,
        CancellationToken cancellationToken) =>
        CommitMdbxSyncMetadataAsync(database, () =>
        {
            database.WorkingCopyPath = workingCopyPath;
            database.CacheCopyPath = workingCopyPath;
            database.IsOfflineAvailable = true;
            database.LastSyncedAt = DateTimeOffset.UtcNow;
            database.LastSyncStatus = status;
            database.LastSyncError = null;
            database.RemoteETag = version.ETag;
            database.RemoteLastModifiedAt = version.LastModified;
        }, cancellationToken);

    private Task CommitMdbxSyncFailureMetadataAsync(
        LocalMdbxDatabase database,
        SyncStatus status,
        string failureCode,
        CancellationToken cancellationToken) =>
        CommitMdbxSyncMetadataAsync(database, () =>
        {
            database.LastSyncStatus = status;
            database.LastSyncError = failureCode;
        }, cancellationToken);

    private async Task CommitMdbxSyncMetadataAsync(
        LocalMdbxDatabase database,
        Action update,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var original = MdbxSyncMetadataState.Capture(database);
        try
        {
            update();
            await _repository.SaveMdbxDatabaseAsync(database, cancellationToken);
        }
        catch
        {
            original.Restore(database);
            throw;
        }
    }

    private static void DeleteMdbxIncomingArtifacts(string incomingPath)
    {
        foreach (var path in new[] { incomingPath, incomingPath + "-wal", incomingPath + "-shm" })
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppDiagnostics.Error("Cleaning temporary MDBX download failed.", ex);
            }
        }
    }

    private sealed record MdbxSyncMetadataState(
        string? WorkingCopyPath,
        string? CacheCopyPath,
        bool IsOfflineAvailable,
        DateTimeOffset? LastSyncedAt,
        SyncStatus LastSyncStatus,
        string? LastSyncError,
        string? RemoteETag,
        DateTimeOffset? RemoteLastModifiedAt)
    {
        public static MdbxSyncMetadataState Capture(LocalMdbxDatabase database) =>
            new(database.WorkingCopyPath, database.CacheCopyPath, database.IsOfflineAvailable,
                database.LastSyncedAt, database.LastSyncStatus, database.LastSyncError,
                database.RemoteETag, database.RemoteLastModifiedAt);

        public void Restore(LocalMdbxDatabase database)
        {
            database.WorkingCopyPath = WorkingCopyPath;
            database.CacheCopyPath = CacheCopyPath;
            database.IsOfflineAvailable = IsOfflineAvailable;
            database.LastSyncedAt = LastSyncedAt;
            database.LastSyncStatus = LastSyncStatus;
            database.LastSyncError = LastSyncError;
            database.RemoteETag = RemoteETag;
            database.RemoteLastModifiedAt = RemoteLastModifiedAt;
        }
    }
}
