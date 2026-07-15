using Monica.Core.Models;
using Monica.Data;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private int _mdbxOperationActive;

    private async Task RunMdbxOperationAsync(string operationKey, Func<Task> action)
    {
        if (Interlocked.CompareExchange(ref _mdbxOperationActive, 1, 0) != 0)
        {
            StatusMessage = _localization.Get("MdbxOperationInProgress");
            return;
        }

        IsMdbxBusy = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format(
                "MdbxOperationFailedFormat",
                _localization.Get(operationKey),
                ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _mdbxOperationActive, 0);
            IsMdbxBusy = false;
        }
    }

    private void RefreshMdbxVaultState()
    {
        var selectedId = SelectedMdbxDatabaseItem?.Database.Id;
        MdbxDatabaseItems.Clear();
        foreach (var database in MdbxDatabases.OrderByDescending(item => item.IsDefault).ThenBy(item => item.SortOrder).ThenBy(item => item.Name))
        {
            MdbxDatabaseItems.Add(ToMdbxDisplayItem(database));
        }

        SelectedMdbxDatabaseItem =
            MdbxDatabaseItems.FirstOrDefault(item => item.Database.Id == selectedId) ??
            MdbxDatabaseItems.FirstOrDefault(item => item.IsDefault) ??
            MdbxDatabaseItems.FirstOrDefault();
        RaiseMdbxVaultState();
    }

    private async Task<LocalMdbxDatabase?> GetLatestMdbxDatabaseAsync(long databaseId) =>
        (await _repository.GetMdbxDatabasesAsync())
            .FirstOrDefault(database => database.Id == databaseId);

    private async Task ReloadMdbxVaultStateAsync()
    {
        var databases = await _repository.GetMdbxDatabasesAsync();
        MdbxDatabases.Clear();
        foreach (var database in databases)
        {
            MdbxDatabases.Add(database);
        }

        RefreshMdbxVaultState();
        RefreshVaultSources();
    }

    private void RaiseMdbxVaultState()
    {
        OnPropertyChanged(nameof(MdbxDatabaseCountText));
        OnPropertyChanged(nameof(MdbxLocalCountText));
        OnPropertyChanged(nameof(MdbxWebDavCountText));
        OnPropertyChanged(nameof(MdbxOneDriveCountText));
        OnPropertyChanged(nameof(MdbxLocalDatabaseCount));
        OnPropertyChanged(nameof(MdbxWebDavDatabaseCount));
        OnPropertyChanged(nameof(MdbxOneDriveDatabaseCount));
        OnPropertyChanged(nameof(MdbxRemoteDatabaseCount));
        OnPropertyChanged(nameof(MdbxWorkingCopyCount));
        OnPropertyChanged(nameof(MdbxOfflineCopyCount));
        OnPropertyChanged(nameof(MdbxPendingSyncCount));
        OnPropertyChanged(nameof(MdbxSyncErrorCount));
        OnPropertyChanged(nameof(HasMdbxDatabases));
        OnPropertyChanged(nameof(HasMdbxSyncErrors));
        OnPropertyChanged(nameof(MdbxDefaultVaultSummaryText));
        OnPropertyChanged(nameof(MdbxWorkingCopySummaryText));
        OnPropertyChanged(nameof(MdbxRemoteSummaryText));
        OnPropertyChanged(nameof(MdbxSyncDiagnosticsSummaryText));
        OnPropertyChanged(nameof(MdbxCachePolicyText));
        OnPropertyChanged(nameof(MdbxLocalSourceStatusText));
        OnPropertyChanged(nameof(MdbxWebDavSourceStatusText));
        OnPropertyChanged(nameof(MdbxOneDriveSourceStatusText));
        OnPropertyChanged(nameof(MdbxRuntimeSummaryText));
        OnPropertyChanged(nameof(MdbxSecuritySummaryText));
        RefreshMdbxHealthItems();
        RefreshSyncHealthItems();
    }

    private void RefreshMdbxHealthItems()
    {
        MdbxHealthItems.Clear();
        MdbxHealthItems.Add(new SyncHealthDisplayItem(
            _localization.Get("MdbxDefaultVault"),
            MdbxDefaultVaultSummaryText,
            MdbxSecuritySummaryText));
        MdbxHealthItems.Add(new SyncHealthDisplayItem(
            _localization.Get("MdbxWorkingCopies"),
            _localization.Format("MdbxWorkingCopyCountFormat", MdbxWorkingCopyCount),
            MdbxWorkingCopySummaryText));
        MdbxHealthItems.Add(new SyncHealthDisplayItem(
            _localization.Get("MdbxRemoteSources"),
            _localization.Format("MdbxRemoteSourceCountFormat", MdbxRemoteDatabaseCount),
            MdbxRemoteSummaryText));
        MdbxHealthItems.Add(new SyncHealthDisplayItem(
            _localization.Get("MdbxDiagnostics"),
            HasMdbxSyncErrors ? _localization.Get("NeedsAttention") : _localization.Get("Available"),
            MdbxSyncDiagnosticsSummaryText));
        OnPropertyChanged(nameof(MdbxHealthItems));
    }

    private MdbxDatabaseDisplayItem ToMdbxDisplayItem(LocalMdbxDatabase database)
    {
        var isLocal = IsLocalMdbxDatabase(database);
        var source = database.StorageLocation switch
        {
            MdbxStorageLocation.Internal => _localization.Get("MdbxSourceLocal"),
            MdbxStorageLocation.External => _localization.Get("MdbxSourceExternal"),
            MdbxStorageLocation.RemoteWebDav => _localization.WebDav,
            MdbxStorageLocation.RemoteOneDrive => _localization.OneDrive,
            _ => database.StorageLocation.ToString()
        };
        var localPath = string.IsNullOrWhiteSpace(database.WorkingCopyPath)
            ? database.FilePath
            : database.WorkingCopyPath;
        var remotePath = isLocal
            ? _localization.Get("LocalOnly")
            : string.IsNullOrWhiteSpace(database.FilePath) ? _localization.Get("NotConfigured") : database.FilePath;
        var workingCopyStatus = HasMdbxWorkingCopy(database)
            ? _localization.Get("MdbxWorkingCopyReady")
            : _localization.Get("MdbxWorkingCopyMissing");
        var remoteStatus = isLocal
            ? _localization.Get("LocalOnly")
            : _localization.Format("MdbxRemoteStatusFormat", source, remotePath);
        var cachePath = string.IsNullOrWhiteSpace(database.CacheCopyPath)
            ? _localization.Get("NotConfigured")
            : database.CacheCopyPath;
        var lastSyncError = string.IsNullOrWhiteSpace(database.LastSyncError)
            ? _localization.Get("MdbxNoSyncErrors")
            : database.LastSyncError!;

        return new MdbxDatabaseDisplayItem(
            database,
            string.IsNullOrWhiteSpace(database.Name) ? "MDBX" : database.Name,
            source,
            string.IsNullOrWhiteSpace(localPath) ? _localization.Get("NotConfigured") : localPath,
            remotePath,
            database.TigaMode.ToString(),
            database.UnlockMethod.ToString(),
            FormatLocalDate(database.CreatedAt),
            FormatLocalDate(database.LastAccessedAt),
            database.LastSyncedAt is null ? _localization.Get("Never") : FormatLocalDate(database.LastSyncedAt.Value),
            LocalizeSyncStatus(database.LastSyncStatus),
            string.IsNullOrWhiteSpace(database.Description) ? _localization.Get("MdbxNoDescription") : database.Description,
            workingCopyStatus,
            remoteStatus,
            cachePath,
            lastSyncError,
            !string.IsNullOrWhiteSpace(database.LastSyncError),
            database.IsDefault,
            isLocal,
            !isLocal,
            database.StorageLocation == MdbxStorageLocation.RemoteWebDav,
            database.LastSyncStatus == SyncStatus.Conflict);
    }

    private static bool IsLocalMdbxDatabase(LocalMdbxDatabase database) =>
        database.StorageLocation is MdbxStorageLocation.Internal or MdbxStorageLocation.External;

    private static bool HasMdbxWorkingCopy(LocalMdbxDatabase database) =>
        !string.IsNullOrWhiteSpace(database.WorkingCopyPath) ||
        (database.StorageLocation is MdbxStorageLocation.Internal or MdbxStorageLocation.External &&
            !string.IsNullOrWhiteSpace(database.FilePath));

    private static bool HasPendingMdbxSync(LocalMdbxDatabase database) =>
        database.LastSyncStatus is SyncStatus.Pending or SyncStatus.PendingUpload or SyncStatus.Syncing or SyncStatus.RemoteChanged;

    private static bool HasMdbxSyncIssue(LocalMdbxDatabase database) =>
        database.LastSyncStatus is SyncStatus.Failed or SyncStatus.Conflict ||
        !string.IsNullOrWhiteSpace(database.LastSyncError);

    private static string BuildMdbxWorkingCopyPath(string fileName)
    {
        return MonicaAppDataPaths.GetPath(Path.Combine("mdbx", fileName));
    }

    private async Task<LocalMdbxDatabase> CreateRemoteMdbxMetadataAsync(
        string name,
        string remotePath,
        MdbxStorageLocation storageLocation,
        string sourceType,
        string workingCopyPath,
        string description)
    {
        var metadata = await _mdbxVaultService.CreateLocalMetadataAsync(name, workingCopyPath, MdbxTigaMode.Multi);
        metadata.FilePath = remotePath;
        metadata.StorageLocation = storageLocation;
        metadata.SourceType = sourceType;
        metadata.LastSyncStatus = SyncStatus.PendingUpload;
        metadata.IsOfflineAvailable = true;
        metadata.Description = description;
        return metadata;
    }
}
