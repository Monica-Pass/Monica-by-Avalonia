using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
public sealed record LocalizedPlatformIntegrationCapability(
    string Key,
    string Title,
    string Description,
    string Status,
    string UnsupportedReason,
    PlatformFeatureStatus StatusValue)
{
    public bool HasUnsupportedReason => !string.IsNullOrWhiteSpace(UnsupportedReason);
    public bool IsUsable => StatusValue is PlatformFeatureStatus.Available or PlatformFeatureStatus.DesktopEquivalent;
}

public sealed class LocalizedPlatformCapability : ObservableObject
{
    private readonly Action<string, bool> _setFeatureEnabled;
    private readonly string _enabledText;
    private readonly string _disabledText;
    private bool _isEnabled;

    public LocalizedPlatformCapability(
        string key,
        string title,
        string description,
        string status,
        bool isEnabled,
        bool canToggle,
        string enabledText,
        string disabledText,
        string unsupportedReason,
        Action<string, bool> setFeatureEnabled)
    {
        Key = key;
        Title = title;
        Description = description;
        Status = status;
        CanToggle = canToggle;
        UnsupportedReason = unsupportedReason;
        _enabledText = enabledText;
        _disabledText = disabledText;
        _setFeatureEnabled = setFeatureEnabled;
        _isEnabled = canToggle && isEnabled;
    }

    public string Key { get; }
    public string Title { get; }
    public string Description { get; }
    public string Status { get; }
    public bool CanToggle { get; }
    public string UnsupportedReason { get; }
    public bool HasUnsupportedReason => !string.IsNullOrWhiteSpace(UnsupportedReason);
    public string ToggleStatus => IsEnabled ? _enabledText : _disabledText;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            var normalizedValue = CanToggle && value;
            if (!SetProperty(ref _isEnabled, normalizedValue))
            {
                return;
            }

            _setFeatureEnabled(Key, normalizedValue);
            OnPropertyChanged(nameof(ToggleStatus));
        }
    }
}
public sealed record TimelineEntry(string Title, string Description, string TimestampText, string OperationType, string ItemType);
public sealed record SecuritySummaryItem(string Label, string Value, string Detail);
public sealed record SecurityIssueItem(string Title, string Subtitle, string Category, string Severity, long PasswordId, PasswordEntry Entry, int SeverityWeight);
public sealed record PasswordHistoryDisplayItem(PasswordHistoryEntry Entry, string DisplayPassword, bool CanCopy);
public sealed record PasswordQuickAccessItem(PasswordEntry Entry, int OpenCount, string LastOpenedText, string Subtitle);
public sealed record PasswordFolderFilterChoice(long? Id, string Name, int Count);
public sealed record VaultSourceDisplayItem(string DisplayName, string Kind, string LocalPath, string RemoteUrl, string SyncStatus);
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

public sealed partial class MainWindowViewModel : ObservableObject
{
    public const string GitHubRepositoryUrl = "https://github.com/JoyinJoester/Monica";

    private const int PasswordHistoryLimit = 10;
    private const int PasswordQuickAccessLimit = 6;
    private static readonly PlatformFilePickerFileType[] MonicaJsonFileTypes =
    [
        new("Monica JSON", ["*.json"])
    ];
    private static readonly PlatformFilePickerFileType[] PasswordCsvFileTypes =
    [
        new("Password CSV", ["*.csv"])
    ];
    private static readonly PlatformFilePickerFileType[] TotpCsvFileTypes =
    [
        new("TOTP CSV", ["*.csv"])
    ];
    private static readonly PlatformFilePickerFileType[] NoteCsvFileTypes =
    [
        new("Notes CSV", ["*.csv"])
    ];
    private static readonly PlatformFilePickerFileType[] WalletCsvFileTypes =
    [
        new("Cards and Documents CSV", ["*.csv"])
    ];
    private static readonly PlatformFilePickerFileType[] AegisJsonFileTypes =
    [
        new("Aegis JSON", ["*.json"])
    ];

    private enum QuickAccessSort
    {
        Recent,
        Frequent
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

    private static readonly string[] KnownTwoFactorDomains =
    [
        "google.com",
        "gmail.com",
        "github.com",
        "microsoft.com",
        "apple.com",
        "amazon.com",
        "paypal.com",
        "dropbox.com",
        "facebook.com",
        "instagram.com",
        "linkedin.com",
        "reddit.com",
        "slack.com",
        "discord.com",
        "x.com",
        "twitter.com",
        "icloud.com",
        "outlook.com",
        "twitch.tv",
        "steam.com"
    ];

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
    private readonly ITotpEditorDialogService _totpEditorDialogService;
    private readonly IWalletItemEditorDialogService _walletItemEditorDialogService;
    private readonly IMasterPasswordMaintenanceService _masterPasswordMaintenanceService;
    private readonly IVaultCredentialStore _credentialStore;
    private readonly ILegacyVaultDetector _legacyVaultDetector;
    private readonly IVaultUnlockCoordinator _vaultUnlockCoordinator;
    private readonly IAppSettingsService _settingsService;
    private readonly ILocalizationService _localization;
    private readonly IReadOnlyList<PlatformCapability> _sourceCapabilities;
    private readonly IReadOnlyList<PlatformIntegrationCapability> _sourcePlatformIntegrationCapabilities;
    private readonly IExternalLinkService _externalLinkService;
    private readonly IFileSystemPickerService _fileSystemPickerService;
    private readonly SecurityQuestionService _securityQuestionService = new();
    private IReadOnlyDictionary<long, IReadOnlyList<CustomField>> _passwordCustomFields = new Dictionary<long, IReadOnlyList<CustomField>>();
    private IReadOnlyDictionary<long, IReadOnlyList<Attachment>> _passwordAttachments = new Dictionary<long, IReadOnlyList<Attachment>>();
    private IReadOnlyDictionary<long, PasswordQuickAccessRecord> _passwordQuickAccessRecords = new Dictionary<long, PasswordQuickAccessRecord>();
    private IReadOnlyDictionary<long, CompromisedPasswordResult> _compromisedPasswordResults = new Dictionary<long, CompromisedPasswordResult>();
    private bool _hasCompromisedPasswordCheckResults;
    private bool _isApplyingSettings;
    private LegacyVaultDetection _legacyVaultDetection = LegacyVaultDetection.Empty;

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
        ITotpEditorDialogService? totpEditorDialogService = null,
        IWalletItemEditorDialogService? walletItemEditorDialogService = null,
        IMasterPasswordMaintenanceService? masterPasswordMaintenanceService = null,
        IVaultUnlockCoordinator? vaultUnlockCoordinator = null,
        IExternalLinkService? externalLinkService = null,
        IFileSystemPickerService? fileSystemPickerService = null)
    {
        _repository = repository;
        _credentialStore = credentialStore;
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
        _totpEditorDialogService = totpEditorDialogService ?? new DisabledTotpEditorDialogService();
        _walletItemEditorDialogService = walletItemEditorDialogService ?? new DisabledWalletItemEditorDialogService();
        _masterPasswordMaintenanceService = masterPasswordMaintenanceService ?? new DisabledMasterPasswordMaintenanceService();
        _legacyVaultDetector = legacyVaultDetector ?? new NoLegacyVaultDetector();
        _vaultUnlockCoordinator = vaultUnlockCoordinator ?? new VaultUnlockCoordinator(_credentialStore, _cryptoService, _legacyVaultDetector);
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
    }

    public ILocalizationService L => _localization;
    public ObservableCollection<PasswordEntry> Passwords { get; } = [];
    public ObservableCollection<PasswordEntry> ArchivedPasswords { get; } = [];
    public ObservableCollection<PasswordEntry> DeletedPasswords { get; } = [];
    public ObservableCollection<SecureItem> NoteItems { get; } = [];
    public ObservableCollection<SecureItem> TotpItems { get; } = [];
    public ObservableCollection<SecureItem> WalletItems { get; } = [];
    public ObservableCollection<Category> Categories { get; } = [];
    public ObservableCollection<LocalizedPlatformIntegrationCapability> PlatformIntegrationCapabilities { get; } = [];
    public ObservableCollection<LocalizedPlatformCapability> Capabilities { get; } = [];
    public ObservableCollection<LocalMdbxDatabase> MdbxDatabases { get; } = [];
    public ObservableCollection<MdbxDatabaseDisplayItem> MdbxDatabaseItems { get; } = [];
    public ObservableCollection<TimelineEntry> TimelineEntries { get; } = [];
    public ObservableCollection<SecuritySummaryItem> SecuritySummaryItems { get; } = [];
    public ObservableCollection<SecurityIssueItem> SecurityIssueItems { get; } = [];
    public ObservableCollection<VaultSourceDisplayItem> VaultSources { get; } = [];
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

    [ObservableProperty]
    private bool _isUnlocked;

    [ObservableProperty]
    private bool _isVaultInitialized;

    public string PlatformName { get; }
    public string PlatformIntegrationsTitle => _localization.Get("PlatformIntegrations");
    public string PlatformIntegrationSummaryText => _localization.Format(
        "PlatformIntegrationsDescriptionFormat",
        PlatformName,
        PlatformIntegrationCapabilities.Count(item => item.IsUsable),
        PlatformIntegrationCapabilities.Count);
    public bool CanUseTrayIntegration => IsPlatformIntegrationUsable(PlatformFeatureKeys.Tray);
    public bool CanUseGlobalHotkeyIntegration => IsPlatformIntegrationUsable(PlatformFeatureKeys.GlobalHotkey);
    public bool CanUseBrowserBridgeIntegration => IsPlatformIntegrationUsable(PlatformFeatureKeys.BrowserBridge);
    public bool CanOpenExternalLinks => IsPlatformIntegrationUsable(PlatformFeatureKeys.ExternalLinks);
    public bool CanUseFilePicker => IsPlatformIntegrationUsable(PlatformFeatureKeys.FilePicker);
    public string TrayIntegrationStatusText => FormatPlatformIntegrationStatus(PlatformFeatureKeys.Tray);
    public string GlobalHotkeyIntegrationStatusText => FormatPlatformIntegrationStatus(PlatformFeatureKeys.GlobalHotkey);
    public string BrowserBridgeIntegrationStatusText => FormatPlatformIntegrationStatus(PlatformFeatureKeys.BrowserBridge);
    public string ExternalLinksIntegrationStatusText => FormatPlatformIntegrationStatus(PlatformFeatureKeys.ExternalLinks);
    public string FilePickerIntegrationStatusText => FormatPlatformIntegrationStatus(PlatformFeatureKeys.FilePicker);
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
    private string _masterPassword = "";

    [ObservableProperty]
    private string _confirmMasterPassword = "";

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
    private string _statusMessage = "Locked";

    [ObservableProperty]
    private string _generatedPassword = "";

    [ObservableProperty]
    private int _generatorLength = 24;

    [ObservableProperty]
    private bool _generatorIncludeUppercase = true;

    [ObservableProperty]
    private bool _generatorIncludeLowercase = true;

    [ObservableProperty]
    private bool _generatorIncludeNumbers = true;

    [ObservableProperty]
    private bool _generatorIncludeSymbols = true;

    [ObservableProperty]
    private string _exportPreview = "";

    [ObservableProperty]
    private string _importJsonText = "";

    [ObservableProperty]
    private string _importAegisJsonText = "";

    [ObservableProperty]
    private string _importTotpCsvText = "";

    [ObservableProperty]
    private string _importNoteCsvText = "";

    [ObservableProperty]
    private string _exportCsvPreview = "";

    [ObservableProperty]
    private string _exportTotpCsvPreview = "";

    [ObservableProperty]
    private string _exportNoteCsvPreview = "";

    [ObservableProperty]
    private string _exportWalletCsvPreview = "";

    [ObservableProperty]
    private string _exportAegisPreview = "";

    [ObservableProperty]
    private string _importCsvText = "";

    [ObservableProperty]
    private string _newFolderName = "";

    [ObservableProperty]
    private PasswordFolderFilterChoice? _selectedPasswordFolderFilter;

    [ObservableProperty]
    private string _selectedPasswordSort = "updated-desc";

    [ObservableProperty]
    private bool _isCheckingCompromisedPasswords;

    [ObservableProperty]
    private string _compromisedPasswordStatus = "";

    [ObservableProperty]
    private SecureItem? _selectedNote;

    [ObservableProperty]
    private string _noteTitle = "";

    [ObservableProperty]
    private string _noteContent = "";

    [ObservableProperty]
    private string _noteTagsText = "";

    [ObservableProperty]
    private bool _noteIsMarkdown = true;

    [ObservableProperty]
    private bool _notePreviewMode;

    [ObservableProperty]
    private bool _noteIsFavorite;

    [ObservableProperty]
    private SecureItem? _selectedWalletItem;

    [ObservableProperty]
    private WalletItemDetailsViewModel? _selectedWalletDetails;

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
    private bool _minimizeToTray;

    [ObservableProperty]
    private bool _quickSearchEnabled = true;

    [ObservableProperty]
    private string _quickSearchHotkey = "Ctrl+Shift+Space";

    [ObservableProperty]
    private bool _browserIntegrationEnabled;

    [ObservableProperty]
    private int _browserIntegrationPort = 49152;

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
    public string LoginTitle => IsVaultInitialized ? _localization.UnlockMonica : _localization.CreateMonicaVault;
    public string LoginDescription => IsVaultInitialized
        ? _localization.UnlockDescription
        : _localization.CreateVaultDescription;
    public string LoginButtonText => IsVaultInitialized ? _localization.Unlock : _localization.CreateVault;

