using Monica.App.Services;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void RaiseSyncPageState()
    {
        OnPropertyChanged(nameof(WebDavConnectionStatusText));
        OnPropertyChanged(nameof(SyncStatusSummaryText));
        OnPropertyChanged(nameof(SyncConfigurationSummaryText));
        OnPropertyChanged(nameof(SyncRecoverySummaryText));
        OnPropertyChanged(nameof(OneDriveConnectionStatusText));
        RefreshSyncHealthItems();
    }

    private void RefreshSyncHealthItems()
    {
        SyncHealthItems.Clear();
        SyncHealthItems.Add(new SyncHealthDisplayItem(
            _localization.WebDav,
            WebDavEnabled ? BuildWebDavSourceStatus() : _localization.Get("Disabled"),
            WebDavConnectionStatusText));
        SyncHealthItems.Add(new SyncHealthDisplayItem(
            _localization.Get("RemoteSync"),
            WebDavEnabled ? _localization.Get("Enabled") : _localization.Get("LocalOnly"),
            SyncConfigurationSummaryText));
        SyncHealthItems.Add(new SyncHealthDisplayItem(
            _localization.Get("BackupHistory"),
            WebDavBackupHistoryCountText,
            SyncRecoverySummaryText));
        SyncHealthItems.Add(new SyncHealthDisplayItem(
            _localization.OneDrive,
            OneDriveConnectionStatusText,
            _localization.Get("OneDriveBoundaryDescription")));
        SyncHealthItems.Add(new SyncHealthDisplayItem(
            _localization.MdbxVaults,
            MdbxDatabaseCountText,
            MdbxSyncDiagnosticsSummaryText));
        OnPropertyChanged(nameof(SyncHealthItems));
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

    private bool TryCreateWebDavProfile(out WebDavProfile profile)
    {
        profile = new WebDavProfile();
        if (!WebDavEnabled)
        {
            StatusMessage = _localization.Get("EnableWebDavFirst");
            return false;
        }

        if (!Uri.TryCreate(WebDavServerUrl, UriKind.Absolute, out var baseUri))
        {
            StatusMessage = _localization.Get("WebDavServerUrlRequired");
            return false;
        }

        profile = new WebDavProfile
        {
            BaseUri = baseUri,
            Username = WebDavUsername.Trim(),
            Password = WebDavPassword,
            RootPath = string.IsNullOrWhiteSpace(WebDavRemotePath) ? "/" : WebDavRemotePath
        };
        return true;
    }

    private WebDavBackupHistoryItem ToWebDavBackupHistoryItem(RemoteFileEntry item)
    {
        var fileName = ExtractWebDavFileName(item.Path);
        var dateString = item.LastModified is null
            ? _localization.Get("UnknownDate")
            : item.LastModified.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm", _localization.Culture);
        return new WebDavBackupHistoryItem(
            fileName,
            item.Path,
            dateString,
            FormatByteSize(item.Length),
            item.LastModified);
    }

    private void RaiseWebDavBackupHistoryState()
    {
        if (SelectedWebDavBackupHistoryItem is not null &&
            !WebDavBackupHistory.Contains(SelectedWebDavBackupHistoryItem))
        {
            SelectedWebDavBackupHistoryItem = WebDavBackupHistory.FirstOrDefault();
        }

        OnPropertyChanged(nameof(WebDavBackupHistoryCountText));
        OnPropertyChanged(nameof(HasWebDavBackupHistory));
        OnPropertyChanged(nameof(HasSelectedWebDavBackupHistoryItem));
        RaiseSyncPageState();
    }

    private static string ExtractWebDavFileName(string path)
    {
        var normalized = Uri.TryCreate(path, UriKind.Absolute, out var uri) ? uri.AbsolutePath : path;
        normalized = normalized.TrimEnd('/');
        var index = normalized.LastIndexOf('/');
        return Uri.UnescapeDataString(index >= 0 ? normalized[(index + 1)..] : normalized);
    }

    private string FormatByteSize(long? length)
    {
        if (length is null)
        {
            return _localization.Get("UnknownSize");
        }

        var value = (double)length.Value;
        string[] units = ["B", "KB", "MB", "GB"];
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return string.Format(_localization.Culture, "{0:0.#} {1}", value, units[unitIndex]);
    }

    private string BuildWebDavSourceStatus()
    {
        if (string.IsNullOrWhiteSpace(WebDavServerUrl))
        {
            return _localization.Get("NotConfigured");
        }

        if (WebDavSyncOnStartup && WebDavSyncAfterChanges)
        {
            return _localization.Get("AutomaticSync");
        }

        if (WebDavSyncOnStartup)
        {
            return _localization.Get("StartupSync");
        }

        if (WebDavSyncAfterChanges)
        {
            return _localization.Get("ChangeSync");
        }

        return _localization.Get("ManualSync");
    }

    private void UpdateWebDavBackupOption(Action<DesktopAppSettings> update)
    {
        UpdateSettings(update);
        OnPropertyChanged(nameof(WebDavBackupOptionsSummaryText));
        RaiseSyncPageState();
    }
}
