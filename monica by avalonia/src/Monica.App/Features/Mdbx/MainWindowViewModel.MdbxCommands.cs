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
    private async Task CreateMdbxVaultAsync()
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
    private async Task CreateWebDavMdbxVaultAsync()
    {
        if (!WebDavEnabled)
        {
            StatusMessage = _localization.Get("EnableWebDavFirst");
            return;
        }

        var remotePath = string.IsNullOrWhiteSpace(WebDavRemotePath) ? "/Monica/local.mdbx" : WebDavRemotePath.TrimEnd('/') + "/local.mdbx";
        var existing = MdbxDatabases.FirstOrDefault(item =>
            item.StorageLocation == MdbxStorageLocation.RemoteWebDav &&
            string.Equals(item.FilePath, remotePath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            StatusMessage = _localization.Format("MdbxMetadataAlreadyRegisteredFormat", existing.Name);
            return;
        }

        var metadata = await CreateRemoteMdbxMetadataAsync(
            _localization.Get("MdbxWebDavVaultName"),
            remotePath,
            MdbxStorageLocation.RemoteWebDav,
            "REMOTE_WEBDAV",
            BuildMdbxWorkingCopyPath("webdav-local.mdbx"),
            _localization.Get("MdbxWebDavMetadataDescription"));
        metadata.IsDefault = MdbxDatabases.Count == 0;
        await _repository.SaveMdbxDatabaseAsync(metadata);
        MdbxDatabases.Add(metadata);
        RefreshMdbxVaultState();
        RefreshVaultSources();
        StatusMessage = _localization.Get("CreatedMdbxWebDavMetadata");
    }

    [RelayCommand]
    private async Task CreateOneDriveMdbxVaultAsync()
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
    private async Task RefreshMdbxVaultsAsync()
    {
        MdbxDatabases.Clear();
        foreach (var database in await _repository.GetMdbxDatabasesAsync())
        {
            MdbxDatabases.Add(database);
        }

        RefreshMdbxVaultState();
        RefreshVaultSources();
        StatusMessage = _localization.Get("MdbxVaultsRefreshed");
    }

    [RelayCommand]
    private async Task OpenMdbxDatabaseAsync(MdbxDatabaseDisplayItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(item.Database.WorkingCopyPath ?? item.Database.FilePath))
        {
            StatusMessage = _localization.Get("MdbxRemoteOpenPending");
            return;
        }

        await using var stream = await _mdbxVaultService.OpenLocalStreamAsync(item.Database);
        item.Database.LastAccessedAt = DateTimeOffset.UtcNow;
        await _repository.SaveMdbxDatabaseAsync(item.Database);
        RefreshMdbxVaultState();
        RefreshVaultSources();
        StatusMessage = _localization.Format("OpenedMdbxDatabaseFormat", item.Name, stream.Length);
    }

    [RelayCommand]
    private async Task SetDefaultMdbxDatabaseAsync(MdbxDatabaseDisplayItem? item)
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
