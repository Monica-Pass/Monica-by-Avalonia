using CommunityToolkit.Mvvm.Input;

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
    private Task SetDefaultMdbxDatabaseAsync(MdbxDatabaseDisplayItem? item) =>
        RunMdbxOperationAsync("MdbxOperationSetDefault", () => SetDefaultMdbxDatabaseCoreAsync(item));

    private async Task SetDefaultMdbxDatabaseCoreAsync(MdbxDatabaseDisplayItem? item)
    {
        if (item is null) return;

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
