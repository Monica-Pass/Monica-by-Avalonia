using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task KeepLocalWebDavMdbxCoreAsync(MdbxDatabaseDisplayItem? item)
    {
        var database = await GetConflictedWebDavDatabaseAsync(item);
        if (database is null || !TryCreateWebDavProfile(out var profile))
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

        var currentRemote = await _webDavBackupService.GetFileVersionAsync(profile, database.FilePath);
        var condition = currentRemote is null
            ? RemoteWriteCondition.CreateOnly
            : RemoteWriteCondition.Match(currentRemote);
        await UploadWebDavMdbxWorkingCopyAsync(database, profile, condition);
        StatusMessage = _localization.Format("MdbxKeepLocalSucceededFormat", database.Name);
    }

    private async Task UseRemoteWebDavMdbxCoreAsync(MdbxDatabaseDisplayItem? item)
    {
        var database = await GetConflictedWebDavDatabaseAsync(item);
        if (database is null || !TryCreateWebDavProfile(out var profile))
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
            recoveryPath = BuildConflictRecoveryPath(workingCopyPath);
            File.Copy(workingCopyPath, recoveryPath, overwrite: false);
        }

        await DownloadWebDavMdbxWorkingCopyAsync(database, profile, SyncStatus.Conflict);
        StatusMessage = recoveryPath is null
            ? _localization.Format("MdbxUseRemoteSucceededFormat", database.Name)
            : _localization.Format("MdbxUseRemoteWithBackupSucceededFormat", database.Name, recoveryPath);
    }

    private async Task<LocalMdbxDatabase?> GetConflictedWebDavDatabaseAsync(MdbxDatabaseDisplayItem? item)
    {
        if (item is null)
        {
            return null;
        }

        var database = await GetLatestMdbxDatabaseAsync(item.Database.Id);
        return database is not null &&
            database.StorageLocation == MdbxStorageLocation.RemoteWebDav &&
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
}
