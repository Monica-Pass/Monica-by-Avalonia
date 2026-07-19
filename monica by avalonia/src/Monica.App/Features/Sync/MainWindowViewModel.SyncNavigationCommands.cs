using CommunityToolkit.Mvvm.Input;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private void SelectSyncPage(string? page)
    {
        SelectedSyncPage = NormalizeSyncPage(page);
    }

    [RelayCommand]
    private void OpenSyncWorkspacePage(string? page)
    {
        SelectedSyncPage = NormalizeSyncPage(page);
        SelectedSection = "Sync";
    }

    [RelayCommand]
    private void ShowWebDavBackupDetails(WebDavBackupHistoryItem? item)
    {
        if (item is not null)
        {
            SelectedWebDavBackupHistoryItem = item;
        }
    }

    private static string NormalizeSyncPage(string? page) =>
        page?.Trim().ToLowerInvariant() switch
        {
            "backup" or "backups" or "history" => "Backup",
            "sources" or "vaults" or "database" => "Sources",
            "import" => "Import",
            "export" => "Export",
            _ => "Configuration"
        };
}
