using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.Styling;
using Monica.App;
using Monica.App.Services;
using Monica.Core.ImportExport;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Repositories;
using Monica.Data.Services;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed record SettingsChoice(object Value, string Label);
public sealed record TimelineEntry(string Title, string Description, string TimestampText, string OperationType, string ItemType);
public sealed record VaultSourceDisplayItem(string DisplayName, string Kind, string LocalPath, string RemoteUrl, string SyncStatus);
public sealed record SyncHealthDisplayItem(string Label, string Value, string Detail);
internal sealed record VaultLoadSnapshot(
    IReadOnlyList<PasswordEntry> ActivePasswords,
    IReadOnlyList<PasswordEntry> ArchivedPasswords,
    IReadOnlyList<PasswordEntry> DeletedPasswords,
    IReadOnlyDictionary<long, IReadOnlyList<CustomField>> PasswordCustomFields,
    IReadOnlyDictionary<long, IReadOnlyList<Attachment>> PasswordAttachments,
    IReadOnlyList<SecureItem> NoteItems,
    IReadOnlyList<SecureItem> WalletItems,
    IReadOnlyList<SecureItem> StoredTotps,
    IReadOnlyList<Category> Categories,
    IReadOnlyDictionary<long, PasswordQuickAccessRecord> PasswordQuickAccessRecords,
    IReadOnlyList<LocalMdbxDatabase> MdbxDatabases);
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
public sealed record WebDavBackupHistoryItem(string FileName, string Path, string DateString, string SizeText, DateTimeOffset? LastModified);
public sealed record MonicaJsonImportResult(int Passwords, int SecureItems, int Categories);

internal sealed class DisabledTotpEditorDialogService : ITotpEditorDialogService
{
    public Task<TotpEditorViewModel?> ShowAsync(SecureItem? item, CancellationToken cancellationToken = default) =>
        Task.FromResult<TotpEditorViewModel?>(null);
}

