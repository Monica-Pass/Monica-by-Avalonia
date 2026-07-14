using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.Core.Models;
using Monica.Data;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed record MdbxDatabaseDisplayItem(
    LocalMdbxDatabase Database,
    string Name,
    string Source,
    string LocalPath,
    string RemotePath,
    string Mode,
    string UnlockMethod,
    string CreatedText,
    string LastAccessedText,
    string LastSyncedText,
    string SyncStatus,
    string Description,
    string WorkingCopyStatus,
    string RemoteStatus,
    string CachePath,
    string LastSyncErrorText,
    bool HasLastSyncError,
    bool IsDefault,
    bool IsLocal,
    bool IsRemote);

public sealed partial class MainWindowViewModel
{
    private readonly IMdbxVaultService _mdbxVaultService;

    public ObservableCollection<LocalMdbxDatabase> MdbxDatabases { get; } = new ObservableRangeCollection<LocalMdbxDatabase>();
    public ObservableCollection<MdbxDatabaseDisplayItem> MdbxDatabaseItems { get; } = [];
    public ObservableCollection<SyncHealthDisplayItem> MdbxHealthItems { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedMdbxDatabaseItem))]
    private MdbxDatabaseDisplayItem? _selectedMdbxDatabaseItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMdbxDetailsSelected))]
    [NotifyPropertyChangedFor(nameof(IsMdbxHealthSelected))]
    [NotifyPropertyChangedFor(nameof(IsMdbxSourcesSelected))]
    [NotifyPropertyChangedFor(nameof(IsMdbxRuntimeSelected))]
    private string _selectedMdbxWorkspacePage = "Details";

    [ObservableProperty]
    private bool _mdbxLocalCacheEnabled = true;

    [ObservableProperty]
    private bool _isMdbxBusy;

    public string MdbxDatabaseCountText => _localization.Format("MdbxDatabaseCountFormat", MdbxDatabases.Count);
    public string MdbxLocalCountText => _localization.Format("MdbxSourceCountFormat", MdbxLocalDatabaseCount);
    public string MdbxWebDavCountText => _localization.Format("MdbxSourceCountFormat", MdbxWebDavDatabaseCount);
    public string MdbxOneDriveCountText => _localization.Format("MdbxSourceCountFormat", MdbxOneDriveDatabaseCount);
    public int MdbxLocalDatabaseCount => MdbxDatabases.Count(IsLocalMdbxDatabase);
    public int MdbxWebDavDatabaseCount => MdbxDatabases.Count(item => item.StorageLocation == MdbxStorageLocation.RemoteWebDav);
    public int MdbxOneDriveDatabaseCount => MdbxDatabases.Count(item => item.StorageLocation == MdbxStorageLocation.RemoteOneDrive);
    public int MdbxRemoteDatabaseCount => MdbxWebDavDatabaseCount + MdbxOneDriveDatabaseCount;
    public int MdbxWorkingCopyCount => MdbxDatabases.Count(HasMdbxWorkingCopy);
    public int MdbxOfflineCopyCount => MdbxDatabases.Count(item => item.IsOfflineAvailable || HasMdbxWorkingCopy(item));
    public int MdbxPendingSyncCount => MdbxDatabases.Count(HasPendingMdbxSync);
    public int MdbxSyncErrorCount => MdbxDatabases.Count(HasMdbxSyncIssue);
    public bool HasMdbxDatabases => MdbxDatabases.Count > 0;
    public bool HasMdbxSyncErrors => MdbxSyncErrorCount > 0;
    public string MdbxDefaultVaultSummaryText
    {
        get
        {
            var defaultVault = MdbxDatabases.FirstOrDefault(item => item.IsDefault);
            return defaultVault is null
                ? _localization.Get("MdbxDefaultVaultMissing")
                : _localization.Format("MdbxDefaultVaultFormat", string.IsNullOrWhiteSpace(defaultVault.Name) ? "MDBX" : defaultVault.Name);
        }
    }

    public string MdbxWorkingCopySummaryText => MdbxWorkingCopyCount == 0
        ? _localization.Get("MdbxNoWorkingCopies")
        : _localization.Format("MdbxWorkingCopySummaryFormat", MdbxWorkingCopyCount, MdbxDatabases.Count, MdbxOfflineCopyCount);
    public string MdbxRemoteSummaryText => MdbxRemoteDatabaseCount == 0
        ? _localization.Get("MdbxRemoteSourceEmpty")
        : _localization.Format("MdbxRemoteSummaryFormat", MdbxRemoteDatabaseCount, MdbxPendingSyncCount);
    public string MdbxSyncDiagnosticsSummaryText => MdbxSyncErrorCount > 0
        ? _localization.Format("MdbxSyncErrorsFormat", MdbxSyncErrorCount)
        : MdbxPendingSyncCount > 0
            ? _localization.Format("MdbxPendingSyncFormat", MdbxPendingSyncCount)
            : _localization.Get("MdbxNoSyncErrors");
    public string MdbxCachePolicyText => MdbxLocalCacheEnabled
        ? _localization.Get("MdbxCacheEnabled")
        : _localization.Get("MdbxCacheDisabled");
    public string MdbxLocalSourceStatusText => MdbxLocalDatabaseCount > 0
        ? _localization.Format("MdbxLocalSourceReadyFormat", MdbxLocalDatabaseCount)
        : _localization.Get("MdbxLocalSourceEmpty");
    public string MdbxWebDavSourceStatusText => !WebDavEnabled
        ? _localization.Get("WebDavDisabled")
        : MdbxWebDavDatabaseCount > 0
            ? _localization.Format("MdbxWebDavSourceReadyFormat", MdbxWebDavDatabaseCount)
            : _localization.Get("MdbxWebDavSourceEmpty");
    public string MdbxOneDriveSourceStatusText => !OneDriveEnabled
        ? _localization.Get("FeatureDisabled")
        : MdbxOneDriveDatabaseCount > 0
            ? _localization.Format("MdbxOneDriveSourceReadyFormat", MdbxOneDriveDatabaseCount)
            : _localization.Get("MdbxOneDriveSourceEmpty");
    public string MdbxRuntimeSummaryText => _localization.Get("MdbxRuntimeSummary");
    public string MdbxSecuritySummaryText => _localization.Get("MdbxSecuritySummary");
    public bool IsMdbxDetailsSelected => IsWorkspacePageSelected(SelectedMdbxWorkspacePage, "Details");
    public bool IsMdbxHealthSelected => IsWorkspacePageSelected(SelectedMdbxWorkspacePage, "Health");
    public bool IsMdbxSourcesSelected => IsWorkspacePageSelected(SelectedMdbxWorkspacePage, "Sources");
    public bool IsMdbxRuntimeSelected => IsWorkspacePageSelected(SelectedMdbxWorkspacePage, "Runtime");
    public bool HasSelectedMdbxDatabaseItem => SelectedMdbxDatabaseItem is not null;

    partial void OnMdbxLocalCacheEnabledChanged(bool value)
    {
        UpdateSettings(settings => settings.MdbxLocalCacheEnabled = value);
        RaiseSyncPageState();
        RefreshMdbxVaultState();
    }
}
