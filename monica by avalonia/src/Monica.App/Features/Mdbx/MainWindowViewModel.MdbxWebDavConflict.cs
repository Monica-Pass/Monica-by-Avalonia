using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task KeepLocalWebDavMdbxCoreAsync(MdbxDatabaseDisplayItem? item)
    {
        var database = await GetConflictedRemoteDatabaseAsync(item);
        if (database is null)
        {
            return;
        }

        var confirmed = await _confirmationDialogService.ConfirmAsync(
            _localization.Get("MdbxKeepLocalConfirmationTitle"),
            _localization.Format("MdbxKeepLocalConfirmationMessageFormat", database.Name),
            _localization.Get("MdbxKeepLocal"),
            _localization.Cancel);
        if (!confirmed)
        {
            return;
        }

        if (database.StorageLocation == MdbxStorageLocation.RemoteOneDrive)
        {
            await EnsureBoundOneDriveAccountAsync(database);
            var accountId = GetBoundOneDriveAccountId(database);
            var currentRemote = await _oneDriveBackupService.GetFileVersionAsync(accountId, database.FilePath);
            var condition = currentRemote is null
                ? RemoteWriteCondition.CreateOnly
                : RemoteWriteCondition.Match(currentRemote);
            await UploadOneDriveMdbxWorkingCopyAsync(database, condition);
        }
        else
        {
            if (!TryCreateWebDavProfile(out var profile))
            {
                return;
            }

            var currentRemote = await _webDavBackupService.GetFileVersionAsync(profile, database.FilePath);
            var condition = currentRemote is null
                ? RemoteWriteCondition.CreateOnly
                : RemoteWriteCondition.Match(currentRemote);
            await UploadWebDavMdbxWorkingCopyAsync(database, profile, condition);
        }
        SetStatusMessage("MdbxKeepLocalSucceededFormat", database.Name);
    }

    private async Task UseRemoteWebDavMdbxCoreAsync(MdbxDatabaseDisplayItem? item)
    {
        var database = await GetConflictedRemoteDatabaseAsync(item);
        if (database is null)
        {
            return;
        }

        var confirmed = await _confirmationDialogService.ConfirmAsync(
            _localization.Get("MdbxUseRemoteConfirmationTitle"),
            _localization.Format("MdbxUseRemoteConfirmationMessageFormat", database.Name),
            _localization.Get("MdbxUseRemote"),
            _localization.Cancel);
        if (!confirmed)
        {
            return;
        }

        var workingCopyPath = GetMdbxWorkingCopyPath(database);
        string? recoveryPath = null;
        if (File.Exists(workingCopyPath))
        {
            recoveryPath = GetOrCreateConflictRecoveryPath(workingCopyPath);
        }

        if (database.StorageLocation == MdbxStorageLocation.RemoteOneDrive)
        {
            await EnsureBoundOneDriveAccountAsync(database);
            await DownloadOneDriveMdbxWorkingCopyAsync(database, SyncStatus.Conflict);
        }
        else
        {
            if (!TryCreateWebDavProfile(out var profile))
            {
                return;
            }

            await DownloadWebDavMdbxWorkingCopyAsync(database, profile, SyncStatus.Conflict);
        }
        if (recoveryPath is null)
        {
            SetStatusMessage("MdbxUseRemoteSucceededFormat", database.Name);
            return;
        }

        SetStatusMessage("MdbxUseRemoteWithBackupSucceededFormat", database.Name, recoveryPath);
    }

    private async Task<LocalMdbxDatabase?> GetConflictedRemoteDatabaseAsync(MdbxDatabaseDisplayItem? item)
    {
        if (item is null)
        {
            return null;
        }

        var database = await GetLatestMdbxDatabaseAsync(item.Database.Id);
        return database is not null &&
            database.StorageLocation is MdbxStorageLocation.RemoteWebDav or MdbxStorageLocation.RemoteOneDrive &&
            database.LastSyncStatus == SyncStatus.Conflict
                ? database
                : null;
    }

    private static string BuildConflictRecoveryPath(string workingCopyPath)
    {
        var directory = Path.GetDirectoryName(workingCopyPath) ?? Environment.CurrentDirectory;
        var name = Path.GetFileNameWithoutExtension(workingCopyPath);
        var extension = Path.GetExtension(workingCopyPath);
        return Path.Combine(
            directory,
            $"{name}.local-conflict-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}{extension}");
    }

    private static string GetOrCreateConflictRecoveryPath(string workingCopyPath)
    {
        var directory = Path.GetDirectoryName(workingCopyPath) ?? Environment.CurrentDirectory;
        var name = Path.GetFileNameWithoutExtension(workingCopyPath);
        var extension = Path.GetExtension(workingCopyPath);
        var prefix = $"{name}.local-conflict-";
        var existing = Directory.EnumerateFiles(directory, $"{prefix}*{extension}")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (existing is not null)
        {
            return existing;
        }

        var recoveryPath = BuildConflictRecoveryPath(workingCopyPath);
        File.Copy(workingCopyPath, recoveryPath, overwrite: false);
        return recoveryPath;
    }
}
