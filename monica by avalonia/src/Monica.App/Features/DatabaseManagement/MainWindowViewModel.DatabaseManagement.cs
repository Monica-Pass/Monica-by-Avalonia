using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Monica.App.ViewModels;

public sealed record VaultSourceDisplayItem(string DisplayName, string Kind, string LocalPath, string RemoteUrl, string SyncStatus);

public sealed partial class MainWindowViewModel
{
    public ObservableCollection<VaultSourceDisplayItem> VaultSources { get; } = [];
    public string VaultSourceCountText => _localization.Format("VaultSourceCountFormat", VaultSources.Count);
    public string LocalDatabaseSummaryText => _localization.Format("DatabaseSummaryFormat", Passwords.Count, NoteItems.Count, TotpItems.Count, WalletItems.Count);
    public bool HasSelectedVaultSource => SelectedVaultSource is not null;
    public bool HasVaultSources => VaultSources.Count > 0;
    public bool IsDatabaseSourceSelected => IsWorkspacePageSelected(SelectedDatabaseManagementPage, "Source");
    public bool IsDatabaseOverviewSelected => IsWorkspacePageSelected(SelectedDatabaseManagementPage, "Overview");
    public bool IsDatabaseCloudSelected => IsWorkspacePageSelected(SelectedDatabaseManagementPage, "Cloud");
    public bool IsDatabaseCapabilitiesSelected => IsWorkspacePageSelected(SelectedDatabaseManagementPage, "Capabilities");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedVaultSource))]
    private VaultSourceDisplayItem? _selectedVaultSource;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDatabaseSourceSelected))]
    [NotifyPropertyChangedFor(nameof(IsDatabaseOverviewSelected))]
    [NotifyPropertyChangedFor(nameof(IsDatabaseCloudSelected))]
    [NotifyPropertyChangedFor(nameof(IsDatabaseCapabilitiesSelected))]
    private string _selectedDatabaseManagementPage = "Source";

    [RelayCommand]
    private void SelectDatabaseManagementPage(string? page)
    {
        SelectedDatabaseManagementPage = NormalizeDatabaseManagementPage(page);
    }

    private static string NormalizeDatabaseManagementPage(string? page) =>
        page?.Trim().ToLowerInvariant() switch
        {
            "overview" or "local" => "Overview",
            "cloud" or "vaults" or "sources" => "Cloud",
            "capabilities" or "features" => "Capabilities",
            _ => "Source"
        };

    [RelayCommand]
    private void ShowVaultSourceDetails(VaultSourceDisplayItem? source)
    {
        if (source is not null)
        {
            SelectedVaultSource = source;
        }
    }

    private void RefreshVaultSources()
    {
        var selectedName = SelectedVaultSource?.DisplayName;
        var selectedKind = SelectedVaultSource?.Kind;
        VaultSources.Clear();
        VaultSources.Add(new VaultSourceDisplayItem(
            _localization.LocalDatabase,
            "SQLite",
            _localization.Get("CanonicalVault"),
            _localization.Get("LocalOnly"),
            _localization.Get("Available")));

        if (WebDavEnabled)
        {
            VaultSources.Add(new VaultSourceDisplayItem(
                _localization.WebDav,
                "WebDAV",
                string.IsNullOrWhiteSpace(WebDavRemotePath) ? "/" : WebDavRemotePath,
                string.IsNullOrWhiteSpace(WebDavServerUrl) ? _localization.Get("NotConfigured") : WebDavServerUrl,
                BuildWebDavSourceStatus()));
        }

        foreach (var database in MdbxDatabases)
        {
            var isLocalMdbx = IsLocalMdbxDatabase(database);
            var localPath = string.IsNullOrWhiteSpace(database.WorkingCopyPath)
                ? database.FilePath
                : database.WorkingCopyPath;
            var remotePath = isLocalMdbx
                ? _localization.Get("LocalOnly")
                : string.IsNullOrWhiteSpace(database.FilePath) ? _localization.Get("NotConfigured") : database.FilePath;
            VaultSources.Add(new VaultSourceDisplayItem(
                string.IsNullOrWhiteSpace(database.Name) ? "MDBX" : database.Name,
                "MDBX",
                string.IsNullOrWhiteSpace(localPath) ? _localization.Get("NotConfigured") : localPath,
                remotePath,
                LocalizeSyncStatus(database.LastSyncStatus)));
        }

        var keePassGroups = Passwords
            .Concat(ArchivedPasswords)
            .Concat(DeletedPasswords)
            .Where(item => item.KeepassDatabaseId is not null)
            .GroupBy(item => item.KeepassDatabaseId!.Value)
            .OrderBy(group => group.Key);

        foreach (var group in keePassGroups)
        {
            var sample = group.First();
            VaultSources.Add(new VaultSourceDisplayItem(
                _localization.Format("KeePassSourceNameFormat", group.Key),
                "KDBX",
                sample.KeepassGroupPath ?? _localization.Get("NotConfigured"),
                _localization.Format("EntryCountFormat", group.Count()),
                _localization.Get("DesktopEquivalent")));
        }

        var bitwardenGroups = Passwords
            .Concat(ArchivedPasswords)
            .Concat(DeletedPasswords)
            .Where(item => item.BitwardenVaultId is not null)
            .GroupBy(item => item.BitwardenVaultId!.Value)
            .OrderBy(group => group.Key);

        foreach (var group in bitwardenGroups)
        {
            var pendingCount = group.Count(item => item.BitwardenLocalModified);
            VaultSources.Add(new VaultSourceDisplayItem(
                _localization.Format("BitwardenSourceNameFormat", group.Key),
                "Bitwarden",
                _localization.Format("EntryCountFormat", group.Count()),
                pendingCount > 0 ? _localization.Format("PendingSyncCountFormat", pendingCount) : _localization.Get("NoPendingChanges"),
                pendingCount > 0 ? _localization.Get("Pending") : _localization.Get("Available")));
        }

        SelectedVaultSource =
            VaultSources.FirstOrDefault(item =>
                string.Equals(item.DisplayName, selectedName, StringComparison.Ordinal) &&
                string.Equals(item.Kind, selectedKind, StringComparison.Ordinal)) ??
            VaultSources.FirstOrDefault();

        OnPropertyChanged(nameof(VaultSourceCountText));
        OnPropertyChanged(nameof(HasVaultSources));
    }
}
