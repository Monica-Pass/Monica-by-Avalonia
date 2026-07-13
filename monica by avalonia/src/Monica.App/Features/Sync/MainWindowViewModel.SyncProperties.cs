using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly IWebDavBackupService _webDavBackupService;

    public ObservableCollection<SyncHealthDisplayItem> SyncHealthItems { get; } = [];
    public ObservableCollection<WebDavBackupHistoryItem> WebDavBackupHistory { get; } = [];
    public ObservableCollection<SettingsChoice> ConflictStrategyOptions { get; } = [];

    public string WebDavConnectionStatusText => WebDavEnabled
        ? _localization.Format("WebDavConfiguredFormat", string.IsNullOrWhiteSpace(WebDavServerUrl) ? _localization.Get("NotConfigured") : WebDavServerUrl)
        : _localization.Get("WebDavDisabled");
    public string SyncStatusSummaryText => WebDavEnabled
        ? _localization.Format("SyncStatusSummaryFormat", BuildWebDavSourceStatus(), WebDavBackupHistory.Count)
        : _localization.Get("SyncStatusLocalOnly");
    public string SyncConfigurationSummaryText
    {
        get
        {
            if (!WebDavEnabled)
            {
                return _localization.Get("SyncConfigurationDisabled");
            }

            return Uri.TryCreate(WebDavServerUrl, UriKind.Absolute, out _)
                ? _localization.Format("SyncConfigurationReadyFormat", string.IsNullOrWhiteSpace(WebDavRemotePath) ? "/" : WebDavRemotePath)
                : _localization.Get("SyncConfigurationIncomplete");
        }
    }
    public string SyncRecoverySummaryText
    {
        get
        {
            if (!WebDavEnabled)
            {
                return _localization.Get("SyncRecoveryLocalOnly");
            }

            return HasWebDavBackupHistory
                ? _localization.Format("SyncRecoveryBackupReadyFormat", WebDavBackupHistory.Count)
                : _localization.Get("SyncRecoveryNoBackupsLoaded");
        }
    }
    public string OneDriveConnectionStatusText => OneDriveEnabled
        ? _localization.Get("OneDriveBoundaryEnabled")
        : _localization.Get("FeatureDisabled");
    public string WebDavBackupHistoryCountText => _localization.Format("WebDavBackupHistoryCountFormat", WebDavBackupHistory.Count);
    public bool HasWebDavBackupHistory => WebDavBackupHistory.Count > 0;
    public bool HasSelectedWebDavBackupHistoryItem => SelectedWebDavBackupHistoryItem is not null;
    public bool IsWebDavBusy => IsLoadingWebDavBackups || IsRunningWebDavBackup;
    public string WebDavBackupOptionsSummaryText => _localization.Format(
        "WebDavBackupOptionsSummaryFormat",
        CountSelectedWebDavBackupOptions(),
        WebDavBackupEncryptionEnabled ? _localization.Get("Encrypted") : _localization.Get("PlainJson"));
    public bool IsSyncConfigurationSelected => IsWorkspacePageSelected(SelectedSyncPage, "Configuration");
    public bool IsSyncBackupSelected => IsWorkspacePageSelected(SelectedSyncPage, "Backup");
    public bool IsSyncSourcesSelected => IsWorkspacePageSelected(SelectedSyncPage, "Sources");
    public bool IsSyncImportSelected => IsWorkspacePageSelected(SelectedSyncPage, "Import");
    public bool IsSyncExportSelected => IsWorkspacePageSelected(SelectedSyncPage, "Export");
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedWebDavBackupHistoryItem))]
    private WebDavBackupHistoryItem? _selectedWebDavBackupHistoryItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSyncConfigurationSelected))]
    [NotifyPropertyChangedFor(nameof(IsSyncBackupSelected))]
    [NotifyPropertyChangedFor(nameof(IsSyncSourcesSelected))]
    [NotifyPropertyChangedFor(nameof(IsSyncImportSelected))]
    [NotifyPropertyChangedFor(nameof(IsSyncExportSelected))]
    private string _selectedSyncPage = "Configuration";

    [ObservableProperty]
    private bool _webDavEnabled;

    [ObservableProperty]
    private string _webDavServerUrl = "";

    [ObservableProperty]
    private string _webDavUsername = "";

    [ObservableProperty]
    private string _webDavPassword = "";

    [ObservableProperty]
    private string _webDavRemotePath = "/Monica";

    [ObservableProperty]
    private bool _webDavSyncOnStartup;

    [ObservableProperty]
    private bool _webDavSyncAfterChanges;

    [ObservableProperty]
    private bool _isLoadingWebDavBackups;

    [ObservableProperty]
    private bool _isRunningWebDavBackup;

    [ObservableProperty]
    private bool _webDavBackupIncludePasswords = true;

    [ObservableProperty]
    private bool _webDavBackupIncludeTotp = true;

    [ObservableProperty]
    private bool _webDavBackupIncludeNotes = true;

    [ObservableProperty]
    private bool _webDavBackupIncludeCards = true;

    [ObservableProperty]
    private bool _webDavBackupIncludeDocuments = true;

    [ObservableProperty]
    private bool _webDavBackupIncludeImages = true;

    [ObservableProperty]
    private bool _webDavBackupIncludeCategories = true;

    [ObservableProperty]
    private bool _webDavBackupEncryptionEnabled = true;

    [ObservableProperty]
    private string _webDavBackupEncryptionPassword = "";

    [ObservableProperty]
    private string _syncConflictStrategy = "ask";

    [ObservableProperty]
    private bool _oneDriveEnabled;

    private sealed class DisabledWebDavBackupService : IWebDavBackupService
    {
        public string NormalizeRemotePath(string rootPath, string relativePath) => relativePath;

        public Task<IReadOnlyList<RemoteFileEntry>> ListAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RemoteFileEntry>>([]);

        public Task UploadTextAsync(WebDavProfile profile, string relativePath, string content, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<string> DownloadTextAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult("");

        public Task DeleteAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