    public string PasswordCountText => _localization.Format("PasswordCountFormat", Passwords.Count);
    public string ArchivedPasswordCountText => _localization.Format("ArchivedPasswordCountFormat", ArchivedPasswords.Count);
    public string DeletedPasswordCountText => _localization.Format("DeletedPasswordCountFormat", DeletedPasswords.Count);
    public string SelectedPasswordCountText => _localization.Format("SelectedPasswordCountFormat", SelectedPasswordCount);
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
    public string TotpCountText => _localization.Format("TotpCountFormat", TotpItems.Count);
    public string WalletCountText => _localization.Format("WalletCountFormat", WalletItems.Count);
    public string TimelineCountText => _localization.Format("TimelineCountFormat", TimelineEntries.Count);
    public string SecurityIssueCountText => _localization.Format("SecurityIssueCountFormat", SecurityIssueItems.Count);
    public string LocalDatabaseSummaryText => _localization.Format("DatabaseSummaryFormat", Passwords.Count, NoteItems.Count, TotpItems.Count, WalletItems.Count);
    public string MdbxDatabaseCountText => _localization.Format("MdbxDatabaseCountFormat", MdbxDatabases.Count);
    public string MdbxLocalCountText => _localization.Format("MdbxSourceCountFormat", MdbxLocalDatabaseCount);
    public string MdbxWebDavCountText => _localization.Format("MdbxSourceCountFormat", MdbxWebDavDatabaseCount);
    public string MdbxOneDriveCountText => _localization.Format("MdbxSourceCountFormat", MdbxOneDriveDatabaseCount);
    public int MdbxLocalDatabaseCount => MdbxDatabases.Count(IsLocalMdbxDatabase);
    public int MdbxWebDavDatabaseCount => MdbxDatabases.Count(item => item.StorageLocation == MdbxStorageLocation.RemoteWebDav);
    public int MdbxOneDriveDatabaseCount => MdbxDatabases.Count(item => item.StorageLocation == MdbxStorageLocation.RemoteOneDrive);
    public bool HasMdbxDatabases => MdbxDatabases.Count > 0;
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
    public string WebDavBackupHistoryCountText => _localization.Format("WebDavBackupHistoryCountFormat", WebDavBackupHistory.Count);
    public bool HasWebDavBackupHistory => WebDavBackupHistory.Count > 0;
    public bool IsWebDavBusy => IsLoadingWebDavBackups || IsRunningWebDavBackup;
    public string WebDavBackupOptionsSummaryText => _localization.Format(
        "WebDavBackupOptionsSummaryFormat",
        CountSelectedWebDavBackupOptions(),
        WebDavBackupEncryptionEnabled ? _localization.Get("Encrypted") : _localization.Get("PlainJson"));
    public bool HasLegacyVaultImportPrompt => _legacyVaultDetection.RequiresImport;
    public string LegacyVaultImportPromptText => _legacyVaultDetection.RequiresImport
        ? _localization.Format("LegacyVaultImportPromptFormat", _legacyVaultDetection.DatabasePath)
        : "";
    public string GeneratorLengthText => _localization.Format("GeneratorLengthFormat", GeneratorLength);
    public string GeneratedPasswordStrengthText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(GeneratedPassword))
            {
                return _localization.Get("GeneratorNoPassword");
            }

            var strength = _passwordGenerator.Analyze(GeneratedPassword);
            return _localization.Format("GeneratedPasswordStrengthFormat", strength.Label, strength.Score, string.Join(" ", strength.Warnings));
        }
    }

    public string NotePreviewMarkdown => NoteIsMarkdown ? NoteContent : "";
    public string NotePlainPreview => NoteContentCodec.ToPlainPreview(NoteContent, NoteIsMarkdown);
    public int SelectedPasswordCount => Passwords.Count(item => item.IsSelected);
    public bool HasSelectedPasswords => SelectedPasswordCount > 0;
    public int SelectedTotpCount => TotpItems.Count(item => item.IsSelected);
    public string SelectedTotpCountText => _localization.Format("SelectedTotpCountFormat", SelectedTotpCount);
    public bool HasSelectedTotpItems => SelectedTotpCount > 0;
    public int SelectedWalletCount => WalletItems.Count(item => item.IsSelected);
    public string SelectedWalletCountText => _localization.Format("SelectedWalletCountFormat", SelectedWalletCount);
    public bool HasSelectedWalletItems => SelectedWalletCount > 0;
    public bool HasSelectedWalletItem => SelectedWalletItem is not null;
    public bool AreAllFilteredPasswordsSelected
    {
        get
        {
            var filtered = FilteredPasswords.ToArray();
            return filtered.Length > 0 && filtered.All(item => item.IsSelected);
        }
        set
        {
            foreach (var item in FilteredPasswords)
            {
                item.IsSelected = value;
            }

            RaisePasswordSelectionState();
        }
    }

    public IEnumerable<PasswordQuickAccessItem> RecentPasswordQuickAccessItems =>
        BuildQuickAccessItems(QuickAccessSort.Recent);

    public IEnumerable<PasswordQuickAccessItem> FrequentPasswordQuickAccessItems =>
        BuildQuickAccessItems(QuickAccessSort.Frequent);

    public bool HasPasswordQuickAccessItems => RecentPasswordQuickAccessItems.Any() || FrequentPasswordQuickAccessItems.Any();

    public IEnumerable<PasswordEntry> FilteredPasswords =>
        ApplyPasswordSort(Passwords.Where(MatchesPasswordFilters));
    public IEnumerable<PasswordEntry> FilteredArchivedPasswords =>
        ArchivedPasswords.Where(item => MatchesPasswordSearch(item, SearchText));
    public IEnumerable<PasswordEntry> FilteredDeletedPasswords =>
        DeletedPasswords.Where(item => MatchesPasswordSearch(item, SearchText));

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredPasswords));
        OnPropertyChanged(nameof(FilteredArchivedPasswords));
        OnPropertyChanged(nameof(FilteredDeletedPasswords));
        RaisePasswordSelectionState();
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
        RefreshPasswordFilters();
        OnPropertyChanged(nameof(CanManageSelectedPasswordFolder));
    }
    partial void OnSelectedPasswordSortChanged(string value)
    {
        UpdateSettings(settings => settings.PasswordSortOrder = value);
        OnPropertyChanged(nameof(FilteredPasswords));
        RaisePasswordSelectionState();
    }

    partial void OnSelectedSectionChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedSectionTitle));
        RaiseShellStatus();
    }

    partial void OnStatusMessageChanged(string value) => RaiseShellStatus();

    partial void OnGeneratedPasswordChanged(string value) => OnPropertyChanged(nameof(GeneratedPasswordStrengthText));

    partial void OnGeneratorLengthChanged(int value)
    {
        GeneratorLength = Math.Clamp(value, 8, 128);
        OnPropertyChanged(nameof(GeneratorLengthText));
    }

    partial void OnNoteContentChanged(string value)
    {
        OnPropertyChanged(nameof(NotePreviewMarkdown));
        OnPropertyChanged(nameof(NotePlainPreview));
    }

    partial void OnNoteIsMarkdownChanged(bool value)
    {
        OnPropertyChanged(nameof(NotePreviewMarkdown));
        OnPropertyChanged(nameof(NotePlainPreview));
    }

    partial void OnSelectedNoteChanged(SecureItem? value) => LoadNoteIntoEditor(value);

    partial void OnSelectedWalletItemChanged(SecureItem? value)
    {
        SelectedWalletDetails = value is null ? null : new WalletItemDetailsViewModel(_localization, value);
        OnPropertyChanged(nameof(HasSelectedWalletItem));
    }

    partial void OnIsVaultInitializedChanged(bool value)
    {
        OnPropertyChanged(nameof(LoginTitle));
        OnPropertyChanged(nameof(LoginDescription));
        OnPropertyChanged(nameof(LoginButtonText));
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

    partial void OnMinimizeToTrayChanged(bool value)
    {
        if (value && !CanUseTrayIntegration)
        {
            MinimizeToTray = false;
            return;
        }

        UpdateSettings(settings => settings.MinimizeToTray = value);
    }

    partial void OnQuickSearchEnabledChanged(bool value) => UpdateSettings(settings => settings.QuickSearchEnabled = value);
    partial void OnQuickSearchHotkeyChanged(string value) => UpdateSettings(settings => settings.QuickSearchHotkey = value);

    partial void OnBrowserIntegrationEnabledChanged(bool value)
    {
        if (value && !CanUseBrowserBridgeIntegration)
        {
            BrowserIntegrationEnabled = false;
            return;
        }

        UpdateSettings(settings => settings.BrowserIntegrationEnabled = value);
    }

    partial void OnBrowserIntegrationPortChanged(int value) => UpdateSettings(settings => settings.BrowserIntegrationPort = value);
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
        OnPropertyChanged(nameof(WebDavConnectionStatusText));
        RefreshVaultSources();
        RefreshMdbxVaultState();
        RaiseShellStatus();
    }
    partial void OnWebDavServerUrlChanged(string value)
    {
        UpdateSettings(settings => settings.WebDavServerUrl = value);
        OnPropertyChanged(nameof(WebDavConnectionStatusText));
        RefreshVaultSources();
    }
    partial void OnWebDavUsernameChanged(string value)
    {
        UpdateSettings(settings => settings.WebDavUsername = value);
        RefreshVaultSources();
    }
    partial void OnWebDavPasswordChanged(string value) => UpdateSettings(settings => settings.WebDavPassword = value);
    partial void OnWebDavRemotePathChanged(string value)
    {
        UpdateSettings(settings => settings.WebDavRemotePath = value);
        RefreshVaultSources();
    }
    partial void OnWebDavSyncOnStartupChanged(bool value)
    {
        UpdateSettings(settings => settings.WebDavSyncOnStartup = value);
        RefreshVaultSources();
    }
    partial void OnWebDavSyncAfterChangesChanged(bool value)
    {
        UpdateSettings(settings => settings.WebDavSyncAfterChanges = value);
        RefreshVaultSources();
    }
    partial void OnIsLoadingWebDavBackupsChanged(bool value) => OnPropertyChanged(nameof(IsWebDavBusy));
    partial void OnIsRunningWebDavBackupChanged(bool value) => OnPropertyChanged(nameof(IsWebDavBusy));
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
        RefreshVaultSources();
    }
    partial void OnOneDriveEnabledChanged(bool value)
    {
        UpdateSettings(settings => settings.OneDriveEnabled = value);
        RefreshMdbxVaultState();
    }

    partial void OnMdbxLocalCacheEnabledChanged(bool value)
    {
        UpdateSettings(settings => settings.MdbxLocalCacheEnabled = value);
        RefreshMdbxVaultState();
    }

    private void RaiseShellStatus()
    {
        OnPropertyChanged(nameof(ShellVaultText));
        OnPropertyChanged(nameof(ShellSyncText));
        OnPropertyChanged(nameof(ShellPageText));
        OnPropertyChanged(nameof(ShellPlatformText));
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        try
        {
            await _settingsService.LoadAsync();
            ApplySettings(_settingsService.Current);
            var initialization = await _vaultUnlockCoordinator.InitializeAsync();
            _legacyVaultDetection = initialization.LegacyVaultDetection;
            RaiseLegacyVaultImportPrompt();
            if (_legacyVaultDetection.RequiresImport)
            {
                IsVaultInitialized = false;
                StatusMessage = _localization.Get("LegacyVaultImportRequired");
                return;
            }

            IsVaultInitialized = initialization.IsVaultInitialized;
            StatusMessage = IsVaultInitialized
                ? _localization.Get("VaultLocked")
                : _localization.Get("FirstRunCreateMasterPassword");
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("VaultMetadataLoadFailedFormat", ex.Message);
        }
    }

    [RelayCommand]
    private async Task UnlockAsync()
    {
        var result = await _vaultUnlockCoordinator.UnlockOrCreateAsync(
            MasterPassword,
            ConfirmMasterPassword,
            _legacyVaultDetection);

        switch (result.Status)
        {
            case VaultUnlockStatus.MissingPassword:
            case VaultUnlockStatus.LegacyImportRequired:
            case VaultUnlockStatus.PasswordTooShort:
            case VaultUnlockStatus.ConfirmationMismatch:
                IsVaultInitialized = result.IsVaultInitialized;
                StatusMessage = _localization.Get(result.MessageKey);
                return;
            case VaultUnlockStatus.WrongPassword:
                IsVaultInitialized = result.IsVaultInitialized;
                IsUnlocked = false;
                StatusMessage = _localization.Get(result.MessageKey);
                MasterPassword = "";
                ConfirmMasterPassword = "";
                return;
            case VaultUnlockStatus.Failed:
                IsVaultInitialized = result.IsVaultInitialized;
                IsUnlocked = false;
                StatusMessage = _localization.Format(result.MessageKey, result.Error?.Message ?? "");
                return;
            case VaultUnlockStatus.CreatedAndUnlocked:
            case VaultUnlockStatus.Unlocked:
                IsVaultInitialized = result.IsVaultInitialized;
                IsUnlocked = true;
                MasterPassword = "";
                ConfirmMasterPassword = "";
                StatusMessage = _localization.Get(result.MessageKey);
                await LoadAsync();
                return;
            default:
                IsUnlocked = false;
                StatusMessage = _localization.Format("UnlockFailedFormat", result.Status.ToString());
                return;
        }
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
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

            var allPasswords = await _repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true);
            var activePasswords = allPasswords.Where(item => !item.IsDeleted && !item.IsArchived).ToArray();
            var archivedPasswords = allPasswords.Where(item => !item.IsDeleted && item.IsArchived).ToArray();
            var deletedPasswords = allPasswords.Where(item => item.IsDeleted).ToArray();
            _passwordCustomFields = await _repository.GetCustomFieldsByEntryIdsAsync(allPasswords.Select(item => item.Id).ToArray());
            _passwordAttachments = await _repository.GetAttachmentsByOwnerIdsAsync("PASSWORD", allPasswords.Select(item => item.Id).ToArray());
            foreach (var item in activePasswords)
            {
                RefreshPasswordTotpDisplay(item);
                RefreshPasswordAttachmentState(item);
                item.IsSelected = false;
                TrackPasswordSelection(item);
                Passwords.Add(item);
            }

            foreach (var item in archivedPasswords)
            {
                RefreshPasswordTotpDisplay(item);
                RefreshPasswordAttachmentState(item);
                item.IsSelected = false;
                TrackPasswordSelection(item);
                ArchivedPasswords.Add(item);
            }

            foreach (var item in deletedPasswords)
            {
                RefreshPasswordTotpDisplay(item);
                RefreshPasswordAttachmentState(item);
                item.IsSelected = false;
                TrackPasswordSelection(item);
                DeletedPasswords.Add(item);
            }

            foreach (var item in await _repository.GetSecureItemsAsync(VaultItemType.Note))
            {
                NoteItems.Add(item);
            }

            foreach (var item in await _repository.GetSecureItemsAsync())
            {
                if (item.ItemType is VaultItemType.BankCard or VaultItemType.Document)
                {
                    item.IsSelected = false;
                    TrackWalletSelection(item);
                    WalletItems.Add(item);
                }
            }

            foreach (var category in await _repository.GetCategoriesAsync())
            {
                Categories.Add(category);
            }

            RefreshPasswordFolderFilters();
            await LoadPasswordQuickAccessAsync();
            foreach (var database in await _repository.GetMdbxDatabasesAsync())
            {
                MdbxDatabases.Add(database);
            }

            RefreshMdbxVaultState();
            RefreshVaultSources();
            await LoadTotpItemsAsync();
            await LoadTimelineAsync();
            RefreshSecurityAnalysis();
            RaiseCounts();
            OnPropertyChanged(nameof(FilteredPasswords));
        }
        catch (Exception ex)
        {
            IsUnlocked = false;
            StatusMessage = _localization.Format("VaultLoadFailedFormat", ex.Message);
        }
    }

    [RelayCommand]
    private void SelectSection(string? section)
    {
        if (!string.IsNullOrWhiteSpace(section))
        {
            SelectedSection = section;
        }
    }

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
    private async Task AddPasswordAsync()
    {
        var initialPassword = string.IsNullOrWhiteSpace(GeneratedPassword) ? "" : GeneratedPassword;
        var editor = await _passwordEditorDialogService.ShowAsync(
            null,
            Categories.ToList(),
            initialPassword,
            notes: NoteItems.ToList());
        if (editor is null)
        {
            return;
        }

        var entries = editor
            .BuildEntries(ProtectPasswords(editor.GetPasswordRows()))
            .ToList();
        foreach (var entry in entries)
        {
            await _repository.SavePasswordAsync(entry);
            await _repository.LogAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemId = entry.Id,
                ItemTitle = entry.Title,
                OperationType = "CREATE",
                DeviceName = Environment.MachineName
            });
        }

        var customFields = BindCustomFields(entries[0].Id, editor.GetCustomFields());
        await _repository.ReplaceCustomFieldsAsync(entries[0].Id, customFields);
        SetPasswordCustomFields(entries[0].Id, customFields);
        foreach (var entry in entries)
        {
            RefreshPasswordTotpDisplay(entry);
        }

        await SynchronizeBoundTotpAsync(entries[0]);
        ReplacePasswordGroup([], entries);
        await LoadTotpItemsAsync();
        await LoadTimelineAsync();
        RefreshSecurityAnalysis();
        RaiseCounts();
        OnPropertyChanged(nameof(FilteredPasswords));
        StatusMessage = _localization.Format("CreatedPasswordFormat", entries[0].Title);
    }

    [RelayCommand]
    private async Task EditPasswordAsync(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        var siblings = GetPasswordSiblings(entry).ToList();
        var customFields = await GetGroupCustomFieldsAsync(entry, siblings);
        var editor = await _passwordEditorDialogService.ShowAsync(
            entry,
            Categories.ToList(),
            UnprotectPassword(entry.Password),
            siblings.Select(item => UnprotectPassword(item.Password)).ToArray(),
            NoteItems.ToList(),
            customFields);
        if (editor is null)
        {
            return;
        }

        var passwordRows = editor.GetPasswordRows();
        var storedPasswords = ProtectPasswords(passwordRows);
        var updatedEntries = new List<PasswordEntry>();
        for (var index = 0; index < storedPasswords.Count; index++)
        {
            var source = index < siblings.Count ? siblings[index] : null;
            var oldPlainPassword = source is null ? "" : UnprotectPassword(source.Password);
            var updated = editor.BuildEntryFrom(source, storedPasswords[index]);
            await _repository.SavePasswordAsync(updated);
            await SavePasswordHistorySnapshotIfChangedAsync(updated.Id, oldPlainPassword, passwordRows[index]);
            await _repository.LogAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemId = updated.Id,
                ItemTitle = updated.Title,
                OperationType = source is null ? "CREATE" : "UPDATE",
                DeviceName = Environment.MachineName
            });
            updatedEntries.Add(updated);
        }

        foreach (var removed in siblings.Skip(storedPasswords.Count))
        {
            await _repository.SoftDeletePasswordAsync(removed.Id);
        }

        var updatedCustomFields = BindCustomFields(updatedEntries[0].Id, editor.GetCustomFields());
        await _repository.ReplaceCustomFieldsAsync(updatedEntries[0].Id, updatedCustomFields);
        SetPasswordCustomFields(updatedEntries[0].Id, updatedCustomFields);
        foreach (var updated in updatedEntries)
        {
            RefreshPasswordTotpDisplay(updated);
        }

        await SynchronizeBoundTotpAsync(updatedEntries[0]);
        ReplacePasswordGroup(siblings, updatedEntries);
        await LoadTotpItemsAsync();
        await LoadTimelineAsync();
        RefreshSecurityAnalysis();
        RaiseCounts();
        OnPropertyChanged(nameof(FilteredPasswords));
        StatusMessage = _localization.Format("UpdatedPasswordFormat", updatedEntries[0].Title);
    }

    [RelayCommand]
    private async Task CopyPasswordAsync(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        var text = entry.Password;
        if (_cryptoService.IsUnlocked)
        {
            try
            {
                text = _cryptoService.DecryptString(entry.Password);
            }
            catch
            {
                text = entry.Password;
            }
        }

        await _clipboardService.SetTextAsync(text);
        StatusMessage = _localization.Format("CopiedPasswordFormat", entry.Title);
    }

    [RelayCommand]
    private async Task CopyUsernameAsync(PasswordEntry? entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Username))
        {
            return;
        }

        await _clipboardService.SetTextAsync(entry.Username);
        StatusMessage = _localization.Format("CopiedUsernameFormat", entry.Title);
    }

    [RelayCommand]
    private async Task CopyWebsiteAsync(PasswordEntry? entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Website))
        {
            return;
        }

        await _clipboardService.SetTextAsync(entry.Website);
        StatusMessage = _localization.Format("CopiedWebsiteFormat", entry.Title);
    }

    [RelayCommand]
    private async Task ShowPasswordDetailsAsync(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        await RecordPasswordQuickAccessAsync(entry);
        var siblings = entry.IsDeleted
            ? GetDeletedPasswordSiblings(entry).ToList()
            : entry.IsArchived
                ? GetArchivedPasswordSiblings(entry).ToList()
                : GetPasswordSiblings(entry).ToList();
        var customFields = await GetGroupCustomFieldsAsync(entry, siblings);
        var category = entry.CategoryId is null
            ? null
            : Categories.FirstOrDefault(item => item.Id == entry.CategoryId);
        var boundNote = entry.BoundNoteId is null
            ? null
            : NoteItems.FirstOrDefault(item => item.Id == entry.BoundNoteId);
        var attachments = GetGroupAttachments(entry, siblings);
        var history = await GetPasswordHistoryDisplayItemsAsync(entry.Id);

        await _passwordDetailDialogService.ShowAsync(
            entry,
            siblings,
            category,
            boundNote,
            attachments,
            customFields,
            history,
            AddPasswordAttachmentAsync,
            DeletePasswordAttachmentAsync,
            DeletePasswordHistoryAsync,
            ClearPasswordHistoryAsync);
    }

    [RelayCommand]
    private async Task OpenQuickAccessPasswordAsync(PasswordQuickAccessItem? item)
    {
        if (item is null)
        {
            return;
        }

        await ShowPasswordDetailsAsync(item.Entry);
    }

    [RelayCommand]
    private void TogglePasswordSelection(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        entry.IsSelected = !entry.IsSelected;
        RaisePasswordSelectionState();
    }

    [RelayCommand]
    private void ClearPasswordSelection()
    {
        foreach (var entry in Passwords.Where(item => item.IsSelected))
        {
            entry.IsSelected = false;
        }

        RaisePasswordSelectionState();
    }

    [RelayCommand]
    private async Task FavoriteSelectedPasswordsAsync()
    {
        var selected = Passwords.Where(item => item.IsSelected).ToArray();
        foreach (var entry in selected)
        {
            if (!entry.IsFavorite)
            {
                entry.IsFavorite = true;
                await _repository.SavePasswordAsync(entry);
                await _repository.LogAsync(new OperationLog
                {
                    ItemType = "PASSWORD",
                    ItemId = entry.Id,
                    ItemTitle = entry.Title,
                    OperationType = "FAVORITE",
                    DeviceName = Environment.MachineName
                });
            }
        }

        foreach (var entry in selected)
        {
            entry.IsSelected = false;
        }

        RaisePasswordSelectionState();
        OnPropertyChanged(nameof(FilteredPasswords));
        RefreshSecurityAnalysis();
        await LoadTimelineAsync();
        StatusMessage = _localization.Format("FavoritedPasswordCountFormat", selected.Length);
    }

    [RelayCommand]
    private async Task DeleteSelectedPasswordsAsync()
    {
        var selected = Passwords.Where(item => item.IsSelected).ToArray();
        var handled = new HashSet<long>();
        foreach (var entry in selected)
        {
            if (!handled.Add(entry.Id))
            {
                continue;
            }

            var siblings = GetPasswordSiblings(entry).ToArray();
            foreach (var sibling in siblings)
            {
                handled.Add(sibling.Id);
            }

            await DeletePasswordGroupAsync(entry, siblings, updateStatus: false);
        }

        RaisePasswordSelectionState();
        await LoadTimelineAsync();
        StatusMessage = _localization.Format("MovedSelectedPasswordsToRecycleBinFormat", selected.Length);
    }

    [RelayCommand]
    private async Task ArchiveSelectedPasswordsAsync()
    {
        var selected = Passwords.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        var handled = new HashSet<long>();
        foreach (var entry in selected)
        {
            if (!handled.Add(entry.Id))
            {
                continue;
            }

            var siblings = GetPasswordSiblings(entry).ToArray();
            foreach (var sibling in siblings)
            {
                handled.Add(sibling.Id);
            }

            await ArchivePasswordGroupAsync(entry, siblings, updateStatus: false);
        }

        RaisePasswordSelectionState();
        await LoadTimelineAsync();
        StatusMessage = _localization.Format("ArchivedSelectedPasswordsFormat", selected.Length);
    }

    [RelayCommand]
    private async Task MoveSelectedPasswordsToCategoryAsync()
    {
        var selected = Passwords.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        var currentCategoryId = selected
            .Select(item => item.CategoryId)
            .Distinct()
            .Count() == 1
                ? selected[0].CategoryId
                : null;
        var choice = await _categoryPickerDialogService.ShowAsync(Categories.ToList(), currentCategoryId);
        if (choice is null)
        {
            return;
        }

        var handled = new HashSet<long>();
        foreach (var entry in selected)
        {
            if (!handled.Add(entry.Id))
            {
                continue;
            }

            var siblings = GetPasswordSiblings(entry).ToArray();
            foreach (var sibling in siblings)
            {
                handled.Add(sibling.Id);
                sibling.CategoryId = choice.Id;
                await _repository.SavePasswordAsync(sibling);
                await SynchronizeBoundTotpAsync(sibling);
                await _repository.LogAsync(new OperationLog
                {
                    ItemType = "PASSWORD",
                    ItemId = sibling.Id,
                    ItemTitle = sibling.Title,
                    OperationType = "MOVE_CATEGORY",
                    DeviceName = Environment.MachineName
                });
            }
        }

        foreach (var entry in selected)
        {
            entry.IsSelected = false;
        }

        await LoadTotpItemsAsync();
        RaisePasswordSelectionState();
        RefreshPasswordFolderFilters(choice.Id);
        OnPropertyChanged(nameof(FilteredPasswords));
        await LoadTimelineAsync();
        StatusMessage = _localization.Format("MovedSelectedPasswordsToFolderFormat", selected.Length, choice.Name);
    }

    [RelayCommand]
    private async Task StackSelectedPasswordsAsync()
    {
        var selected = Passwords
            .Where(item => item.IsSelected)
            .OrderBy(item => item.Id == 0 ? long.MaxValue : item.Id)
            .ToArray();
        if (selected.Length < 2)
        {
            return;
        }

        var replicaGroupId = selected
            .Select(item => item.ReplicaGroupId)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? $"manual-{Guid.NewGuid():N}";
        foreach (var entry in selected)
        {
            entry.ReplicaGroupId = replicaGroupId;
            entry.IsSelected = false;
            await _repository.SavePasswordAsync(entry);
            await _repository.LogAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemId = entry.Id,
                ItemTitle = entry.Title,
                OperationType = "STACK",
                DeviceName = Environment.MachineName
            });
        }

        RaisePasswordSelectionState();
        OnPropertyChanged(nameof(FilteredPasswords));
        RefreshSecurityAnalysis();
        await LoadTimelineAsync();
        StatusMessage = _localization.Format("StackedPasswordCountFormat", selected.Length);
    }

    [RelayCommand]
    private async Task CopyPasswordTotpAsync(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        RefreshPasswordTotpDisplay(entry);
        await _clipboardService.SetTextAsync(entry.TotpCode);
        StatusMessage = _localization.Format("CopiedTotpFormat", entry.Title);
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        entry.IsFavorite = !entry.IsFavorite;
        await _repository.SavePasswordAsync(entry);
        await _repository.LogAsync(new OperationLog
        {
            ItemType = "PASSWORD",
            ItemId = entry.Id,
            ItemTitle = entry.Title,
            OperationType = "FAVORITE",
            DeviceName = Environment.MachineName
        });
        await LoadTimelineAsync();
        RefreshSecurityAnalysis();
        OnPropertyChanged(nameof(FilteredPasswords));
    }

    [RelayCommand]
    private async Task DeletePasswordAsync(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        var siblings = entry.IsArchived
            ? GetArchivedPasswordSiblings(entry).ToList()
            : GetPasswordSiblings(entry).ToList();
        await DeletePasswordGroupAsync(entry, siblings, updateStatus: true);
    }

    [RelayCommand]
    private async Task ArchivePasswordAsync(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        var siblings = GetPasswordSiblings(entry).ToList();
        await ArchivePasswordGroupAsync(entry, siblings, updateStatus: true);
    }

    private async Task ArchivePasswordGroupAsync(PasswordEntry entry, IReadOnlyList<PasswordEntry> siblings, bool updateStatus)
    {
        foreach (var item in siblings)
        {
            item.IsArchived = true;
            item.ArchivedAt = DateTimeOffset.UtcNow;
            item.IsSelected = false;
            await _repository.SavePasswordAsync(item);
            await _repository.LogAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemId = item.Id,
                ItemTitle = item.Title,
                OperationType = "ARCHIVE",
                DeviceName = Environment.MachineName
            });
            Passwords.Remove(item);
            var current = Passwords.FirstOrDefault(password => password.Id == item.Id);
            if (current is not null)
            {
                current.IsSelected = false;
                Passwords.Remove(current);
            }

            TrackPasswordSelection(item);
            ArchivedPasswords.Insert(0, item);
        }

        await LoadTotpItemsAsync();
        RaiseCounts();
        RaisePasswordSelectionState();
        OnPropertyChanged(nameof(FilteredPasswords));
        RefreshSecurityAnalysis();
        await LoadTimelineAsync();
        if (updateStatus)
        {
            StatusMessage = _localization.Format("ArchivedPasswordFormat", entry.Title);
        }
    }

    [RelayCommand]
    private async Task UnarchivePasswordAsync(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        var siblings = GetArchivedPasswordSiblings(entry).ToList();
        foreach (var item in siblings)
        {
            item.IsArchived = false;
            item.ArchivedAt = null;
            item.IsSelected = false;
            await _repository.SavePasswordAsync(item);
            await _repository.LogAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemId = item.Id,
                ItemTitle = item.Title,
                OperationType = "UNARCHIVE",
                DeviceName = Environment.MachineName
            });
            ArchivedPasswords.Remove(item);
            var current = ArchivedPasswords.FirstOrDefault(password => password.Id == item.Id);
            if (current is not null)
            {
                ArchivedPasswords.Remove(current);
            }

            RefreshPasswordTotpDisplay(item);
            RefreshPasswordAttachmentState(item);
        }

        ReplacePasswordGroup([], siblings);
        await LoadTotpItemsAsync();
        await LoadTimelineAsync();
        RaiseCounts();
        OnPropertyChanged(nameof(FilteredPasswords));
        RefreshSecurityAnalysis();
        StatusMessage = _localization.Format("UnarchivedPasswordFormat", entry.Title);
    }

    private async Task DeletePasswordGroupAsync(PasswordEntry entry, IReadOnlyList<PasswordEntry> siblings, bool updateStatus)
    {
        foreach (var item in siblings)
        {
            await _repository.SoftDeletePasswordAsync(item.Id);
            await _repository.LogAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemId = item.Id,
                ItemTitle = item.Title,
                OperationType = "DELETE",
                DeviceName = Environment.MachineName
            });
            item.IsSelected = false;
            Passwords.Remove(item);
            var current = Passwords.FirstOrDefault(password => password.Id == item.Id);
            if (current is not null)
            {
                current.IsSelected = false;
                Passwords.Remove(current);
            }

            ArchivedPasswords.Remove(item);
            var archived = ArchivedPasswords.FirstOrDefault(password => password.Id == item.Id);
            if (archived is not null)
            {
                archived.IsSelected = false;
                ArchivedPasswords.Remove(archived);
            }

            item.IsDeleted = true;
            item.DeletedAt = DateTimeOffset.UtcNow;
            item.IsArchived = false;
            item.ArchivedAt = null;
            TrackPasswordSelection(item);
            DeletedPasswords.Insert(0, item);
        }

        await LoadTotpItemsAsync();
        RaiseCounts();
        RaisePasswordSelectionState();
        OnPropertyChanged(nameof(FilteredPasswords));
        RefreshSecurityAnalysis();
        await LoadTimelineAsync();
        if (updateStatus)
        {
            StatusMessage = _localization.Format("MovedToRecycleBinFormat", entry.Title);
        }
    }

    [RelayCommand]
    private async Task RestorePasswordAsync(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        var siblings = GetDeletedPasswordSiblings(entry).ToList();
        foreach (var item in siblings)
        {
            await _repository.RestorePasswordAsync(item.Id);
            await _repository.LogAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemId = item.Id,
                ItemTitle = item.Title,
                OperationType = "RESTORE",
                DeviceName = Environment.MachineName
            });
            DeletedPasswords.Remove(item);
            var current = DeletedPasswords.FirstOrDefault(password => password.Id == item.Id);
            if (current is not null)
            {
                DeletedPasswords.Remove(current);
            }

            item.IsDeleted = false;
            item.DeletedAt = null;
            item.IsSelected = false;
            RefreshPasswordTotpDisplay(item);
        }

        ReplacePasswordGroup([], siblings);
        await LoadTotpItemsAsync();
        await LoadTimelineAsync();
        RaiseCounts();
        OnPropertyChanged(nameof(FilteredPasswords));
        RefreshSecurityAnalysis();
        StatusMessage = _localization.Format("RestoredPasswordFormat", entry.Title);
    }

    [RelayCommand]
    private async Task DeletePasswordPermanentlyAsync(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        var siblings = GetDeletedPasswordSiblings(entry).ToList();
        foreach (var item in siblings)
        {
            await _repository.DeletePasswordPermanentlyAsync(item.Id);
            await _repository.LogAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemId = item.Id,
                ItemTitle = item.Title,
                OperationType = "PURGE",
                DeviceName = Environment.MachineName
            });
            DeletedPasswords.Remove(item);
            var current = DeletedPasswords.FirstOrDefault(password => password.Id == item.Id);
            if (current is not null)
            {
                DeletedPasswords.Remove(current);
            }
        }

        await LoadTotpItemsAsync();
        await LoadTimelineAsync();
        RaiseCounts();
        RefreshSecurityAnalysis();
        StatusMessage = _localization.Format("DeletedPasswordPermanentlyFormat", entry.Title);
    }

    [RelayCommand]
    private async Task AddTotpAsync()
    {
        var editor = await _totpEditorDialogService.ShowAsync(null);
        if (editor is null)
        {
            return;
        }

        var item = editor.ApplyTo();
        RefreshTotpDisplay(item);
        await _repository.SaveSecureItemAsync(item);
        await _repository.LogAsync(new OperationLog
        {
            ItemType = "TOTP",
            ItemId = item.Id,
            ItemTitle = item.Title,
            OperationType = "CREATE",
            DeviceName = Environment.MachineName
        });
        TotpItems.Insert(0, item);
        RaiseCounts();
        await LoadTimelineAsync();
        StatusMessage = _localization.Format("SavedTotpFormat", item.Title);
    }

    [RelayCommand]
    private async Task CopyTotpAsync(SecureItem? item)
    {
        if (item is null)
        {
            return;
        }

        RefreshTotpDisplay(item);
        await _clipboardService.SetTextAsync(item.TotpCode);
        StatusMessage = _localization.Format("CopiedTotpFormat", item.Title);
    }

    [RelayCommand]
    private async Task EditTotpAsync(SecureItem? item)
    {
        if (item is null)
        {
            return;
        }

        var editor = await _totpEditorDialogService.ShowAsync(item);
        if (editor is null)
        {
            return;
        }

        if (item.BoundPasswordId is { } passwordId)
        {
            var password = Passwords.FirstOrDefault(entry => entry.Id == passwordId)
                ?? (await _repository.GetPasswordsAsync()).FirstOrDefault(entry => entry.Id == passwordId);
            if (password is null)
            {
                StatusMessage = _localization.Get("BoundPasswordMissing");
                return;
            }

            password.AuthenticatorKey = editor.ToAuthenticatorKey();
            password.Title = editor.Title.Trim();
            password.Username = editor.AccountName.Trim();
            password.IsFavorite = editor.IsFavorite;
            await _repository.SavePasswordAsync(password);
            await SynchronizeBoundTotpAsync(password);
            RefreshPasswordTotpDisplay(password);
            await LoadTotpItemsAsync();
        }
        else
        {
            editor.ApplyTo(item);
            RefreshTotpDisplay(item);
            await _repository.SaveSecureItemAsync(item);
        }

        await _repository.LogAsync(new OperationLog
        {
            ItemType = "TOTP",
            ItemId = item.Id,
            ItemTitle = editor.Title.Trim(),
            OperationType = "UPDATE",
            DeviceName = Environment.MachineName
        });
        ClearTotpSelection();
        await LoadTimelineAsync();
        StatusMessage = _localization.Format("SavedTotpFormat", editor.Title.Trim());
    }

    [RelayCommand]
    private async Task ToggleTotpFavoriteAsync(SecureItem? item)
    {
        if (item is null)
        {
            return;
        }

        var next = !item.IsFavorite;
        await SetTotpFavoriteAsync(item, next);
        await LoadTimelineAsync();
        StatusMessage = _localization.Format(next ? "FavoritedTotpFormat" : "UnfavoritedTotpFormat", item.Title);
    }

    [RelayCommand]
    private async Task DeleteTotpAsync(SecureItem? item)
    {
        if (item is null)
        {
            return;
        }

        await DeleteTotpItemAsync(item, updateStatus: true);
    }

    [RelayCommand]
    private void ToggleTotpSelection(SecureItem? item)
    {
        if (item is null)
        {
            return;
        }

        item.IsSelected = !item.IsSelected;
        RaiseTotpSelectionState();
    }

    [RelayCommand]
    private void ClearTotpSelection()
    {
        foreach (var item in TotpItems.Where(item => item.IsSelected))
        {
            item.IsSelected = false;
        }

        RaiseTotpSelectionState();
    }

    [RelayCommand]
    private async Task FavoriteSelectedTotpAsync()
    {
        var selected = TotpItems.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        foreach (var item in selected)
        {
            if (!item.IsFavorite)
            {
                await SetTotpFavoriteAsync(item, true);
            }
        }

        foreach (var item in selected)
        {
            item.IsSelected = false;
        }

        RaiseTotpSelectionState();
        await LoadTimelineAsync();
        StatusMessage = _localization.Format("FavoritedTotpCountFormat", selected.Length);
    }

    [RelayCommand]
    private async Task DeleteSelectedTotpAsync()
    {
        var selected = TotpItems.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        foreach (var item in selected)
        {
            await DeleteTotpItemAsync(item, updateStatus: false);
        }

        RaiseTotpSelectionState();
        await LoadTimelineAsync();
        StatusMessage = _localization.Format("MovedSelectedTotpToRecycleBinFormat", selected.Length);
    }

    [RelayCommand]
    private async Task AddWalletItemAsync()
    {
        var editor = await _walletItemEditorDialogService.ShowAsync(null, WalletItems.Count % 2 == 0 ? VaultItemType.BankCard : VaultItemType.Document);
        if (editor is null)
        {
            return;
        }

        var item = editor.ApplyTo();
        await _repository.SaveSecureItemAsync(item);
        await _repository.LogAsync(new OperationLog
        {
            ItemType = "WALLET",
            ItemId = item.Id,
            ItemTitle = item.Title,
            OperationType = "CREATE",
            DeviceName = Environment.MachineName
        });
        TrackWalletSelection(item);
        WalletItems.Insert(0, item);
        SelectedWalletItem = item;
        RaiseCounts();
        await LoadTimelineAsync();
        StatusMessage = _localization.Format("SavedWalletItemFormat", item.Title);
    }

    [RelayCommand]
    private async Task EditWalletItemAsync(SecureItem? item)
    {
        item ??= SelectedWalletItem;
        if (item is null)
        {
            return;
        }

        var editor = await _walletItemEditorDialogService.ShowAsync(item);
        if (editor is null)
        {
            return;
        }

        editor.ApplyTo(item);
        await _repository.SaveSecureItemAsync(item);
        await _repository.LogAsync(new OperationLog
        {
            ItemType = "WALLET",
            ItemId = item.Id,
            ItemTitle = item.Title,
            OperationType = "UPDATE",
            DeviceName = Environment.MachineName
        });
        SelectedWalletItem = item;
        SelectedWalletDetails = new WalletItemDetailsViewModel(_localization, item);
        await LoadTimelineAsync();
        RaiseCounts();
        StatusMessage = _localization.Format("SavedWalletItemFormat", item.Title);
    }

    [RelayCommand]
    private void ShowWalletDetails(SecureItem? item)
    {
        if (item is not null)
        {
            SelectedWalletItem = item;
        }
    }

    [RelayCommand]
    private async Task DeleteWalletItemAsync(SecureItem? item)
    {
        if (item is null)
        {
            return;
        }

        await DeleteWalletItemCoreAsync(item, updateStatus: true);
    }

    [RelayCommand]
    private void ToggleWalletSelection(SecureItem? item)
    {
        if (item is null)
        {
            return;
        }

        item.IsSelected = !item.IsSelected;
        RaiseWalletSelectionState();
    }

    [RelayCommand]
    private void ClearWalletSelection()
    {
        foreach (var item in WalletItems.Where(item => item.IsSelected))
        {
            item.IsSelected = false;
        }

        RaiseWalletSelectionState();
    }

    [RelayCommand]
    private async Task DeleteSelectedWalletItemsAsync()
    {
        var selected = WalletItems.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        foreach (var item in selected)
        {
            await DeleteWalletItemCoreAsync(item, updateStatus: false);
        }

        RaiseWalletSelectionState();
        await LoadTimelineAsync();
        StatusMessage = _localization.Format("MovedSelectedWalletItemsToRecycleBinFormat", selected.Length);
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
        OnPropertyChanged(nameof(FilteredPasswords));
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

    private async Task DeletePasswordAttachmentAsync(Attachment attachment)
    {
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
            OnPropertyChanged(nameof(FilteredPasswords));
        }

        StatusMessage = _localization.Format("DeletedAttachmentFormat", attachment.FileName);
    }

    [RelayCommand]
    private void AddNote()
    {
        SelectedNote = null;
        NoteTitle = "";
        NoteContent = "";
        NoteTagsText = "";
        NoteIsMarkdown = true;
        NotePreviewMode = false;
        NoteIsFavorite = false;
        StatusMessage = _localization.Get("EditingNewSecureNote");
    }

    [RelayCommand]
    private async Task SaveNoteAsync()
    {
        if (string.IsNullOrWhiteSpace(NoteTitle) && string.IsNullOrWhiteSpace(NoteContent))
        {
            StatusMessage = _localization.Get("NoteRequiresContent");
            return;
        }

        var payload = NoteContentCodec.BuildSavePayload(
            NoteTitle,
            NoteContent,
            NoteTagsText,
            NoteIsMarkdown,
            SelectedNote is null ? [] : NoteContentCodec.DecodeImagePaths(SelectedNote.ImagePaths));

        var item = SelectedNote ?? new SecureItem
        {
            ItemType = VaultItemType.Note,
            CreatedAt = DateTimeOffset.UtcNow
        };

        item.Title = payload.Title;
        item.Notes = payload.NotesCache;
        item.ItemData = payload.ItemData;
        item.ImagePaths = payload.ImagePaths;
        item.IsFavorite = NoteIsFavorite;
        item.ItemType = VaultItemType.Note;
        item.SyncStatus = item.BitwardenVaultId is null ? SyncStatus.None : SyncStatus.Pending;

        await _repository.SaveSecureItemAsync(item);
        await _repository.LogAsync(new OperationLog
        {
            ItemType = "NOTE",
            ItemId = item.Id,
            ItemTitle = item.Title,
            OperationType = SelectedNote is null ? "CREATE" : "UPDATE",
            DeviceName = Environment.MachineName
        });

        if (!NoteItems.Contains(item))
        {
            NoteItems.Insert(0, item);
        }

        SelectedNote = item;
        await LoadTimelineAsync();
        RaiseCounts();
        StatusMessage = _localization.Format("SavedNoteFormat", item.Title);
    }

    [RelayCommand]
    private async Task ToggleNoteFavoriteAsync()
    {
        NoteIsFavorite = !NoteIsFavorite;
        if (SelectedNote is null)
        {
            return;
        }

        SelectedNote.IsFavorite = NoteIsFavorite;
        await _repository.SaveSecureItemAsync(SelectedNote);
        StatusMessage = _localization.Format("SavedNoteFormat", SelectedNote.Title);
    }

    [RelayCommand]
    private async Task DeleteNoteAsync(SecureItem? item)
    {
        item ??= SelectedNote;
        if (item is null)
        {
            return;
        }

        await _repository.SoftDeleteSecureItemAsync(item.Id);
        NoteItems.Remove(item);
        if (ReferenceEquals(SelectedNote, item) || SelectedNote?.Id == item.Id)
        {
            AddNote();
        }

        RaiseCounts();
        StatusMessage = _localization.Format("MovedToRecycleBinFormat", item.Title);
    }

    [RelayCommand]
    private void GeneratePassword()
    {
        GeneratedPassword = _passwordGenerator.GeneratePassword(
            GeneratorLength,
            GeneratorIncludeUppercase,
            GeneratorIncludeLowercase,
            GeneratorIncludeNumbers,
            GeneratorIncludeSymbols);
        StatusMessage = _localization.Get("GeneratedPassword");
    }

    [RelayCommand]
    private async Task CopyGeneratedPasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(GeneratedPassword))
        {
            GeneratePassword();
        }

        await _clipboardService.SetTextAsync(GeneratedPassword);
        StatusMessage = _localization.Get("CopiedGeneratedPassword");
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
    private async Task ExportTotpCsvAsync()
    {
        ExportTotpCsvPreview = await BuildTotpCsvExportAsync();
        StatusMessage = _localization.Get("ExportedTotpCsv");
    }

    [RelayCommand]
    private async Task ExportNoteCsvAsync()
    {
        ExportNoteCsvPreview = await BuildNoteCsvExportAsync();
        StatusMessage = _localization.Get("ExportedNoteCsv");
    }

    [RelayCommand]
    private async Task ExportWalletCsvAsync()
    {
        ExportWalletCsvPreview = await BuildWalletCsvExportAsync();
        StatusMessage = _localization.Get("ExportedWalletCsv");
    }

    [RelayCommand]
    private async Task ExportAegisJsonAsync()
    {
        ExportAegisPreview = await BuildAegisJsonExportAsync();
        StatusMessage = _localization.Get("ExportedAegisJson");
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
    private async Task ImportTotpCsvFileAsync()
    {
        try
        {
            var file = await _fileSystemPickerService.OpenTextFileAsync(_localization.Get("ImportTotpCsv"), TotpCsvFileTypes);
            if (file is null)
            {
                return;
            }

            ImportTotpCsvText = file.Content;
            await ImportTotpCsvAsync();
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
    private async Task ImportAegisJsonFileAsync()
    {
        try
        {
            var file = await _fileSystemPickerService.OpenTextFileAsync(_localization.Get("ImportAegisJson"), AegisJsonFileTypes);
            if (file is null)
            {
                return;
            }

            ImportAegisJsonText = file.Content;
            await ImportAegisJsonAsync();
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
    private async Task SaveTotpCsvExportAsync()
    {
        if (string.IsNullOrWhiteSpace(ExportTotpCsvPreview))
        {
            await ExportTotpCsvAsync();
        }

        await SaveExportTextAsync(
            _localization.Get("ExportTotpCsv"),
            $"monica_totp_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.csv",
            ExportTotpCsvPreview,
            TotpCsvFileTypes);
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

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task SaveWalletCsvExportAsync()
    {
        if (string.IsNullOrWhiteSpace(ExportWalletCsvPreview))
        {
            await ExportWalletCsvAsync();
        }

        await SaveExportTextAsync(
            _localization.Get("ExportWalletCsv"),
            $"monica_cards_documents_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.csv",
            ExportWalletCsvPreview,
            WalletCsvFileTypes);
    }

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task SaveAegisJsonExportAsync()
    {
        if (string.IsNullOrWhiteSpace(ExportAegisPreview))
        {
            await ExportAegisJsonAsync();
        }

        await SaveExportTextAsync(
            _localization.Get("ExportAegisJson"),
            $"monica_totp_aegis_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.json",
            ExportAegisPreview,
            AegisJsonFileTypes);
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

            WebDavBackupHistory.Insert(0, new WebDavBackupHistoryItem(
                fileName,
                path,
                DateTimeOffset.UtcNow.ToLocalTime().ToString("yyyy/MM/dd HH:mm", _localization.Culture),
                FormatByteSize(Encoding.UTF8.GetByteCount(content)),
                DateTimeOffset.UtcNow));
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

        try
        {
            await _webDavBackupService.DeleteAsync(profile, item.FileName);
            WebDavBackupHistory.Remove(item);
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

        return _importExportService.ExportJson(
            exportPasswords,
            exportSecureItems,
            exportCategories,
            customFieldsByPasswordId,
            passwordHistoryByPasswordId,
            passwordAttachmentsByPasswordId);
    }

    private async Task<string> BuildTotpCsvExportAsync()
    {
        var exportTotps = BuildStoredAndVirtualTotpItems(
                await _repository.GetPasswordsAsync(),
                await _repository.GetSecureItemsAsync(VaultItemType.Totp))
            .Select(item => CloneSecureItemForExport(item))
            .ToArray();

        return _importExportService.ExportTotpCsv(exportTotps);
    }

    private async Task<string> BuildNoteCsvExportAsync()
    {
        var exportNotes = (await _repository.GetSecureItemsAsync(VaultItemType.Note))
            .Select(item => CloneSecureItemForExport(item))
            .ToArray();

        return _importExportService.ExportNoteCsv(exportNotes);
    }

    private async Task<string> BuildWalletCsvExportAsync()
    {
        var exportWalletItems = (await _repository.GetSecureItemsAsync())
            .Where(item => item.ItemType is VaultItemType.BankCard or VaultItemType.Document)
            .Select(item => CloneSecureItemForExport(item))
            .ToArray();

        return _importExportService.ExportWalletCsv(exportWalletItems);
    }

    private async Task<string> BuildAegisJsonExportAsync()
    {
        var exportTotps = BuildStoredAndVirtualTotpItems(
                await _repository.GetPasswordsAsync(),
                await _repository.GetSecureItemsAsync(VaultItemType.Totp))
            .Select(item => CloneSecureItemForExport(item))
            .ToArray();

        return _importExportService.ExportAegisJson(exportTotps);
    }

    private static IReadOnlyList<SecureItem> BuildStoredAndVirtualTotpItems(
        IEnumerable<PasswordEntry> passwords,
        IEnumerable<SecureItem> secureItems)
    {
        var activePasswords = passwords.ToArray();
        var activePasswordIds = activePasswords.Select(item => item.Id).ToHashSet();
        var seenVirtualPasswordIds = new HashSet<long>();
        var result = new List<SecureItem>();

        foreach (var item in secureItems.Where(item => item.ItemType == VaultItemType.Totp))
        {
            if (item.BoundPasswordId is { } boundPasswordId && !activePasswordIds.Contains(boundPasswordId))
            {
                continue;
            }

            result.Add(item);
            if (item.BoundPasswordId is { } passwordId)
            {
                seenVirtualPasswordIds.Add(passwordId);
            }
        }

        foreach (var password in activePasswords.Where(item => item.HasAuthenticator && !seenVirtualPasswordIds.Contains(item.Id)))
        {
            result.Add(BuildVirtualTotpItem(password));
        }

        return result;
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
    private async Task ImportAegisJsonAsync()
    {
        if (string.IsNullOrWhiteSpace(ImportAegisJsonText))
        {
            StatusMessage = _localization.Get("ImportAegisJsonRequired");
            return;
        }

        try
        {
            var entries = _importExportService.ImportAegisJson(ImportAegisJsonText);
            var existingTitles = (await _repository.GetSecureItemsAsync(VaultItemType.Totp))
                .Select(item => item.Title)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var importedTotps = 0;
            var skippedTotps = 0;

            foreach (var source in entries)
            {
                if (!existingTitles.Add(source.Title))
                {
                    skippedTotps++;
                    continue;
                }

                await _repository.SaveSecureItemAsync(source);
                importedTotps++;
            }

            await _repository.LogAsync(new OperationLog
            {
                ItemType = "TOTP",
                ItemTitle = _localization.Get("AegisJson"),
                OperationType = "IMPORT",
                DeviceName = Environment.MachineName
            });

            ImportAegisJsonText = "";
            await LoadAsync();
            StatusMessage = _localization.Format("ImportedAegisJsonFormat", importedTotps, skippedTotps);
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ImportFailedFormat", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ImportTotpCsvAsync()
    {
        if (string.IsNullOrWhiteSpace(ImportTotpCsvText))
        {
            StatusMessage = _localization.Get("ImportTotpCsvRequired");
            return;
        }

        try
        {
            var entries = _importExportService.ImportTotpCsv(ImportTotpCsvText);
            var existingTitles = (await _repository.GetSecureItemsAsync(VaultItemType.Totp))
                .Select(item => item.Title)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var importedTotps = 0;
            var skippedTotps = 0;

            foreach (var source in entries)
            {
                if (!existingTitles.Add(source.Title))
                {
                    skippedTotps++;
                    continue;
                }

                await _repository.SaveSecureItemAsync(source);
                importedTotps++;
            }

            await _repository.LogAsync(new OperationLog
            {
                ItemType = "TOTP",
                ItemTitle = _localization.Get("TotpCsv"),
                OperationType = "IMPORT",
                DeviceName = Environment.MachineName
            });

            ImportTotpCsvText = "";
            await LoadAsync();
            StatusMessage = _localization.Format("ImportedTotpCsvFormat", importedTotps, skippedTotps);
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

        var metadata = CreateRemoteMdbxMetadata(
            _localization.Get("MdbxWebDavVaultName"),
            remotePath,
            MdbxStorageLocation.RemoteWebDav,
            "REMOTE_WEBDAV",
            BuildMdbxWorkingCopyPath("webdav-local.mdbx"),
            _localization.Get("MdbxWebDavMetadataDescription"));
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

        var metadata = CreateRemoteMdbxMetadata(
            _localization.Get("MdbxOneDriveVaultName"),
            remotePath,
            MdbxStorageLocation.RemoteOneDrive,
            "REMOTE_ONEDRIVE",
            BuildMdbxWorkingCopyPath("onedrive-local.mdbx"),
            _localization.Get("MdbxOneDriveMetadataDescription"));
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

        if (!item.IsLocal)
        {
            StatusMessage = _localization.Get("MdbxRemoteOpenPending");
            return;
        }

        await using var stream = await _mdbxVaultService.OpenLocalStreamAsync(item.Database);
        item.Database.LastAccessedAt = DateTimeOffset.UtcNow;
        item.Database.LastSyncStatus = SyncStatus.LocalOnly;
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

    public void RefreshTotpDisplay(SecureItem item)
    {
        var data = TotpDataResolver.ParseStoredItemData(item.ItemData, item.Title, item.Notes);
        if (data is null || string.IsNullOrWhiteSpace(data.Secret))
        {
            item.TotpCode = "------";
            item.TotpTimeRemaining = "";
            item.TotpProgress = 0;
            return;
        }

        item.TotpCode = _totpService.GenerateCode(data.Secret, data.Period, data.Digits, data.OtpType, data.Counter);
        item.TotpTimeRemaining = $"{_totpService.GetRemainingSeconds(data.Period)}s";
        item.TotpProgress = _totpService.GetProgress(data.Period);
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(PasswordCountText));
        OnPropertyChanged(nameof(ArchivedPasswordCountText));
        OnPropertyChanged(nameof(DeletedPasswordCountText));
        OnPropertyChanged(nameof(NoteCountText));
        OnPropertyChanged(nameof(TotpCountText));
        OnPropertyChanged(nameof(WalletCountText));
        OnPropertyChanged(nameof(TimelineCountText));
        OnPropertyChanged(nameof(SecurityIssueCountText));
        OnPropertyChanged(nameof(LocalDatabaseSummaryText));
        OnPropertyChanged(nameof(MdbxDatabaseCountText));
        RaiseMdbxVaultState();
        OnPropertyChanged(nameof(VaultSourceCountText));
        RaiseTotpSelectionState();
        RaiseWalletSelectionState();
    }

    private void RefreshMdbxVaultState()
    {
        MdbxDatabaseItems.Clear();
        foreach (var database in MdbxDatabases.OrderByDescending(item => item.IsDefault).ThenBy(item => item.SortOrder).ThenBy(item => item.Name))
        {
            MdbxDatabaseItems.Add(ToMdbxDisplayItem(database));
        }

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
        OnPropertyChanged(nameof(HasMdbxDatabases));
        OnPropertyChanged(nameof(MdbxLocalSourceStatusText));
        OnPropertyChanged(nameof(MdbxWebDavSourceStatusText));
        OnPropertyChanged(nameof(MdbxOneDriveSourceStatusText));
        OnPropertyChanged(nameof(MdbxRuntimeSummaryText));
        OnPropertyChanged(nameof(MdbxSecuritySummaryText));
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
            database.IsDefault,
            isLocal,
            !isLocal);
    }

    private static bool IsLocalMdbxDatabase(LocalMdbxDatabase database) =>
        database.StorageLocation is MdbxStorageLocation.Internal or MdbxStorageLocation.External;

    private static string BuildMdbxWorkingCopyPath(string fileName)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Monica by Avalonia", "mdbx");
        return Path.Combine(root, fileName);
    }

    private LocalMdbxDatabase CreateRemoteMdbxMetadata(
        string name,
        string remotePath,
        MdbxStorageLocation storageLocation,
        string sourceType,
        string workingCopyPath,
        string description) => new()
    {
        Name = name,
        FilePath = remotePath,
        StorageLocation = storageLocation,
        SourceType = sourceType,
        TigaMode = MdbxTigaMode.Multi,
        UnlockMethod = MdbxUnlockMethod.MasterPassword,
        LastSyncStatus = SyncStatus.Pending,
        WorkingCopyPath = workingCopyPath,
        IsOfflineAvailable = MdbxLocalCacheEnabled,
        Description = description,
        CreatedAt = DateTimeOffset.UtcNow,
        LastAccessedAt = DateTimeOffset.UtcNow
    };

    private string FormatLocalDate(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy/MM/dd HH:mm", _localization.Culture);

    private void RefreshVaultSources()
    {
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
            VaultSources.Add(new VaultSourceDisplayItem(
                string.IsNullOrWhiteSpace(database.Name) ? "MDBX" : database.Name,
                "MDBX",
                string.IsNullOrWhiteSpace(database.FilePath) ? _localization.Get("NotConfigured") : database.FilePath,
                database.StorageLocation.ToString(),
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

        OnPropertyChanged(nameof(VaultSourceCountText));
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
        OnPropertyChanged(nameof(WebDavBackupHistoryCountText));
        OnPropertyChanged(nameof(HasWebDavBackupHistory));
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

    private void RaisePasswordSelectionState()
    {
        OnPropertyChanged(nameof(SelectedPasswordCount));
        OnPropertyChanged(nameof(SelectedPasswordCountText));
        OnPropertyChanged(nameof(HasSelectedPasswords));
        OnPropertyChanged(nameof(CanStackSelectedPasswords));
        OnPropertyChanged(nameof(AreAllFilteredPasswordsSelected));
    }

    private void RaiseTotpSelectionState()
    {
        OnPropertyChanged(nameof(SelectedTotpCount));
        OnPropertyChanged(nameof(SelectedTotpCountText));
        OnPropertyChanged(nameof(HasSelectedTotpItems));
    }

    private void RaiseWalletSelectionState()
    {
        OnPropertyChanged(nameof(SelectedWalletCount));
        OnPropertyChanged(nameof(SelectedWalletCountText));
        OnPropertyChanged(nameof(HasSelectedWalletItems));
    }

    private void RefreshPasswordFilters()
    {
        OnPropertyChanged(nameof(FilteredPasswords));
        OnPropertyChanged(nameof(FilteredArchivedPasswords));
        OnPropertyChanged(nameof(FilteredDeletedPasswords));
        RaisePasswordSelectionState();
        RefreshPasswordFolderFilters();
    }

    private void TrackPasswordSelection(PasswordEntry entry)
    {
        entry.PropertyChanged -= PasswordEntryPropertyChanged;
        entry.PropertyChanged += PasswordEntryPropertyChanged;
    }

    private void PasswordEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PasswordEntry.IsSelected))
        {
            RaisePasswordSelectionState();
        }
    }

    private void TrackTotpSelection(SecureItem item)
    {
        item.PropertyChanged -= SecureItemPropertyChanged;
        item.PropertyChanged += SecureItemPropertyChanged;
    }

    private void SecureItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SecureItem.IsSelected))
        {
            RaiseTotpSelectionState();
        }
    }

    private void TrackWalletSelection(SecureItem item)
    {
        item.PropertyChanged -= WalletItemPropertyChanged;
        item.PropertyChanged += WalletItemPropertyChanged;
    }

    private void WalletItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SecureItem.IsSelected))
        {
            RaiseWalletSelectionState();
        }
    }

    private async Task LoadPasswordQuickAccessAsync()
    {
        _passwordQuickAccessRecords = (await _repository.GetPasswordQuickAccessRecordsAsync())
            .Where(record => record.OpenCount > 0 && record.PasswordId > 0)
            .ToDictionary(record => record.PasswordId);
        RaisePasswordQuickAccessState();
    }

    private async Task RecordPasswordQuickAccessAsync(PasswordEntry entry)
    {
        if (entry.Id <= 0 || entry.IsDeleted || entry.IsArchived)
        {
            return;
        }

        await _repository.RecordPasswordQuickAccessAsync(entry.Id);
        var next = _passwordQuickAccessRecords.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (next.TryGetValue(entry.Id, out var existing))
        {
            existing.OpenCount++;
            existing.LastOpenedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            next[entry.Id] = new PasswordQuickAccessRecord
            {
                PasswordId = entry.Id,
                OpenCount = 1,
                LastOpenedAt = DateTimeOffset.UtcNow
            };
        }

        _passwordQuickAccessRecords = next;
        RaisePasswordQuickAccessState();
    }

    private IEnumerable<PasswordQuickAccessItem> BuildQuickAccessItems(QuickAccessSort sort)
    {
        var records = sort == QuickAccessSort.Frequent
            ? _passwordQuickAccessRecords.Values
                .OrderByDescending(record => record.OpenCount)
                .ThenByDescending(record => record.LastOpenedAt)
            : _passwordQuickAccessRecords.Values
                .OrderByDescending(record => record.LastOpenedAt)
                .ThenByDescending(record => record.OpenCount);

        return records
            .Select(record =>
            {
                var entry = Passwords.FirstOrDefault(item => item.Id == record.PasswordId);
                return entry is null
                    ? null
                    : new PasswordQuickAccessItem(
                        entry,
                        record.OpenCount,
                        record.LastOpenedAt.ToString("g", _localization.Culture),
                        BuildQuickAccessSubtitle(entry));
            })
            .OfType<PasswordQuickAccessItem>()
            .Take(PasswordQuickAccessLimit)
            .ToArray();
    }

    private static string BuildQuickAccessSubtitle(PasswordEntry entry)
    {
        return string.IsNullOrWhiteSpace(entry.Website)
            ? entry.Username
            : string.IsNullOrWhiteSpace(entry.Username)
                ? entry.Website
                : $"{entry.Username} - {entry.Website}";
    }

    private void RaisePasswordQuickAccessState()
    {
        OnPropertyChanged(nameof(RecentPasswordQuickAccessItems));
        OnPropertyChanged(nameof(FrequentPasswordQuickAccessItems));
        OnPropertyChanged(nameof(HasPasswordQuickAccessItems));
    }

    private IEnumerable<PasswordEntry> ApplyPasswordSort(IEnumerable<PasswordEntry> items)
    {
        return SelectedPasswordSort switch
        {
            "title-asc" => items
                .OrderBy(item => NormalizeSortText(item.Title), StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(item => item.UpdatedAt)
                .ThenBy(item => item.Id),
            "website-asc" => items
                .OrderBy(item => NormalizeSortText(item.Website), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => NormalizeSortText(item.Title), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Id),
            "username-asc" => items
                .OrderBy(item => NormalizeSortText(item.Username), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => NormalizeSortText(item.Title), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Id),
            "created-desc" => items
                .OrderByDescending(item => item.CreatedAt)
                .ThenBy(item => NormalizeSortText(item.Title), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Id),
            "favorites-first" => items
                .OrderByDescending(item => item.IsFavorite)
                .ThenByDescending(item => item.UpdatedAt)
                .ThenBy(item => NormalizeSortText(item.Title), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Id),
            _ => items
                .OrderByDescending(item => item.UpdatedAt)
                .ThenBy(item => NormalizeSortText(item.Title), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Id)
        };
    }

    private static string NormalizeSortText(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? "\uffff" : text;
    }

    private Category? GetSelectedPasswordFolderCategory()
    {
        var selectedId = SelectedPasswordFolderFilter?.Id;
        return selectedId is > 0
            ? Categories.FirstOrDefault(item => item.Id == selectedId.Value)
            : null;
    }

    private void RefreshPasswordFolderFilters(long? preferredCategoryId = null)
    {
        var selectedId = preferredCategoryId ?? SelectedPasswordFolderFilter?.Id;
        PasswordFolderFilters.Clear();
        PasswordFolderFilters.Add(new PasswordFolderFilterChoice(null, _localization.Get("AllFolders"), Passwords.Count));
        foreach (var category in Categories.OrderBy(item => item.SortOrder).ThenBy(item => item.Name))
        {
            PasswordFolderFilters.Add(new PasswordFolderFilterChoice(
                category.Id,
                category.Name,
                Passwords.Count(password => password.CategoryId == category.Id)));
        }

        PasswordFolderFilters.Add(new PasswordFolderFilterChoice(
            -1,
            _localization.Get("NoFolder"),
            Passwords.Count(password => password.CategoryId is null)));

        SelectedPasswordFolderFilter =
            PasswordFolderFilters.FirstOrDefault(item => item.Id == selectedId) ??
            PasswordFolderFilters.FirstOrDefault();
        OnPropertyChanged(nameof(FilteredPasswords));
        OnPropertyChanged(nameof(CanManageSelectedPasswordFolder));
    }

    private string ProtectPassword(string password)
    {
        return _cryptoService.IsUnlocked ? _cryptoService.EncryptString(password) : password;
    }

    private IReadOnlyList<string> ProtectPasswords(IReadOnlyList<string> passwords)
    {
        if (passwords.Count == 0)
        {
            return [ProtectPassword("")];
        }

        return passwords.Select(ProtectPassword).ToArray();
    }

    private string UnprotectPassword(string storedPassword)
    {
        if (!_cryptoService.IsUnlocked)
        {
            return storedPassword;
        }

        try
        {
            return _cryptoService.DecryptString(storedPassword);
        }
        catch
        {
            return storedPassword;
        }
    }

    private async Task SavePasswordHistorySnapshotIfChangedAsync(long entryId, string oldPlainPassword, string newPlainPassword)
    {
        if (entryId <= 0 ||
            string.IsNullOrWhiteSpace(oldPlainPassword) ||
            string.Equals(oldPlainPassword, newPlainPassword, StringComparison.Ordinal))
        {
            return;
        }

        var latestHistory = (await _repository.GetPasswordHistoryAsync(entryId)).FirstOrDefault();
        if (latestHistory is not null &&
            string.Equals(UnprotectPassword(latestHistory.Password), oldPlainPassword, StringComparison.Ordinal))
        {
            return;
        }

        await _repository.SavePasswordHistoryAsync(new PasswordHistoryEntry
        {
            EntryId = entryId,
            Password = ProtectPassword(oldPlainPassword),
            LastUsedAt = DateTimeOffset.UtcNow
        });
        await _repository.TrimPasswordHistoryAsync(entryId, PasswordHistoryLimit);
    }

    private async Task<IReadOnlyList<PasswordHistoryDisplayItem>> GetPasswordHistoryDisplayItemsAsync(long entryId)
    {
        var history = await _repository.GetPasswordHistoryAsync(entryId);
        return history
            .Select(item =>
            {
                var password = TryUnprotectHistoryPassword(item.Password);
                return new PasswordHistoryDisplayItem(item, password.DisplayValue, password.CanCopy);
            })
            .ToArray();
    }

    private (string DisplayValue, bool CanCopy) TryUnprotectHistoryPassword(string storedPassword)
    {
        if (string.IsNullOrWhiteSpace(storedPassword))
        {
            return (_localization.Get("PasswordHistoryUnavailable"), false);
        }

        if (!_cryptoService.IsUnlocked)
        {
            return ("********", false);
        }

        try
        {
            return (_cryptoService.DecryptString(storedPassword), true);
        }
        catch
        {
            return (storedPassword, true);
        }
    }

    private async Task DeletePasswordHistoryAsync(PasswordHistoryEntry entry)
    {
        await _repository.DeletePasswordHistoryAsync(entry.Id);
        StatusMessage = _localization.Get("DeletedPasswordHistoryEntry");
    }

    private async Task ClearPasswordHistoryAsync(long entryId)
    {
        await _repository.ClearPasswordHistoryAsync(entryId);
        StatusMessage = _localization.Get("ClearedPasswordHistory");
    }

    private PasswordEntry ClonePasswordForExport(PasswordEntry source, bool includeCategory = true)
    {
        var clone = ClonePassword(source);
        clone.Password = UnprotectPassword(source.Password);
        if (!includeCategory)
        {
            clone.CategoryId = null;
        }

        return clone;
    }

    private PasswordEntry ClonePasswordForImport(PasswordEntry source, IReadOnlyDictionary<long, long>? categoryIdMap = null)
    {
        var clone = ClonePassword(source);
        clone.Id = 0;
        clone.Password = ProtectPassword(UnprotectPassword(source.Password));
        clone.MdbxDatabaseId = null;
        clone.MdbxFolderId = null;
        if (clone.CategoryId is { } categoryId)
        {
            clone.CategoryId = categoryIdMap?.TryGetValue(categoryId, out var importedCategoryId) == true
                ? importedCategoryId
                : null;
        }

        clone.IsDeleted = false;
        clone.DeletedAt = null;
        clone.IsArchived = false;
        clone.ArchivedAt = null;
        clone.BitwardenLocalModified = true;
        return clone;
    }

    private static PasswordEntry ClonePassword(PasswordEntry source)
    {
        return new PasswordEntry
        {
            Id = source.Id,
            Title = source.Title,
            Website = source.Website,
            Username = source.Username,
            Password = source.Password,
            Notes = source.Notes,
            IsFavorite = source.IsFavorite,
            SortOrder = source.SortOrder,
            IsGroupCover = source.IsGroupCover,
            AppPackageName = source.AppPackageName,
            AppName = source.AppName,
            Email = source.Email,
            Phone = source.Phone,
            AddressLine = source.AddressLine,
            City = source.City,
            State = source.State,
            ZipCode = source.ZipCode,
            Country = source.Country,
            CreditCardNumber = source.CreditCardNumber,
            CreditCardHolder = source.CreditCardHolder,
            CreditCardExpiry = source.CreditCardExpiry,
            CreditCardCvv = source.CreditCardCvv,
            CategoryId = source.CategoryId,
            BoundNoteId = source.BoundNoteId,
            KeepassDatabaseId = source.KeepassDatabaseId,
            KeepassGroupPath = source.KeepassGroupPath,
            KeepassEntryUuid = source.KeepassEntryUuid,
            KeepassGroupUuid = source.KeepassGroupUuid,
            MdbxDatabaseId = source.MdbxDatabaseId,
            MdbxFolderId = source.MdbxFolderId,
            AuthenticatorKey = source.AuthenticatorKey,
            PasskeyBindings = source.PasskeyBindings,
            SshKeyData = source.SshKeyData,
            LoginType = source.LoginType,
            SsoProvider = source.SsoProvider,
            SsoRefEntryId = source.SsoRefEntryId,
            WifiMetadata = source.WifiMetadata,
            CustomIconType = source.CustomIconType,
            CustomIconValue = source.CustomIconValue,
            CustomIconUpdatedAt = source.CustomIconUpdatedAt,
            IsDeleted = source.IsDeleted,
            DeletedAt = source.DeletedAt,
            IsArchived = source.IsArchived,
            ArchivedAt = source.ArchivedAt,
            ReplicaGroupId = source.ReplicaGroupId,
            BitwardenVaultId = source.BitwardenVaultId,
            BitwardenCipherId = source.BitwardenCipherId,
            BitwardenFolderId = source.BitwardenFolderId,
            BitwardenRevisionDate = source.BitwardenRevisionDate,
            BitwardenCipherType = source.BitwardenCipherType,
            BitwardenLocalModified = source.BitwardenLocalModified,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt
        };
    }

    private static CustomField CloneCustomFieldForImport(CustomField source, long importedPasswordId)
    {
        return new CustomField
        {
            Id = 0,
            EntryId = importedPasswordId,
            Title = source.Title,
            Value = source.Value,
            IsProtected = source.IsProtected,
            SortOrder = source.SortOrder
        };
    }

    private PasswordHistoryEntry ClonePasswordHistoryForExport(PasswordHistoryEntry source)
    {
        return new PasswordHistoryEntry
        {
            Id = source.Id,
            EntryId = source.EntryId,
            Password = UnprotectPassword(source.Password),
            LastUsedAt = source.LastUsedAt
        };
    }

    private PasswordHistoryEntry ClonePasswordHistoryForImport(PasswordHistoryEntry source, long importedPasswordId)
    {
        return new PasswordHistoryEntry
        {
            Id = 0,
            EntryId = importedPasswordId,
            Password = ProtectPassword(UnprotectPassword(source.Password)),
            LastUsedAt = source.LastUsedAt
        };
    }

    private static Attachment CloneAttachmentForExport(Attachment source)
    {
        return new Attachment
        {
            Id = source.Id,
            OwnerType = source.OwnerType,
            OwnerId = source.OwnerId,
            FileName = source.FileName,
            ContentType = source.ContentType,
            StoragePath = source.StoragePath,
            SizeBytes = source.SizeBytes,
            CreatedAt = source.CreatedAt,
            BitwardenVaultId = source.BitwardenVaultId,
            KeepassBinaryRef = source.KeepassBinaryRef
        };
    }

    private static Attachment CloneAttachmentForImport(Attachment source, long importedPasswordId)
    {
        return new Attachment
        {
            Id = 0,
            OwnerType = "PASSWORD",
            OwnerId = importedPasswordId,
            FileName = source.FileName,
            ContentType = source.ContentType,
            StoragePath = "",
            SizeBytes = source.SizeBytes,
            CreatedAt = source.CreatedAt == default ? DateTimeOffset.UtcNow : source.CreatedAt,
            BitwardenVaultId = source.BitwardenVaultId,
            KeepassBinaryRef = source.KeepassBinaryRef
        };
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
            SortOrder = source.SortOrder,
            MdbxDatabaseId = source.MdbxDatabaseId
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

    private void ReplacePasswordGroup(IReadOnlyList<PasswordEntry> previousEntries, IReadOnlyList<PasswordEntry> updatedEntries)
    {
        foreach (var previous in previousEntries)
        {
            Passwords.Remove(previous);
            var current = Passwords.FirstOrDefault(item => item.Id == previous.Id);
            if (current is not null)
            {
                Passwords.Remove(current);
            }
        }

        for (var index = updatedEntries.Count - 1; index >= 0; index--)
        {
            updatedEntries[index].IsSelected = false;
            TrackPasswordSelection(updatedEntries[index]);
            Passwords.Insert(0, updatedEntries[index]);
        }

        RaisePasswordSelectionState();
    }

    private static IReadOnlyList<CustomField> BindCustomFields(long entryId, IReadOnlyList<CustomField> fields)
    {
        return fields
            .Select((field, index) => new CustomField
            {
                EntryId = entryId,
                Title = field.Title,
                Value = field.Value,
                IsProtected = field.IsProtected,
                SortOrder = index
            })
            .ToArray();
    }

    private void SetPasswordCustomFields(long entryId, IReadOnlyList<CustomField> fields)
    {
        var next = _passwordCustomFields.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (fields.Count == 0)
        {
            next.Remove(entryId);
        }
        else
        {
            next[entryId] = fields;
        }

        _passwordCustomFields = next;
    }

    private IReadOnlyList<Attachment> GetPasswordAttachments(long entryId)
    {
        return _passwordAttachments.TryGetValue(entryId, out var attachments)
            ? attachments
            : [];
    }

    private IReadOnlyList<Attachment> GetGroupAttachments(PasswordEntry entry, IReadOnlyList<PasswordEntry> siblings)
    {
        var siblingIds = siblings.Count == 0
            ? [entry.Id]
            : siblings.Select(item => item.Id).ToArray();
        return siblingIds
            .SelectMany(GetPasswordAttachments)
            .OrderByDescending(attachment => attachment.CreatedAt)
            .ThenByDescending(attachment => attachment.Id)
            .ToArray();
    }

    private void SetPasswordAttachments(long entryId, IReadOnlyList<Attachment> attachments)
    {
        var next = _passwordAttachments.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (attachments.Count == 0)
        {
            next.Remove(entryId);
        }
        else
        {
            next[entryId] = attachments;
        }

        _passwordAttachments = next;
    }

    private void RefreshPasswordAttachmentState(PasswordEntry entry)
    {
        entry.HasAttachments = GetPasswordAttachments(entry.Id).Count > 0;
    }

    private async Task<IReadOnlyList<CustomField>> GetGroupCustomFieldsAsync(PasswordEntry entry, IReadOnlyList<PasswordEntry> siblings)
    {
        foreach (var candidate in siblings)
        {
            var fields = await _repository.GetCustomFieldsAsync(candidate.Id);
            if (fields.Count > 0 || candidate.Id == entry.Id)
            {
                return fields;
            }
        }

        return [];
    }

    private async Task LoadTotpItemsAsync()
    {
        TotpItems.Clear();
        var storedTotps = await _repository.GetSecureItemsAsync(VaultItemType.Totp);
        var activePasswordIds = Passwords.Select(item => item.Id).ToHashSet();
        var seenVirtualPasswordIds = new HashSet<long>();

        foreach (var item in storedTotps)
        {
            if (item.BoundPasswordId is { } boundPasswordId && !activePasswordIds.Contains(boundPasswordId))
            {
                continue;
            }

            TrackTotpSelection(item);
            RefreshTotpDisplay(item);
            TotpItems.Add(item);
            if (item.BoundPasswordId is { } passwordId)
            {
                seenVirtualPasswordIds.Add(passwordId);
            }
        }

        foreach (var password in Passwords.Where(item => item.HasAuthenticator && !seenVirtualPasswordIds.Contains(item.Id)))
        {
            var virtualItem = BuildVirtualTotpItem(password);
            TrackTotpSelection(virtualItem);
            RefreshTotpDisplay(virtualItem);
            TotpItems.Add(virtualItem);
        }

        RaiseTotpSelectionState();
    }

    private async Task LoadTimelineAsync()
    {
        TimelineEntries.Clear();
        foreach (var log in await _repository.GetOperationLogsAsync(150))
        {
            TimelineEntries.Add(new TimelineEntry(
                string.IsNullOrWhiteSpace(log.ItemTitle) ? _localization.Get("Untitled") : log.ItemTitle,
                _localization.Format("TimelineEntryDescriptionFormat", LocalizeOperationType(log.OperationType), log.ItemType, log.DeviceName),
                log.Timestamp.LocalDateTime.ToString("g", _localization.Culture),
                log.OperationType,
                log.ItemType));
        }

        OnPropertyChanged(nameof(TimelineCountText));
    }

    [RelayCommand]
    private async Task CheckCompromisedPasswordsAsync()
    {
        if (IsCheckingCompromisedPasswords)
        {
            return;
        }

        var snapshots = BuildSecurityPasswordSnapshots();
        var plainPasswords = snapshots
            .Select(item => item.PlainPassword)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        IsCheckingCompromisedPasswords = true;
        CompromisedPasswordStatus = _localization.Format("CompromisedPasswordCheckingFormat", plainPasswords.Length);

        try
        {
            var countsByPassword = await _pwnedPasswordService.CheckPasswordsAsync(plainPasswords);
            var next = new Dictionary<long, CompromisedPasswordResult>();
            foreach (var snapshot in snapshots.Where(item => !string.IsNullOrWhiteSpace(item.PlainPassword)))
            {
                if (!countsByPassword.TryGetValue(snapshot.PlainPassword, out var count) || count <= 0)
                {
                    continue;
                }

                next[snapshot.Entry.Id] = new CompromisedPasswordResult(HashPasswordForSecurityCache(snapshot.PlainPassword), count);
            }

            _compromisedPasswordResults = next;
            _hasCompromisedPasswordCheckResults = true;
            CompromisedPasswordStatus = _localization.Format(
                "CompromisedPasswordCheckCompleteFormat",
                plainPasswords.Length,
                next.Count);
            RefreshSecurityAnalysis();
        }
        catch (Exception ex)
        {
            CompromisedPasswordStatus = _localization.Format("CompromisedPasswordCheckUnavailableFormat", ex.Message);
        }
        finally
        {
            IsCheckingCompromisedPasswords = false;
        }
    }

    public void RefreshSecurityAnalysis()
    {
        SecuritySummaryItems.Clear();
        SecurityIssueItems.Clear();

        var analyzed = BuildSecurityPasswordSnapshots();

        var compromisedCount = AddCompromisedPasswordIssues(analyzed);
        var weakCount = AddWeakPasswordIssues(analyzed);
        var duplicatePasswordCount = AddDuplicatePasswordIssues(analyzed);
        var duplicateWebsiteCount = AddDuplicateWebsiteIssues(analyzed);
        var missingTwoFactorCount = AddMissingTwoFactorIssues(analyzed);
        var staleCount = AddStalePasswordIssues(analyzed);

        var totalPenalty =
            compromisedCount * 10 +
            weakCount * 4 +
            duplicatePasswordCount * 6 +
            duplicateWebsiteCount * 2 +
            missingTwoFactorCount * 2 +
            staleCount;
        var score = Math.Clamp(100 - totalPenalty, 0, 100);

        SecuritySummaryItems.Add(new SecuritySummaryItem(
            _localization.SecurityScore,
            _localization.Format("SecurityScoreFormat", score),
            _localization.Format("SecurityAnalyzedPasswordCountFormat", analyzed.Length)));
        SecuritySummaryItems.Add(new SecuritySummaryItem(
            _localization.CompromisedPasswords,
            _hasCompromisedPasswordCheckResults ? compromisedCount.ToString(_localization.Culture) : "-",
            _localization.Get("CompromisedPasswordsSummary")));
        SecuritySummaryItems.Add(new SecuritySummaryItem(
            _localization.WeakPasswords,
            weakCount.ToString(_localization.Culture),
            _localization.Get("WeakPasswordsSummary")));
        SecuritySummaryItems.Add(new SecuritySummaryItem(
            _localization.DuplicatePasswords,
            duplicatePasswordCount.ToString(_localization.Culture),
            _localization.Get("DuplicatePasswordsSummary")));
        SecuritySummaryItems.Add(new SecuritySummaryItem(
            _localization.MissingTwoFactor,
            missingTwoFactorCount.ToString(_localization.Culture),
            _localization.Get("MissingTwoFactorSummary")));

        var orderedIssues = SecurityIssueItems
            .OrderByDescending(item => item.SeverityWeight)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        SecurityIssueItems.Clear();
        foreach (var issue in orderedIssues)
        {
            SecurityIssueItems.Add(issue);
        }

        OnPropertyChanged(nameof(SecurityIssueCountText));
    }

    private SecurityPasswordSnapshot[] BuildSecurityPasswordSnapshots()
    {
        return Passwords
            .Where(item => !item.IsDeleted && !item.IsArchived)
            .Select(item => new SecurityPasswordSnapshot(
                item,
                UnprotectPassword(item.Password).Trim(),
                SplitAndNormalizeWebsites(item.Website).ToArray()))
            .ToArray();
    }

    private int AddCompromisedPasswordIssues(IReadOnlyList<SecurityPasswordSnapshot> snapshots)
    {
        if (!_hasCompromisedPasswordCheckResults || _compromisedPasswordResults.Count == 0)
        {
            return 0;
        }

        var count = 0;
        foreach (var snapshot in snapshots.Where(item => !string.IsNullOrWhiteSpace(item.PlainPassword)))
        {
            if (!_compromisedPasswordResults.TryGetValue(snapshot.Entry.Id, out var result) ||
                !string.Equals(result.PasswordHash, HashPasswordForSecurityCache(snapshot.PlainPassword), StringComparison.Ordinal) ||
                result.ExposureCount <= 0)
            {
                continue;
            }

            count++;
            SecurityIssueItems.Add(new SecurityIssueItem(
                snapshot.Entry.Title,
                _localization.Format("CompromisedPasswordIssueFormat", result.ExposureCount),
                _localization.CompromisedPasswords,
                _localization.Get("HighSeverity"),
                snapshot.Entry.Id,
                snapshot.Entry,
                40));
        }

        return count;
    }

    private int AddWeakPasswordIssues(IReadOnlyList<SecurityPasswordSnapshot> snapshots)
    {
        var count = 0;
        foreach (var snapshot in snapshots)
        {
            if (string.IsNullOrWhiteSpace(snapshot.PlainPassword))
            {
                continue;
            }

            var strength = _passwordGenerator.Analyze(snapshot.PlainPassword);
            if (strength.Score > 2)
            {
                continue;
            }

            count++;
            SecurityIssueItems.Add(new SecurityIssueItem(
                snapshot.Entry.Title,
                _localization.Format("WeakPasswordIssueFormat", strength.Label),
                _localization.WeakPasswords,
                _localization.Get("HighSeverity"),
                snapshot.Entry.Id,
                snapshot.Entry,
                30));
        }

        return count;
    }

    private int AddDuplicatePasswordIssues(IReadOnlyList<SecurityPasswordSnapshot> snapshots)
    {
        var count = 0;
        foreach (var group in snapshots
            .Where(item => !string.IsNullOrWhiteSpace(item.PlainPassword))
            .GroupBy(item => item.PlainPassword, StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            var titles = string.Join(", ", group.Select(item => item.Entry.Title).Distinct().Take(3));
            foreach (var snapshot in group)
            {
                count++;
                SecurityIssueItems.Add(new SecurityIssueItem(
                    snapshot.Entry.Title,
                    _localization.Format("DuplicatePasswordIssueFormat", group.Count(), titles),
                    _localization.DuplicatePasswords,
                    _localization.Get("HighSeverity"),
                    snapshot.Entry.Id,
                    snapshot.Entry,
                    28));
            }
        }

        return count;
    }

    private int AddDuplicateWebsiteIssues(IReadOnlyList<SecurityPasswordSnapshot> snapshots)
    {
        var websites = snapshots
            .SelectMany(snapshot => snapshot.NormalizedWebsites.Select(website => new WebsiteSnapshot(snapshot.Entry, website)))
            .GroupBy(item => item.Website, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(item => item.Entry.Id).Distinct().Count() > 1);

        var count = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in websites)
        {
            var entries = group
                .GroupBy(item => item.Entry.Id)
                .Select(item => item.First().Entry)
                .ToArray();
            foreach (var entry in entries)
            {
                if (!seen.Add($"{entry.Id}:{group.Key}"))
                {
                    continue;
                }

                count++;
                SecurityIssueItems.Add(new SecurityIssueItem(
                    entry.Title,
                    _localization.Format("DuplicateWebsiteIssueFormat", group.Key, entries.Length),
                    _localization.DuplicateWebsites,
                    _localization.Get("MediumSeverity"),
                    entry.Id,
                    entry,
                    18));
            }
        }

        return count;
    }

    private int AddMissingTwoFactorIssues(IReadOnlyList<SecurityPasswordSnapshot> snapshots)
    {
        var count = 0;
        foreach (var snapshot in snapshots)
        {
            if (snapshot.Entry.HasAuthenticator ||
                !string.IsNullOrWhiteSpace(snapshot.Entry.PasskeyBindings) ||
                snapshot.Entry.LoginType is not PasswordLoginType.Password ||
                snapshot.NormalizedWebsites.Length == 0)
            {
                continue;
            }

            var domain = snapshot.NormalizedWebsites.First();
            if (!KnownTwoFactorDomains.Any(known => domain.Equals(known, StringComparison.OrdinalIgnoreCase) || domain.EndsWith($".{known}", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            count++;
            SecurityIssueItems.Add(new SecurityIssueItem(
                snapshot.Entry.Title,
                _localization.Format("MissingTwoFactorIssueFormat", domain),
                _localization.MissingTwoFactor,
                _localization.Get("MediumSeverity"),
                snapshot.Entry.Id,
                snapshot.Entry,
                16));
        }

        return count;
    }

    private int AddStalePasswordIssues(IReadOnlyList<SecurityPasswordSnapshot> snapshots)
    {
        var threshold = DateTimeOffset.UtcNow.AddDays(-365);
        var count = 0;
        foreach (var snapshot in snapshots.Where(item => item.Entry.UpdatedAt < threshold))
        {
            count++;
            SecurityIssueItems.Add(new SecurityIssueItem(
                snapshot.Entry.Title,
                _localization.Format("StalePasswordIssueFormat", snapshot.Entry.UpdatedAt.LocalDateTime.ToString("d", _localization.Culture)),
                _localization.StalePasswords,
                _localization.Get("LowSeverity"),
                snapshot.Entry.Id,
                snapshot.Entry,
                8));
        }

        return count;
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

    private void RefreshPasswordTotpDisplay(PasswordEntry entry)
    {
        var data = TotpDataResolver.FromAuthenticatorKey(entry.AuthenticatorKey, entry.Title, entry.Username);
        if (data is null || string.IsNullOrWhiteSpace(data.Secret))
        {
            entry.TotpCode = "------";
            entry.TotpTimeRemaining = "";
            entry.TotpProgress = 0;
            return;
        }

        entry.TotpCode = _totpService.GenerateCode(data.Secret, data.Period, data.Digits, data.OtpType, data.Counter);
        entry.TotpTimeRemaining = $"{_totpService.GetRemainingSeconds(data.Period)}s";
        entry.TotpProgress = _totpService.GetProgress(data.Period);
    }

    private async Task SetTotpFavoriteAsync(SecureItem item, bool isFavorite)
    {
        item.IsFavorite = isFavorite;
        if (item.BoundPasswordId is { } passwordId)
        {
            var password = Passwords.FirstOrDefault(entry => entry.Id == passwordId)
                ?? (await _repository.GetPasswordsAsync()).FirstOrDefault(entry => entry.Id == passwordId);
            if (password is not null)
            {
                password.IsFavorite = isFavorite;
                await _repository.SavePasswordAsync(password);
                await SynchronizeBoundTotpAsync(password);
                var synchronized = (await _repository.GetSecureItemsByBoundPasswordIdAsync(password.Id))
                    .FirstOrDefault(secureItem => secureItem.ItemType == VaultItemType.Totp);
                if (synchronized is not null)
                {
                    synchronized.IsFavorite = isFavorite;
                    await _repository.SaveSecureItemAsync(synchronized);
                }
            }
        }
        else if (item.Id > 0)
        {
            await _repository.SaveSecureItemAsync(item);
        }

        await _repository.LogAsync(new OperationLog
        {
            ItemType = "TOTP",
            ItemId = item.Id,
            ItemTitle = item.Title,
            OperationType = "FAVORITE",
            DeviceName = Environment.MachineName
        });
    }

    private async Task DeleteTotpItemAsync(SecureItem item, bool updateStatus)
    {
        if (item.BoundPasswordId is { } passwordId)
        {
            var password = Passwords.FirstOrDefault(entry => entry.Id == passwordId)
                ?? (await _repository.GetPasswordsAsync()).FirstOrDefault(entry => entry.Id == passwordId);
            if (password is not null)
            {
                password.IsFavorite = item.IsFavorite;
                password.AuthenticatorKey = "";
                await _repository.SavePasswordAsync(password);
                await SynchronizeBoundTotpAsync(password);
                RefreshPasswordTotpDisplay(password);
            }
        }
        else if (item.Id > 0)
        {
            await _repository.SoftDeleteSecureItemAsync(item.Id);
        }

        TotpItems.Remove(item);
        item.IsSelected = false;
        await _repository.LogAsync(new OperationLog
        {
            ItemType = "TOTP",
            ItemId = item.Id,
            ItemTitle = item.Title,
            OperationType = "DELETE",
            DeviceName = Environment.MachineName
        });
        RaiseCounts();
        RaiseTotpSelectionState();
        if (updateStatus)
        {
            await LoadTimelineAsync();
            StatusMessage = _localization.Format("MovedToRecycleBinFormat", item.Title);
        }
    }

    private async Task DeleteWalletItemCoreAsync(SecureItem item, bool updateStatus)
    {
        if (item.Id > 0)
        {
            await _repository.SoftDeleteSecureItemAsync(item.Id);
        }

        WalletItems.Remove(item);
        item.IsSelected = false;
        if (SelectedWalletItem?.Id == item.Id)
        {
            SelectedWalletItem = WalletItems.FirstOrDefault();
        }

        await _repository.LogAsync(new OperationLog
        {
            ItemType = "WALLET",
            ItemId = item.Id,
            ItemTitle = item.Title,
            OperationType = "DELETE",
            DeviceName = Environment.MachineName
        });
        RaiseCounts();
        RaiseWalletSelectionState();
        if (updateStatus)
        {
            await LoadTimelineAsync();
            StatusMessage = _localization.Format("MovedToRecycleBinFormat", item.Title);
        }
    }

    private async Task SynchronizeBoundTotpAsync(PasswordEntry entry)
    {
        var existing = await _repository.GetSecureItemsByBoundPasswordIdAsync(entry.Id, includeDeleted: true);
        var active = existing.Where(item => !item.IsDeleted).OrderBy(item => item.Id).ToArray();
        var data = TotpDataResolver.FromAuthenticatorKey(entry.AuthenticatorKey, entry.Title, entry.Username);
        if (data is null || string.IsNullOrWhiteSpace(data.Secret))
        {
            foreach (var item in active)
            {
                await _repository.SoftDeleteSecureItemAsync(item.Id);
            }

            return;
        }

        var primary = active.FirstOrDefault() ?? existing.OrderBy(item => item.Id).FirstOrDefault() ?? new SecureItem
        {
            ItemType = VaultItemType.Totp,
            CreatedAt = DateTimeOffset.UtcNow
        };

        primary.ItemType = VaultItemType.Totp;
        primary.Title = entry.Title;
        primary.Notes = string.IsNullOrWhiteSpace(data.AccountName) ? entry.Username : data.AccountName;
        primary.ItemData = TotpDataResolver.ToItemData(data);
        primary.BoundPasswordId = entry.Id;
        primary.CategoryId = entry.CategoryId;
        primary.KeepassDatabaseId = entry.KeepassDatabaseId;
        primary.KeepassGroupPath = entry.KeepassGroupPath;
        primary.KeepassEntryUuid = entry.KeepassEntryUuid;
        primary.KeepassGroupUuid = entry.KeepassGroupUuid;
        primary.MdbxDatabaseId = entry.MdbxDatabaseId;
        primary.MdbxFolderId = entry.MdbxFolderId;
        primary.BitwardenVaultId = entry.BitwardenVaultId;
        primary.BitwardenFolderId = entry.BitwardenFolderId;
        primary.BitwardenCipherId = entry.BitwardenCipherId;
        primary.BitwardenRevisionDate = entry.BitwardenRevisionDate;
        primary.BitwardenLocalModified = entry.BitwardenLocalModified;
        primary.IsFavorite = entry.IsFavorite;
        primary.IsDeleted = false;
        primary.DeletedAt = null;
        primary.SyncStatus = entry.BitwardenVaultId is null ? SyncStatus.None : SyncStatus.Pending;
        await _repository.SaveSecureItemAsync(primary);

        foreach (var duplicate in active.Skip(1))
        {
            await _repository.SoftDeleteSecureItemAsync(duplicate.Id);
        }
    }

    private static SecureItem BuildVirtualTotpItem(PasswordEntry entry)
    {
        var data = TotpDataResolver.FromAuthenticatorKey(entry.AuthenticatorKey, entry.Title, entry.Username);
        return new SecureItem
        {
            Id = -entry.Id,
            ItemType = VaultItemType.Totp,
            Title = entry.Title,
            Notes = string.IsNullOrWhiteSpace(data?.AccountName) ? entry.Username : data.AccountName,
            ItemData = data is null ? "{}" : TotpDataResolver.ToItemData(data),
            BoundPasswordId = entry.Id,
            CategoryId = entry.CategoryId,
            IsFavorite = entry.IsFavorite,
            CreatedAt = entry.CreatedAt,
            UpdatedAt = entry.UpdatedAt
        };
    }

    private bool MatchesPasswordFilters(PasswordEntry item)
    {
        if (SelectedPasswordFolderFilter?.Id is { } folderId)
        {
            if (folderId == -1)
            {
                if (item.CategoryId is not null)
                {
                    return false;
                }
            }
            else if (item.CategoryId != folderId)
            {
                return false;
            }
        }

        if (QuickFilterFavorite && !item.IsFavorite)
        {
            return false;
        }

        if (QuickFilter2Fa && !item.HasAuthenticator)
        {
            return false;
        }

        if (QuickFilterNotes && string.IsNullOrWhiteSpace(item.Notes))
        {
            return false;
        }

        if (QuickFilterPasskey && string.IsNullOrWhiteSpace(item.PasskeyBindings))
        {
            return false;
        }

        if (QuickFilterBoundNote && item.BoundNoteId is null)
        {
            return false;
        }

        if (QuickFilterUncategorized && item.CategoryId is not null)
        {
            return false;
        }

        if (QuickFilterLocalOnly && !IsLocalOnlyPassword(item))
        {
            return false;
        }

        if (QuickFilterAttachments && !item.HasAttachments)
        {
            return false;
        }

        return MatchesPasswordSearch(item, SearchText);
    }

    private bool MatchesPasswordSearch(PasswordEntry item, string query)
    {
        var term = query.Trim();
        if (term.Length == 0)
        {
            return true;
        }

        if (ContainsAny(term,
            item.Title,
            item.Username,
            item.Website,
            item.Notes,
            item.AuthenticatorKey,
            item.AppName,
            item.AppPackageName,
            item.Email,
            item.Phone,
            item.AddressLine,
            item.City,
            item.State,
            item.ZipCode,
            item.Country,
            item.CreditCardHolder,
            item.CreditCardExpiry,
            item.SsoProvider,
            item.PasskeyBindings,
            item.WifiMetadata,
            item.SshKeyData,
            item.KeepassGroupPath ?? "",
            item.MdbxFolderId ?? "",
            item.BitwardenFolderId ?? ""))
        {
            return true;
        }

        if (_passwordCustomFields.TryGetValue(item.Id, out var fields) &&
            fields.Any(field => ContainsAny(term, field.Title, field.Value)))
        {
            return true;
        }

        return _passwordAttachments.TryGetValue(item.Id, out var attachments) &&
            attachments.Any(attachment => ContainsAny(
                term,
                attachment.FileName,
                attachment.ContentType,
                attachment.StoragePath,
                attachment.KeepassBinaryRef ?? ""));
    }

    private static bool IsLocalOnlyPassword(PasswordEntry item)
    {
        return item.BitwardenVaultId is null &&
            item.KeepassDatabaseId is null &&
            item.MdbxDatabaseId is null;
    }

    private static bool ContainsAny(string query, params string[] values) =>
        values.Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase));

    private IEnumerable<PasswordEntry> GetPasswordSiblings(PasswordEntry entry)
    {
        var key = BuildSiblingGroupKey(entry);
        return Passwords
            .Where(item => BuildSiblingGroupKey(item) == key)
            .OrderBy(item => item.Id == 0 ? long.MaxValue : item.Id);
    }

    private IEnumerable<PasswordEntry> GetDeletedPasswordSiblings(PasswordEntry entry)
    {
        var key = BuildSiblingGroupKey(entry);
        return DeletedPasswords
            .Where(item => BuildSiblingGroupKey(item) == key)
            .OrderBy(item => item.Id == 0 ? long.MaxValue : item.Id);
    }

    private IEnumerable<PasswordEntry> GetArchivedPasswordSiblings(PasswordEntry entry)
    {
        var key = BuildSiblingGroupKey(entry);
        return ArchivedPasswords
            .Where(item => BuildSiblingGroupKey(item) == key)
            .OrderBy(item => item.Id == 0 ? long.MaxValue : item.Id);
    }

    private static string BuildSiblingGroupKey(PasswordEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.ReplicaGroupId))
        {
            return $"replica:{entry.ReplicaGroupId.Trim()}";
        }

        var sourceKey = entry.BitwardenCipherId is not null
            ? $"bw:{entry.BitwardenVaultId}:{entry.BitwardenCipherId}"
            : entry.BitwardenVaultId is not null
                ? $"bw-local:{entry.BitwardenVaultId}:{entry.BitwardenFolderId ?? ""}"
                : entry.KeepassDatabaseId is not null
                    ? $"kp:{entry.KeepassDatabaseId}:{entry.KeepassGroupPath ?? ""}"
                    : $"local:{entry.CategoryId?.ToString() ?? "root"}";
        return string.Join("|",
            sourceKey,
            entry.Title.Trim().ToLowerInvariant(),
            NormalizeWebsiteForSiblingGroupKey(entry.Website),
            entry.Username.Trim().ToLowerInvariant());
    }

    private static string NormalizeWebsiteForSiblingGroupKey(string value)
    {
        var normalized = value
            .Trim()
            .ToLowerInvariant();

        if (normalized.StartsWith("http://", StringComparison.Ordinal))
        {
            normalized = normalized["http://".Length..];
        }
        else if (normalized.StartsWith("https://", StringComparison.Ordinal))
        {
            normalized = normalized["https://".Length..];
        }

        if (normalized.StartsWith("www.", StringComparison.Ordinal))
        {
            normalized = normalized["www.".Length..];
        }

        return normalized.TrimEnd('/');
    }

    private static IEnumerable<string> SplitAndNormalizeWebsites(string value)
    {
        return value
            .Split([',', ';', '\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeWebsiteForSecurityAnalysis)
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeWebsiteForSecurityAnalysis(string value)
    {
        var normalized = NormalizeWebsiteForSiblingGroupKey(value);
        var slashIndex = normalized.IndexOf('/', StringComparison.Ordinal);
        if (slashIndex >= 0)
        {
            normalized = normalized[..slashIndex];
        }

        var queryIndex = normalized.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex >= 0)
        {
            normalized = normalized[..queryIndex];
        }

        var fragmentIndex = normalized.IndexOf('#', StringComparison.Ordinal);
        if (fragmentIndex >= 0)
        {
            normalized = normalized[..fragmentIndex];
        }

        return normalized.TrimEnd('.');
    }

    private static string HashPasswordForSecurityCache(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private sealed record SecurityPasswordSnapshot(PasswordEntry Entry, string PlainPassword, string[] NormalizedWebsites);
    private sealed record WebsiteSnapshot(PasswordEntry Entry, string Website);
    private sealed record CompromisedPasswordResult(string PasswordHash, int ExposureCount);
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
    }

    private void QueueSaveSettings()
    {
        _ = SaveSettingsAsync();
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            await _settingsService.SaveAsync();
        }
        catch (Exception ex)
        {
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
        OnPropertyChanged(nameof(GeneratorLengthText));
        OnPropertyChanged(nameof(GeneratedPasswordStrengthText));
        OnPropertyChanged(nameof(WebDavConnectionStatusText));
        OnPropertyChanged(nameof(LegacyVaultImportPromptText));
        OnPropertyChanged(nameof(WebDavBackupOptionsSummaryText));
        RefreshVaultSources();
        RaiseWebDavBackupHistoryState();
        RaisePasswordQuickAccessState();
        RaiseCounts();
        OnPropertyChanged(nameof(SecurityIssueCountText));
        OnPropertyChanged(nameof(NotePreviewMarkdown));
        OnPropertyChanged(nameof(NotePlainPreview));
        if (!_hasCompromisedPasswordCheckResults)
        {
            CompromisedPasswordStatus = _localization.Get("CompromisedPasswordNotChecked");
        }
    }

    private void RaiseLegacyVaultImportPrompt()
    {
        OnPropertyChanged(nameof(HasLegacyVaultImportPrompt));
        OnPropertyChanged(nameof(LegacyVaultImportPromptText));
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
            new("dark", _localization.Get("Dark")));

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

        ReplaceOptions(
            SecurityQuestionOptions,
            _securityQuestionService.PredefinedQuestions
                .Select(question => new SettingsChoice(question.Id, question.Text))
                .ToArray());

        OnPropertyChanged(nameof(FilteredPasswords));
    }

    private static void ReplaceOptions(ObservableCollection<SettingsChoice> target, params SettingsChoice[] choices)
    {
        target.Clear();
        foreach (var choice in choices)
        {
            target.Add(choice);
        }
    }

    private void RefreshPlatformIntegrationCapabilities()
    {
        PlatformIntegrationCapabilities.Clear();
        foreach (var capability in _sourcePlatformIntegrationCapabilities)
        {
            var descriptionKey = $"Integration.{capability.Key}.Description";
            var localizedDescription = _localization.Get(descriptionKey);
            PlatformIntegrationCapabilities.Add(new LocalizedPlatformIntegrationCapability(
                capability.Key,
                _localization.Get($"Integration.{capability.Key}.Title"),
                localizedDescription == descriptionKey ? capability.Description : localizedDescription,
                LocalizeFeatureStatus(capability.Status),
                capability.UnsupportedReason ?? "",
                capability.Status));
        }

        RaisePlatformIntegrationState();
    }

    private void RaisePlatformIntegrationState()
    {
        OnPropertyChanged(nameof(PlatformIntegrationSummaryText));
        OnPropertyChanged(nameof(CanUseTrayIntegration));
        OnPropertyChanged(nameof(CanUseGlobalHotkeyIntegration));
        OnPropertyChanged(nameof(CanUseBrowserBridgeIntegration));
        OnPropertyChanged(nameof(CanOpenExternalLinks));
        OnPropertyChanged(nameof(CanUseFilePicker));
        OnPropertyChanged(nameof(TrayIntegrationStatusText));
        OnPropertyChanged(nameof(GlobalHotkeyIntegrationStatusText));
        OnPropertyChanged(nameof(BrowserBridgeIntegrationStatusText));
        OnPropertyChanged(nameof(ExternalLinksIntegrationStatusText));
        OnPropertyChanged(nameof(FilePickerIntegrationStatusText));
        OpenGitHubRepositoryCommand.NotifyCanExecuteChanged();
        ImportMonicaJsonFileCommand.NotifyCanExecuteChanged();
        ImportPasswordCsvFileCommand.NotifyCanExecuteChanged();
        ImportTotpCsvFileCommand.NotifyCanExecuteChanged();
        ImportNoteCsvFileCommand.NotifyCanExecuteChanged();
        ImportAegisJsonFileCommand.NotifyCanExecuteChanged();
        SaveMonicaJsonExportCommand.NotifyCanExecuteChanged();
        SavePasswordCsvExportCommand.NotifyCanExecuteChanged();
        SaveTotpCsvExportCommand.NotifyCanExecuteChanged();
        SaveNoteCsvExportCommand.NotifyCanExecuteChanged();
        SaveWalletCsvExportCommand.NotifyCanExecuteChanged();
        SaveAegisJsonExportCommand.NotifyCanExecuteChanged();
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

    private void RefreshCapabilities()
    {
        Capabilities.Clear();
        foreach (var capability in _sourceCapabilities)
        {
            var canToggle = capability.Status is not (PlatformFeatureStatus.Unsupported or PlatformFeatureStatus.Planned);
            Capabilities.Add(new LocalizedPlatformCapability(
                capability.Key,
                _localization.Get($"Capability.{capability.Key}.Title"),
                _localization.Get($"Capability.{capability.Key}.Description"),
                LocalizeFeatureStatus(capability.Status),
                _settingsService.IsFeatureEnabled(capability.Key),
                canToggle,
                _localization.FeatureEnabled,
                _localization.FeatureDisabled,
                capability.UnsupportedReason ?? "",
                UpdateFeatureToggle));
        }
    }

    private void UpdateFeatureToggle(string key, bool isEnabled)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        _settingsService.SetFeatureEnabled(key, isEnabled);
        QueueSaveSettings();
    }

    private string LocalizeFeatureStatus(PlatformFeatureStatus status)
    {
        return status switch
        {
            PlatformFeatureStatus.Available => _localization.Available,
            PlatformFeatureStatus.DesktopEquivalent => _localization.DesktopEquivalent,
            PlatformFeatureStatus.PlatformLimited => _localization.PlatformLimited,
            PlatformFeatureStatus.Unsupported => _localization.Get("Unsupported"),
            PlatformFeatureStatus.Planned => _localization.Planned,
            _ => status.ToString()
        };
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

    private bool IsPlatformIntegrationUsable(string key) => GetPlatformIntegration(key).IsUsable;

    private string FormatPlatformIntegrationStatus(string key)
    {
        var capability = GetPlatformIntegration(key);
        var status = LocalizeFeatureStatus(capability.Status);
        return string.IsNullOrWhiteSpace(capability.UnsupportedReason)
            ? status
            : $"{status}: {capability.UnsupportedReason}";
    }

    private PlatformIntegrationCapability GetPlatformIntegration(string key) =>
        _sourcePlatformIntegrationCapabilities.FirstOrDefault(
            item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
        ?? new PlatformIntegrationCapability(
            key,
            PlatformFeatureStatus.Unsupported,
            "This platform adapter has not declared this feature.",
            "This platform adapter has not declared this feature.");

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

        Application.Current.RequestedThemeVariant = theme switch
        {
            "light" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    private void LoadNoteIntoEditor(SecureItem? item)
    {
        if (item is null)
        {
            return;
        }

        var decoded = NoteContentCodec.DecodeFromItem(item);
        NoteTitle = item.Title;
        NoteContent = decoded.Content;
        NoteTagsText = string.Join(", ", decoded.Tags);
        NoteIsMarkdown = decoded.IsMarkdown;
        NoteIsFavorite = item.IsFavorite;
        NotePreviewMode = decoded.IsMarkdown;
        StatusMessage = _localization.Format("EditingNoteFormat", item.Title);
    }
}
