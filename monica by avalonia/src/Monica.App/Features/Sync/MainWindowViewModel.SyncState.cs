using Monica.App.Services;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private int _webDavOperationActive;

    private bool TryBeginWebDavOperation(bool isLoading)
    {
        if (Interlocked.CompareExchange(ref _webDavOperationActive, 1, 0) != 0)
        {
            StatusMessage = _localization.Get("WebDavOperationInProgress");
            return false;
        }

        if (isLoading)
        {
            IsLoadingWebDavBackups = true;
            WebDavOperationStageText = _localization.Get("WebDavLoadingBackups");
        }
        else
        {
            IsRunningWebDavBackup = true;
            WebDavOperationStageText = _localization.Get("WebDavPreparingOperation");
        }

        return true;
    }

    private void EndWebDavOperation(bool wasLoading)
    {
        Interlocked.Exchange(ref _webDavOperationActive, 0);
        if (wasLoading)
        {
            IsLoadingWebDavBackups = false;
        }
        else
        {
            IsRunningWebDavBackup = false;
        }

        WebDavOperationStageText = "";
    }

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

        try
        {
            WebDavEndpointPolicy.EnsureSecure(baseUri);
        }
        catch (InvalidOperationException)
        {
            StatusMessage = _localization.Get("WebDavHttpsRequired");
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
