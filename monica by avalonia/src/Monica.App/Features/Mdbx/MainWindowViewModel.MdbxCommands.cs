using CommunityToolkit.Mvvm.Input;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private void SelectMdbxWorkspacePage(string? page)
    {
        SelectedMdbxWorkspacePage = NormalizeMdbxWorkspacePage(page);
    }

    private static string NormalizeMdbxWorkspacePage(string? page) =>
        page?.Trim().ToLowerInvariant() switch
        {
            "health" or "diagnostics" => "Health",
            "sources" or "remote" => "Sources",
            "runtime" or "android" => "Runtime",
            _ => "Details"
        };

    [RelayCommand]
    private void ShowMdbxDatabaseDetails(MdbxDatabaseDisplayItem? item)
    {
        if (item is not null)
        {
            SelectedMdbxDatabaseItem = item;
        }
    }

    [RelayCommand]
    private Task CreateMdbxVaultAsync() =>
        RunMdbxOperationAsync("MdbxOperationCreate", CreateMdbxVaultCoreAsync);

    private async Task CreateMdbxVaultCoreAsync()
    {
        var path = BuildMdbxWorkingCopyPath("local.mdbx");
        var existing = MdbxDatabases.FirstOrDefault(item => string.Equals(item.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.LastAccessedAt = DateTimeOffset.UtcNow;
            await _repository.SaveMdbxDatabaseAsync(existing);
            RefreshMdbxVaultState();
            RefreshVaultSources();
            StatusMessage = _localization.Format("MdbxMetadataAlreadyRegisteredFormat", existing.Name);
            return;
        }

        var metadata = await _mdbxVaultService.CreateLocalMetadataAsync(_localization.Get("MdbxLocalVaultName"), path);
        metadata.IsDefault = MdbxDatabases.Count == 0;
        await _repository.SaveMdbxDatabaseAsync(metadata);
        MdbxDatabases.Add(metadata);
        RefreshMdbxVaultState();
        RefreshVaultSources();
        StatusMessage = _localization.Get("CreatedMdbxMetadata");
    }

    [RelayCommand]
    private Task CreateWebDavMdbxVaultAsync() =>
        RunMdbxOperationAsync("MdbxOperationCreate", CreateWebDavMdbxVaultCoreAsync);

    private async Task CreateWebDavMdbxVaultCoreAsync()
    {
        if (!TryCreateWebDavProfile(out var profile))
        {
            return;
        }

        var remotePath = string.IsNullOrWhiteSpace(WebDavRemotePath) ? "/Monica/local.mdbx" : WebDavRemotePath.TrimEnd('/') + "/local.mdbx";
        var existing = MdbxDatabases.FirstOrDefault(item =>
            item.StorageLocation == MdbxStorageLocation.RemoteWebDav &&
            string.Equals(item.FilePath, remotePath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (existing.LastSyncStatus == SyncStatus.PendingUpload)
            {
                await UploadWebDavMdbxWorkingCopyAsync(existing, profile);
                StatusMessage = _localization.Format("MdbxWebDavUploadSucceededFormat", existing.Name);
                return;
            }

            StatusMessage = _localization.Format("MdbxMetadataAlreadyRegisteredFormat", existing.Name);
            return;
        }

        var metadata = await CreateRemoteMdbxMetadataAsync(
            _localization.Get("MdbxWebDavVaultName"),
            remotePath,
            MdbxStorageLocation.RemoteWebDav,
            "REMOTE_WEBDAV",
            BuildWebDavMdbxWorkingCopyPath(profile, remotePath),
            _localization.Get("MdbxWebDavMetadataDescription"));
        metadata.IsDefault = MdbxDatabases.Count == 0;
        await _repository.SaveMdbxDatabaseAsync(metadata);
        MdbxDatabases.Add(metadata);
        await UploadWebDavMdbxWorkingCopyAsync(metadata, profile);
        StatusMessage = _localization.Get("CreatedMdbxWebDavMetadata");
    }

    [RelayCommand]
    private Task CreateOneDriveMdbxVaultAsync() =>
        RunMdbxOperationAsync("MdbxOperationCreate", CreateOneDriveMdbxVaultCoreAsync);

    private async Task CreateOneDriveMdbxVaultCoreAsync()
    {
        if (!OneDriveEnabled)
        {
            StatusMessage = _localization.Get("EnableOneDriveFirst");
            return;
        }

        const string remotePath = "OneDrive:/Monica/local.mdbx";
        var existing = MdbxDatabases.FirstOrDefault(item =>
            item.StorageLocation == MdbxStorageLocation.RemoteOneDrive &&
            string.Equals(item.FilePath, remotePath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            StatusMessage = _localization.Format("MdbxMetadataAlreadyRegisteredFormat", existing.Name);
            return;
        }

        var metadata = await CreateRemoteMdbxMetadataAsync(
            _localization.Get("MdbxOneDriveVaultName"),
            remotePath,
            MdbxStorageLocation.RemoteOneDrive,
            "REMOTE_ONEDRIVE",
            BuildMdbxWorkingCopyPath("onedrive-local.mdbx"),
            _localization.Get("MdbxOneDriveMetadataDescription"));
        metadata.IsDefault = MdbxDatabases.Count == 0;
        await _repository.SaveMdbxDatabaseAsync(metadata);
        MdbxDatabases.Add(metadata);
        RefreshMdbxVaultState();
        RefreshVaultSources();
        StatusMessage = _localization.Get("CreatedMdbxOneDriveMetadata");
    }

    [RelayCommand]
    private Task RefreshMdbxVaultsAsync() =>
        RunMdbxOperationAsync("MdbxOperationRefresh", RefreshMdbxVaultsCoreAsync);

    private async Task RefreshMdbxVaultsCoreAsync()
    {
        await ReloadMdbxVaultStateAsync();
        StatusMessage = _localization.Get("MdbxVaultsRefreshed");
    }

    [RelayCommand]
    private Task OpenMdbxDatabaseAsync(MdbxDatabaseDisplayItem? item) =>
        RunMdbxOperationAsync("MdbxOperationOpen", () => OpenMdbxDatabaseCoreAsync(item));

    private async Task OpenMdbxDatabaseCoreAsync(MdbxDatabaseDisplayItem? item)
    {
        if (item is null)
        {
            return;
        }

        var database = await GetLatestMdbxDatabaseAsync(item.Database.Id);
        if (database is null)
        {
            return;
        }

        if (database.StorageLocation == MdbxStorageLocation.RemoteWebDav)
        {
            if (!TryCreateWebDavProfile(out var profile))
            {
                return;
            }

            if (database.LastSyncStatus == SyncStatus.PendingUpload)
            {
                await UploadWebDavMdbxWorkingCopyAsync(database, profile);
            }
            else if (!File.Exists(database.WorkingCopyPath))
            {
                await DownloadWebDavMdbxWorkingCopyAsync(database, profile);
            }
        }

        if (string.IsNullOrWhiteSpace(database.WorkingCopyPath ?? database.FilePath))
        {
            StatusMessage = _localization.Get("MdbxRemoteOpenPending");
            return;
        }

        await using var stream = await _mdbxVaultService.OpenLocalStreamAsync(database);
        database.LastAccessedAt = DateTimeOffset.UtcNow;
        await _repository.SaveMdbxDatabaseAsync(database);
        await ReloadMdbxVaultStateAsync();
        StatusMessage = _localization.Format("OpenedMdbxDatabaseFormat", item.Name, stream.Length);
    }

    [RelayCommand]
    private Task SyncMdbxDatabaseAsync(MdbxDatabaseDisplayItem? item) =>
        RunMdbxOperationAsync("MdbxOperationSync", () => SyncMdbxDatabaseCoreAsync(item));

    private async Task SyncMdbxDatabaseCoreAsync(MdbxDatabaseDisplayItem? item)
    {
        if (item is null)
        {
            return;
        }

        var database = await GetLatestMdbxDatabaseAsync(item.Database.Id);
        if (database is null || database.StorageLocation != MdbxStorageLocation.RemoteWebDav)
        {
            return;
        }

        if (!TryCreateWebDavProfile(out var profile))
        {
            return;
        }

        if (database.LastSyncStatus == SyncStatus.PendingUpload)
        {
            await UploadWebDavMdbxWorkingCopyAsync(database, profile);
            StatusMessage = _localization.Format("MdbxWebDavUploadSucceededFormat", item.Name);
            return;
        }

        if (database.LastSyncStatus == SyncStatus.Conflict)
        {
            StatusMessage = _localization.Get("MdbxWebDavConflictRequiresResolution");
            return;
        }

        await DownloadWebDavMdbxWorkingCopyAsync(database, profile);
        StatusMessage = _localization.Format("MdbxWebDavDownloadSucceededFormat", item.Name);
    }

    [RelayCommand]
    private Task KeepLocalWebDavMdbxAsync(MdbxDatabaseDisplayItem? item) =>
        RunMdbxOperationAsync(
            "MdbxOperationResolveConflict",
            () => KeepLocalWebDavMdbxCoreAsync(item));

    [RelayCommand]
    private Task UseRemoteWebDavMdbxAsync(MdbxDatabaseDisplayItem? item) =>
        RunMdbxOperationAsync(
            "MdbxOperationResolveConflict",
            () => UseRemoteWebDavMdbxCoreAsync(item));

    [RelayCommand]
    private Task SetDefaultMdbxDatabaseAsync(MdbxDatabaseDisplayItem? item) =>
        RunMdbxOperationAsync("MdbxOperationSetDefault", () => SetDefaultMdbxDatabaseCoreAsync(item));

    private async Task SetDefaultMdbxDatabaseCoreAsync(MdbxDatabaseDisplayItem? item)
    {
        if (item is null)
        {
            return;
        }

        foreach (var database in MdbxDatabases)
        {
            database.IsDefault = database.Id == item.Database.Id;
            await _repository.SaveMdbxDatabaseAsync(database);
        }

        RefreshMdbxVaultState();
        RefreshVaultSources();
        StatusMessage = _localization.Format("SelectedMdbxDefaultFormat", item.Name);
    }

    [RelayCommand]
    private void ConfigureMdbxRemoteSources()
    {
        SelectedSection = "Sync";
        StatusMessage = _localization.Get("ConfigureMdbxRemoteSourcesHint");
    }
}
