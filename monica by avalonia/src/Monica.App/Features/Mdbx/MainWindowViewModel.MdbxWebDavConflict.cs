using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task KeepLocalWebDavMdbxCoreAsync(MdbxDatabaseDisplayItem? item)
    {
        var cancellationToken = _vaultSessionService.SessionCancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
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

        cancellationToken.ThrowIfCancellationRequested();
        if (database.StorageLocation == MdbxStorageLocation.RemoteOneDrive)
        {
            await EnsureBoundOneDriveAccountAsync(database, cancellationToken);
            var accountId = GetBoundOneDriveAccountId(database);
            var currentRemote = await _oneDriveBackupService.GetFileVersionAsync(accountId, database.FilePath, cancellationToken);
            var condition = currentRemote is null
                ? RemoteWriteCondition.CreateOnly
                : RemoteWriteCondition.Match(currentRemote);
            await UploadOneDriveMdbxWorkingCopyAsync(database, condition, cancellationToken);
        }
        else
        {
            if (!TryCreateWebDavProfile(out var profile))
            {
                return;
            }

            var currentRemote = await _webDavBackupService.GetFileVersionAsync(profile, database.FilePath, cancellationToken);
            var condition = currentRemote is null
                ? RemoteWriteCondition.CreateOnly
                : RemoteWriteCondition.Match(currentRemote);
            await UploadWebDavMdbxWorkingCopyAsync(database, profile, condition, cancellationToken);
        }
        SetStatusNotice("MdbxKeepLocalSucceededFormat", database.Name);
    }

    private async Task UseRemoteWebDavMdbxCoreAsync(MdbxDatabaseDisplayItem? item)
    {
        var cancellationToken = _vaultSessionService.SessionCancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
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

        cancellationToken.ThrowIfCancellationRequested();
        string? recoveryPath;
        if (database.StorageLocation == MdbxStorageLocation.RemoteOneDrive)
        {
            await EnsureBoundOneDriveAccountAsync(database, cancellationToken);
            recoveryPath = await DownloadOneDriveMdbxWorkingCopyAsync(database, SyncStatus.Conflict, cancellationToken);
        }
        else
        {
            if (!TryCreateWebDavProfile(out var profile))
            {
                return;
            }

            recoveryPath = await DownloadWebDavMdbxWorkingCopyAsync(database, profile, SyncStatus.Conflict, cancellationToken);
        }
        if (recoveryPath is null)
        {
            SetStatusNotice("MdbxUseRemoteSucceededFormat", database.Name);
            return;
        }

        SetStatusNotice("MdbxUseRemoteWithBackupSucceededFormat", database.Name, recoveryPath);
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
}