internal sealed class DisabledWalletItemEditorDialogService : IWalletItemEditorDialogService
{
    public Task<WalletItemEditorViewModel?> ShowAsync(SecureItem? item, VaultItemType? newItemType = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<WalletItemEditorViewModel?>(null);
}

internal sealed class DisabledMasterPasswordMaintenanceService : IMasterPasswordMaintenanceService
{
    public Task<MasterPasswordMaintenanceResult> ChangeMasterPasswordAsync(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(MasterPasswordMaintenanceResult.Failure("Master password maintenance is not available."));

    public Task<MasterPasswordMaintenanceResult> ResetMasterPasswordFromUnlockedVaultAsync(
        string newPassword,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(MasterPasswordMaintenanceResult.Failure("Master password maintenance is not available."));
}

internal sealed class DisabledConfirmationDialogService : IConfirmationDialogService
{
    public Task<bool> ConfirmAsync(
        string title,
        string message,
        string primaryButtonText,
        string? closeButtonText = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<bool> ConfirmTypedAsync(
        string title,
        string message,
        string requiredPhrase,
        string instruction,
        string primaryButtonText,
        string? closeButtonText = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

public sealed partial class MainWindowViewModel : ObservableObject
{
    public const string GitHubRepositoryUrl = "https://github.com/JoyinJoester/Monica";

    private const int PasswordHistoryLimit = 10;
    private const int PasswordQuickAccessLimit = 6;
    private static readonly TimeSpan SelectedPasswordDetailsCoalesceDelay = TimeSpan.FromMilliseconds(60);
    private static readonly TimeSpan SelectedPasswordDetailsLoadingDelay = TimeSpan.FromMilliseconds(120);
    private static readonly PlatformFilePickerFileType[] MonicaJsonFileTypes =
    [
        new("Monica JSON", ["*.json"])
    ];
    private static readonly PlatformFilePickerFileType[] PasswordCsvFileTypes =
    [
        new("Password CSV", ["*.csv"])
    ];
    private static readonly PlatformFilePickerFileType[] NoteCsvFileTypes =
    [
        new("Notes CSV", ["*.csv"])
    ];
    private static readonly PlatformFilePickerFileType[] MarkdownFileTypes =
    [
        new("Markdown", ["*.md", "*.markdown"])
    ];
    private static readonly PlatformFilePickerFileType[] NoteImageFileTypes =
    [
        new("Images", ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp"])
    ];

    private enum QuickAccessSort
    {
        Recent,
        Frequent
    }

    private sealed class PasswordFolderTreeNode(string key, string displayName, int level)
    {
        public string Key { get; } = key;
        public string DisplayName { get; } = displayName;
        public int Level { get; } = level;
        public Category? Category { get; set; }
        public int ExactCount { get; set; }
        public int DescendantCount { get; set; }
        public List<PasswordFolderTreeNode> Children { get; } = [];
    }

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


    private readonly IMonicaRepository _repository;
    private readonly ICryptoService _cryptoService;
    private readonly ITotpService _totpService;
    private readonly IPasswordGeneratorService _passwordGenerator;
    private readonly IPwnedPasswordService _pwnedPasswordService;
    private readonly IImportExportService _importExportService;
    private readonly IClipboardService _clipboardService;
    private readonly IWebDavBackupService _webDavBackupService;
    private readonly IMdbxVaultService _mdbxVaultService;
    private readonly IPasswordAttachmentFileService _passwordAttachmentFileService;
    private readonly IPasswordEditorDialogService _passwordEditorDialogService;
    private readonly IPasswordDetailDialogService _passwordDetailDialogService;
    private readonly ICategoryPickerDialogService _categoryPickerDialogService;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly ITotpEditorDialogService _totpEditorDialogService;
    private readonly IWalletItemEditorDialogService _walletItemEditorDialogService;
    private readonly IMasterPasswordMaintenanceService _masterPasswordMaintenanceService;
    private readonly IAppSettingsService _settingsService;
    private readonly ILocalizationService _localization;
    private readonly SecurityQuestionService _securityQuestionService = new();
    private IReadOnlyDictionary<long, IReadOnlyList<CustomField>> _passwordCustomFields = new Dictionary<long, IReadOnlyList<CustomField>>();
    private IReadOnlyDictionary<long, IReadOnlyList<Attachment>> _passwordAttachments = new Dictionary<long, IReadOnlyList<Attachment>>();
    private IReadOnlyDictionary<long, PasswordQuickAccessRecord> _passwordQuickAccessRecords = new Dictionary<long, PasswordQuickAccessRecord>();
    private IReadOnlyList<PasswordEntry> _filteredPasswords = [];
    private IReadOnlyList<PasswordListRow> _filteredPasswordRows = [];
    private bool _filteredPasswordsDirty = true;
    private bool _filteredPasswordRowsDirty = true;
    private int _selectedPasswordCount;
    private bool _suppressPasswordSelectionStateNotifications;
    private bool _isSyncingSelectedPasswordListRow;
    private bool _isApplyingSettings;
    private bool _isApplyingPasswordSearchImmediately;
    private CancellationTokenSource? _passwordSearchDebounceCts;
    private CancellationTokenSource? _selectedPasswordDetailsCts;
    private int _selectedPasswordDetailsVersion;
    private readonly HashSet<string> _collapsedPasswordFolderKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedPasswordStackKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _settingsSaveSync = new();
    private bool _isSavingSettings;
    private bool _hasPendingSettingsSave;

    public MainWindowViewModel(
        IMonicaRepository repository,
        IVaultCredentialStore credentialStore,
        ICryptoService cryptoService,
        ITotpService totpService,
        IPasswordGeneratorService passwordGenerator,
        IImportExportService importExportService,
        IPlatformCapabilityService platformCapabilityService,
        IPlatformIntegrationService platformIntegrationService,
        IClipboardService clipboardService,
        IWebDavBackupService? webDavBackupService,
        IMdbxVaultService mdbxVaultService,
        IPasswordAttachmentFileService passwordAttachmentFileService,
        IPasswordEditorDialogService passwordEditorDialogService,
        IPasswordDetailDialogService passwordDetailDialogService,
        ICategoryPickerDialogService categoryPickerDialogService,
        ILegacyVaultDetector? legacyVaultDetector,
        IAppSettingsService settingsService,
        ILocalizationService localization,
        IPwnedPasswordService? pwnedPasswordService = null,
        IConfirmationDialogService? confirmationDialogService = null,
        ITotpEditorDialogService? totpEditorDialogService = null,
        IWalletItemEditorDialogService? walletItemEditorDialogService = null,
        IMasterPasswordMaintenanceService? masterPasswordMaintenanceService = null,
        IVaultUnlockCoordinator? vaultUnlockCoordinator = null,
        IExternalLinkService? externalLinkService = null,
        IFileSystemPickerService? fileSystemPickerService = null)
    {
        _repository = repository;
        _cryptoService = cryptoService;
        _totpService = totpService;
        _passwordGenerator = passwordGenerator;
        _pwnedPasswordService = pwnedPasswordService ?? new PwnedPasswordService();
        _importExportService = importExportService;
        _clipboardService = clipboardService;
        _webDavBackupService = webDavBackupService ?? new DisabledWebDavBackupService();
        _mdbxVaultService = mdbxVaultService;
        _passwordAttachmentFileService = passwordAttachmentFileService;
        _passwordEditorDialogService = passwordEditorDialogService;
        _passwordDetailDialogService = passwordDetailDialogService;
        _categoryPickerDialogService = categoryPickerDialogService;
        _confirmationDialogService = confirmationDialogService ?? new DisabledConfirmationDialogService();
        _totpEditorDialogService = totpEditorDialogService ?? new DisabledTotpEditorDialogService();
        _walletItemEditorDialogService = walletItemEditorDialogService ?? new DisabledWalletItemEditorDialogService();
        _masterPasswordMaintenanceService = masterPasswordMaintenanceService ?? new DisabledMasterPasswordMaintenanceService();
        _vaultUnlockCoordinator = vaultUnlockCoordinator ?? new VaultUnlockCoordinator(
            credentialStore,
            _cryptoService,
            legacyVaultDetector ?? new NoLegacyVaultDetector());
        _settingsService = settingsService;
        _localization = localization;
        _localization.PropertyChanged += (_, _) => RefreshLocalizedProperties();
        _sourceCapabilities = platformCapabilityService.GetCapabilities();
        _sourcePlatformIntegrationCapabilities = platformIntegrationService.GetCapabilities();
        _externalLinkService = externalLinkService ?? new SystemExternalLinkService(platformIntegrationService);
        _fileSystemPickerService = fileSystemPickerService ?? new CapabilityOnlyFileSystemPickerService(platformIntegrationService);
        PlatformName = platformIntegrationService.PlatformName;
        CompromisedPasswordStatus = _localization.Get("CompromisedPasswordNotChecked");
        RefreshPlatformIntegrationCapabilities();
        RefreshCapabilities();
        RefreshChoiceLabels();
        RefreshMdbxHealthItems();
        RefreshSyncHealthItems();
    }

    public ILocalizationService L => _localization;
    public ObservableCollection<PasswordEntry> Passwords { get; } = new ObservableRangeCollection<PasswordEntry>();
    public ObservableCollection<Category> Categories { get; } = new ObservableRangeCollection<Category>();
    public ObservableCollection<LocalMdbxDatabase> MdbxDatabases { get; } = new ObservableRangeCollection<LocalMdbxDatabase>();
    public ObservableCollection<MdbxDatabaseDisplayItem> MdbxDatabaseItems { get; } = [];
    public ObservableCollection<TimelineEntry> TimelineEntries { get; } = new ObservableRangeCollection<TimelineEntry>();
    public ObservableCollection<VaultSourceDisplayItem> VaultSources { get; } = [];
    public ObservableCollection<SyncHealthDisplayItem> MdbxHealthItems { get; } = [];
    public ObservableCollection<SyncHealthDisplayItem> SyncHealthItems { get; } = [];
    public ObservableCollection<WebDavBackupHistoryItem> WebDavBackupHistory { get; } = [];
    public ObservableCollection<SettingsChoice> LanguageOptions { get; } = [];
    public ObservableCollection<SettingsChoice> ThemeOptions { get; } = [];
    public ObservableCollection<SettingsChoice> StartupSectionOptions { get; } = [];
    public ObservableCollection<SettingsChoice> AutoLockMinuteOptions { get; } = [];
    public ObservableCollection<SettingsChoice> ClipboardSecondOptions { get; } = [];
    public ObservableCollection<SettingsChoice> ConflictStrategyOptions { get; } = [];
    public ObservableCollection<SettingsChoice> PasswordSortOptions { get; } = [];
    public ObservableCollection<SettingsChoice> SecurityQuestionOptions { get; } = [];
    public ObservableCollection<PasswordFolderFilterChoice> PasswordFolderFilters { get; } = [];
    public IEnumerable<PasswordFolderFilterChoice> SystemPasswordFolderFilters =>
        PasswordFolderFilters.Where(item => item.IsSystemNode);
    public IEnumerable<PasswordFolderFilterChoice> RegularPasswordFolderFilters =>
        PasswordFolderFilters.Where(item => !item.IsSystemNode);
    public bool HasRegularPasswordFolderFilters => PasswordFolderFilters.Any(item => !item.IsSystemNode);

    public string AboutTitle => _localization.Get("About");
    public string AboutDescription => _localization.Get("AboutDescription");
    public string AppVersionLabel => _localization.Get("AppVersion");
    public string GitHubRepositoryLabel => _localization.Get("GitHubRepository");
    public string OpenRepositoryText => _localization.Get("OpenRepository");
    public string RepositoryUrlText => GitHubRepositoryUrl;
    public string AppVersionText => GetAppVersionText();
    public string DangerZoneTitle => _localization.Get("DangerZone");
    public string DangerZoneDescription => _localization.Get("DangerZoneDescription");
    public string ClearVaultDataTitle => _localization.Get("ClearVaultData");
    public string ClearVaultDataDescription => _localization.Get("ClearVaultDataDescription");
    public string ClearPasswordsOnlyText => _localization.Get("ClearPasswordsOnly");
    public string ClearSecureItemsOnlyText => _localization.Get("ClearSecureItemsOnly");
    public string ClearAllVaultDataText => _localization.Get("ClearAllVaultData");
    public string ClearVaultConfirmationInstructionText =>
        _localization.Format("ClearVaultConfirmationInstructionFormat", _localization.Get("ClearVaultConfirmationPhrase"));
    public string ChangeMasterPasswordTitle => _localization.Get("ChangeMasterPassword");
    public string ChangeMasterPasswordDescription => _localization.Get("ChangeMasterPasswordDescription");
    public string CurrentMasterPasswordText => _localization.Get("CurrentMasterPassword");
    public string NewMasterPasswordText => _localization.Get("NewMasterPassword");
    public string ConfirmNewMasterPasswordText => _localization.Get("ConfirmNewMasterPassword");
    public string ChangeMasterPasswordActionText => _localization.Get("ChangeMasterPasswordAction");
    public string SecurityRecoveryTitle => _localization.Get("SecurityRecovery");
    public string SecurityRecoveryDescription => _localization.Get("SecurityRecoveryDescription");
    public string SecurityRecoveryStatusText => _settingsService.Current.SecurityRecovery.HasCompleteSetup
        ? _localization.Get("SecurityQuestionsConfigured")
        : _localization.Get("SecurityQuestionsNotConfigured");
    public string SecurityRecoveryEnabledText => _localization.Get("SecurityRecoveryEnabled");
    public string SecurityQuestion1Text => _localization.Get("SecurityQuestion1");
    public string SecurityQuestion2Text => _localization.Get("SecurityQuestion2");
    public string SecurityQuestionAnswerText => _localization.Get("SecurityQuestionAnswer");
    public string CustomSecurityQuestionText => _localization.Get("CustomSecurityQuestion");
    public string SaveSecurityQuestionsText => _localization.Get("SaveSecurityQuestions");
    public string ResetMasterPasswordTitle => _localization.Get("ResetMasterPassword");
    public string ResetMasterPasswordDescription => _localization.Get("ResetMasterPasswordDescription");
    public string ResetMasterPasswordActionText => _localization.Get("ResetMasterPasswordAction");
    public string SecurityRecoveryQuestion1PromptText => _settingsService.Current.SecurityRecovery.Question1Text;
    public string SecurityRecoveryQuestion2PromptText => _settingsService.Current.SecurityRecovery.Question2Text;
    public bool IsSecurityQuestion1Custom => SecurityQuestion1Id == SecurityQuestionService.CustomQuestionId;
    public bool IsSecurityQuestion2Custom => SecurityQuestion2Id == SecurityQuestionService.CustomQuestionId;
    public bool CanResetMasterPasswordWithSecurityQuestions => _settingsService.Current.SecurityRecovery.HasCompleteSetup;
    public bool CanRunResetMasterPassword => CanResetMasterPasswordWithSecurityQuestions && !IsResettingMasterPassword;

    [ObservableProperty]
    private string _selectedSection = "Passwords";

    [ObservableProperty]
    private string _currentMasterPassword = "";

    [ObservableProperty]
    private string _newMasterPassword = "";

    [ObservableProperty]
    private string _confirmNewMasterPassword = "";

    [ObservableProperty]
    private bool _isChangingMasterPassword;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _passwordSearchText = "";

    [ObservableProperty]
    private string _passwordSearchQuery = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecoverableStatusMessage))]
    private string _statusMessage = "Locked";

    [ObservableProperty]
    private string _exportPreview = "";

    [ObservableProperty]
    private string _importJsonText = "";

    [ObservableProperty]
    private string _importNoteCsvText = "";

    [ObservableProperty]
    private string _exportCsvPreview = "";

    [ObservableProperty]
    private string _exportNoteCsvPreview = "";

    [ObservableProperty]
    private string _exportTimelinePreview = "";

    [ObservableProperty]
    private string _importCsvText = "";

    [ObservableProperty]
    private string _newFolderName = "";

    [ObservableProperty]
    private PasswordFolderFilterChoice? _selectedPasswordFolderFilter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordSortButtonTip))]
    [NotifyPropertyChangedFor(nameof(IsSortUpdatedSelected))]
    [NotifyPropertyChangedFor(nameof(IsSortTitleSelected))]
    [NotifyPropertyChangedFor(nameof(IsSortWebsiteSelected))]
    [NotifyPropertyChangedFor(nameof(IsSortUsernameSelected))]
    [NotifyPropertyChangedFor(nameof(IsSortCreatedSelected))]
    [NotifyPropertyChangedFor(nameof(IsSortFavoritesSelected))]
    private string _selectedPasswordSort = "updated-desc";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecoverableStatusMessage))]
    private bool _isLoadingVault;

    [ObservableProperty]
    private string _vaultLoadStageText = "";

    [ObservableProperty]
    private long _lastVaultLoadDurationMilliseconds;

    public bool IsOtherWorkspaceCompact =>
        OtherWorkspaceViewportWidth > 0 &&
        (OtherWorkspaceViewportWidth < 980 || OtherWorkspaceViewportHeight < 460);
    public GridLength TotpAccountColumnWidth => IsOtherWorkspaceCompact
        ? new GridLength(1.15, GridUnitType.Star)
        : new GridLength(300);
    public GridLength TotpCodeColumnWidth => IsOtherWorkspaceCompact
        ? new GridLength(1, GridUnitType.Star)
        : new GridLength(1, GridUnitType.Star);
    public GridLength TotpInspectorColumnWidth => IsOtherWorkspaceCompact
        ? new GridLength(1.05, GridUnitType.Star)
        : new GridLength(300);
    public Thickness TotpCodeConsolePadding => IsOtherWorkspaceCompact
        ? new Thickness(16)
        : new Thickness(24);
    public double TotpCodeFontSize => IsOtherWorkspaceCompact ? 40 : 56;
    private double _otherWorkspaceViewportWidth;

    private double _otherWorkspaceViewportHeight;

    public double OtherWorkspaceViewportWidth
    {
        get => _otherWorkspaceViewportWidth;
        set
        {
            if (SetProperty(ref _otherWorkspaceViewportWidth, Math.Max(0, value)))
            {
                RaiseOtherWorkspaceLayoutState();
            }
        }
    }

    public double OtherWorkspaceViewportHeight
    {
        get => _otherWorkspaceViewportHeight;
        set
        {
            if (SetProperty(ref _otherWorkspaceViewportHeight, Math.Max(0, value)))
            {
                RaiseOtherWorkspaceLayoutState();
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTimelineEntry))]
    private TimelineEntry? _selectedTimelineEntry;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedMdbxDatabaseItem))]
    private MdbxDatabaseDisplayItem? _selectedMdbxDatabaseItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedVaultSource))]
    private VaultSourceDisplayItem? _selectedVaultSource;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedWebDavBackupHistoryItem))]
    private WebDavBackupHistoryItem? _selectedWebDavBackupHistoryItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsGeneralSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsSecuritySelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsSecurityRecoverySelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsDataSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsDesktopSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsIntegrationsSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsAboutSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsDangerSelected))]
    private string _selectedSettingsPage = "General";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSyncConfigurationSelected))]
    [NotifyPropertyChangedFor(nameof(IsSyncBackupSelected))]
    [NotifyPropertyChangedFor(nameof(IsSyncSourcesSelected))]
    [NotifyPropertyChangedFor(nameof(IsSyncImportSelected))]
    [NotifyPropertyChangedFor(nameof(IsSyncExportSelected))]
    private string _selectedSyncPage = "Configuration";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDatabaseSourceSelected))]
    [NotifyPropertyChangedFor(nameof(IsDatabaseOverviewSelected))]
    [NotifyPropertyChangedFor(nameof(IsDatabaseCloudSelected))]
    [NotifyPropertyChangedFor(nameof(IsDatabaseCapabilitiesSelected))]
    private string _selectedDatabaseManagementPage = "Source";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMdbxDetailsSelected))]
    [NotifyPropertyChangedFor(nameof(IsMdbxHealthSelected))]
    [NotifyPropertyChangedFor(nameof(IsMdbxSourcesSelected))]
    [NotifyPropertyChangedFor(nameof(IsMdbxRuntimeSelected))]
    private string _selectedMdbxWorkspacePage = "Details";

    [ObservableProperty]
    private string _settingsLanguage = "system";

    [ObservableProperty]
    private string _settingsTheme = "system";

    [ObservableProperty]
    private string _startupSection = "Passwords";

    [ObservableProperty]
    private bool _autoLockEnabled = true;

    [ObservableProperty]
    private int _autoLockMinutes = 5;

    [ObservableProperty]
    private bool _clearClipboardEnabled = true;

    [ObservableProperty]
    private int _clipboardClearSeconds = 30;

    [ObservableProperty]
    private bool _requirePasswordBeforeExport = true;

    [ObservableProperty]
    private bool _securityRecoveryEnabled;

    [ObservableProperty]
    private int _securityQuestion1Id = 11;

    [ObservableProperty]
    private string _securityQuestion1CustomText = "";

    [ObservableProperty]
    private string _securityQuestion1Answer = "";

    [ObservableProperty]
    private int _securityQuestion2Id = 1;

    [ObservableProperty]
    private string _securityQuestion2CustomText = "";

    [ObservableProperty]
    private string _securityQuestion2Answer = "";

    [ObservableProperty]
    private string _securityRecoveryAnswer1 = "";

    [ObservableProperty]
    private string _securityRecoveryAnswer2 = "";

    [ObservableProperty]
    private string _recoveryNewMasterPassword = "";

    [ObservableProperty]
    private string _recoveryConfirmNewMasterPassword = "";

    [ObservableProperty]
    private bool _isResettingMasterPassword;

    [ObservableProperty]
    private bool _compactPasswordList;

    [ObservableProperty]
    private string _dangerZoneConfirmationText = "";

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
    private bool _webDavBackupEncryptionEnabled;

    [ObservableProperty]
    private string _webDavBackupEncryptionPassword = "";

    [ObservableProperty]
    private string _syncConflictStrategy = "ask";

    [ObservableProperty]
    private bool _oneDriveEnabled;

    [ObservableProperty]
    private bool _mdbxLocalCacheEnabled = true;

    [ObservableProperty]
    private bool _quickFilterFavorite;

    [ObservableProperty]
    private bool _quickFilter2Fa;

    [ObservableProperty]
    private bool _quickFilterNotes;

    [ObservableProperty]
    private bool _quickFilterPasskey;

    [ObservableProperty]
    private bool _quickFilterBoundNote;

    [ObservableProperty]
    private bool _quickFilterUncategorized;

    [ObservableProperty]
    private bool _quickFilterLocalOnly;

    [ObservableProperty]
    private bool _quickFilterAttachments;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPasswordTitle))]
    [NotifyPropertyChangedFor(nameof(SelectedPasswordSubtitle))]
    [NotifyPropertyChangedFor(nameof(SelectedPasswordSourceText))]
    [NotifyPropertyChangedFor(nameof(HasSelectedPassword))]
    [NotifyPropertyChangedFor(nameof(HasNoSelectedPassword))]
    [NotifyPropertyChangedFor(nameof(HasCurrentSelectedPasswordDetails))]
    [NotifyPropertyChangedFor(nameof(HasSelectedPasswordLoadingState))]
    private PasswordEntry? _selectedPassword;

    [ObservableProperty]
    private PasswordListRow? _selectedPasswordListRow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPasswordDetails))]
    [NotifyPropertyChangedFor(nameof(HasCurrentSelectedPasswordDetails))]
    [NotifyPropertyChangedFor(nameof(HasSelectedPasswordLoadingState))]
    private PasswordDetailViewModel? _selectedPasswordDetails;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPasswordLoadingState))]
    private bool _isLoadingSelectedPasswordDetails;

    public string SelectedSectionTitle => SectionTitle(SelectedSection);
    public string ShellVaultText => SelectedSection switch
    {
        "Mdbx" => "MDBX",
        "DatabaseManagement" => "Database",
        "Sync" => WebDavEnabled ? "WebDAV" : "Local",
        "Settings" => "Monica",
        "Archive" => "Archive",
        "RecycleBin" => "Recycle Bin",
        _ => "Monica Local"
    };
    public string ShellSyncText => SelectedSection switch
    {
        "Mdbx" => MdbxDatabases.Count > 0 ? "Vaults Ready" : "Metadata",
        "DatabaseManagement" => "Sources Ready",
        "Sync" => WebDavEnabled ? "Sync Ready" : "Local Only",
        "Settings" => "Ready",
        _ => StatusMessage
    };
    public string ShellPageText => SelectedSectionTitle;
    public string ShellPlatformText => OperatingSystem.IsWindows() ? "Windows" :
        OperatingSystem.IsMacOS() ? "macOS" :
        OperatingSystem.IsLinux() ? "Linux" :
        "Desktop";
    public string PasswordCountText => _localization.Format("PasswordCountFormat", Passwords.Count);
    public string SelectedPasswordCountText => _localization.Format("SelectedPasswordCountFormat", SelectedPasswordCount);
    public string SelectedPasswordTitle => SelectedPassword?.Title ?? _localization.Get("PasswordDetails");
    public string SelectedPasswordSubtitle => SelectedPassword is null
        ? PasswordCountText
        : BuildPasswordSubtitle(SelectedPassword);
    public string SelectedPasswordSourceText => SelectedPassword is null
        ? ""
        : SelectedPassword.IsMdbxEntry
            ? "MDBX"
            : SelectedPassword.IsKeePassEntry
                ? "KeePass"
                : SelectedPassword.IsBitwardenEntry
                    ? "Bitwarden"
                    : "Local";
    public bool HasPasswordFilters =>
        !string.IsNullOrWhiteSpace(PasswordSearchText) ||
        QuickFilterFavorite ||
        QuickFilter2Fa ||
        QuickFilterNotes ||
        QuickFilterPasskey ||
        QuickFilterBoundNote ||
        QuickFilterUncategorized ||
        QuickFilterLocalOnly ||
        QuickFilterAttachments ||
        (SelectedPasswordFolderFilter is not null &&
            !string.Equals(SelectedPasswordFolderFilter.SelectionKey, "system:all", StringComparison.OrdinalIgnoreCase));
    public string ClearPasswordFiltersText => _localization.Get("ClearPasswordFilters");
    public string PasswordFilterSummaryText
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(PasswordSearchText))
            {
                parts.Add(PasswordSearchText.Trim());
            }

            if (!string.Equals(SelectedPasswordFolderFilter?.SelectionKey, "system:all", StringComparison.OrdinalIgnoreCase) &&
                SelectedPasswordFolderFilter is not null)
            {
                parts.Add(SelectedPasswordFolderFilter.FolderDisplayName);
            }

            if (QuickFilterFavorite)
            {
                parts.Add(_localization.Get("QuickFilterFavorite"));
            }

            if (QuickFilter2Fa)
            {
                parts.Add(_localization.Get("QuickFilter2Fa"));
            }

            if (QuickFilterNotes)
            {
                parts.Add(_localization.Get("QuickFilterNotes"));
            }

            if (QuickFilterPasskey)
            {
                parts.Add(_localization.Get("QuickFilterPasskey"));
            }

            if (QuickFilterBoundNote)
            {
                parts.Add(_localization.Get("QuickFilterBoundNote"));
            }

            if (QuickFilterUncategorized)
            {
                parts.Add(_localization.Get("QuickFilterUncategorized"));
            }

            if (QuickFilterLocalOnly)
            {
                parts.Add(_localization.Get("QuickFilterLocalOnly"));
            }

            if (QuickFilterAttachments)
            {
                parts.Add(_localization.Get("QuickFilterAttachments"));
            }

            return parts.Count == 0
                ? _localization.Get("AllFolders")
                : string.Join(" / ", parts);
        }
    }
    public string SortUpdatedText => _localization.Get("SortUpdated");
    public string SortTitleText => _localization.Get("SortTitle");
    public string SortWebsiteText => _localization.Get("SortWebsite");
    public string SortUsernameText => _localization.Get("SortUsername");
    public string SortCreatedText => _localization.Get("SortCreated");
    public string SortFavoritesText => _localization.Get("SortFavorites");
    public string PasswordSortButtonTip => $"{_localization.SortPasswords}: {GetPasswordSortLabel(SelectedPasswordSort)}";
    public bool IsSortUpdatedSelected => string.Equals(SelectedPasswordSort, "updated-desc", StringComparison.Ordinal);
    public bool IsSortTitleSelected => string.Equals(SelectedPasswordSort, "title-asc", StringComparison.Ordinal);
    public bool IsSortWebsiteSelected => string.Equals(SelectedPasswordSort, "website-asc", StringComparison.Ordinal);
    public bool IsSortUsernameSelected => string.Equals(SelectedPasswordSort, "username-asc", StringComparison.Ordinal);
    public bool IsSortCreatedSelected => string.Equals(SelectedPasswordSort, "created-desc", StringComparison.Ordinal);
    public bool IsSortFavoritesSelected => string.Equals(SelectedPasswordSort, "favorites-first", StringComparison.Ordinal);
    public bool CanStackSelectedPasswords => SelectedPasswordCount > 1;
    public bool CanManageSelectedPasswordFolder => SelectedPasswordFolderFilter?.Id is > 0;
    public Thickness PasswordListCardPadding => CompactPasswordList ? new Thickness(12, 8) : new Thickness(16);
    public double PasswordListAvatarSize => CompactPasswordList ? 36 : 48;
    public double PasswordListAvatarFontSize => CompactPasswordList ? 14 : 18;
    public double PasswordListRowMinHeight => CompactPasswordList ? 40 : 54;
    public CornerRadius PasswordListAvatarCornerRadius => new(PasswordListAvatarSize / 2);
    public Thickness PasswordListContentMargin => CompactPasswordList ? new Thickness(10, 0, 0, 0) : new Thickness(14, 0, 0, 0);
    public bool ShowPasswordListDetails => !CompactPasswordList;
    public string NoteCountText => _localization.Format("NoteCountFormat", NoteItems.Count);
    public string TimelineCountText => _localization.Format("TimelineCountFormat", TimelineEntries.Count);
    public string LocalDatabaseSummaryText => _localization.Format("DatabaseSummaryFormat", Passwords.Count, NoteItems.Count, TotpItems.Count, WalletItems.Count);
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
    public string VaultSourceCountText => _localization.Format("VaultSourceCountFormat", VaultSources.Count);
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
    public int SmokeVaultLoadDelayMilliseconds { get; set; }
    public string WebDavBackupHistoryCountText => _localization.Format("WebDavBackupHistoryCountFormat", WebDavBackupHistory.Count);
    public bool HasWebDavBackupHistory => WebDavBackupHistory.Count > 0;
    public bool HasSelectedWebDavBackupHistoryItem => SelectedWebDavBackupHistoryItem is not null;
    public bool IsWebDavBusy => IsLoadingWebDavBackups || IsRunningWebDavBackup;
    public string WebDavBackupOptionsSummaryText => _localization.Format(
        "WebDavBackupOptionsSummaryFormat",
        CountSelectedWebDavBackupOptions(),
        WebDavBackupEncryptionEnabled ? _localization.Get("Encrypted") : _localization.Get("PlainJson"));
    public bool IsSettingsGeneralSelected => IsWorkspacePageSelected(SelectedSettingsPage, "General");
    public bool IsSettingsSecuritySelected => IsWorkspacePageSelected(SelectedSettingsPage, "Security");
    public bool IsSettingsSecurityRecoverySelected => IsWorkspacePageSelected(SelectedSettingsPage, "SecurityRecovery");
    public bool IsSettingsDataSelected => IsWorkspacePageSelected(SelectedSettingsPage, "Data");
    public bool IsSettingsDesktopSelected => IsWorkspacePageSelected(SelectedSettingsPage, "Desktop");
    public bool IsSettingsIntegrationsSelected => IsWorkspacePageSelected(SelectedSettingsPage, "Integrations");
    public bool IsSettingsAboutSelected => IsWorkspacePageSelected(SelectedSettingsPage, "About");
    public bool IsSettingsDangerSelected => IsWorkspacePageSelected(SelectedSettingsPage, "Danger");
    public bool IsSyncConfigurationSelected => IsWorkspacePageSelected(SelectedSyncPage, "Configuration");
    public bool IsSyncBackupSelected => IsWorkspacePageSelected(SelectedSyncPage, "Backup");
    public bool IsSyncSourcesSelected => IsWorkspacePageSelected(SelectedSyncPage, "Sources");
    public bool IsSyncImportSelected => IsWorkspacePageSelected(SelectedSyncPage, "Import");
    public bool IsSyncExportSelected => IsWorkspacePageSelected(SelectedSyncPage, "Export");
    public bool IsDatabaseSourceSelected => IsWorkspacePageSelected(SelectedDatabaseManagementPage, "Source");
    public bool IsDatabaseOverviewSelected => IsWorkspacePageSelected(SelectedDatabaseManagementPage, "Overview");
    public bool IsDatabaseCloudSelected => IsWorkspacePageSelected(SelectedDatabaseManagementPage, "Cloud");
    public bool IsDatabaseCapabilitiesSelected => IsWorkspacePageSelected(SelectedDatabaseManagementPage, "Capabilities");
    public bool IsMdbxDetailsSelected => IsWorkspacePageSelected(SelectedMdbxWorkspacePage, "Details");
    public bool IsMdbxHealthSelected => IsWorkspacePageSelected(SelectedMdbxWorkspacePage, "Health");
    public bool IsMdbxSourcesSelected => IsWorkspacePageSelected(SelectedMdbxWorkspacePage, "Sources");
    public bool IsMdbxRuntimeSelected => IsWorkspacePageSelected(SelectedMdbxWorkspacePage, "Runtime");
    public string NotePreviewMarkdown => NoteIsMarkdown ? BuildNotePreviewMarkdown(NoteContent) : "";
    public string NotePlainPreview => NoteContentCodec.ToPlainPreview(NoteContent, NoteIsMarkdown);
    public int SelectedPasswordCount => _selectedPasswordCount;
    public bool HasSelectedPasswords => SelectedPasswordCount > 0;
    public bool HasSelectedPassword => SelectedPassword is not null;
    public bool HasNoSelectedPassword => SelectedPassword is null;
    public bool HasSelectedPasswordDetails => SelectedPasswordDetails is not null;
    public bool HasCurrentSelectedPasswordDetails =>
        SelectedPassword is not null &&
        SelectedPasswordDetails?.Entry.Id == SelectedPassword.Id;
    public bool HasSelectedPasswordLoadingState =>
        SelectedPassword is not null &&
        IsLoadingSelectedPasswordDetails;
    public bool HasSelectedTimelineEntry => SelectedTimelineEntry is not null;
    public bool HasSelectedMdbxDatabaseItem => SelectedMdbxDatabaseItem is not null;
    public bool HasSelectedVaultSource => SelectedVaultSource is not null;
    public bool HasVaultSources => VaultSources.Count > 0;
    public bool HasRecoverableStatusMessage =>
        IsUnlocked &&
        !IsLoadingVault &&
        IsRecoverableStatusMessage(StatusMessage);
    public bool AreAllFilteredPasswordsSelected
    {
        get
        {
            var filtered = FilteredPasswords.ToArray();
            return filtered.Length > 0 && filtered.All(item => item.IsSelected);
        }
        set
        {
            UpdatePasswordSelectionsInBatch(() =>
            {
                foreach (var item in FilteredPasswords)
                {
                    item.IsSelected = value;
                }
            });
        }
    }

    public IEnumerable<PasswordQuickAccessItem> RecentPasswordQuickAccessItems =>
        BuildQuickAccessItems(QuickAccessSort.Recent);

    public IEnumerable<PasswordQuickAccessItem> FrequentPasswordQuickAccessItems =>
        BuildQuickAccessItems(QuickAccessSort.Frequent);

    public bool HasPasswordQuickAccessItems => RecentPasswordQuickAccessItems.Any() || FrequentPasswordQuickAccessItems.Any();

    public IReadOnlyList<PasswordEntry> FilteredPasswords => GetFilteredPasswords();
    public IReadOnlyList<PasswordListRow> FilteredPasswordRows => GetFilteredPasswordRows();
    public IReadOnlyList<PasswordEntry> VisiblePasswordNavigationEntries =>
        FilteredPasswordRows
            .Where(row => row.IsPasswordEntryRow || row.IsStackHeader)
            .Select(row => row.Entry)
            .ToArray();
    public bool HasTimelineEntries => TimelineEntries.Count > 0;

    partial void OnSearchTextChanged(string value)
    {
        RaiseFilteredPasswordsChanged();
        RefreshArchiveSearchState();
        RefreshRecycleBinSearchState();
        RaisePasswordSelectionState();
        ReconcileSelectedPasswordDetails();
        RaiseTotpFilterState();
    }

    partial void OnPasswordSearchTextChanged(string value)
    {
        RaisePasswordFilterState();
        if (_isApplyingPasswordSearchImmediately)
        {
            return;
        }

        QueuePasswordSearchQuery(value);
    }

    partial void OnPasswordSearchQueryChanged(string value)
    {
        RefreshPasswordFilters();
    }

    partial void OnQuickFilterFavoriteChanged(bool value) => RefreshPasswordFilters();
    partial void OnQuickFilter2FaChanged(bool value) => RefreshPasswordFilters();
    partial void OnQuickFilterNotesChanged(bool value) => RefreshPasswordFilters();
    partial void OnQuickFilterPasskeyChanged(bool value) => RefreshPasswordFilters();
    partial void OnQuickFilterBoundNoteChanged(bool value) => RefreshPasswordFilters();
    partial void OnQuickFilterUncategorizedChanged(bool value) => RefreshPasswordFilters();
    partial void OnQuickFilterLocalOnlyChanged(bool value) => RefreshPasswordFilters();
    partial void OnQuickFilterAttachmentsChanged(bool value) => RefreshPasswordFilters();
    partial void OnSelectedPasswordFolderFilterChanged(PasswordFolderFilterChoice? value)
    {
        RaiseFilteredPasswordsChanged();
        RaisePasswordFilterState();
        RaisePasswordSelectionState();
        ReconcileSelectedPasswordDetails();
        OnPropertyChanged(nameof(CanManageSelectedPasswordFolder));
    }
    partial void OnSelectedPasswordSortChanged(string value)
    {
        UpdateSettings(settings => settings.PasswordSortOrder = value);
        RaiseFilteredPasswordsChanged();
        RefreshPasswordSelectionStateFromPasswords();
    }

    partial void OnSelectedPasswordChanged(PasswordEntry? value)
    {
        SyncSelectedPasswordListRow(value);
        QueueSelectedPasswordDetailsRefresh(value);
    }

    partial void OnSelectedPasswordListRowChanged(PasswordListRow? value)
    {
        if (_isSyncingSelectedPasswordListRow)
        {
            return;
        }

        SelectedPassword = value?.Entry;
    }

    partial void OnSelectedSectionChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedSectionTitle));
        RaiseShellStatus();
    }

    partial void OnStatusMessageChanged(string value) => RaiseShellStatus();

    private static bool IsRecoverableStatusMessage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("failure", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("无法", StringComparison.Ordinal) ||
            value.Contains("失败", StringComparison.Ordinal) ||
            value.Contains("错误", StringComparison.Ordinal);
    }

    partial void OnSettingsLanguageChanged(string value)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        _localization.SetLanguage(value);
        _settingsService.Current.Language = value;
        QueueSaveSettings();
    }

    partial void OnSettingsThemeChanged(string value)
    {
        ApplyTheme(value);
        UpdateSettings(settings => settings.Theme = value);
    }

    partial void OnStartupSectionChanged(string value) => UpdateSettings(settings => settings.StartupSection = value);
    partial void OnAutoLockEnabledChanged(bool value) => UpdateSettings(settings => settings.AutoLockEnabled = value);
    partial void OnAutoLockMinutesChanged(int value) => UpdateSettings(settings => settings.AutoLockMinutes = value);
    partial void OnClearClipboardEnabledChanged(bool value) => UpdateSettings(settings => settings.ClearClipboardEnabled = value);
    partial void OnClipboardClearSecondsChanged(int value) => UpdateSettings(settings => settings.ClipboardClearSeconds = value);
    partial void OnRequirePasswordBeforeExportChanged(bool value) => UpdateSettings(settings => settings.RequirePasswordBeforeExport = value);
    partial void OnSecurityRecoveryEnabledChanged(bool value)
    {
        UpdateSettings(settings => settings.SecurityRecovery.IsEnabled = value);
        OnPropertyChanged(nameof(SecurityRecoveryStatusText));
        OnPropertyChanged(nameof(CanResetMasterPasswordWithSecurityQuestions));
        OnPropertyChanged(nameof(CanRunResetMasterPassword));
    }

    partial void OnIsResettingMasterPasswordChanged(bool value) => OnPropertyChanged(nameof(CanRunResetMasterPassword));

    partial void OnSecurityQuestion1IdChanged(int value)
    {
        if (!IsSecurityQuestion1Custom)
        {
            SecurityQuestion1CustomText = "";
        }

        OnPropertyChanged(nameof(IsSecurityQuestion1Custom));
    }

    partial void OnSecurityQuestion2IdChanged(int value)
    {
        if (!IsSecurityQuestion2Custom)
        {
            SecurityQuestion2CustomText = "";
        }

        OnPropertyChanged(nameof(IsSecurityQuestion2Custom));
    }

    partial void OnCompactPasswordListChanged(bool value)
    {
        UpdateSettings(settings => settings.CompactPasswordList = value);
        OnPropertyChanged(nameof(PasswordListCardPadding));
        OnPropertyChanged(nameof(PasswordListAvatarSize));
        OnPropertyChanged(nameof(PasswordListAvatarFontSize));
        OnPropertyChanged(nameof(PasswordListRowMinHeight));
        OnPropertyChanged(nameof(PasswordListAvatarCornerRadius));
        OnPropertyChanged(nameof(PasswordListContentMargin));
        OnPropertyChanged(nameof(ShowPasswordListDetails));
    }
    partial void OnWebDavEnabledChanged(bool value)
    {
        UpdateSettings(settings => settings.WebDavEnabled = value);
        RaiseSyncPageState();
        RefreshVaultSources();
        RefreshMdbxVaultState();
        RaiseShellStatus();
    }
    partial void OnWebDavServerUrlChanged(string value)
    {
        UpdateSettings(settings => settings.WebDavServerUrl = value);
        RaiseSyncPageState();
        RefreshVaultSources();
    }
    partial void OnWebDavUsernameChanged(string value)
    {
        UpdateSettings(settings => settings.WebDavUsername = value);
        RaiseSyncPageState();
        RefreshVaultSources();
    }
    partial void OnWebDavPasswordChanged(string value)
    {
        UpdateSettings(settings => settings.WebDavPassword = value);
        RaiseSyncPageState();
    }
    partial void OnWebDavRemotePathChanged(string value)
    {
        UpdateSettings(settings => settings.WebDavRemotePath = value);
        RaiseSyncPageState();
        RefreshVaultSources();
    }
    partial void OnWebDavSyncOnStartupChanged(bool value)
    {
        UpdateSettings(settings => settings.WebDavSyncOnStartup = value);
        RaiseSyncPageState();
        RefreshVaultSources();
    }
    partial void OnWebDavSyncAfterChangesChanged(bool value)
    {
        UpdateSettings(settings => settings.WebDavSyncAfterChanges = value);
        RaiseSyncPageState();
        RefreshVaultSources();
    }
    partial void OnIsLoadingWebDavBackupsChanged(bool value)
    {
        OnPropertyChanged(nameof(IsWebDavBusy));
        RaiseSyncPageState();
    }
    partial void OnIsRunningWebDavBackupChanged(bool value)
    {
        OnPropertyChanged(nameof(IsWebDavBusy));
        RaiseSyncPageState();
    }
    partial void OnWebDavBackupIncludePasswordsChanged(bool value) => UpdateWebDavBackupOption(settings => settings.WebDavBackupIncludePasswords = value);
    partial void OnWebDavBackupIncludeTotpChanged(bool value) => UpdateWebDavBackupOption(settings => settings.WebDavBackupIncludeTotp = value);
    partial void OnWebDavBackupIncludeNotesChanged(bool value) => UpdateWebDavBackupOption(settings => settings.WebDavBackupIncludeNotes = value);
    partial void OnWebDavBackupIncludeCardsChanged(bool value) => UpdateWebDavBackupOption(settings => settings.WebDavBackupIncludeCards = value);
    partial void OnWebDavBackupIncludeDocumentsChanged(bool value) => UpdateWebDavBackupOption(settings => settings.WebDavBackupIncludeDocuments = value);
    partial void OnWebDavBackupIncludeImagesChanged(bool value) => UpdateWebDavBackupOption(settings => settings.WebDavBackupIncludeImages = value);
    partial void OnWebDavBackupIncludeCategoriesChanged(bool value) => UpdateWebDavBackupOption(settings => settings.WebDavBackupIncludeCategories = value);
    partial void OnWebDavBackupEncryptionEnabledChanged(bool value) => UpdateWebDavBackupOption(settings => settings.WebDavBackupEncryptionEnabled = value);
    partial void OnWebDavBackupEncryptionPasswordChanged(string value) => UpdateSettings(settings => settings.WebDavBackupEncryptionPassword = value);
    partial void OnSyncConflictStrategyChanged(string value)
    {
        UpdateSettings(settings => settings.SyncConflictStrategy = value);
        RaiseSyncPageState();
        RefreshVaultSources();
    }
    partial void OnOneDriveEnabledChanged(bool value)
    {
        UpdateSettings(settings => settings.OneDriveEnabled = value);
        RaiseSyncPageState();
        RefreshMdbxVaultState();
    }

    partial void OnMdbxLocalCacheEnabledChanged(bool value)
    {
        UpdateSettings(settings => settings.MdbxLocalCacheEnabled = value);
        RaiseSyncPageState();
        RefreshMdbxVaultState();
    }

    private void RaiseShellStatus()
    {
        OnPropertyChanged(nameof(ShellVaultText));
        OnPropertyChanged(nameof(ShellSyncText));
        OnPropertyChanged(nameof(ShellPageText));
        OnPropertyChanged(nameof(ShellPlatformText));
    }

    private async Task LoadAfterUnlockAsync()
    {
        await Task.Yield();
        await LoadCoreAsync(deferSecurityAnalysis: true);
    }

    private static async Task YieldVaultLoadUiAsync()
    {
        await Task.Yield();
    }

    [RelayCommand]
    public Task LoadAsync() => LoadCoreAsync(deferSecurityAnalysis: false);

    private async Task LoadCoreAsync(bool deferSecurityAnalysis)
    {
        if (IsLoadingVault)
        {
            AppDiagnostics.Info("Vault load skipped because another load is running");
            return;
        }

        IsLoadingVault = true;
        VaultLoadStageText = "准备加载保险库...";
        var loadStopwatch = Stopwatch.StartNew();
        AppDiagnostics.Info("Vault load started");
        try
        {
            StatusMessage = "正在加载保险库数据...";
            SelectedPassword = null;
            SelectedPasswordDetails = null;
            _selectedPasswordCount = 0;
            Passwords.Clear();
            ArchivedPasswords.Clear();
            DeletedPasswords.Clear();
            NoteItems.Clear();
            TotpItems.Clear();
            WalletItems.Clear();
            Categories.Clear();
            MdbxDatabases.Clear();
            MdbxDatabaseItems.Clear();
            VaultSources.Clear();
            TimelineEntries.Clear();

            StatusMessage = "正在后台读取保险库数据...";
            VaultLoadStageText = "正在读取密码、笔记和分类...";
            if (SmokeVaultLoadDelayMilliseconds > 0)
            {
                var delay = Math.Clamp(SmokeVaultLoadDelayMilliseconds, 0, 30000);
                AppDiagnostics.Info($"Smoke UI vault load delay started. milliseconds={delay}");
                await Task.Delay(delay);
                AppDiagnostics.Info("Smoke UI vault load delay completed");
            }

            var snapshot = await Task.Run(LoadVaultSnapshotAsync);
            VaultLoadStageText = "正在整理密码列表...";
            await YieldVaultLoadUiAsync();
            _passwordCustomFields = snapshot.PasswordCustomFields;
            _passwordAttachments = snapshot.PasswordAttachments;
            _passwordQuickAccessRecords = snapshot.PasswordQuickAccessRecords;

            AppDiagnostics.Measure("Apply password collections", () =>
            {
                foreach (var item in snapshot.ActivePasswords)
                {
                    RefreshPasswordTotpDisplay(item);
                    RefreshPasswordAttachmentState(item);
                    item.IsSelected = false;
                    TrackPasswordSelection(item);
                }

                foreach (var item in snapshot.ArchivedPasswords)
                {
                    RefreshPasswordTotpDisplay(item);
                    RefreshPasswordAttachmentState(item);
                    item.IsSelected = false;
                    TrackPasswordSelection(item);
                }

                foreach (var item in snapshot.DeletedPasswords)
                {
                    RefreshPasswordTotpDisplay(item);
                    RefreshPasswordAttachmentState(item);
                    item.IsSelected = false;
                    TrackPasswordSelection(item);
                }
            });
            await YieldVaultLoadUiAsync();
            AppDiagnostics.Measure("Replace password collections", () =>
            {
                ReplaceItems(Passwords, snapshot.ActivePasswords);
                ReplaceItems(ArchivedPasswords, snapshot.ArchivedPasswords);
                ReplaceItems(DeletedPasswords, snapshot.DeletedPasswords);
                RefreshPasswordSelectionStateFromPasswords();
                RaisePasswordQuickAccessState();
            });
            await YieldVaultLoadUiAsync();

            VaultLoadStageText = "正在加载笔记和安全项目...";
            await YieldVaultLoadUiAsync();
            AppDiagnostics.Measure("Replace secure item collections", () =>
            {
                ReplaceItems(NoteItems, snapshot.NoteItems);

                foreach (var item in snapshot.WalletItems)
                {
                    item.IsSelected = false;
                    TrackWalletSelection(item);
                }

                ReplaceItems(WalletItems, snapshot.WalletItems);
            });
            await YieldVaultLoadUiAsync();

            VaultLoadStageText = "正在加载文件夹和保险库源...";
            await YieldVaultLoadUiAsync();
            AppDiagnostics.Measure("Replace folder and source collections", () =>
            {
                ReplaceItems(Categories, snapshot.Categories);
                RefreshPasswordFolderFilters();
                ReplaceItems(MdbxDatabases, snapshot.MdbxDatabases);
                RefreshMdbxVaultState();
                RefreshVaultSources();
            });
            await YieldVaultLoadUiAsync();
            VaultLoadStageText = "正在加载验证码...";
            await YieldVaultLoadUiAsync();
            await AppDiagnostics.MeasureAsync("Apply TOTP collections", () => LoadTotpItemsAsync(snapshot.StoredTotps));
            AppDiagnostics.Measure("Finalize vault load UI state", () =>
            {
                ReconcileSecureItemSelectionsAfterLoad();
                RaiseCounts();
                RaiseFilteredPasswordsChanged();
            });
            StatusMessage = _localization.Get("VaultUnlocked");
            VaultLoadStageText = "保险库已就绪";
            _ = LoadTimelineDeferredAsync();
            if (deferSecurityAnalysis)
            {
                _ = RefreshSecurityAnalysisDeferredAsync();
            }
            else
            {
                AppDiagnostics.Measure("Refresh security analysis", RefreshSecurityAnalysis);
            }

            LastVaultLoadDurationMilliseconds = loadStopwatch.ElapsedMilliseconds;
            AppDiagnostics.Info($"Vault load completed in {LastVaultLoadDurationMilliseconds} ms. passwords={Passwords.Count}, archived={ArchivedPasswords.Count}, deleted={DeletedPasswords.Count}, notes={NoteItems.Count}, totp={TotpItems.Count}, wallet={WalletItems.Count}");
        }
        catch (Exception ex)
        {
            LastVaultLoadDurationMilliseconds = loadStopwatch.ElapsedMilliseconds;
            AppDiagnostics.Error($"Vault load failed after {loadStopwatch.ElapsedMilliseconds} ms", ex);
            IsUnlocked = false;
            VaultLoadStageText = "保险库加载失败";
            StatusMessage = _localization.Format("VaultLoadFailedFormat", ex.Message);
        }
        finally
        {
            IsLoadingVault = false;
        }
    }

    private async Task<VaultLoadSnapshot> LoadVaultSnapshotAsync()
    {
        var allPasswords = await AppDiagnostics.MeasureAsync(
            "Load passwords",
            () => _repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true));
        var allPasswordItems = allPasswords.ToArray();
        var activePasswords = allPasswordItems.Where(item => !item.IsDeleted && !item.IsArchived).ToArray();
        var archivedPasswords = allPasswordItems.Where(item => !item.IsDeleted && item.IsArchived).ToArray();
        var deletedPasswords = allPasswordItems.Where(item => item.IsDeleted).ToArray();
        var passwordIds = allPasswordItems.Select(item => item.Id).ToArray();

        var customFields = await AppDiagnostics.MeasureAsync(
            "Load password custom fields",
            () => _repository.GetCustomFieldsByEntryIdsAsync(passwordIds));
        var attachments = await AppDiagnostics.MeasureAsync(
            "Load password attachments",
            () => _repository.GetAttachmentsByOwnerIdsAsync("PASSWORD", passwordIds));

        var secureItems = await AppDiagnostics.MeasureAsync(
            "Load secure items",
            () => _repository.GetSecureItemsAsync());
        var noteItems = secureItems
            .Where(item => item.ItemType == VaultItemType.Note)
            .ToArray();
        var walletItems = secureItems
            .Where(item => item.ItemType is VaultItemType.BankCard or VaultItemType.Document)
            .ToArray();
        var storedTotps = secureItems
            .Where(item => item.ItemType == VaultItemType.Totp)
            .ToArray();

        var categories = await AppDiagnostics.MeasureAsync(
            "Load categories",
            () => _repository.GetCategoriesAsync());
        var quickAccessRecords = (await AppDiagnostics.MeasureAsync(
                "Load password quick access",
                () => _repository.GetPasswordQuickAccessRecordsAsync()))
            .Where(record => record.OpenCount > 0 && record.PasswordId > 0)
            .ToDictionary(record => record.PasswordId);
        var databases = await AppDiagnostics.MeasureAsync(
            "Load MDBX database metadata",
            () => _repository.GetMdbxDatabasesAsync());

        return new VaultLoadSnapshot(
            activePasswords,
            archivedPasswords,
            deletedPasswords,
            customFields,
            attachments,
            noteItems,
            walletItems,
            storedTotps,
            categories,
            quickAccessRecords,
            databases);
    }

    [RelayCommand]
    private void SelectSection(string? section)
    {
        if (!string.IsNullOrWhiteSpace(section))
        {
            SelectedSection = section;
        }
    }

    [RelayCommand]
    private void SelectSettingsPage(string? page)
    {
        SelectedSettingsPage = NormalizeSettingsPage(page);
    }

    [RelayCommand]
    private void SelectSyncPage(string? page)
    {
        SelectedSyncPage = NormalizeSyncPage(page);
    }

    [RelayCommand]
    private void SelectDatabaseManagementPage(string? page)
    {
        SelectedDatabaseManagementPage = NormalizeDatabaseManagementPage(page);
    }

    [RelayCommand]
    private void SelectMdbxWorkspacePage(string? page)
    {
        SelectedMdbxWorkspacePage = NormalizeMdbxWorkspacePage(page);
    }

    private static bool IsWorkspacePageSelected(string selectedPage, string expectedPage) =>
        string.Equals(selectedPage, expectedPage, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSettingsPage(string? page) =>
        page?.Trim().ToLowerInvariant() switch
        {
            "security" => "Security",
            "securityrecovery" or "security-recovery" or "recovery" => "SecurityRecovery",
            "data" or "datamanagement" or "data-management" => "Data",
            "desktop" => "Desktop",
            "integrations" or "platform" => "Integrations",
            "about" => "About",
            "danger" or "dangerzone" or "danger-zone" => "Danger",
            _ => "General"
        };

    private static string NormalizeSyncPage(string? page) =>
        page?.Trim().ToLowerInvariant() switch
        {
            "backup" or "backups" or "history" => "Backup",
            "sources" or "vaults" or "database" => "Sources",
            "import" => "Import",
            "export" => "Export",
            _ => "Configuration"
        };

    private static string NormalizeDatabaseManagementPage(string? page) =>
        page?.Trim().ToLowerInvariant() switch
        {
            "overview" or "local" => "Overview",
            "cloud" or "vaults" or "sources" => "Cloud",
            "capabilities" or "features" => "Capabilities",
            _ => "Source"
        };

    private static string NormalizeMdbxWorkspacePage(string? page) =>
        page?.Trim().ToLowerInvariant() switch
        {
            "health" or "diagnostics" => "Health",
            "sources" or "remote" => "Sources",
            "runtime" or "android" => "Runtime",
            _ => "Details"
        };

    [RelayCommand(CanExecute = nameof(CanOpenExternalLinks))]
    private async Task OpenGitHubRepositoryAsync()
    {
        try
        {
            await _externalLinkService.OpenAsync(new Uri(GitHubRepositoryUrl, UriKind.Absolute));
            StatusMessage = _localization.Get("GitHubRepositoryOpened");
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("GitHubRepositoryOpenFailedFormat", ex.Message);
        }
    }

    private bool CanOpenNoteReference(NoteReferenceItem? item) =>
        CanOpenExternalLinks && TryCreateExternalReferenceUri(item?.Target, out _);

    [RelayCommand(CanExecute = nameof(CanOpenNoteReference))]
    private async Task OpenNoteReferenceAsync(NoteReferenceItem? item)
    {
        if (!TryCreateExternalReferenceUri(item?.Target, out var uri))
        {
            StatusMessage = "无法打开此引用";
            return;
        }

        try
        {
            await _externalLinkService.OpenAsync(uri);
            StatusMessage = $"已打开 {uri.Host}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开引用失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CopyNoteReferenceAsync(NoteReferenceItem? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Target))
        {
            return;
        }

        await _clipboardService.SetTextAsync(item.Target);
        StatusMessage = "已复制引用";
    }

    [RelayCommand]
    private async Task ClearVaultDataAsync(string? scope)
    {
        if (!IsUnlocked)
        {
            StatusMessage = _localization.Get("VaultLocked");
            return;
        }

        var requiredPhrase = _localization.Get("ClearVaultConfirmationPhrase");
        if (!string.Equals(DangerZoneConfirmationText.Trim(), requiredPhrase, StringComparison.Ordinal))
        {
            StatusMessage = _localization.Format("ClearVaultConfirmationFailedFormat", requiredPhrase);
            return;
        }

        var clearScope = scope?.ToLowerInvariant() switch
        {
            "passwords" => VaultClearScope.Passwords,
            "secureitems" or "secure-items" => VaultClearScope.SecureItems,
            _ => VaultClearScope.All
        };

        await _repository.ClearVaultDataAsync(clearScope);
        DangerZoneConfirmationText = "";
        await LoadAsync();
        StatusMessage = _localization.Format("ClearedVaultDataFormat", LocalizeVaultClearScope(clearScope));
    }

    [RelayCommand]
    private async Task ChangeMasterPasswordAsync()
    {
        if (!IsUnlocked)
        {
            StatusMessage = _localization.Get("VaultLocked");
            return;
        }

        if (string.IsNullOrWhiteSpace(CurrentMasterPassword))
        {
            StatusMessage = _localization.Get("EnterCurrentMasterPassword");
            return;
        }

        if (string.IsNullOrWhiteSpace(NewMasterPassword))
        {
            StatusMessage = _localization.Get("EnterNewMasterPassword");
            return;
        }

        if (NewMasterPassword.Length < 8)
        {
            StatusMessage = _localization.Get("MasterPasswordMinLength");
            return;
        }

        if (!string.Equals(NewMasterPassword, ConfirmNewMasterPassword, StringComparison.Ordinal))
        {
            StatusMessage = _localization.Get("ConfirmationMismatch");
            return;
        }

        IsChangingMasterPassword = true;
        StatusMessage = _localization.Get("ChangeMasterPasswordInProgress");
        try
        {
            var result = await _masterPasswordMaintenanceService.ChangeMasterPasswordAsync(
                CurrentMasterPassword,
                NewMasterPassword);
            if (!result.Success)
            {
                var message = result.Message.Contains("incorrect", StringComparison.OrdinalIgnoreCase)
                    ? _localization.Get("WrongMasterPassword")
                    : result.Message;
                StatusMessage = _localization.Format("ChangeMasterPasswordFailedFormat", message);
                return;
            }

            CurrentMasterPassword = "";
            NewMasterPassword = "";
            ConfirmNewMasterPassword = "";
            MasterPassword = "";
            ConfirmMasterPassword = "";
            StatusMessage = _localization.Format("MasterPasswordChangedFormat", result.TotalSecretsReencrypted);
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ChangeMasterPasswordFailedFormat", ex.Message);
        }
        finally
        {
            IsChangingMasterPassword = false;
        }
    }

    [RelayCommand]
    private async Task ResetMasterPasswordWithSecurityQuestionsAsync()
    {
        if (!IsUnlocked)
        {
            StatusMessage = _localization.Get("VaultLocked");
            return;
        }

        var recovery = _settingsService.Current.SecurityRecovery;
        if (!recovery.HasCompleteSetup)
        {
            StatusMessage = _localization.Get("SecurityQuestionsNotConfigured");
            return;
        }

        if (string.IsNullOrWhiteSpace(SecurityRecoveryAnswer1) || string.IsNullOrWhiteSpace(SecurityRecoveryAnswer2))
        {
            StatusMessage = _localization.Get("SecurityQuestionAnswersRequired");
            return;
        }

        if (!_securityQuestionService.VerifyAnswer(SecurityRecoveryAnswer1, recovery.Question1AnswerHash, recovery.Question1AnswerSalt) ||
            !_securityQuestionService.VerifyAnswer(SecurityRecoveryAnswer2, recovery.Question2AnswerHash, recovery.Question2AnswerSalt))
        {
            StatusMessage = _localization.Get("SecurityQuestionAnswersIncorrect");
            return;
        }

        if (string.IsNullOrWhiteSpace(RecoveryNewMasterPassword))
        {
            StatusMessage = _localization.Get("EnterNewMasterPassword");
            return;
        }

        if (RecoveryNewMasterPassword.Length < 8)
        {
            StatusMessage = _localization.Get("MasterPasswordMinLength");
            return;
        }

        if (!string.Equals(RecoveryNewMasterPassword, RecoveryConfirmNewMasterPassword, StringComparison.Ordinal))
        {
            StatusMessage = _localization.Get("ConfirmationMismatch");
            return;
        }

        IsResettingMasterPassword = true;
        StatusMessage = _localization.Get("ResetMasterPasswordInProgress");
        try
        {
            var result = await _masterPasswordMaintenanceService.ResetMasterPasswordFromUnlockedVaultAsync(RecoveryNewMasterPassword);
            if (!result.Success)
            {
                StatusMessage = _localization.Format("ResetMasterPasswordFailedFormat", result.Message);
                return;
            }

            SecurityRecoveryAnswer1 = "";
            SecurityRecoveryAnswer2 = "";
            RecoveryNewMasterPassword = "";
            RecoveryConfirmNewMasterPassword = "";
            MasterPassword = "";
            ConfirmMasterPassword = "";
            StatusMessage = _localization.Format("ResetMasterPasswordChangedFormat", result.TotalSecretsReencrypted);
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ResetMasterPasswordFailedFormat", ex.Message);
        }
        finally
        {
            IsResettingMasterPassword = false;
        }
    }

    [RelayCommand]
    private void SaveSecurityQuestions()
    {
        if (!SecurityRecoveryEnabled)
        {
            _settingsService.Current.SecurityRecovery.IsEnabled = false;
            QueueSaveSettings();
            OnPropertyChanged(nameof(SecurityRecoveryStatusText));
            OnPropertyChanged(nameof(CanResetMasterPasswordWithSecurityQuestions));
            OnPropertyChanged(nameof(CanRunResetMasterPassword));
            StatusMessage = _localization.Get("SecurityQuestionsDisabled");
            return;
        }

        try
        {
            var setup = _securityQuestionService.CreateSetup(
                new SecurityQuestionDraft(SecurityQuestion1Id, GetSecurityQuestionText(SecurityQuestion1Id, SecurityQuestion1CustomText), SecurityQuestion1Answer),
                new SecurityQuestionDraft(SecurityQuestion2Id, GetSecurityQuestionText(SecurityQuestion2Id, SecurityQuestion2CustomText), SecurityQuestion2Answer));
            _settingsService.Current.SecurityRecovery = setup;
            ApplySecurityRecoverySettings(setup);
            SecurityQuestion1Answer = "";
            SecurityQuestion2Answer = "";
            QueueSaveSettings();
            OnPropertyChanged(nameof(SecurityRecoveryStatusText));
            OnPropertyChanged(nameof(SecurityRecoveryQuestion1PromptText));
            OnPropertyChanged(nameof(SecurityRecoveryQuestion2PromptText));
            OnPropertyChanged(nameof(CanResetMasterPasswordWithSecurityQuestions));
            OnPropertyChanged(nameof(CanRunResetMasterPassword));
            StatusMessage = _localization.Get("SecurityQuestionsSaved");
        }
        catch (ArgumentException ex)
        {
            StatusMessage = _localization.Format("SecurityQuestionsSaveFailedFormat", ex.Message);
        }
    }








    [RelayCommand]
    private void ShowTimelineEntryDetails(TimelineEntry? entry)
    {
        if (entry is not null)
        {
            SelectedTimelineEntry = entry;
        }
    }

    [RelayCommand]
    private void ShowSecurityIssueDetails(SecurityIssueItem? issue)
    {
        if (issue is not null)
        {
            SelectedSecurityIssue = issue;
        }
    }

    [RelayCommand]
    private void ShowVaultSourceDetails(VaultSourceDisplayItem? source)
    {
        if (source is not null)
        {
            SelectedVaultSource = source;
        }
    }

    [RelayCommand]
    private void ShowMdbxDatabaseDetails(MdbxDatabaseDisplayItem? item)
    {
        if (item is not null)
        {
            SelectedMdbxDatabaseItem = item;
        }
    }

    [RelayCommand]
    private void ShowWebDavBackupDetails(WebDavBackupHistoryItem? item)
    {
        if (item is not null)
        {
            SelectedWebDavBackupHistoryItem = item;
        }
    }

    public async Task<long> AddPasswordAttachmentMetadataAsync(
        PasswordEntry entry,
        string fileName,
        string storagePath,
        long sizeBytes = 0,
        string contentType = "",
        byte[]? content = null,
        CancellationToken cancellationToken = default)
    {
        if (entry.Id == 0)
        {
            throw new ArgumentException("Password entry must be saved before adding attachments.", nameof(entry));
        }

        var attachment = new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = entry.Id,
            FileName = string.IsNullOrWhiteSpace(fileName) ? _localization.Get("Attachment") : fileName.Trim(),
            ContentType = contentType.Trim(),
            StoragePath = storagePath.Trim(),
            SizeBytes = Math.Max(0, sizeBytes),
            CreatedAt = DateTimeOffset.UtcNow,
            BitwardenVaultId = entry.BitwardenVaultId
        };

        var originalStoragePath = attachment.StoragePath;
        var id = content is null
            ? await _repository.SaveAttachmentAsync(attachment, cancellationToken)
            : await _repository.SaveAttachmentAsync(attachment, content, cancellationToken);
        if (content is not null &&
            !string.Equals(originalStoragePath, attachment.StoragePath, StringComparison.Ordinal) &&
            !originalStoragePath.StartsWith("mdbx:", StringComparison.OrdinalIgnoreCase))
        {
            await _passwordAttachmentFileService.DeleteStoredAttachmentAsync(originalStoragePath, cancellationToken);
        }

        SetPasswordAttachments(entry.Id, [.. GetPasswordAttachments(entry.Id), attachment]);
        RefreshPasswordAttachmentState(entry);
        RaiseFilteredPasswordsChanged();
        await _repository.LogAsync(new OperationLog
        {
            ItemType = "PASSWORD",
            ItemId = entry.Id,
            ItemTitle = entry.Title,
            OperationType = "ATTACHMENT",
            DeviceName = Environment.MachineName
        }, cancellationToken);
        await LoadTimelineAsync();
        StatusMessage = _localization.Format("AddedAttachmentFormat", attachment.FileName, entry.Title);
        return id;
    }

    [RelayCommand]
    private async Task AddPasswordAttachmentAsync(PasswordEntry? entry)
    {
        if (entry is null || entry.Id <= 0 || entry.IsDeleted)
        {
            return;
        }

        var draft = await _passwordAttachmentFileService.PickAndStoreAttachmentAsync(entry);
        if (draft is null)
        {
            return;
        }

        await AddPasswordAttachmentMetadataAsync(entry, draft.FileName, draft.StoragePath, draft.SizeBytes, draft.ContentType, draft.Content);
    }

    private async Task<bool> DeletePasswordAttachmentAsync(Attachment attachment)
    {
        if (!await ConfirmDeleteAttachmentAsync(attachment.FileName))
        {
            return false;
        }

        await _repository.DeleteAttachmentAsync(attachment.Id, attachment);
        await _passwordAttachmentFileService.DeleteStoredAttachmentAsync(attachment.StoragePath);
        var remaining = GetPasswordAttachments(attachment.OwnerId)
            .Where(item => item.Id != attachment.Id)
            .ToArray();
        SetPasswordAttachments(attachment.OwnerId, remaining);

        var entry = Passwords
            .Concat(ArchivedPasswords)
            .Concat(DeletedPasswords)
            .FirstOrDefault(item => item.Id == attachment.OwnerId);
        if (entry is not null)
        {
            RefreshPasswordAttachmentState(entry);
            RaiseFilteredPasswordsChanged();
        }

        StatusMessage = _localization.Format("DeletedAttachmentFormat", attachment.FileName);
        return true;
    }




    [RelayCommand]
    private async Task ExportDataAsync()
    {
        ExportPreview = await BuildMonicaJsonExportAsync(
            includePasswords: true,
            includeTotp: true,
            includeNotes: true,
            includeCards: true,
            includeDocuments: true,
            includeImages: true,
            includeCategories: true);
        StatusMessage = _localization.Get("ExportPrepared");
    }

    [RelayCommand]
    private async Task ExportPasswordCsvAsync()
    {
        var exportPasswords = (await _repository.GetPasswordsAsync())
            .Select(item => ClonePasswordForExport(item))
            .ToArray();
        ExportCsvPreview = _importExportService.ExportPasswordCsv(exportPasswords);
        StatusMessage = _localization.Get("ExportedPasswordCsv");
    }

    [RelayCommand]
    private async Task ExportNoteCsvAsync()
    {
        ExportNoteCsvPreview = await BuildNoteCsvExportAsync();
        StatusMessage = _localization.Get("ExportedNoteCsv");
    }

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task ImportMonicaJsonFileAsync()
    {
        try
        {
            var file = await _fileSystemPickerService.OpenTextFileAsync(_localization.Get("ImportMonicaJson"), MonicaJsonFileTypes);
            if (file is null)
            {
                return;
            }

            ImportJsonText = file.Content;
            await ImportDataAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ImportFailedFormat", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task ImportPasswordCsvFileAsync()
    {
        try
        {
            var file = await _fileSystemPickerService.OpenTextFileAsync(_localization.Get("ImportPasswordCsv"), PasswordCsvFileTypes);
            if (file is null)
            {
                return;
            }

            ImportCsvText = file.Content;
            await ImportPasswordCsvAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ImportFailedFormat", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task ImportNoteCsvFileAsync()
    {
        try
        {
            var file = await _fileSystemPickerService.OpenTextFileAsync(_localization.Get("ImportNoteCsv"), NoteCsvFileTypes);
            if (file is null)
            {
                return;
            }

            ImportNoteCsvText = file.Content;
            await ImportNoteCsvAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ImportFailedFormat", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task SaveMonicaJsonExportAsync()
    {
        if (string.IsNullOrWhiteSpace(ExportPreview))
        {
            await ExportDataAsync();
        }

        await SaveExportTextAsync(
            _localization.Get("ExportData"),
            $"monica_export_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.json",
            ExportPreview,
            MonicaJsonFileTypes);
    }

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task SavePasswordCsvExportAsync()
    {
        if (string.IsNullOrWhiteSpace(ExportCsvPreview))
        {
            await ExportPasswordCsvAsync();
        }

        await SaveExportTextAsync(
            _localization.Get("ExportPasswordCsv"),
            $"monica_passwords_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.csv",
            ExportCsvPreview,
            PasswordCsvFileTypes);
    }

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task SaveNoteCsvExportAsync()
    {
        if (string.IsNullOrWhiteSpace(ExportNoteCsvPreview))
        {
            await ExportNoteCsvAsync();
        }

        await SaveExportTextAsync(
            _localization.Get("ExportNoteCsv"),
            $"monica_notes_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.csv",
            ExportNoteCsvPreview,
            NoteCsvFileTypes);
    }

    private async Task SaveExportTextAsync(
        string title,
        string suggestedFileName,
        string content,
        IReadOnlyList<PlatformFilePickerFileType> fileTypes)
    {
        try
        {
            var fileName = await _fileSystemPickerService.SaveTextFileAsync(title, suggestedFileName, content, fileTypes);
            if (fileName is not null)
            {
                StatusMessage = _localization.Format("SavedExportFileFormat", fileName);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("SaveExportFileFailedFormat", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ImportDataAsync()
    {
        if (string.IsNullOrWhiteSpace(ImportJsonText))
        {
            StatusMessage = _localization.Get("ImportJsonRequired");
            return;
        }

        try
        {
            var result = await ImportMonicaJsonAsync(ImportJsonText);
            ImportJsonText = "";
            StatusMessage = FormatMonicaJsonImportStatus(result);
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ImportFailedFormat", ex.Message);
        }
    }

    [RelayCommand]
    private async Task LoadWebDavBackupsAsync()
    {
        if (!TryCreateWebDavProfile(out var profile))
        {
            return;
        }

        try
        {
            IsLoadingWebDavBackups = true;
            var entries = await _webDavBackupService.ListAsync(profile, "");
            WebDavBackupHistory.Clear();
            foreach (var item in entries
                .Where(item => !item.IsDirectory)
                .OrderByDescending(item => item.LastModified ?? DateTimeOffset.MinValue)
                .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
            {
                WebDavBackupHistory.Add(ToWebDavBackupHistoryItem(item));
            }

            SelectedWebDavBackupHistoryItem = WebDavBackupHistory.FirstOrDefault();
            RaiseWebDavBackupHistoryState();
            StatusMessage = _localization.Format("LoadedWebDavBackupsFormat", WebDavBackupHistory.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("WebDavBackupHistoryFailedFormat", ex.Message);
        }
        finally
        {
            IsLoadingWebDavBackups = false;
        }
    }

    [RelayCommand]
    private async Task TestWebDavConnectionAsync()
    {
        if (!TryCreateWebDavProfile(out var profile))
        {
            return;
        }

        try
        {
            IsLoadingWebDavBackups = true;
            var entries = await _webDavBackupService.ListAsync(profile, "");
            StatusMessage = _localization.Format("WebDavConnectionTestSucceededFormat", entries.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("WebDavConnectionTestFailedFormat", ex.Message);
        }
        finally
        {
            IsLoadingWebDavBackups = false;
            RaiseSyncPageState();
        }
    }

    [RelayCommand]
    private async Task CreateWebDavBackupAsync()
    {
        if (!TryCreateWebDavProfile(out var profile))
        {
            return;
        }

        if (!HasSelectedWebDavBackupOptions())
        {
            StatusMessage = _localization.Get("SelectWebDavBackupContent");
            return;
        }

        if (WebDavBackupEncryptionEnabled && string.IsNullOrWhiteSpace(WebDavBackupEncryptionPassword))
        {
            StatusMessage = _localization.Get("WebDavEncryptionPasswordRequired");
            return;
        }

        try
        {
            IsRunningWebDavBackup = true;
            var json = await BuildMonicaJsonExportAsync(
                WebDavBackupIncludePasswords,
                WebDavBackupIncludeTotp,
                WebDavBackupIncludeNotes,
                WebDavBackupIncludeCards,
                WebDavBackupIncludeDocuments,
                WebDavBackupIncludeImages,
                WebDavBackupIncludeCategories);
            var extension = WebDavBackupEncryptionEnabled ? "monica.enc.json" : "monica.json";
            var fileName = $"monica_backup_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.{extension}";
            var content = WebDavBackupEncryptionEnabled
                ? EncryptWebDavBackupPayload(json, WebDavBackupEncryptionPassword)
                : json;

            await _webDavBackupService.UploadTextAsync(profile, fileName, content);
            var path = _webDavBackupService.NormalizeRemotePath(profile.RootPath, fileName);
            var existing = WebDavBackupHistory.FirstOrDefault(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                WebDavBackupHistory.Remove(existing);
            }

            var backupItem = new WebDavBackupHistoryItem(
                fileName,
                path,
                DateTimeOffset.UtcNow.ToLocalTime().ToString("yyyy/MM/dd HH:mm", _localization.Culture),
                FormatByteSize(Encoding.UTF8.GetByteCount(content)),
                DateTimeOffset.UtcNow);
            WebDavBackupHistory.Insert(0, backupItem);
            SelectedWebDavBackupHistoryItem = backupItem;
            RaiseWebDavBackupHistoryState();
            StatusMessage = _localization.Format("CreatedWebDavBackupFormat", fileName);
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("CreateWebDavBackupFailedFormat", ex.Message);
        }
        finally
        {
            IsRunningWebDavBackup = false;
        }
    }

    [RelayCommand]
    private async Task RestoreWebDavBackupAsync(WebDavBackupHistoryItem? item)
    {
        if (item is null || !TryCreateWebDavProfile(out var profile))
        {
            return;
        }

        if (IsEncryptedWebDavBackup(item.FileName) && string.IsNullOrWhiteSpace(WebDavBackupEncryptionPassword))
        {
            StatusMessage = _localization.Get("WebDavEncryptionPasswordRequired");
            return;
        }

        try
        {
            IsRunningWebDavBackup = true;
            var content = await _webDavBackupService.DownloadTextAsync(profile, item.FileName);
            var json = IsEncryptedWebDavBackup(item.FileName)
                ? DecryptWebDavBackupPayload(content, WebDavBackupEncryptionPassword)
                : content;
            var result = await ImportMonicaJsonAsync(json);
            StatusMessage = _localization.Format("RestoredWebDavBackupFormat", item.FileName, result.Passwords, result.SecureItems, result.Categories);
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("RestoreWebDavBackupFailedFormat", ex.Message);
        }
        finally
        {
            IsRunningWebDavBackup = false;
        }
    }

    [RelayCommand]
    private async Task RestoreLatestWebDavBackupAsync()
    {
        if (!WebDavBackupHistory.Any())
        {
            await LoadWebDavBackupsAsync();
        }

        await RestoreWebDavBackupAsync(WebDavBackupHistory.FirstOrDefault());
    }

    [RelayCommand]
    private async Task DeleteWebDavBackupAsync(WebDavBackupHistoryItem? item)
    {
        if (item is null || !TryCreateWebDavProfile(out var profile))
        {
            return;
        }

        if (!await ConfirmDeleteWebDavBackupAsync(item.FileName))
        {
            return;
        }

        try
        {
            await _webDavBackupService.DeleteAsync(profile, item.FileName);
            WebDavBackupHistory.Remove(item);
            if (SelectedWebDavBackupHistoryItem == item)
            {
                SelectedWebDavBackupHistoryItem = WebDavBackupHistory.FirstOrDefault();
            }

            RaiseWebDavBackupHistoryState();
            StatusMessage = _localization.Format("DeletedWebDavBackupFormat", item.FileName);
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("DeleteWebDavBackupFailedFormat", ex.Message);
        }
    }

    private async Task<string> BuildMonicaJsonExportAsync(
        bool includePasswords,
        bool includeTotp,
        bool includeNotes,
        bool includeCards,
        bool includeDocuments,
        bool includeImages,
        bool includeCategories)
    {
        var passwords = await _repository.GetPasswordsAsync();
        var secureItems = await _repository.GetSecureItemsAsync();
        var categories = includeCategories
            ? await _repository.GetCategoriesAsync()
            : Array.Empty<Category>();
        var totpItems = BuildStoredAndVirtualTotpItems(passwords, secureItems);
        var exportPasswords = includePasswords
            ? passwords.Select(item => ClonePasswordForExport(item, includeCategories)).ToArray()
            : Array.Empty<PasswordEntry>();
        var exportSecureItems = totpItems
            .Where(_ => includeTotp)
            .Concat(secureItems.Where(item => includeNotes && item.ItemType == VaultItemType.Note))
            .Concat(secureItems.Where(item =>
                (item.ItemType == VaultItemType.BankCard && includeCards) ||
                (item.ItemType == VaultItemType.Document && includeDocuments)))
            .Where(item => item.Id > 0)
            .Select(item => CloneSecureItemForExport(item, includeCategories, includeImages))
            .ToArray();
        var exportCategories = includeCategories
            ? categories.Select(CloneCategory).ToArray()
            : Array.Empty<Category>();
        var customFieldsByPasswordId = includePasswords
            ? await _repository.GetCustomFieldsByEntryIdsAsync(exportPasswords.Select(item => item.Id).ToArray())
            : new Dictionary<long, IReadOnlyList<CustomField>>();
        var passwordHistoryByPasswordId = includePasswords
            ? await GetPasswordHistoryForExportAsync(exportPasswords.Select(item => item.Id).ToArray())
            : new Dictionary<long, IReadOnlyList<PasswordHistoryEntry>>();
        var passwordAttachmentsByPasswordId = includePasswords
            ? await GetPasswordAttachmentsForExportAsync(exportPasswords.Select(item => item.Id).ToArray())
            : new Dictionary<long, IReadOnlyList<PasswordAttachmentExport>>();
        var secureItemAttachmentsByItemId = includeImages
            ? await GetSecureItemAttachmentsForExportAsync(exportSecureItems)
            : new Dictionary<long, IReadOnlyList<SecureItemAttachmentExport>>();

        return _importExportService.ExportJson(
            exportPasswords,
            exportSecureItems,
            exportCategories,
            customFieldsByPasswordId,
            passwordHistoryByPasswordId,
            passwordAttachmentsByPasswordId,
            secureItemAttachmentsByItemId);
    }

    private async Task<string> BuildNoteCsvExportAsync()
    {
        var exportNotes = (await _repository.GetSecureItemsAsync(VaultItemType.Note))
            .Select(item => CloneSecureItemForExport(item))
            .ToArray();

        return _importExportService.ExportNoteCsv(exportNotes);
    }

    private async Task<IReadOnlyDictionary<long, IReadOnlyList<PasswordHistoryEntry>>> GetPasswordHistoryForExportAsync(IReadOnlyList<long> passwordIds)
    {
        var result = new Dictionary<long, IReadOnlyList<PasswordHistoryEntry>>();
        foreach (var passwordId in passwordIds.Where(id => id > 0).Distinct())
        {
            var history = (await _repository.GetPasswordHistoryAsync(passwordId))
                .Select(ClonePasswordHistoryForExport)
                .ToArray();
            if (history.Length > 0)
            {
                result[passwordId] = history;
            }
        }

        return result;
    }

    private async Task<IReadOnlyDictionary<long, IReadOnlyList<PasswordAttachmentExport>>> GetPasswordAttachmentsForExportAsync(IReadOnlyList<long> passwordIds)
    {
        var ids = passwordIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<long, IReadOnlyList<PasswordAttachmentExport>>();
        }

        var result = new Dictionary<long, IReadOnlyList<PasswordAttachmentExport>>();
        var attachmentsByPasswordId = await _repository.GetAttachmentsByOwnerIdsAsync("PASSWORD", ids);
        foreach (var group in attachmentsByPasswordId.OrderBy(item => item.Key))
        {
            var exports = new List<PasswordAttachmentExport>();
            foreach (var attachment in group.Value)
            {
                var content = await _repository.TryReadAttachmentContentAsync(attachment);
                if (content is null)
                {
                    continue;
                }

                exports.Add(new PasswordAttachmentExport(
                    CloneAttachmentForExport(attachment),
                    Convert.ToBase64String(content)));
            }

            if (exports.Count > 0)
            {
                result[group.Key] = exports;
            }
        }

        return result;
    }

    private async Task<IReadOnlyDictionary<long, IReadOnlyList<SecureItemAttachmentExport>>> GetSecureItemAttachmentsForExportAsync(IReadOnlyList<SecureItem> secureItems)
    {
        var result = new Dictionary<long, IReadOnlyList<SecureItemAttachmentExport>>();
        foreach (var item in secureItems.Where(item => item.Id > 0).OrderBy(item => item.Id))
        {
            var exports = new List<SecureItemAttachmentExport>();
            var imagePaths = DecodeSecureItemImagePaths(item);
            for (var index = 0; index < imagePaths.Count; index++)
            {
                var imagePath = imagePaths[index];
                var attachment = CreateSecureItemImageAttachmentForExport(item, imagePath, index);
                var content = await _repository.TryReadAttachmentContentAsync(attachment);
                if (content is null)
                {
                    continue;
                }

                exports.Add(new SecureItemAttachmentExport(
                    CloneAttachmentForExport(attachment),
                    Convert.ToBase64String(content)));
            }

            if (exports.Count > 0)
            {
                result[item.Id] = exports;
            }
        }

        return result;
    }

    private async Task<MonicaJsonImportResult> ImportMonicaJsonAsync(string json)
    {
        var package = _importExportService.ImportJson(json);
        var categoryIdMap = new Dictionary<long, long>();
        var importedCategories = 0;

        if (package.Categories.Count > 0)
        {
            var existingCategories = (await _repository.GetCategoriesAsync())
                .ToDictionary(item => item.Name, item => item, StringComparer.OrdinalIgnoreCase);
            foreach (var source in package.Categories.OrderBy(item => item.SortOrder).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(source.Name))
                {
                    continue;
                }

                var name = source.Name.Trim();
                if (existingCategories.TryGetValue(name, out var existing))
                {
                    if (source.Id != 0)
                    {
                        categoryIdMap[source.Id] = existing.Id;
                    }

                    continue;
                }

                var imported = CloneCategory(source);
                imported.Id = 0;
                imported.Name = name;
                imported.MdbxDatabaseId = null;
                imported.MdbxFolderId = null;
                await _repository.SaveCategoryAsync(imported);
                existingCategories[imported.Name] = imported;
                if (source.Id != 0)
                {
                    categoryIdMap[source.Id] = imported.Id;
                }

                importedCategories++;
            }
        }

        var passwordIdMap = new Dictionary<long, long>();
        var importedPasswords = 0;
        foreach (var source in package.Passwords)
        {
            var imported = ClonePasswordForImport(source, categoryIdMap);
            var sourceId = source.Id;
            await _repository.SavePasswordAsync(imported);
            if (sourceId != 0)
            {
                passwordIdMap[sourceId] = imported.Id;
            }

            importedPasswords++;
        }

        foreach (var group in package.PasswordCustomFields)
        {
            if (!passwordIdMap.TryGetValue(group.PasswordId, out var importedPasswordId))
            {
                continue;
            }

            await _repository.ReplaceCustomFieldsAsync(
                importedPasswordId,
                group.Fields.Select(field => CloneCustomFieldForImport(field, importedPasswordId)).ToArray());
        }

        foreach (var group in package.PasswordHistory)
        {
            if (!passwordIdMap.TryGetValue(group.PasswordId, out var importedPasswordId))
            {
                continue;
            }

            foreach (var source in group.Entries.OrderBy(item => item.LastUsedAt))
            {
                await _repository.SavePasswordHistoryAsync(ClonePasswordHistoryForImport(source, importedPasswordId));
            }
        }

        foreach (var group in package.PasswordAttachments)
        {
            if (!passwordIdMap.TryGetValue(group.PasswordId, out var importedPasswordId))
            {
                continue;
            }

            foreach (var source in group.Attachments)
            {
                if (!TryDecodeAttachmentContent(source.ContentBase64, out var content))
                {
                    continue;
                }

                await ImportPasswordAttachmentAsync(source.Metadata, importedPasswordId, content);
            }
        }

        var importedSecureItems = 0;
        foreach (var source in package.SecureItems)
        {
            var imported = CloneSecureItemForImport(source, passwordIdMap, categoryIdMap);
            await _repository.SaveSecureItemAsync(imported);
            if (source.Id > 0)
            {
                var restoredImagePaths = await ImportSecureItemAttachmentsAsync(
                    imported,
                    package.SecureItemAttachments.FirstOrDefault(group => group.SecureItemId == source.Id)?.Attachments ?? []);
                if (restoredImagePaths.Count > 0)
                {
                    ApplySecureItemImagePaths(imported, restoredImagePaths);
                    await _repository.SaveSecureItemAsync(imported);
                }
            }

            importedSecureItems++;
        }

        await _repository.LogAsync(new OperationLog
        {
            ItemType = "VAULT",
            ItemTitle = _localization.Get("MonicaJson"),
            OperationType = "IMPORT",
            ChangesJson = JsonSerializer.Serialize(new { importedPasswords, importedSecureItems, importedCategories }),
            DeviceName = Environment.MachineName
        });

        await LoadAsync();
        return new MonicaJsonImportResult(importedPasswords, importedSecureItems, importedCategories);
    }

    private async Task ImportPasswordAttachmentAsync(Attachment source, long importedPasswordId, byte[] content)
    {
        var attachment = CloneAttachmentForImport(source, importedPasswordId);
        var draft = await _passwordAttachmentFileService.StoreAttachmentAsync(
            attachment.FileName,
            content,
            attachment.ContentType);
        attachment.StoragePath = draft.StoragePath;
        attachment.SizeBytes = draft.SizeBytes;
        if (string.IsNullOrWhiteSpace(attachment.ContentType))
        {
            attachment.ContentType = draft.ContentType;
        }

        var originalStoragePath = attachment.StoragePath;
        await _repository.SaveAttachmentAsync(attachment, content);
        if (!string.Equals(originalStoragePath, attachment.StoragePath, StringComparison.Ordinal) &&
            !originalStoragePath.StartsWith("mdbx:", StringComparison.OrdinalIgnoreCase))
        {
            await _passwordAttachmentFileService.DeleteStoredAttachmentAsync(originalStoragePath);
        }
    }

    private string FormatMonicaJsonImportStatus(MonicaJsonImportResult result)
    {
        return result.Categories > 0
            ? _localization.Format("ImportedMonicaJsonWithCategoriesFormat", result.Passwords, result.SecureItems, result.Categories)
            : _localization.Format("ImportedMonicaJsonFormat", result.Passwords, result.SecureItems);
    }

    private bool HasSelectedWebDavBackupOptions() =>
        WebDavBackupIncludePasswords ||
        WebDavBackupIncludeTotp ||
        WebDavBackupIncludeNotes ||
        WebDavBackupIncludeCards ||
        WebDavBackupIncludeDocuments ||
        WebDavBackupIncludeImages ||
        WebDavBackupIncludeCategories;

    private int CountSelectedWebDavBackupOptions() =>
        (WebDavBackupIncludePasswords ? 1 : 0) +
        (WebDavBackupIncludeTotp ? 1 : 0) +
        (WebDavBackupIncludeNotes ? 1 : 0) +
        (WebDavBackupIncludeCards ? 1 : 0) +
        (WebDavBackupIncludeDocuments ? 1 : 0) +
        (WebDavBackupIncludeImages ? 1 : 0) +
        (WebDavBackupIncludeCategories ? 1 : 0);

    private static bool IsEncryptedWebDavBackup(string fileName) =>
        fileName.EndsWith(".enc.json", StringComparison.OrdinalIgnoreCase);

    private static string EncryptWebDavBackupPayload(string json, string password)
    {
        const int saltSize = 16;
        const int nonceSize = 12;
        const int tagSize = 16;
        const int iterations = 300_000;

        var salt = RandomNumberGenerator.GetBytes(saltSize);
        var nonce = RandomNumberGenerator.GetBytes(nonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(json);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[tagSize];
        var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);

        using var aes = new AesGcm(key, tagSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        return JsonSerializer.Serialize(new WebDavEncryptedBackupPackage(
            1,
            "pbkdf2-sha256",
            iterations,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag),
            Convert.ToBase64String(cipherBytes)));
    }

    private static string DecryptWebDavBackupPayload(string content, string password)
    {
        const int tagSize = 16;
        var package = JsonSerializer.Deserialize<WebDavEncryptedBackupPackage>(content)
            ?? throw new InvalidOperationException("Invalid encrypted Monica backup payload.");
        if (!string.Equals(package.Kdf, "pbkdf2-sha256", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Unsupported Monica backup encryption KDF.");
        }

        var salt = Convert.FromBase64String(package.Salt);
        var nonce = Convert.FromBase64String(package.Nonce);
        var tag = Convert.FromBase64String(package.Tag);
        var cipherBytes = Convert.FromBase64String(package.CipherText);
        var plainBytes = new byte[cipherBytes.Length];
        var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, package.Iterations, HashAlgorithmName.SHA256, 32);

        using var aes = new AesGcm(key, tagSize);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
        return Encoding.UTF8.GetString(plainBytes);
    }

    [RelayCommand]
    private async Task ImportPasswordCsvAsync()
    {
        if (string.IsNullOrWhiteSpace(ImportCsvText))
        {
            StatusMessage = _localization.Get("ImportCsvRequired");
            return;
        }

        try
        {
            var entries = _importExportService.ImportPasswordCsv(ImportCsvText);
            var importedPasswords = 0;
            foreach (var source in entries)
            {
                var imported = ClonePasswordForImport(source);
                await _repository.SavePasswordAsync(imported);
                importedPasswords++;
            }

            await _repository.LogAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemTitle = _localization.Get("PasswordCsv"),
                OperationType = "IMPORT",
                DeviceName = Environment.MachineName
            });

            ImportCsvText = "";
            await LoadAsync();
            StatusMessage = _localization.Format("ImportedPasswordCsvFormat", importedPasswords);
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ImportFailedFormat", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ImportNoteCsvAsync()
    {
        if (string.IsNullOrWhiteSpace(ImportNoteCsvText))
        {
            StatusMessage = _localization.Get("ImportNoteCsvRequired");
            return;
        }

        try
        {
            var entries = _importExportService.ImportNoteCsv(ImportNoteCsvText);
            var existingTitles = (await _repository.GetSecureItemsAsync(VaultItemType.Note))
                .Select(item => item.Title)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var importedNotes = 0;
            var skippedNotes = 0;

            foreach (var source in entries)
            {
                if (!existingTitles.Add(source.Title))
                {
                    skippedNotes++;
                    continue;
                }

                await _repository.SaveSecureItemAsync(source);
                importedNotes++;
            }

            await _repository.LogAsync(new OperationLog
            {
                ItemType = "NOTE",
                ItemTitle = _localization.Get("NoteCsv"),
                OperationType = "IMPORT",
                DeviceName = Environment.MachineName
            });

            ImportNoteCsvText = "";
            await LoadAsync();
            StatusMessage = _localization.Format("ImportedNoteCsvFormat", importedNotes, skippedNotes);
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ImportFailedFormat", ex.Message);
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

    [RelayCommand]
    private void TogglePasswordFolderExpansion(PasswordFolderFilterChoice? item)
    {
        if (item is null || !item.HasChildren || string.IsNullOrWhiteSpace(item.SelectionKey))
        {
            return;
        }

        if (!_collapsedPasswordFolderKeys.Add(item.SelectionKey))
        {
            _collapsedPasswordFolderKeys.Remove(item.SelectionKey);
        }

        RefreshPasswordFolderFilters();
    }

    [RelayCommand]
    private async Task CreatePasswordFolderAsync()
    {
        var name = NewFolderName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = _localization.Get("FolderNameRequired");
            return;
        }

        var existing = Categories.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectedPasswordFolderFilter = PasswordFolderFilters.FirstOrDefault(item => item.Id == existing.Id);
            NewFolderName = "";
            StatusMessage = _localization.Format("SelectedFolderFormat", existing.Name);
            return;
        }

        var category = new Category
        {
            Name = name,
            SortOrder = Categories.Count == 0 ? 1 : Categories.Max(item => item.SortOrder) + 1
        };
        await _repository.SaveCategoryAsync(category);
        Categories.Add(category);
        RefreshPasswordFolderFilters(category.Id);
        NewFolderName = "";
        StatusMessage = _localization.Format("CreatedFolderFormat", category.Name);
    }

    [RelayCommand]
    private async Task RenameSelectedPasswordFolderAsync()
    {
        var category = GetSelectedPasswordFolderCategory();
        var name = NewFolderName.Trim();
        if (category is null)
        {
            StatusMessage = _localization.Get("SelectFolderToManage");
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = _localization.Get("FolderNameRequired");
            return;
        }

        var duplicate = Categories.FirstOrDefault(item =>
            item.Id != category.Id &&
            item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
        {
            StatusMessage = _localization.Format("FolderAlreadyExistsFormat", duplicate.Name);
            return;
        }

        var oldName = category.Name;
        category.Name = name;
        await _repository.SaveCategoryAsync(category);
        RefreshPasswordFolderFilters(category.Id);
        NewFolderName = "";
        await _repository.LogAsync(new OperationLog
        {
            ItemType = "CATEGORY",
            ItemId = category.Id,
            ItemTitle = category.Name,
            OperationType = "UPDATE",
            DeviceName = Environment.MachineName
        });
        await LoadTimelineAsync();
        StatusMessage = _localization.Format("RenamedFolderFormat", oldName, category.Name);
    }

    [RelayCommand]
    private async Task DeleteSelectedPasswordFolderAsync()
    {
        var category = GetSelectedPasswordFolderCategory();
        if (category is null)
        {
            StatusMessage = _localization.Get("SelectFolderToManage");
            return;
        }

        var movedPasswords = Passwords.Count(item => item.CategoryId == category.Id);
        var name = category.Name;
        if (!await ConfirmDeleteFolderAsync(name, movedPasswords))
        {
            return;
        }

        await _repository.DeleteCategoryAsync(category.Id);
        Categories.Remove(category);
        foreach (var password in Passwords.Where(item => item.CategoryId == category.Id))
        {
            password.CategoryId = null;
        }

        foreach (var item in TotpItems.Where(item => item.CategoryId == category.Id))
        {
            item.CategoryId = null;
        }

        foreach (var item in NoteItems.Where(item => item.CategoryId == category.Id))
        {
            item.CategoryId = null;
        }

        foreach (var item in WalletItems.Where(item => item.CategoryId == category.Id))
        {
            item.CategoryId = null;
        }

        await _repository.LogAsync(new OperationLog
        {
            ItemType = "CATEGORY",
            ItemId = category.Id,
            ItemTitle = name,
            OperationType = "DELETE",
            DeviceName = Environment.MachineName
        });
        RefreshPasswordFolderFilters(-1);
        await LoadTimelineAsync();
        StatusMessage = _localization.Format("DeletedFolderFormat", name, movedPasswords);
    }

    private void RaiseCounts()
    {
        RefreshArchiveCountState();
        RefreshRecycleBinCountState();

        OnPropertyChanged(nameof(PasswordCountText));
        OnPropertyChanged(nameof(NoteCountText));
        OnPropertyChanged(nameof(TotpCountText));
        OnPropertyChanged(nameof(HasTotpItems));
        RaiseTotpFilterState(reconcileSelection: false);
        OnPropertyChanged(nameof(WalletCountText));
        OnPropertyChanged(nameof(HasWalletItems));
        OnPropertyChanged(nameof(TimelineCountText));
        OnPropertyChanged(nameof(HasTimelineEntries));
        OnPropertyChanged(nameof(SecurityIssueCountText));
        OnPropertyChanged(nameof(HasSecurityIssues));
        OnPropertyChanged(nameof(LocalDatabaseSummaryText));
        OnPropertyChanged(nameof(MdbxDatabaseCountText));
        RaiseNoteTreeState();
        RaiseMdbxVaultState();
        OnPropertyChanged(nameof(VaultSourceCountText));
        RaiseTotpSelectionState();
        RaiseWalletSelectionState();
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

    private void RaiseSyncPageState()
    {
        OnPropertyChanged(nameof(WebDavConnectionStatusText));
        OnPropertyChanged(nameof(SyncStatusSummaryText));
        OnPropertyChanged(nameof(SyncConfigurationSummaryText));
        OnPropertyChanged(nameof(SyncRecoverySummaryText));
        OnPropertyChanged(nameof(OneDriveConnectionStatusText));
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
            !isLocal);
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

    private string FormatLocalDate(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy/MM/dd HH:mm", _localization.Culture);

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

    private string LocalizeSyncStatus(SyncStatus status)
    {
        return status switch
        {
            SyncStatus.Synced => _localization.Get("Synced"),
            SyncStatus.Syncing => _localization.Get("Syncing"),
            SyncStatus.Pending => _localization.Get("Pending"),
            SyncStatus.PendingUpload => _localization.Get("PendingUpload"),
            SyncStatus.InSync => _localization.Get("Synced"),
            SyncStatus.RemoteChanged => _localization.Get("RemoteChanged"),
            SyncStatus.LocalOnly => _localization.Get("LocalOnly"),
            SyncStatus.Conflict => _localization.Get("Conflict"),
            SyncStatus.Failed => _localization.Get("Failed"),
            _ => _localization.Get("None")
        };
    }






    private static void ReplaceItems<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        if (target is ObservableRangeCollection<T> range)
        {
            range.ReplaceRange(items);
            return;
        }

        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }



    private static IReadOnlyList<string> DecodeSecureItemImagePaths(SecureItem item) => item.ItemType switch
    {
        VaultItemType.Document => WalletItemDataCodec.DecodeDocument(item).ImagePaths,
        VaultItemType.BankCard => WalletItemDataCodec.DecodeBankCard(item).ImagePaths,
        VaultItemType.Note => NoteContentCodec.DecodeImagePaths(item.ImagePaths),
        _ => WalletItemDataCodec.DecodeImagePaths(item.ImagePaths)
    };

    private static Attachment CreateSecureItemImageAttachmentForExport(SecureItem item, string imagePath, int index)
    {
        return new Attachment
        {
            Id = 0,
            OwnerType = "SECURE_ITEM",
            OwnerId = item.Id,
            FileName = ResolveSecureItemImageFileName(item, imagePath, index),
            ContentType = InferAttachmentContentType(imagePath),
            StoragePath = imagePath,
            SizeBytes = 0,
            CreatedAt = item.UpdatedAt == default ? DateTimeOffset.UtcNow : item.UpdatedAt
        };
    }

    private static string ResolveSecureItemImageFileName(SecureItem item, string imagePath, int index)
    {
        var fileName = Path.GetFileName(imagePath.Replace('\\', Path.DirectorySeparatorChar));
        if (!string.IsNullOrWhiteSpace(fileName) && !imagePath.StartsWith("mdbx:", StringComparison.OrdinalIgnoreCase))
        {
            return fileName;
        }

        var prefix = item.ItemType switch
        {
            VaultItemType.BankCard => "card-image",
            VaultItemType.Document => "document-image",
            VaultItemType.Note => "note-image",
            _ => "secure-item-image"
        };
        return $"{prefix}-{index + 1}";
    }

    private static string InferAttachmentContentType(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            ".pdf" => "application/pdf",
            _ => ""
        };
    }

    private static void ApplySecureItemImagePaths(SecureItem item, IReadOnlyList<string> imagePaths)
    {
        item.ImagePaths = WalletItemDataCodec.EncodeImagePaths(imagePaths);
        if (item.ItemType == VaultItemType.Note)
        {
            var note = NoteContentCodec.DecodeFromItem(item);
            item.ItemData = NoteContentCodec.BuildSavePayload(
                item.Title,
                note.Content,
                string.Join(",", note.Tags),
                note.IsMarkdown,
                imagePaths).ItemData;
            return;
        }

        if (item.ItemType == VaultItemType.Document)
        {
            var data = WalletItemDataCodec.DecodeDocument(item);
            data.ImagePaths = imagePaths.ToList();
            item.ItemData = WalletItemDataCodec.EncodeDocument(data);
            return;
        }

        if (item.ItemType == VaultItemType.BankCard)
        {
            var data = WalletItemDataCodec.DecodeBankCard(item);
            data.ImagePaths = imagePaths.ToList();
            item.ItemData = WalletItemDataCodec.EncodeBankCard(data);
        }
    }

    private static bool TryDecodeAttachmentContent(string contentBase64, out byte[] content)
    {
        try
        {
            content = Convert.FromBase64String(contentBase64);
            return true;
        }
        catch (FormatException)
        {
            content = [];
            return false;
        }
    }

    private async Task<IReadOnlyList<string>> ImportSecureItemAttachmentsAsync(SecureItem item, IReadOnlyList<SecureItemAttachmentExport> attachments)
    {
        if (attachments.Count == 0)
        {
            return [];
        }

        var restoredPaths = new List<string>();
        foreach (var source in attachments)
        {
            if (!TryDecodeAttachmentContent(source.ContentBase64, out var content))
            {
                continue;
            }

            var draft = await _passwordAttachmentFileService.StoreAttachmentAsync(
                source.Metadata.FileName,
                content,
                source.Metadata.ContentType);
            restoredPaths.Add(draft.StoragePath);
        }

        return restoredPaths;
    }

    private static SecureItem CloneSecureItemForExport(SecureItem source, bool includeCategory = true, bool includeImages = true)
    {
        var clone = CloneSecureItem(source);
        if (!includeCategory)
        {
            clone.CategoryId = null;
        }

        if (!includeImages)
        {
            StripSecureItemImages(clone);
        }

        clone.MdbxDatabaseId = null;
        clone.MdbxFolderId = null;
        return clone;
    }

    private static SecureItem CloneSecureItemForImport(
        SecureItem source,
        IReadOnlyDictionary<long, long> passwordIdMap,
        IReadOnlyDictionary<long, long>? categoryIdMap = null)
    {
        var clone = CloneSecureItem(source);
        clone.Id = 0;
        clone.MdbxDatabaseId = null;
        clone.MdbxFolderId = null;
        if (clone.BoundPasswordId is { } boundPasswordId)
        {
            clone.BoundPasswordId = passwordIdMap.TryGetValue(boundPasswordId, out var importedPasswordId)
                ? importedPasswordId
                : null;
        }

        if (clone.CategoryId is { } categoryId)
        {
            clone.CategoryId = categoryIdMap?.TryGetValue(categoryId, out var importedCategoryId) == true
                ? importedCategoryId
                : null;
        }

        clone.IsDeleted = false;
        clone.DeletedAt = null;
        clone.BitwardenLocalModified = true;
        clone.SyncStatus = clone.BitwardenVaultId is null ? SyncStatus.None : SyncStatus.Pending;
        return clone;
    }

    private static SecureItem CloneSecureItem(SecureItem source)
    {
        return new SecureItem
        {
            Id = source.Id,
            ItemType = source.ItemType,
            Title = source.Title,
            Notes = source.Notes,
            IsFavorite = source.IsFavorite,
            SortOrder = source.SortOrder,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt,
            ItemData = source.ItemData,
            ImagePaths = source.ImagePaths,
            BoundPasswordId = source.BoundPasswordId,
            CategoryId = source.CategoryId,
            KeepassDatabaseId = source.KeepassDatabaseId,
            KeepassGroupPath = source.KeepassGroupPath,
            KeepassEntryUuid = source.KeepassEntryUuid,
            KeepassGroupUuid = source.KeepassGroupUuid,
            MdbxDatabaseId = source.MdbxDatabaseId,
            MdbxFolderId = source.MdbxFolderId,
            IsDeleted = source.IsDeleted,
            DeletedAt = source.DeletedAt,
            ReplicaGroupId = source.ReplicaGroupId,
            BitwardenVaultId = source.BitwardenVaultId,
            BitwardenCipherId = source.BitwardenCipherId,
            BitwardenFolderId = source.BitwardenFolderId,
            BitwardenRevisionDate = source.BitwardenRevisionDate,
            BitwardenLocalModified = source.BitwardenLocalModified,
            SyncStatus = source.SyncStatus
        };
    }

    private static Category CloneCategory(Category source)
    {
        return new Category
        {
            Id = source.Id,
            Name = source.Name,
            SortOrder = source.SortOrder
        };
    }

    private static void StripSecureItemImages(SecureItem item)
    {
        item.ImagePaths = "[]";
        if (item.ItemType == VaultItemType.Note)
        {
            var note = NoteContentCodec.DecodeFromItem(item);
            item.ItemData = NoteContentCodec.BuildSavePayload(
                item.Title,
                note.Content,
                string.Join(",", note.Tags),
                note.IsMarkdown,
                []).ItemData;
            return;
        }

        if (item.ItemType == VaultItemType.Document)
        {
            var data = WalletItemDataCodec.DecodeDocument(item);
            data.ImagePaths.Clear();
            item.ItemData = WalletItemDataCodec.EncodeDocument(data);
            return;
        }

        if (item.ItemType == VaultItemType.BankCard)
        {
            var data = WalletItemDataCodec.DecodeBankCard(item);
            data.ImagePaths.Clear();
            item.ItemData = WalletItemDataCodec.EncodeBankCard(data);
        }
    }


    private async Task LoadTimelineAsync()
    {
        var logs = await AppDiagnostics.MeasureAsync(
            "Load timeline",
            () => _repository.GetOperationLogsAsync(150));
        ApplyTimelineLogs(logs);
    }

    private async Task LoadTimelineDeferredAsync()
    {
        try
        {
            await LoadTimelineAsync();
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error("Deferred timeline load failed", ex);
        }
    }

    private void ApplyTimelineLogs(IReadOnlyList<OperationLog> logs)
    {
        var selectedStamp = SelectedTimelineEntry?.TimestampText;
        var selectedTitle = SelectedTimelineEntry?.Title;
        var entries = logs
            .Select(log => new TimelineEntry(
                string.IsNullOrWhiteSpace(log.ItemTitle) ? _localization.Get("Untitled") : log.ItemTitle,
                _localization.Format("TimelineEntryDescriptionFormat", LocalizeOperationType(log.OperationType), log.ItemType, log.DeviceName),
                log.Timestamp.LocalDateTime.ToString("g", _localization.Culture),
                log.OperationType,
                log.ItemType))
            .ToArray();
        ReplaceItems(TimelineEntries, entries);
        SelectedTimelineEntry =
            TimelineEntries.FirstOrDefault(item =>
                string.Equals(item.TimestampText, selectedStamp, StringComparison.Ordinal) &&
                string.Equals(item.Title, selectedTitle, StringComparison.Ordinal)) ??
            TimelineEntries.FirstOrDefault();

        OnPropertyChanged(nameof(TimelineCountText));
        OnPropertyChanged(nameof(HasTimelineEntries));
    }

    [RelayCommand]
    private async Task ExportTimelineAsync()
    {
        if (TimelineEntries.Count == 0)
        {
            StatusMessage = _localization.Get("TimelineExportEmpty");
            return;
        }

        var lines = new List<string>
        {
            $"{_localization.Get("Title")}\t{_localization.Get("Description")}\t{_localization.Get("Timestamp")}\t{_localization.Get("OperationType")}\t{_localization.Get("ItemType")}"
        };

        foreach (var entry in TimelineEntries)
        {
            lines.Add($"{entry.Title}\t{entry.Description}\t{entry.TimestampText}\t{entry.OperationType}\t{entry.ItemType}");
        }

        ExportTimelinePreview = string.Join(Environment.NewLine, lines);
        StatusMessage = _localization.Format("ExportedTimelineFormat", TimelineEntries.Count);
        await Task.CompletedTask;
    }




    private string LocalizeOperationType(string operationType)
    {
        return operationType.ToUpperInvariant() switch
        {
            "CREATE" => _localization.Get("OperationCreate"),
            "UPDATE" => _localization.Get("OperationUpdate"),
            "DELETE" => _localization.Get("OperationDelete"),
            "RESTORE" => _localization.Get("OperationRestore"),
            "PURGE" => _localization.Get("OperationPurge"),
            "FAVORITE" => _localization.Get("OperationFavorite"),
            "MOVE_CATEGORY" => _localization.Get("OperationMoveCategory"),
            "STACK" => _localization.Get("OperationStack"),
            "ATTACHMENT" => _localization.Get("OperationAttachment"),
            "ARCHIVE" => _localization.Get("OperationArchive"),
            "UNARCHIVE" => _localization.Get("OperationUnarchive"),
            "IMPORT" => _localization.Get("OperationImport"),
            _ => operationType
        };
    }







    private sealed record WebDavEncryptedBackupPackage(
        int Version,
        string Kdf,
        int Iterations,
        string Salt,
        string Nonce,
        string Tag,
        string CipherText);

    private void ApplySettings(DesktopAppSettings settings)
    {
        _isApplyingSettings = true;
        try
        {
            _localization.SetLanguage(settings.Language);
            SettingsLanguage = settings.Language;
            SettingsTheme = settings.Theme;
            StartupSection = settings.StartupSection;
            AutoLockEnabled = settings.AutoLockEnabled;
            AutoLockMinutes = settings.AutoLockMinutes;
            ClearClipboardEnabled = settings.ClearClipboardEnabled;
            ClipboardClearSeconds = settings.ClipboardClearSeconds;
            RequirePasswordBeforeExport = settings.RequirePasswordBeforeExport;
            ApplySecurityRecoverySettings(settings.SecurityRecovery);
            MinimizeToTray = settings.MinimizeToTray && CanUseTrayIntegration;
            QuickSearchEnabled = settings.QuickSearchEnabled;
            QuickSearchHotkey = settings.QuickSearchHotkey;
            BrowserIntegrationEnabled = settings.BrowserIntegrationEnabled && CanUseBrowserBridgeIntegration;
            BrowserIntegrationPort = settings.BrowserIntegrationPort;
            CompactPasswordList = settings.CompactPasswordList;
            SelectedPasswordSort = settings.PasswordSortOrder;
            WebDavEnabled = settings.WebDavEnabled;
            WebDavServerUrl = settings.WebDavServerUrl;
            WebDavUsername = settings.WebDavUsername;
            WebDavPassword = settings.WebDavPassword;
            WebDavRemotePath = settings.WebDavRemotePath;
            WebDavSyncOnStartup = settings.WebDavSyncOnStartup;
            WebDavSyncAfterChanges = settings.WebDavSyncAfterChanges;
            WebDavBackupIncludePasswords = settings.WebDavBackupIncludePasswords;
            WebDavBackupIncludeTotp = settings.WebDavBackupIncludeTotp;
            WebDavBackupIncludeNotes = settings.WebDavBackupIncludeNotes;
            WebDavBackupIncludeCards = settings.WebDavBackupIncludeCards;
            WebDavBackupIncludeDocuments = settings.WebDavBackupIncludeDocuments;
            WebDavBackupIncludeImages = settings.WebDavBackupIncludeImages;
            WebDavBackupIncludeCategories = settings.WebDavBackupIncludeCategories;
            WebDavBackupEncryptionEnabled = settings.WebDavBackupEncryptionEnabled;
            WebDavBackupEncryptionPassword = settings.WebDavBackupEncryptionPassword;
            SyncConflictStrategy = settings.SyncConflictStrategy;
            OneDriveEnabled = settings.OneDriveEnabled;
            MdbxLocalCacheEnabled = settings.MdbxLocalCacheEnabled;
            ApplyTheme(settings.Theme);
            RefreshLocalizedProperties();
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private void ApplySecurityRecoverySettings(SecurityRecoverySettings settings)
    {
        var wasApplyingSettings = _isApplyingSettings;
        _isApplyingSettings = true;
        try
        {
            SecurityRecoveryEnabled = settings.IsEnabled;
            SecurityQuestion1Id = settings.Question1Id;
            SecurityQuestion1CustomText = settings.Question1Id == SecurityQuestionService.CustomQuestionId ? settings.Question1Text : "";
            SecurityQuestion1Answer = "";
            SecurityQuestion2Id = settings.Question2Id;
            SecurityQuestion2CustomText = settings.Question2Id == SecurityQuestionService.CustomQuestionId ? settings.Question2Text : "";
            SecurityQuestion2Answer = "";
            OnPropertyChanged(nameof(SecurityRecoveryStatusText));
            OnPropertyChanged(nameof(SecurityRecoveryQuestion1PromptText));
            OnPropertyChanged(nameof(SecurityRecoveryQuestion2PromptText));
            OnPropertyChanged(nameof(CanResetMasterPasswordWithSecurityQuestions));
            OnPropertyChanged(nameof(CanRunResetMasterPassword));
            OnPropertyChanged(nameof(IsSecurityQuestion1Custom));
            OnPropertyChanged(nameof(IsSecurityQuestion2Custom));
        }
        finally
        {
            _isApplyingSettings = wasApplyingSettings;
        }
    }

    private void UpdateSettings(Action<DesktopAppSettings> update)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        update(_settingsService.Current);
        QueueSaveSettings();
    }

    private void UpdateWebDavBackupOption(Action<DesktopAppSettings> update)
    {
        UpdateSettings(update);
        OnPropertyChanged(nameof(WebDavBackupOptionsSummaryText));
        RaiseSyncPageState();
    }

    private void QueueSaveSettings()
    {
        var shouldStartSave = false;
        lock (_settingsSaveSync)
        {
            _hasPendingSettingsSave = true;
            if (!_isSavingSettings)
            {
                _isSavingSettings = true;
                shouldStartSave = true;
            }
        }

        if (shouldStartSave)
        {
            _ = SaveSettingsAsync();
        }
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            while (true)
            {
                lock (_settingsSaveSync)
                {
                    if (!_hasPendingSettingsSave)
                    {
                        _isSavingSettings = false;
                        return;
                    }

                    _hasPendingSettingsSave = false;
                }

                await _settingsService.SaveAsync();
            }
        }
        catch (Exception ex)
        {
            lock (_settingsSaveSync)
            {
                _isSavingSettings = false;
                _hasPendingSettingsSave = false;
            }

            StatusMessage = _localization.Format("VaultMetadataLoadFailedFormat", ex.Message);
        }
    }

    private void RefreshLocalizedProperties()
    {
        RefreshChoiceLabels();
        RefreshPlatformIntegrationCapabilities();
        RefreshCapabilities();
        OnPropertyChanged(nameof(SelectedSectionTitle));
        OnPropertyChanged(nameof(PlatformIntegrationsTitle));
        RaisePlatformIntegrationState();
        RaiseAboutText();
        RaiseSecurityRecoveryText();
        RaiseMasterPasswordMaintenanceText();
        RaiseDangerZoneText();
        OnPropertyChanged(nameof(LoginTitle));
        OnPropertyChanged(nameof(LoginDescription));
        OnPropertyChanged(nameof(LoginButtonText));
        RefreshGeneratorLocalizedState();
        OnPropertyChanged(nameof(LegacyVaultImportPromptText));
        OnPropertyChanged(nameof(WebDavBackupOptionsSummaryText));
        RaiseSyncPageState();
        RefreshVaultSources();
        RaiseWebDavBackupHistoryState();
        RaisePasswordQuickAccessState();
        RaisePasswordFilterState();
        OnPropertyChanged(nameof(ClearPasswordFiltersText));
        RaisePasswordSortText();
        OnPropertyChanged(nameof(TotpScanQrText));
        OnPropertyChanged(nameof(TotpManualAddText));
        OnPropertyChanged(nameof(TotpMoreActionsText));
        OnPropertyChanged(nameof(TotpFilterTitleText));
        OnPropertyChanged(nameof(TotpIssuerGroupsText));
        OnPropertyChanged(nameof(TotpNoFilteredResultsText));
        OnPropertyChanged(nameof(TotpEmptyStateText));
        OnPropertyChanged(nameof(ClearTotpFiltersText));
        OnPropertyChanged(nameof(TotpShowHiddenText));
        OnPropertyChanged(nameof(TotpHelpText));
        RaiseTotpFilterState(reconcileSelection: false);
        RaiseCounts();
        OnPropertyChanged(nameof(SecurityIssueCountText));
        OnPropertyChanged(nameof(NotePreviewMarkdown));
        OnPropertyChanged(nameof(NotePlainPreview));
        if (!_hasCompromisedPasswordCheckResults)
        {
            CompromisedPasswordStatus = _localization.Get("CompromisedPasswordNotChecked");
        }
    }

    private void RefreshChoiceLabels()
    {
        ReplaceOptions(LanguageOptions,
            new("system", _localization.GetLanguageName("system")),
            new("en-US", _localization.GetLanguageName("en-US")),
            new("zh-CN", _localization.GetLanguageName("zh-CN")));

        ReplaceOptions(ThemeOptions,
            new("system", _localization.Get("SystemDefault")),
            new("light", _localization.Get("Light")),
            new("dark", _localization.Get("Dark")),
            new("high-contrast", _localization.Get("HighContrast")));

        ReplaceOptions(StartupSectionOptions,
            new("Passwords", _localization.Passwords),
            new("Notes", _localization.SecureNotes),
            new("Totp", _localization.Totp),
            new("Cards", _localization.Cards),
            new("Generator", _localization.Generator),
            new("Archive", _localization.Archive),
            new("RecycleBin", _localization.RecycleBin),
            new("SecurityAnalysis", _localization.SecurityAnalysis),
            new("Timeline", _localization.Timeline),
            new("Mdbx", _localization.Get("MdbxVaults")),
            new("DatabaseManagement", _localization.DatabaseManagement),
            new("Sync", _localization.SyncAndBackup),
            new("Settings", _localization.Settings));

        ReplaceOptions(AutoLockMinuteOptions,
            new(1, _localization.Format("MinuteFormat", 1)),
            new(5, _localization.Format("MinuteFormat", 5)),
            new(15, _localization.Format("MinuteFormat", 15)),
            new(30, _localization.Format("MinuteFormat", 30)),
            new(60, _localization.Format("MinuteFormat", 60)));

        ReplaceOptions(ClipboardSecondOptions,
            new(10, _localization.Format("SecondFormat", 10)),
            new(30, _localization.Format("SecondFormat", 30)),
            new(60, _localization.Format("SecondFormat", 60)),
            new(120, _localization.Format("SecondFormat", 120)));

        ReplaceOptions(ConflictStrategyOptions,
            new("ask", _localization.Get("AskEveryTime")),
            new("local-wins", _localization.Get("LocalWins")),
            new("remote-wins", _localization.Get("RemoteWins")));

        ReplaceOptions(PasswordSortOptions,
            new("updated-desc", _localization.Get("SortUpdated")),
            new("title-asc", _localization.Get("SortTitle")),
            new("website-asc", _localization.Get("SortWebsite")),
            new("username-asc", _localization.Get("SortUsername")),
            new("created-desc", _localization.Get("SortCreated")),
            new("favorites-first", _localization.Get("SortFavorites")));

        RefreshGeneratorChoiceLabels();

        ReplaceOptions(
            SecurityQuestionOptions,
            _securityQuestionService.PredefinedQuestions
                .Select(question => new SettingsChoice(question.Id, question.Text))
                .ToArray());

        RaiseFilteredPasswordsChanged();
    }

    private static void ReplaceOptions(ObservableCollection<SettingsChoice> target, params SettingsChoice[] choices)
    {
        target.Clear();
        foreach (var choice in choices)
        {
            target.Add(choice);
        }
    }

    private static string FindChoiceLabel(IEnumerable<SettingsChoice> choices, object value)
    {
        var choice = choices.FirstOrDefault(item => Equals(item.Value, value));
        return choice?.Label ?? Convert.ToString(value, CultureInfo.CurrentCulture) ?? "";
    }

    private void RaisePasswordSortText()
    {
        OnPropertyChanged(nameof(SortUpdatedText));
        OnPropertyChanged(nameof(SortTitleText));
        OnPropertyChanged(nameof(SortWebsiteText));
        OnPropertyChanged(nameof(SortUsernameText));
        OnPropertyChanged(nameof(SortCreatedText));
        OnPropertyChanged(nameof(SortFavoritesText));
        OnPropertyChanged(nameof(PasswordSortButtonTip));
    }

    private void RaiseAboutText()
    {
        OnPropertyChanged(nameof(AboutTitle));
        OnPropertyChanged(nameof(AboutDescription));
        OnPropertyChanged(nameof(AppVersionLabel));
        OnPropertyChanged(nameof(GitHubRepositoryLabel));
        OnPropertyChanged(nameof(OpenRepositoryText));
        OnPropertyChanged(nameof(RepositoryUrlText));
        OnPropertyChanged(nameof(AppVersionText));
    }

    private void RaiseDangerZoneText()
    {
        OnPropertyChanged(nameof(DangerZoneTitle));
        OnPropertyChanged(nameof(DangerZoneDescription));
        OnPropertyChanged(nameof(ClearVaultDataTitle));
        OnPropertyChanged(nameof(ClearVaultDataDescription));
        OnPropertyChanged(nameof(ClearPasswordsOnlyText));
        OnPropertyChanged(nameof(ClearSecureItemsOnlyText));
        OnPropertyChanged(nameof(ClearAllVaultDataText));
        OnPropertyChanged(nameof(ClearVaultConfirmationInstructionText));
    }

    private void RaiseMasterPasswordMaintenanceText()
    {
        OnPropertyChanged(nameof(ChangeMasterPasswordTitle));
        OnPropertyChanged(nameof(ChangeMasterPasswordDescription));
        OnPropertyChanged(nameof(CurrentMasterPasswordText));
        OnPropertyChanged(nameof(NewMasterPasswordText));
        OnPropertyChanged(nameof(ConfirmNewMasterPasswordText));
        OnPropertyChanged(nameof(ChangeMasterPasswordActionText));
    }

    private void RaiseSecurityRecoveryText()
    {
        OnPropertyChanged(nameof(SecurityRecoveryTitle));
        OnPropertyChanged(nameof(SecurityRecoveryDescription));
        OnPropertyChanged(nameof(SecurityRecoveryStatusText));
        OnPropertyChanged(nameof(SecurityRecoveryEnabledText));
        OnPropertyChanged(nameof(SecurityQuestion1Text));
        OnPropertyChanged(nameof(SecurityQuestion2Text));
        OnPropertyChanged(nameof(SecurityQuestionAnswerText));
        OnPropertyChanged(nameof(CustomSecurityQuestionText));
        OnPropertyChanged(nameof(SaveSecurityQuestionsText));
        OnPropertyChanged(nameof(ResetMasterPasswordTitle));
        OnPropertyChanged(nameof(ResetMasterPasswordDescription));
        OnPropertyChanged(nameof(ResetMasterPasswordActionText));
        OnPropertyChanged(nameof(SecurityRecoveryQuestion1PromptText));
        OnPropertyChanged(nameof(SecurityRecoveryQuestion2PromptText));
        OnPropertyChanged(nameof(CanResetMasterPasswordWithSecurityQuestions));
        OnPropertyChanged(nameof(CanRunResetMasterPassword));
    }

    private string LocalizeVaultClearScope(VaultClearScope scope) => scope switch
    {
        VaultClearScope.Passwords => _localization.Get("ClearPasswordsOnly"),
        VaultClearScope.SecureItems => _localization.Get("ClearSecureItemsOnly"),
        _ => _localization.Get("ClearAllVaultData")
    };

    private string GetSecurityQuestionText(int questionId, string customText) =>
        questionId == SecurityQuestionService.CustomQuestionId
            ? customText.Trim()
            : _securityQuestionService.GetQuestion(questionId).Text;

    private static string GetAppVersionText()
    {
        var assembly = typeof(MainWindowViewModel).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var version = string.IsNullOrWhiteSpace(informationalVersion)
            ? assembly.GetName().Version?.ToString()
            : informationalVersion;

        if (string.IsNullOrWhiteSpace(version))
        {
            return "V0.0.0";
        }

        var metadataIndex = version.IndexOf('+', StringComparison.Ordinal);
        if (metadataIndex >= 0)
        {
            version = version[..metadataIndex];
        }

        return version.StartsWith('V') || version.StartsWith('v')
            ? version
            : $"V{version}";
    }

    private string SectionTitle(string section)
    {
        return section switch
        {
            "Passwords" => _localization.Passwords,
            "Notes" => _localization.SecureNotes,
            "Totp" => _localization.Totp,
            "Cards" => _localization.Cards,
            "Generator" => _localization.Generator,
            "Archive" => _localization.Archive,
            "RecycleBin" => _localization.RecycleBin,
            "SecurityAnalysis" => _localization.SecurityAnalysis,
            "Timeline" => _localization.Timeline,
            "Mdbx" => _localization.Get("MdbxVaults"),
            "DatabaseManagement" => _localization.DatabaseManagement,
            "Sync" => _localization.SyncAndBackup,
            "Settings" => _localization.Settings,
            _ => section
        };
    }

    private static void ApplyTheme(string theme)
    {
        if (Application.Current is null)
        {
            return;
        }

        var normalizedTheme = NormalizeThemeValue(theme);
        var themeVariant = normalizedTheme switch
        {
            "light" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            "high-contrast" => FluentAvaloniaTheme.HighContrastTheme,
            _ => ThemeVariant.Default
        };
        Application.Current.RequestedThemeVariant = themeVariant;
        if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow })
        {
            mainWindow.RequestedThemeVariant = themeVariant;
        }

        var useDarkTheme = themeVariant == ThemeVariant.Dark ||
            themeVariant == FluentAvaloniaTheme.HighContrastTheme ||
            themeVariant == ThemeVariant.Default && Application.Current.ActualThemeVariant == ThemeVariant.Dark;
        ApplyMonicaThemeResources(Application.Current.Resources, useDarkTheme, normalizedTheme == "high-contrast");
    }

    private static string NormalizeThemeValue(string theme) =>
        theme.Trim().ToLowerInvariant() switch
        {
            "highcontrast" or "high-contrast" or "contrast" => "high-contrast",
            "light" => "light",
            "dark" => "dark",
            _ => "system"
        };

    private static void ApplyMonicaThemeResources(
        IResourceDictionary resources,
        bool useDarkTheme,
        bool useHighContrastTheme)
    {
        var colors = useHighContrastTheme
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["LayerFillColorDefaultBrush"] = "#FFFFFF",
                ["LayerFillColorAltBrush"] = "#000000",
                ["LayerFillColorSubtleBrush"] = "#F2F2F2",
                ["CardBackgroundBrush"] = "#FFFFFF",
                ["CardBorderBrush"] = "#000000",
                ["CardBackgroundFillColorDefaultBrush"] = "#FFFFFF",
                ["CardBackgroundFillColorSecondaryBrush"] = "#F2F2F2",
                ["CardStrokeColorDefaultBrush"] = "#000000",
                ["DividerStrokeColorDefaultBrush"] = "#000000",
                ["ControlFillColorDefaultBrush"] = "#FFFFFF",
                ["ControlFillColorSecondaryBrush"] = "#F2F2F2",
                ["ControlFillColorTertiaryBrush"] = "#E0E0E0",
                ["ListViewItemBackgroundPointerOver"] = "#E6F7FF",
                ["ListViewItemBackgroundSelected"] = "#FFF200",
                ["ListViewItemBackgroundSelectedPointerOver"] = "#FFE000",
                ["TextFillColorPrimaryBrush"] = "#000000",
                ["TextFillColorSecondaryBrush"] = "#000000",
                ["TextFillColorTertiaryBrush"] = "#1A1A1A",
                ["AccentFillColorDefaultBrush"] = "#FFFF00",
                ["AccentFillColorSecondaryBrush"] = "#00FFFF",
                ["AccentFillColorTertiaryBrush"] = "#E6F7FF",
                ["AccentTextFillColorPrimaryBrush"] = "#000000",
                ["SystemFillColorCautionBrush"] = "#FFFF00",
                ["SystemFillColorCriticalBrush"] = "#B00000",
                ["MutedTextBrush"] = "#CC000000",
                ["OverlayFillColorDefaultBrush"] = "#CC000000"
            }
            : useDarkTheme
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["LayerFillColorDefaultBrush"] = "#202020",
                ["LayerFillColorAltBrush"] = "#1B1B1B",
                ["LayerFillColorSubtleBrush"] = "#242424",
                ["CardBackgroundBrush"] = "#2B2B2B",
                ["CardBorderBrush"] = "#3A3A3A",
                ["CardBackgroundFillColorDefaultBrush"] = "#2B2B2B",
                ["CardBackgroundFillColorSecondaryBrush"] = "#252525",
                ["CardStrokeColorDefaultBrush"] = "#3A3A3A",
                ["DividerStrokeColorDefaultBrush"] = "#343434",
                ["ControlFillColorDefaultBrush"] = "#323232",
                ["ControlFillColorSecondaryBrush"] = "#383838",
                ["ControlFillColorTertiaryBrush"] = "#424242",
                ["ListViewItemBackgroundPointerOver"] = "#343434",
                ["ListViewItemBackgroundSelected"] = "#3A3A3A",
                ["ListViewItemBackgroundSelectedPointerOver"] = "#414141",
                ["TextFillColorPrimaryBrush"] = "#F3F3F3",
                ["TextFillColorSecondaryBrush"] = "#C9C9C9",
                ["TextFillColorTertiaryBrush"] = "#9D9D9D",
                ["AccentFillColorDefaultBrush"] = "#60CDFF",
                ["AccentFillColorSecondaryBrush"] = "#3AADE2",
                ["AccentFillColorTertiaryBrush"] = "#275A70",
                ["AccentTextFillColorPrimaryBrush"] = "#9CDCFE",
                ["SystemFillColorCautionBrush"] = "#FCE100",
                ["SystemFillColorCriticalBrush"] = "#FF99A4",
                ["MutedTextBrush"] = "#99000000",
                ["OverlayFillColorDefaultBrush"] = "#A0000000"
            }
            : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["LayerFillColorDefaultBrush"] = "#F7F7F7",
                ["LayerFillColorAltBrush"] = "#FFFFFF",
                ["LayerFillColorSubtleBrush"] = "#EFEFEF",
                ["CardBackgroundBrush"] = "#FFFFFF",
                ["CardBorderBrush"] = "#D8D8D8",
                ["CardBackgroundFillColorDefaultBrush"] = "#FFFFFF",
                ["CardBackgroundFillColorSecondaryBrush"] = "#F4F4F4",
                ["CardStrokeColorDefaultBrush"] = "#D8D8D8",
                ["DividerStrokeColorDefaultBrush"] = "#E0E0E0",
                ["ControlFillColorDefaultBrush"] = "#FFFFFF",
                ["ControlFillColorSecondaryBrush"] = "#F4F4F4",
                ["ControlFillColorTertiaryBrush"] = "#EAEAEA",
                ["ListViewItemBackgroundPointerOver"] = "#F0F6FC",
                ["ListViewItemBackgroundSelected"] = "#E7F2FF",
                ["ListViewItemBackgroundSelectedPointerOver"] = "#DCEEFF",
                ["TextFillColorPrimaryBrush"] = "#1A1A1A",
                ["TextFillColorSecondaryBrush"] = "#5C5C5C",
                ["TextFillColorTertiaryBrush"] = "#767676",
                ["AccentFillColorDefaultBrush"] = "#0078D4",
                ["AccentFillColorSecondaryBrush"] = "#106EBE",
                ["AccentFillColorTertiaryBrush"] = "#D7EBF8",
                ["AccentTextFillColorPrimaryBrush"] = "#005A9E",
                ["SystemFillColorCautionBrush"] = "#FCE100",
                ["SystemFillColorCriticalBrush"] = "#C42B1C",
                ["MutedTextBrush"] = "#66000000",
                ["OverlayFillColorDefaultBrush"] = "#66000000"
            };

        foreach (var (key, color) in colors)
        {
            resources[key] = new SolidColorBrush(Color.Parse(color));
        }
    }




    private void ReconcileSecureItemSelectionsAfterLoad()
    {
        if (SelectedNote is not null)
        {
            SelectedNote = SelectedNote.Id > 0
                ? NoteItems.FirstOrDefault(item => item.Id == SelectedNote.Id)
                : null;
        }

        if (SelectedWalletItem is not null)
        {
            SelectedWalletItem = SelectedWalletItem.Id > 0
                ? WalletItems.FirstOrDefault(item => item.Id == SelectedWalletItem.Id)
                : null;
        }

        SelectedWalletItem ??= WalletItems.FirstOrDefault();
    }

}
