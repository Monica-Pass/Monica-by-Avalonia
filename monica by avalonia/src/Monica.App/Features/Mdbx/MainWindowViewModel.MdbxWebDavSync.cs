using Monica.Core.Models;
using Monica.Platform.Services;
using System.Security.Cryptography;
using System.Text;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task UploadWebDavMdbxWorkingCopyAsync(
        LocalMdbxDatabase database,
        WebDavProfile profile,
        RemoteWriteCondition? writeCondition = null,
        CancellationToken cancellationToken = default)
    {
        var workingCopyPath = GetMdbxWorkingCopyPath(database);
        if (!File.Exists(workingCopyPath))
        {
            throw new InvalidOperationException(_localization.Get("MdbxWorkingCopyMissing"));
        }

        try
        {
            var condition = writeCondition ?? BuildWebDavWriteCondition(database);
            await using var content = await _mdbxVaultService.OpenLocalStreamAsync(database, cancellationToken);
            content.Position = 0;
            var version = await _webDavBackupService.UploadBinaryConditionallyAsync(
                profile,
                database.FilePath,
                content,
                condition,
                cancellationToken);
            await MarkWebDavMdbxSyncedAsync(database, workingCopyPath, version);
        }
        catch (RemoteFileConflictException ex)
        {
            var message = _localization.Format("MdbxWebDavConflictDetectedFormat", database.Name, ex.Message);
            await MarkWebDavMdbxSyncFailedAsync(database, SyncStatus.Conflict, message);
            throw new RemoteFileConflictException(message);
        }
        catch (Exception ex)
        {
            var failureStatus = writeCondition is null
                ? SyncStatus.PendingUpload
                : SyncStatus.Conflict;
            await MarkWebDavMdbxSyncFailedAsync(database, failureStatus, ex.Message);
            throw;
        }
    }

    private async Task DownloadWebDavMdbxWorkingCopyAsync(
        LocalMdbxDatabase database,
        WebDavProfile profile,
        SyncStatus failureStatus = SyncStatus.Failed,
        CancellationToken cancellationToken = default)
    {
        var workingCopyPath = GetMdbxWorkingCopyPath(database);
        var directory = Path.GetDirectoryName(workingCopyPath) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(directory);
        var incomingPath = Path.Combine(
            directory,
            $".{Path.GetFileName(workingCopyPath)}.{Guid.NewGuid():N}.download");

        try
        {
            RemoteFileVersion version;
            await using (var destination = new FileStream(
                incomingPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                version = await _webDavBackupService.DownloadBinaryVersionedAsync(
                    profile,
                    database.FilePath,
                    destination,
                    cancellationToken);
                await destination.FlushAsync(cancellationToken);
                destination.Flush(flushToDisk: true);
            }

            await ValidateIncomingMdbxWorkingCopyAsync(database, incomingPath, cancellationToken);
            File.Move(incomingPath, workingCopyPath, overwrite: true);
            await MarkWebDavMdbxSyncedAsync(database, workingCopyPath, version);
        }
        catch (Exception ex)
        {
            await MarkWebDavMdbxSyncFailedAsync(database, failureStatus, ex.Message);
            throw;
        }
        finally
        {
            if (File.Exists(incomingPath))
            {
                File.Delete(incomingPath);
            }
        }
    }

    private async Task ValidateIncomingMdbxWorkingCopyAsync(
        LocalMdbxDatabase database,
        string incomingPath,
        CancellationToken cancellationToken)
    {
        var validationMetadata = new LocalMdbxDatabase
        {
            FilePath = incomingPath,
            WorkingCopyPath = incomingPath,
            EncryptedPassword = database.EncryptedPassword,
            TigaMode = database.TigaMode,
            UnlockMethod = database.UnlockMethod
        };
        await using var stream = await _mdbxVaultService.OpenLocalStreamAsync(
            validationMetadata,
            cancellationToken);
    }

    private async Task MarkWebDavMdbxSyncedAsync(
        LocalMdbxDatabase database,
        string workingCopyPath,
        RemoteFileVersion version)
    {
        database.WorkingCopyPath = workingCopyPath;
        database.CacheCopyPath = workingCopyPath;
        database.IsOfflineAvailable = true;
        database.LastSyncedAt = DateTimeOffset.UtcNow;
        database.LastSyncStatus = SyncStatus.Synced;
        database.LastSyncError = null;
        database.RemoteETag = version.ETag;
        database.RemoteLastModifiedAt = version.LastModified;
        await SaveMdbxSyncStateAsync(database);
    }

    private async Task MarkWebDavMdbxSyncFailedAsync(
        LocalMdbxDatabase database,
        SyncStatus status,
        string error)
    {
        database.LastSyncStatus = status;
        database.LastSyncError = error;
        await SaveMdbxSyncStateAsync(database);
    }

    private async Task SaveMdbxSyncStateAsync(LocalMdbxDatabase database)
    {
        await _repository.SaveMdbxDatabaseAsync(database);
        await ReloadMdbxVaultStateAsync();
    }

    private RemoteWriteCondition BuildWebDavWriteCondition(LocalMdbxDatabase database)
    {
        var expected = new RemoteFileVersion(
            database.RemoteETag,
            database.RemoteLastModifiedAt,
            Length: null);
        if (expected.HasValidator)
        {
            return RemoteWriteCondition.Match(expected);
        }

        if (database.LastSyncedAt is null)
        {
            return RemoteWriteCondition.CreateOnly;
        }

        throw new RemoteFileConflictException(_localization.Get("MdbxWebDavMissingRevision"));
    }

    private static string GetMdbxWorkingCopyPath(LocalMdbxDatabase database)
    {
        var path = database.WorkingCopyPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("MDBX working copy path is missing.");
        }

        return Path.GetFullPath(path);
    }

    private static string BuildWebDavMdbxWorkingCopyPath(WebDavProfile profile, string remotePath)
    {
        var identity = $"{profile.BaseUri.AbsoluteUri.TrimEnd('/')}\n{remotePath}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return BuildMdbxWorkingCopyPath($"webdav-{hash[..16].ToLowerInvariant()}.mdbx");
    }
}
