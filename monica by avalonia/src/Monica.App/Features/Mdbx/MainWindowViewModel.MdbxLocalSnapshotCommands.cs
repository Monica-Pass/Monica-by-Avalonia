using CommunityToolkit.Mvvm.Input;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand(CanExecute = nameof(CanUseMdbxSnapshotActions))]
    private Task ExportMdbxSnapshotAsync(MdbxDatabaseDisplayItem? item) =>
        !CanUseMdbxSnapshotActions ? Task.CompletedTask :
        RunMdbxOperationAsync("MdbxOperationExportSnapshot", () => ExportMdbxSnapshotCoreAsync(item));

    private async Task ExportMdbxSnapshotCoreAsync(MdbxDatabaseDisplayItem? item)
    {
        using var cancellation = CreateMdbxTransferCancellation(CancellationToken.None);
        var token = cancellation.Token;
        var database = await ResolveMdbxSnapshotDatabaseAsync(item, token);
        if (database is null || !await AuthorizeFileExportAsync()) return;
        token.ThrowIfCancellationRequested();
        var source = MdbxSnapshotLocalSource(database)!;
        var name = Path.GetFileNameWithoutExtension(source);
        var suggested = $"{name}.backup-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.mdbx";
        var target = await _fileSystemPickerService.PickSaveFileTargetAsync(
            _localization.Get("MdbxSnapshotExportTitle"), suggested, MdbxSnapshotFileTypes(), token);
        token.ThrowIfCancellationRequested();
        if (target is null)
        {
            SetStatusNotice("MdbxOperationCanceled");
            return;
        }

        if (string.IsNullOrWhiteSpace(target.FullPath))
        {
            SetStatusFailure("MdbxSnapshotLocalPathRequired");
            return;
        }

        await _mdbxVaultService.CreateSnapshotAsync(database, target.FullPath, token);
        _mdbxOperationCommitted = true;
        if (IsUnlocked && !token.IsCancellationRequested)
        {
            SetStatusNotice("MdbxSnapshotExportedFormat", target.FileName);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseMdbxSnapshotActions))]
    private Task RestoreMdbxSnapshotAsync(MdbxDatabaseDisplayItem? item) =>
        !CanUseMdbxSnapshotActions ? Task.CompletedTask :
        RunMdbxOperationAsync("MdbxOperationRestoreSnapshot", () => RestoreMdbxSnapshotCoreAsync(item));

    private async Task RestoreMdbxSnapshotCoreAsync(MdbxDatabaseDisplayItem? item)
    {
        using var cancellation = CreateMdbxTransferCancellation(CancellationToken.None);
        var token = cancellation.Token;
        var database = await ResolveMdbxSnapshotDatabaseAsync(item, token);
        if (database is null) return;
        var picked = await _fileSystemPickerService.PickOpenFileTargetAsync(
            _localization.Get("MdbxSnapshotRestoreTitle"), MdbxSnapshotFileTypes(), token);
        token.ThrowIfCancellationRequested();
        if (picked is null)
        {
            SetStatusNotice("MdbxOperationCanceled");
            return;
        }

        if (string.IsNullOrWhiteSpace(picked.FullPath))
        {
            SetStatusFailure("MdbxSnapshotLocalPathRequired");
            return;
        }

        var confirmed = await _confirmationDialogService.ConfirmAsync(
            _localization.Get("MdbxSnapshotRestoreConfirmationTitle"),
            _localization.Format("MdbxSnapshotRestoreConfirmationMessageFormat", picked.FileName, database.Name),
            _localization.Get("MdbxRestoreSnapshot"), _localization.Cancel);
        token.ThrowIfCancellationRequested();
        if (!confirmed)
        {
            SetStatusNotice("MdbxOperationCanceled");
            return;
        }

        // Keep the original selection path: the service checks its WAL and .blobs associations,
        // creates private validation copies, and never removes the user-selected backup.
        var result = await RestoreMdbxSnapshotInSessionAsync(database, picked.FullPath,
            commitToken => CommitMdbxLocalRestoreMetadataAsync(database, commitToken), token);
        if (!IsUnlocked || token.IsCancellationRequested) return;
        if (result.RecoveryPath is null)
        {
            SetStatusNotice("MdbxSnapshotRestoredFormat", database.Name);
        }
        else
        {
            SetStatusNotice("MdbxSnapshotRestoredWithRecoveryFormat", database.Name, result.RecoveryPath);
        }
    }

    private async Task<LocalMdbxDatabase?> ResolveMdbxSnapshotDatabaseAsync(
        MdbxDatabaseDisplayItem? item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (item is null) return null;
        // Capture the command's ID before the picker opens; selection can change while it is open.
        var database = (await _repository.GetMdbxDatabasesAsync(cancellationToken))
            .FirstOrDefault(value => value.Id == item.Database.Id);
        cancellationToken.ThrowIfCancellationRequested();
        if (database is null || !HasMdbxSnapshotSource(database))
        {
            SetStatusFailure("MdbxSnapshotAvailabilityMissingCopy");
            return null;
        }

        return database;
    }

    private Task CommitMdbxLocalRestoreMetadataAsync(LocalMdbxDatabase database, CancellationToken cancellationToken) =>
        CommitMdbxSyncMetadataAsync(database, () =>
        {
            var path = MdbxSnapshotLocalSource(database)!;
            database.WorkingCopyPath = path;
            database.CacheCopyPath = path;
            database.IsOfflineAvailable = true;
            database.LastSyncStatus = IsLocalMdbxDatabase(database) ? SyncStatus.LocalOnly : SyncStatus.PendingUpload;
            database.LastSyncError = null;
            // Preserve remote validators and LastSyncedAt: restoring local data does not upload it.
        }, cancellationToken);

    private IReadOnlyList<PlatformFilePickerFileType> MdbxSnapshotFileTypes() =>
        [new(_localization.Get("MdbxSnapshotFileType"), ["*.mdbx"])];
}
