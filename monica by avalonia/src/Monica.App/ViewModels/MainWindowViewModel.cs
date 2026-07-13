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
using Avalonia.Media.Imaging;
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
public sealed record NoteOutlineItem(int Level, string Title, int LineNumber, Thickness Indent);
public sealed record NoteReferenceItem(string Label, string Target, int LineNumber, bool IsImage);
public sealed record NoteImagePreviewItem(string StoragePath, string DisplayName, string SizeText, Bitmap Image);
public sealed record NoteTreeGroup(string Name, int Count, IReadOnlyList<SecureItem> Items, bool IsUntagged);
public sealed record GeneratorHistoryItem(string Value, string ModeLabel, string StrengthText, string CreatedAtText);
public sealed class PasswordListRow
{
    public PasswordListRow(
        string key,
        PasswordEntry entry,
        IReadOnlyList<PasswordEntry> members,
        bool isStackHeader,
        bool isStackChild,
        bool isFirstStackChild,
        bool isLastStackChild,
        bool isExpanded)
    {
        Key = key;
        Entry = entry;
        Members = members;
        IsStackHeader = isStackHeader;
        IsStackChild = isStackChild;
        IsFirstStackChild = isFirstStackChild;
        IsLastStackChild = isLastStackChild;
        IsExpanded = isExpanded;
    }

    public string Key { get; }
    public PasswordEntry Entry { get; }
    public IReadOnlyList<PasswordEntry> Members { get; }
    public bool IsStackHeader { get; }
    public bool IsStackChild { get; }
    public bool IsFirstStackChild { get; }
    public bool IsLastStackChild { get; }
    public bool IsPasswordEntryRow => !IsStackHeader;
    public bool IsPlainPassword => IsPasswordEntryRow && !IsStackChild;
    public bool IsExpanded { get; }
    public bool IsCollapsed => IsStackHeader && !IsExpanded;
    public string StackCountText => Members.Count.ToString(CultureInfo.InvariantCulture);
    public string StackSubtitle => Members.Count == 1
        ? Entry.Username
        : string.Join(" / ", Members
            .Select(item => string.IsNullOrWhiteSpace(item.Username) ? item.Website : item.Username)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2));
    public bool HasGroupFavorite => Members.Any(item => item.IsFavorite);
    public bool HasGroupAuthenticator => Members.Any(item => item.HasAuthenticator);
    public bool HasGroupAttachments => Members.Any(item => item.HasAttachments);
    public Thickness RowMargin => IsStackChild ? new Thickness(22, 0, 0, 0) : new Thickness(0, 0, 0, 4);
    public double RowMinHeight => IsStackChild ? 50 : 58;
    public Thickness StackLineMargin => new(-5, IsFirstStackChild ? -8 : -34, 0, IsLastStackChild ? -8 : -34);
    public bool IsGroupSelected
    {
        get => Members.Count > 0 && Members.All(item => item.IsSelected);
        set
        {
            foreach (var member in Members)
            {
                member.IsSelected = value;
            }
        }
    }
}

public sealed partial class NoteEditorTab : ObservableObject
{
    public NoteEditorTab(long id, SecureItem? source, string title)
    {
        Id = id;
        Source = source;
        Title = string.IsNullOrWhiteSpace(title) ? "New Note" : title.Trim();
        DraftTitle = Title;
    }

    public long Id { get; }
    public SecureItem? Source { get; set; }
    public bool DraftInitialized { get; set; }
    public string DraftTitle { get; set; } = "";
    public string DraftContent { get; set; } = "";
    public string DraftTagsText { get; set; } = "";
    public bool DraftIsMarkdown { get; set; } = true;
    public bool DraftIsFavorite { get; set; }
    public bool DraftPreviewMode { get; set; }
    public bool DraftSplitPreviewMode { get; set; }
    public int DraftSelectionStart { get; set; }
    public int DraftSelectionEnd { get; set; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private bool _isSelected;
}

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
internal sealed record PasswordDetailSnapshot(
    PasswordEntry Entry,
    IReadOnlyList<PasswordEntry> Siblings,
    Category? Category,
    SecureItem? BoundNote,
    IReadOnlyList<Attachment> Attachments,
    IReadOnlyList<CustomField> CustomFields,
    IReadOnlyList<PasswordHistoryDisplayItem> History);
internal sealed record PasswordDetailSourceSnapshot(
    PasswordEntry Entry,
    IReadOnlyList<PasswordEntry> SiblingCandidates,
    IReadOnlyList<Category> Categories,
    IReadOnlyList<SecureItem> NoteItems,
    IReadOnlyDictionary<long, IReadOnlyList<Attachment>> PasswordAttachments,
    IReadOnlyDictionary<long, IReadOnlyList<CustomField>> PasswordCustomFields);
public sealed record PasswordFolderFilterChoice(
    long? Id,
    string Name,
    int Count,
    string DisplayName = "",
    int Level = 0,
    bool IsSystemNode = false,
    string SelectionKey = "",
    string? PathPrefix = null,
    bool HasChildren = false,
    bool IsExpanded = false)
{
    public string FolderDisplayName => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;
    public Thickness Indent => new(Math.Max(0, Level) * 14, 0, 0, 0);
    public bool IsCollapsed => HasChildren && !IsExpanded;
}
public sealed record TotpFilterChoice(string Key, string Label, int Count, int Level, bool IsSelected)
{
    public Thickness Indent => new(Math.Max(0, Level) * 12, 0, 0, 0);
}
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
    private const int MaxGeneratorHistoryItems = 8;
    private const string GeneratorModeRandom = "random";
    private const string GeneratorModePassphrase = "passphrase";
    private const string GeneratorModePin = "pin";
    private const string GeneratorModeUsername = "username";
    private const string GeneratorTemplateBalanced = "balanced";
    private const string GeneratorTemplateMaximum = "maximum";
    private const string GeneratorTemplateMemorable = "memorable";
    private const string GeneratorTemplatePin = "pin";
    private const string GeneratorTemplateUsername = "username";
    private const string SimilarGeneratorCharacters = "0OolI1|`";
    private const string TotpFilterAll = "all";
    private const string TotpFilterFavorites = "favorites";
    private const string TotpFilterExpiringSoon = "expiring-soon";
    private const string TotpFilterUnbound = "unbound";
    private const string TotpFilterIssuerPrefix = "issuer:";
    private static readonly TimeSpan SelectedPasswordDetailsCoalesceDelay = TimeSpan.FromMilliseconds(60);
    private static readonly TimeSpan SelectedPasswordDetailsLoadingDelay = TimeSpan.FromMilliseconds(120);
    private static readonly string[] GeneratorPassphraseWords =
    [
        "amber", "atlas", "brisk", "cedar", "cinder", "cobalt", "coral", "delta",
        "ember", "falcon", "frost", "harbor", "ivory", "juniper", "kinetic", "linen",
        "meadow", "meteor", "nebula", "onyx", "orchid", "pixel", "quartz", "ripple",
        "saffron", "signal", "silver", "summit", "tundra", "velvet", "violet", "willow"
    ];
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
    private static readonly PlatformFilePickerFileType[] MarkdownFileTypes =
    [
        new("Markdown", ["*.md", "*.markdown"])
    ];
    private static readonly PlatformFilePickerFileType[] WalletCsvFileTypes =
    [
        new("Cards and Documents CSV", ["*.csv"])
    ];
    private static readonly PlatformFilePickerFileType[] AegisJsonFileTypes =
    [
        new("Aegis JSON", ["*.json"])
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
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly ITotpEditorDialogService _totpEditorDialogService;
    private readonly IWalletItemEditorDialogService _walletItemEditorDialogService;
    private readonly IMasterPasswordMaintenanceService _masterPasswordMaintenanceService;
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
    private IReadOnlyList<PasswordEntry> _filteredPasswords = [];
    private IReadOnlyList<PasswordListRow> _filteredPasswordRows = [];
    private bool _filteredPasswordsDirty = true;
    private bool _filteredPasswordRowsDirty = true;
    private int _selectedPasswordCount;
    private bool _suppressPasswordSelectionStateNotifications;
    private bool _isSyncingSelectedPasswordListRow;
    private bool _hasCompromisedPasswordCheckResults;
    private bool _isApplyingSettings;
    private bool _isApplyingPasswordSearchImmediately;
    private CancellationTokenSource? _passwordSearchDebounceCts;
    private CancellationTokenSource? _selectedPasswordDetailsCts;
    private int _selectedPasswordDetailsVersion;
    private bool _isLoadingNoteEditor;
    private int _noteImagePreviewVersion;
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
    public ObservableCollection<PasswordEntry> ArchivedPasswords { get; } = new ObservableRangeCollection<PasswordEntry>();
    public ObservableCollection<PasswordEntry> DeletedPasswords { get; } = new ObservableRangeCollection<PasswordEntry>();
    public ObservableCollection<SecureItem> NoteItems { get; } = new ObservableRangeCollection<SecureItem>();
    public ObservableCollection<NoteEditorTab> OpenNoteTabs { get; } = [];
    public ObservableCollection<NoteImagePreviewItem> NoteImagePreviewItems { get; } = [];
    public ObservableCollection<SecureItem> TotpItems { get; } = new ObservableRangeCollection<SecureItem>();
    public ObservableCollection<TotpFilterChoice> TotpFilterChoices { get; } = [];
    public ObservableCollection<SecureItem> WalletItems { get; } = new ObservableRangeCollection<SecureItem>();
    public ObservableCollection<Category> Categories { get; } = new ObservableRangeCollection<Category>();
    public ObservableCollection<LocalizedPlatformIntegrationCapability> PlatformIntegrationCapabilities { get; } = [];
    public ObservableCollection<LocalizedPlatformCapability> Capabilities { get; } = [];
    public ObservableCollection<LocalMdbxDatabase> MdbxDatabases { get; } = new ObservableRangeCollection<LocalMdbxDatabase>();
    public ObservableCollection<MdbxDatabaseDisplayItem> MdbxDatabaseItems { get; } = [];
    public ObservableCollection<TimelineEntry> TimelineEntries { get; } = new ObservableRangeCollection<TimelineEntry>();
    public ObservableCollection<SecuritySummaryItem> SecuritySummaryItems { get; } = [];
    public ObservableCollection<SecurityIssueItem> SecurityIssueItems { get; } = [];
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
    public ObservableCollection<SettingsChoice> GeneratorModeOptions { get; } = [];
    public ObservableCollection<SettingsChoice> GeneratorTemplateOptions { get; } = [];
    public ObservableCollection<GeneratorHistoryItem> GeneratedPasswordHistory { get; } = [];
    public ObservableCollection<PasswordFolderFilterChoice> PasswordFolderFilters { get; } = [];
    public IEnumerable<PasswordFolderFilterChoice> SystemPasswordFolderFilters =>
        PasswordFolderFilters.Where(item => item.IsSystemNode);
    public IEnumerable<PasswordFolderFilterChoice> RegularPasswordFolderFilters =>
        PasswordFolderFilters.Where(item => !item.IsSystemNode);
    public bool HasRegularPasswordFolderFilters => PasswordFolderFilters.Any(item => !item.IsSystemNode);

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
    private bool _generatorExcludeSimilarCharacters;

    [ObservableProperty]
    private string _generatorMode = GeneratorModeRandom;

    [ObservableProperty]
    private string _generatorTemplate = GeneratorTemplateBalanced;

    [ObservableProperty]
    private int _generatorWordCount = 4;

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
    [NotifyPropertyChangedFor(nameof(HasSelectedArchivedPassword))]
    private PasswordEntry? _selectedArchivedPassword;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDeletedPassword))]
    private PasswordEntry? _selectedDeletedPassword;

    [ObservableProperty]
    private bool _isCheckingCompromisedPasswords;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecoverableStatusMessage))]
    private bool _isLoadingVault;

    [ObservableProperty]
    private string _vaultLoadStageText = "";

    [ObservableProperty]
    private long _lastVaultLoadDurationMilliseconds;

    [ObservableProperty]
    private string _compromisedPasswordStatus = "";

    [ObservableProperty]
    private SecureItem? _selectedNote;

    [ObservableProperty]
    private NoteEditorTab? _selectedNoteTab;

    [ObservableProperty]
    private string _noteTitle = "";

    [ObservableProperty]
    private string _noteContent = "";

    [ObservableProperty]
    private string _noteTagsText = "";

    [ObservableProperty]
    private string _noteSearchText = "";

    [ObservableProperty]
    private bool _noteIsMarkdown = true;

    [ObservableProperty]
    private bool _notePreviewMode;

    [ObservableProperty]
    private bool _noteSplitPreviewMode;

    [ObservableProperty]
    private bool _noteIsFavorite;

    public string NoteLineNumbersText => BuildLineNumbersText(NoteContent);
    public int NoteLineCount => CountNoteLines(NoteContent);
    public int NoteWordCount => CountNoteWords(NoteContent);
    public int NoteCharacterCount => NoteContent.Length;
    public IReadOnlyList<NoteOutlineItem> NoteOutlineItems => BuildNoteOutlineItems(NoteContent);
    public IReadOnlyList<NoteReferenceItem> NoteReferenceItems => BuildNoteReferenceItems(NoteContent);
    public int NoteOutlineCount => NoteOutlineItems.Count;
    public int NoteReferenceCount => NoteReferenceItems.Count;
    public bool HasNoteOutlineItems => NoteOutlineCount > 0;
    public bool HasNoteReferenceItems => NoteReferenceCount > 0;
    public int NoteImagePreviewCount => NoteImagePreviewItems.Count;
    public bool HasNoteImagePreviewItems => NoteImagePreviewCount > 0;
    public string NoteFormatText => NoteIsMarkdown ? "Markdown" : "Plain text";
    public IReadOnlyList<SecureItem> FavoriteNoteItems => BuildFilteredNoteItems(favoritesOnly: true);
    public IReadOnlyList<SecureItem> FilteredNoteItems => BuildFilteredNoteItems(favoritesOnly: false);
    public IReadOnlyList<NoteTreeGroup> NoteTreeGroups => BuildNoteTreeGroups(FilteredNoteItems);
    public int FavoriteNoteCount => NoteItems.Count(item => item.IsFavorite);
    public bool HasFavoriteNoteItems => FavoriteNoteItems.Count > 0;
    public bool HasFilteredNoteItems => FilteredNoteItems.Count > 0;
    public bool HasNoteTreeGroups => NoteTreeGroups.Count > 0;
    public string NoteTreeStatusText => string.IsNullOrWhiteSpace(NoteSearchText)
        ? NoteCountText
        : $"{FilteredNoteItems.Count}/{NoteItems.Count}";
    public bool IsNoteEditorPaneVisible => !NotePreviewMode || NoteSplitPreviewMode;
    public bool IsNotePreviewPaneVisible => NotePreviewMode || NoteSplitPreviewMode;
    public GridLength NoteEditorColumnWidth => IsNoteEditorPaneVisible
        ? new GridLength(1, GridUnitType.Star)
        : new GridLength(0);
    public GridLength NotePreviewSeparatorColumnWidth => NoteSplitPreviewMode
        ? new GridLength(18)
        : new GridLength(0);
    public GridLength NotePreviewColumnWidth => IsNotePreviewPaneVisible
        ? new GridLength(1, GridUnitType.Star)
        : new GridLength(0);
    public Thickness NotePreviewContentPadding => NoteSplitPreviewMode
        ? new Thickness(0)
        : new Thickness(32, 0, 0, 0);
    public bool IsNoteTreePaneVisible => !NoteSplitPreviewMode;
    public GridLength NoteTreeColumnWidth => IsNoteTreePaneVisible
        ? new GridLength(280)
        : new GridLength(0);
    public bool IsNoteInspectorPaneVisible =>
        NoteWorkspaceViewportWidth <= 0 || NoteWorkspaceViewportWidth >= 780;
    public GridLength NoteInspectorColumnWidth => IsNoteInspectorPaneVisible
        ? new GridLength(260)
        : new GridLength(0);
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
    public GridLength GeneratorOptionsColumnWidth => IsOtherWorkspaceCompact
        ? new GridLength(300)
        : new GridLength(340);
    public Thickness GeneratorResultPanelPadding => IsOtherWorkspaceCompact
        ? new Thickness(18)
        : new Thickness(24);
    public Thickness GeneratorOptionsPanelPadding => IsOtherWorkspaceCompact
        ? new Thickness(14)
        : new Thickness(18);
    public double GeneratorOptionsSpacing => IsOtherWorkspaceCompact ? 12 : 18;
    public double GeneratorCheckboxSpacing => IsOtherWorkspaceCompact ? 6 : 10;
    public double GeneratorPasswordBoxMinHeight => IsOtherWorkspaceCompact ? 96 : 170;
    public double GeneratorHistoryPanelMaxHeight => IsOtherWorkspaceCompact ? 78 : 104;
    public bool ShowGeneratorStrengthSummaryCard => !IsOtherWorkspaceCompact;
    public string NoteEditorStatusText =>
        NoteSelectedCharacterCount > 0
            ? $"行 {NoteCaretLine}, 列 {NoteCaretColumn} · 已选 {NoteSelectedCharacterCount} · {NoteLineCount} 行 · {NoteWordCount} 词 · {NoteCharacterCount} 字符"
            : $"行 {NoteCaretLine}, 列 {NoteCaretColumn} · {NoteLineCount} 行 · {NoteWordCount} 词 · {NoteCharacterCount} 字符";
    public bool HasOpenNoteTabs => OpenNoteTabs.Count > 0;
    public double NoteTabWidth => CalculateNoteTabWidth(OpenNoteTabs.Count, NoteTabRailViewportWidth);
    public double NoteTabStripWidth
    {
        get
        {
            const double fallbackWidth = 720;
            const double minWidth = 260;
            const double maxWidth = 680;
            var viewportWidth = NoteWorkspaceViewportWidth;
            if (viewportWidth <= 0 || double.IsNaN(viewportWidth))
            {
                return Math.Min(fallbackWidth, maxWidth);
            }

            var treeWidth = IsNoteTreePaneVisible ? 280 : 0;
            return Math.Clamp(viewportWidth - treeWidth, minWidth, maxWidth);
        }
    }

    private double _noteTabRailViewportWidth;

    private double _noteWorkspaceViewportWidth;

    private double _otherWorkspaceViewportWidth;

    private double _otherWorkspaceViewportHeight;

    public double NoteTabRailViewportWidth
    {
        get => _noteTabRailViewportWidth;
        set
        {
            if (SetProperty(ref _noteTabRailViewportWidth, Math.Max(0, value)))
            {
                OnPropertyChanged(nameof(NoteTabWidth));
            }
        }
    }

    public double NoteWorkspaceViewportWidth
    {
        get => _noteWorkspaceViewportWidth;
        set
        {
            if (SetProperty(ref _noteWorkspaceViewportWidth, Math.Max(0, value)))
            {
                RaiseNoteWorkspaceLayoutState();
            }
        }
    }

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

    private static double CalculateNoteTabWidth(int tabCount, double viewportWidth)
    {
        const double maxWidth = 148;
        const double minWidth = 76;
        const double tabGap = 4;

        if (tabCount <= 0)
        {
            return maxWidth;
        }

        if (viewportWidth <= 0 || double.IsNaN(viewportWidth))
        {
            return tabCount switch
            {
                <= 1 => maxWidth,
                <= 4 => 136,
                <= 7 => 112,
                <= 10 => 92,
                _ => minWidth
            };
        }

        var widthThatFits = (viewportWidth - ((tabCount - 1) * tabGap) - 8) / tabCount;
        return Math.Clamp(widthThatFits, minWidth, maxWidth);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoteEditorStatusText))]
    private int _noteCaretLine = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoteEditorStatusText))]
    private int _noteCaretColumn = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoteEditorStatusText))]
    private int _noteSelectedCharacterCount;

    [ObservableProperty]
    private SecureItem? _selectedTotpItem;

    [ObservableProperty]
    private TotpItemDetailsViewModel? _selectedTotpDetails;

    [ObservableProperty]
    private string _selectedTotpFilterKey = TotpFilterAll;

    [ObservableProperty]
    private SecureItem? _selectedWalletItem;

    [ObservableProperty]
    private WalletItemDetailsViewModel? _selectedWalletDetails;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTimelineEntry))]
    private TimelineEntry? _selectedTimelineEntry;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedSecurityIssue))]
    private SecurityIssueItem? _selectedSecurityIssue;

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
    public string ArchivedPasswordCountText => _localization.Format("ArchivedPasswordCountFormat", ArchivedPasswords.Count);
    public string DeletedPasswordCountText => _localization.Format("DeletedPasswordCountFormat", DeletedPasswords.Count);
    public bool HasDeletedPasswords => DeletedPasswords.Count > 0;
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
    public string TotpCountText => _localization.Format("TotpCountFormat", TotpItems.Count);
    public int TotpExpiringSoonCount => TotpItems.Count(IsTotpExpiringSoon);
    public string TotpConsoleStatusText => _localization.Format("TotpConsoleStatusFormat", TotpItems.Count, TotpExpiringSoonCount);
    public string TotpFilteredStatusText => _localization.Format("TotpFilteredStatusFormat", FilteredTotpItems.Count, TotpItems.Count);
    public string TotpScanQrText => _localization.Get("TotpScanQr");
    public string TotpManualAddText => _localization.Get("TotpManualAdd");
    public string TotpMoreActionsText => _localization.Get("MoreActions");
    public string TotpFilterTitleText => _localization.Get("TotpFilterTitle");
    public string TotpIssuerGroupsText => _localization.Get("TotpIssuerGroups");
    public string TotpNoFilteredResultsText => _localization.Get("TotpNoFilteredResults");
    public string TotpEmptyStateText => HasTotpFilterOrSearch && HasTotpItems
        ? _localization.Get("TotpNoFilteredResults")
        : _localization.Get("TotpEmptyHint");
    public string ClearTotpFiltersText => _localization.Get("ClearTotpFilters");
    public string TotpShowHiddenText => _localization.Get("ShowHidden");
    public string TotpHelpText => _localization.Get("Help");
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
    public string GeneratorLengthText => _localization.Format("GeneratorLengthFormat", GeneratorLength);
    public string GeneratorWordCountText => _localization.Format("GeneratorWordCountFormat", GeneratorWordCount);
    public int GeneratorLengthMinimum => GeneratorMode == GeneratorModePin ? 4 : 8;
    public int GeneratorLengthMaximum => GeneratorMode == GeneratorModePin ? 32 : 128;
    public bool IsGeneratorPassphraseMode => GeneratorMode == GeneratorModePassphrase;
    public bool ShowGeneratorCharacterOptions => GeneratorMode is GeneratorModeRandom or GeneratorModeUsername;
    public bool ShowGeneratorLengthOptions => GeneratorMode is not GeneratorModePassphrase;
    public bool ShowGeneratorWordCountOptions => GeneratorMode == GeneratorModePassphrase;
    public bool HasGeneratedPasswordHistory => GeneratedPasswordHistory.Count > 0;
    public string SelectedGeneratorModeLabel => FindChoiceLabel(GeneratorModeOptions, GeneratorMode);
    public string SelectedGeneratorTemplateLabel => FindChoiceLabel(GeneratorTemplateOptions, GeneratorTemplate);
    public SettingsChoice? SelectedGeneratorModeOption
    {
        get => GeneratorModeOptions.FirstOrDefault(item => Equals(item.Value, GeneratorMode));
        set
        {
            if (value?.Value is string mode)
            {
                GeneratorMode = mode;
            }
        }
    }

    public SettingsChoice? SelectedGeneratorTemplateOption
    {
        get => GeneratorTemplateOptions.FirstOrDefault(item => Equals(item.Value, GeneratorTemplate));
        set
        {
            if (value?.Value is string template)
            {
                GeneratorTemplate = template;
            }
        }
    }

    public string GeneratorStrategySummaryText => GeneratorMode switch
    {
        GeneratorModePassphrase => _localization.Format(
            "GeneratorStrategyPassphraseFormat",
            SelectedGeneratorTemplateLabel,
            GeneratorWordCount),
        GeneratorModePin => _localization.Format(
            "GeneratorStrategyLengthFormat",
            SelectedGeneratorTemplateLabel,
            GeneratorLength),
        _ => _localization.Format(
            "GeneratorStrategyLengthFormat",
            SelectedGeneratorTemplateLabel,
            GeneratorLength)
    };
    public string GeneratedPasswordStrengthText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(GeneratedPassword))
            {
                return _localization.Get("GeneratorNoPassword");
            }

            var strength = _passwordGenerator.Analyze(GeneratedPassword);
            return _localization.Format(
                "GeneratedPasswordStrengthFormat",
                PasswordStrengthLocalization.Label(_localization, strength.Label),
                strength.Score,
                PasswordStrengthLocalization.Warnings(_localization, strength.Warnings));
        }
    }

    public string NotePreviewMarkdown => NoteIsMarkdown ? BuildNotePreviewMarkdown(NoteContent) : "";
    public string NotePlainPreview => NoteContentCodec.ToPlainPreview(NoteContent, NoteIsMarkdown);
    public int SelectedPasswordCount => _selectedPasswordCount;
    public bool HasSelectedPasswords => SelectedPasswordCount > 0;
    public bool HasSelectedPassword => SelectedPassword is not null;
    public bool HasNoSelectedPassword => SelectedPassword is null;
    public bool HasSelectedArchivedPassword => SelectedArchivedPassword is not null;
    public bool HasSelectedDeletedPassword => SelectedDeletedPassword is not null;
    public bool HasSelectedPasswordDetails => SelectedPasswordDetails is not null;
    public bool HasCurrentSelectedPasswordDetails =>
        SelectedPassword is not null &&
        SelectedPasswordDetails?.Entry.Id == SelectedPassword.Id;
    public bool HasSelectedPasswordLoadingState =>
        SelectedPassword is not null &&
        IsLoadingSelectedPasswordDetails;
    public int SelectedTotpCount => TotpItems.Count(item => item.IsSelected);
    public string SelectedTotpCountText => _localization.Format("SelectedTotpCountFormat", SelectedTotpCount);
    public bool HasSelectedTotpItems => SelectedTotpCount > 0;
    public bool HasSelectedTotpItem => SelectedTotpItem is not null;
    public bool HasTotpItems => TotpItems.Count > 0;
    public int SelectedWalletCount => WalletItems.Count(item => item.IsSelected);
    public string SelectedWalletCountText => _localization.Format("SelectedWalletCountFormat", SelectedWalletCount);
    public bool HasSelectedWalletItems => SelectedWalletCount > 0;
    public bool HasSelectedWalletItem => SelectedWalletItem is not null;
    public bool HasSelectedTimelineEntry => SelectedTimelineEntry is not null;
    public bool HasSelectedSecurityIssue => SelectedSecurityIssue is not null;
    public bool HasSelectedMdbxDatabaseItem => SelectedMdbxDatabaseItem is not null;
    public bool HasSelectedVaultSource => SelectedVaultSource is not null;
    public bool HasVaultSources => VaultSources.Count > 0;
    public bool HasWalletItems => WalletItems.Count > 0;
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
    public IReadOnlyList<SecureItem> FilteredTotpItems => TotpItems.Where(MatchesTotpFilters).ToArray();
    public IEnumerable<PasswordEntry> FilteredArchivedPasswords =>
        ArchivedPasswords.Where(item => MatchesPasswordSearch(item, SearchText));
    public IEnumerable<PasswordEntry> FilteredDeletedPasswords =>
        DeletedPasswords.Where(item => MatchesPasswordSearch(item, SearchText));
    public bool HasFilteredArchivedPasswords => FilteredArchivedPasswords.Any();
    public bool HasFilteredDeletedPasswords => FilteredDeletedPasswords.Any();
    public bool HasFilteredTotpItems => FilteredTotpItems.Count > 0;
    public bool HasTotpFilterOrSearch =>
        !string.Equals(SelectedTotpFilterKey, TotpFilterAll, StringComparison.OrdinalIgnoreCase) ||
        !string.IsNullOrWhiteSpace(SearchText);
    public bool HasTimelineEntries => TimelineEntries.Count > 0;
    public bool HasSecurityIssues => SecurityIssueItems.Count > 0;

    partial void OnSearchTextChanged(string value)
    {
        RaiseFilteredPasswordsChanged();
        OnPropertyChanged(nameof(FilteredArchivedPasswords));
        OnPropertyChanged(nameof(FilteredDeletedPasswords));
        OnPropertyChanged(nameof(HasFilteredArchivedPasswords));
        OnPropertyChanged(nameof(HasFilteredDeletedPasswords));
        SelectedArchivedPassword =
            FilteredArchivedPasswords.FirstOrDefault(item => item.Id == SelectedArchivedPassword?.Id) ??
            FilteredArchivedPasswords.FirstOrDefault();
        SelectedDeletedPassword =
            FilteredDeletedPasswords.FirstOrDefault(item => item.Id == SelectedDeletedPassword?.Id) ??
            FilteredDeletedPasswords.FirstOrDefault();
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

    partial void OnGeneratedPasswordChanged(string value) => OnPropertyChanged(nameof(GeneratedPasswordStrengthText));

    partial void OnGeneratorLengthChanged(int value)
    {
        GeneratorLength = Math.Clamp(value, GeneratorLengthMinimum, GeneratorLengthMaximum);
        RaiseGeneratorState();
    }

    partial void OnGeneratorWordCountChanged(int value)
    {
        GeneratorWordCount = Math.Clamp(value, 2, 8);
        RaiseGeneratorState();
    }

    partial void OnGeneratorModeChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            GeneratorMode = GeneratorModeRandom;
            return;
        }

        GeneratorLength = Math.Clamp(GeneratorLength, GeneratorLengthMinimum, GeneratorLengthMaximum);
        RaiseGeneratorState();
    }

    partial void OnGeneratorTemplateChanged(string value)
    {
        ApplyGeneratorTemplate(value);
        RaiseGeneratorState();
    }

    partial void OnGeneratorIncludeUppercaseChanged(bool value) => RaiseGeneratorState();

    partial void OnGeneratorIncludeLowercaseChanged(bool value) => RaiseGeneratorState();

    partial void OnGeneratorIncludeNumbersChanged(bool value) => RaiseGeneratorState();

    partial void OnGeneratorIncludeSymbolsChanged(bool value) => RaiseGeneratorState();

    partial void OnGeneratorExcludeSimilarCharactersChanged(bool value) => RaiseGeneratorState();

    private void RaiseGeneratorState()
    {
        OnPropertyChanged(nameof(GeneratorLengthText));
        OnPropertyChanged(nameof(GeneratorWordCountText));
        OnPropertyChanged(nameof(GeneratorLengthMinimum));
        OnPropertyChanged(nameof(GeneratorLengthMaximum));
        OnPropertyChanged(nameof(IsGeneratorPassphraseMode));
        OnPropertyChanged(nameof(ShowGeneratorCharacterOptions));
        OnPropertyChanged(nameof(ShowGeneratorLengthOptions));
        OnPropertyChanged(nameof(ShowGeneratorWordCountOptions));
        OnPropertyChanged(nameof(SelectedGeneratorModeLabel));
        OnPropertyChanged(nameof(SelectedGeneratorTemplateLabel));
        OnPropertyChanged(nameof(SelectedGeneratorModeOption));
        OnPropertyChanged(nameof(SelectedGeneratorTemplateOption));
        OnPropertyChanged(nameof(GeneratorStrategySummaryText));
        OnPropertyChanged(nameof(GeneratedPasswordStrengthText));
    }

    partial void OnNoteContentChanged(string value)
    {
        OnPropertyChanged(nameof(NotePreviewMarkdown));
        OnPropertyChanged(nameof(NotePlainPreview));
        OnPropertyChanged(nameof(NoteLineNumbersText));
        OnPropertyChanged(nameof(NoteLineCount));
        OnPropertyChanged(nameof(NoteWordCount));
        OnPropertyChanged(nameof(NoteCharacterCount));
        OnPropertyChanged(nameof(NoteOutlineItems));
        OnPropertyChanged(nameof(NoteReferenceItems));
        OnPropertyChanged(nameof(NoteOutlineCount));
        OnPropertyChanged(nameof(NoteReferenceCount));
        OnPropertyChanged(nameof(HasNoteOutlineItems));
        OnPropertyChanged(nameof(HasNoteReferenceItems));
        OnPropertyChanged(nameof(NoteEditorStatusText));
        MarkSelectedNoteTabDirty();
        _ = RefreshNoteImagePreviewsAsync(value);
    }

    partial void OnNoteTagsTextChanged(string value) => MarkSelectedNoteTabDirty();

    partial void OnNoteIsMarkdownChanged(bool value)
    {
        OnPropertyChanged(nameof(NotePreviewMarkdown));
        OnPropertyChanged(nameof(NotePlainPreview));
        OnPropertyChanged(nameof(NoteFormatText));
        MarkSelectedNoteTabDirty();
    }

    partial void OnNotePreviewModeChanged(bool value)
    {
        if (value && NoteSplitPreviewMode)
        {
            NoteSplitPreviewMode = false;
        }

        RaiseNoteEditorLayoutState();
        CaptureSelectedNoteTabViewState();
    }

    partial void OnNoteSplitPreviewModeChanged(bool value)
    {
        if (value && NotePreviewMode)
        {
            NotePreviewMode = false;
        }

        RaiseNoteEditorLayoutState();
        CaptureSelectedNoteTabViewState();
    }

    partial void OnNoteTitleChanged(string value)
    {
        MarkSelectedNoteTabDirty();
    }

    partial void OnNoteSearchTextChanged(string value) => RaiseNoteTreeState();

    partial void OnSelectedNoteChanged(SecureItem? value)
    {
        if (_isLoadingNoteEditor)
        {
            return;
        }

        if (value is not null)
        {
            OpenNoteTab(value);
            return;
        }

        if (!_isLoadingNoteEditor)
        {
            SelectedNoteTab = null;
            ResetNoteEditor();
        }
    }

    partial void OnSelectedNoteTabChanged(NoteEditorTab? oldValue, NoteEditorTab? newValue)
    {
        if (!_isLoadingNoteEditor && oldValue is not null)
        {
            CaptureNoteEditorState(oldValue, markDirty: false);
        }

        LoadNoteTab(newValue);
        RefreshNoteTabState();
    }

    partial void OnSelectedTotpItemChanged(SecureItem? value)
    {
        if (value is not null)
        {
            RefreshTotpDisplay(value);
        }

        SelectedTotpDetails = value is null ? null : new TotpItemDetailsViewModel(_localization, value);
        OnPropertyChanged(nameof(HasSelectedTotpItem));
    }

    partial void OnSelectedTotpFilterKeyChanged(string value) => RaiseTotpFilterState();

    partial void OnSelectedWalletItemChanged(SecureItem? value)
    {
        SelectedWalletDetails = value is null ? null : new WalletItemDetailsViewModel(_localization, value);
        OnPropertyChanged(nameof(HasSelectedWalletItem));
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
    private void ShowTotpDetails(SecureItem? item)
    {
        if (item is not null)
        {
            SelectedTotpItem = item;
        }
    }

    [RelayCommand]
    private void SelectTotpFilter(string? key)
    {
        SelectedTotpFilterKey = string.IsNullOrWhiteSpace(key) ? TotpFilterAll : key;
    }

    [RelayCommand]
    private void ClearTotpFilters()
    {
        SearchText = "";
        SelectedTotpFilterKey = TotpFilterAll;
        RaiseTotpFilterState();
        StatusMessage = _localization.Get("ClearedTotpFilters");
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
        TrackTotpSelection(item);
        TotpItems.Insert(0, item);
        SelectedTotpItem = item;
        RaiseCounts();
        RaiseTotpFilterState(reconcileSelection: false);
        RaiseTotpSelectionState();
        await LoadTimelineAsync();
        StatusMessage = _localization.Format("SavedTotpFormat", item.Title);
    }

    [RelayCommand]
    private async Task ScanTotpQrAsync()
    {
        StatusMessage = _localization.Get("TotpScanQrFallback");
        await AddTotpAsync();
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
            SelectedTotpItem = item;
            SelectedTotpDetails = new TotpItemDetailsViewModel(_localization, item);
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
        if (SelectedTotpItem?.Id == item.Id)
        {
            SelectedTotpDetails = new TotpItemDetailsViewModel(_localization, item);
        }

        RaiseTotpFilterState();
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

        if (!await ConfirmMoveItemToRecycleBinAsync(item.Title))
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

        if (SelectedTotpItem is not null && selected.Any(item => item.Id == SelectedTotpItem.Id))
        {
            SelectedTotpDetails = new TotpItemDetailsViewModel(_localization, SelectedTotpItem);
        }

        RaiseTotpFilterState();
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

        if (!await ConfirmMoveSelectedItemsToRecycleBinAsync(selected.Length))
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
    private async Task CopyWalletFieldAsync(WalletFieldDisplayItem? field)
    {
        if (field is null || string.IsNullOrWhiteSpace(field.Value))
        {
            return;
        }

        await _clipboardService.SetTextAsync(field.Value);
        StatusMessage = _localization.Format("CopiedWalletFieldFormat", field.Label);
    }

    [RelayCommand]
    private async Task DeleteWalletItemAsync(SecureItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (!await ConfirmMoveItemToRecycleBinAsync(item.Title))
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

        if (!await ConfirmMoveSelectedItemsToRecycleBinAsync(selected.Length))
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
    private void AddNote()
    {
        var tab = new NoteEditorTab(-DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), null, _localization.Get("NewSecureNote"))
        {
            IsDirty = true,
            DraftInitialized = true,
            DraftContent = "",
            DraftTagsText = "",
            DraftIsMarkdown = true,
            DraftIsFavorite = false,
            DraftPreviewMode = false,
            DraftSplitPreviewMode = false
        };
        OpenNoteTabs.Add(tab);
        NotifyNoteTabsChanged();
        SelectedNoteTab = tab;
        StatusMessage = _localization.Get("EditingNewSecureNote");
    }

    [RelayCommand]
    private void OpenNote(SecureItem? item)
    {
        if (item is not null)
        {
            OpenNoteTab(item);
        }
    }

    [RelayCommand]
    private void SelectNoteTab(NoteEditorTab? tab)
    {
        if (tab is not null)
        {
            SelectedNoteTab = tab;
        }
    }

    [RelayCommand]
    private void CloseNoteTab(NoteEditorTab? tab)
    {
        tab ??= SelectedNoteTab;
        if (tab is null)
        {
            return;
        }

        var index = OpenNoteTabs.IndexOf(tab);
        OpenNoteTabs.Remove(tab);
        NotifyNoteTabsChanged();
        if (ReferenceEquals(SelectedNoteTab, tab))
        {
            SelectedNoteTab = OpenNoteTabs.Count == 0
                ? null
                : OpenNoteTabs[Math.Clamp(index, 0, OpenNoteTabs.Count - 1)];
        }

        RefreshNoteTabState();
    }

    private bool CanSelectPreviousNoteTab() =>
        SelectedNoteTab is not null && OpenNoteTabs.IndexOf(SelectedNoteTab) > 0;

    [RelayCommand(CanExecute = nameof(CanSelectPreviousNoteTab))]
    private void SelectPreviousNoteTab()
    {
        var index = SelectedNoteTab is null ? -1 : OpenNoteTabs.IndexOf(SelectedNoteTab);
        if (index > 0)
        {
            SelectedNoteTab = OpenNoteTabs[index - 1];
        }
    }

    private bool CanSelectNextNoteTab()
    {
        var index = SelectedNoteTab is null ? -1 : OpenNoteTabs.IndexOf(SelectedNoteTab);
        return index >= 0 && index < OpenNoteTabs.Count - 1;
    }

    [RelayCommand(CanExecute = nameof(CanSelectNextNoteTab))]
    private void SelectNextNoteTab()
    {
        var index = SelectedNoteTab is null ? -1 : OpenNoteTabs.IndexOf(SelectedNoteTab);
        if (index >= 0 && index < OpenNoteTabs.Count - 1)
        {
            SelectedNoteTab = OpenNoteTabs[index + 1];
        }
    }

    private void NotifyNoteTabsChanged()
    {
        OnPropertyChanged(nameof(HasOpenNoteTabs));
        OnPropertyChanged(nameof(NoteTabWidth));
    }

    private void RaiseNoteEditorLayoutState()
    {
        OnPropertyChanged(nameof(IsNoteEditorPaneVisible));
        OnPropertyChanged(nameof(IsNotePreviewPaneVisible));
        OnPropertyChanged(nameof(NoteEditorColumnWidth));
        OnPropertyChanged(nameof(NotePreviewSeparatorColumnWidth));
        OnPropertyChanged(nameof(NotePreviewColumnWidth));
        OnPropertyChanged(nameof(NotePreviewContentPadding));
        RaiseNoteWorkspaceLayoutState();
    }

    private void RaiseNoteWorkspaceLayoutState()
    {
        OnPropertyChanged(nameof(IsNoteTreePaneVisible));
        OnPropertyChanged(nameof(NoteTreeColumnWidth));
        OnPropertyChanged(nameof(NoteTabStripWidth));
        OnPropertyChanged(nameof(IsNoteInspectorPaneVisible));
        OnPropertyChanged(nameof(NoteInspectorColumnWidth));
    }

    private void RaiseOtherWorkspaceLayoutState()
    {
        OnPropertyChanged(nameof(IsOtherWorkspaceCompact));
        OnPropertyChanged(nameof(TotpAccountColumnWidth));
        OnPropertyChanged(nameof(TotpCodeColumnWidth));
        OnPropertyChanged(nameof(TotpInspectorColumnWidth));
        OnPropertyChanged(nameof(TotpCodeConsolePadding));
        OnPropertyChanged(nameof(TotpCodeFontSize));
        OnPropertyChanged(nameof(GeneratorOptionsColumnWidth));
        OnPropertyChanged(nameof(GeneratorResultPanelPadding));
        OnPropertyChanged(nameof(GeneratorOptionsPanelPadding));
        OnPropertyChanged(nameof(GeneratorOptionsSpacing));
        OnPropertyChanged(nameof(GeneratorCheckboxSpacing));
        OnPropertyChanged(nameof(GeneratorPasswordBoxMinHeight));
        OnPropertyChanged(nameof(GeneratorHistoryPanelMaxHeight));
        OnPropertyChanged(nameof(ShowGeneratorStrengthSummaryCard));
    }

    private void RaiseNoteTreeState()
    {
        OnPropertyChanged(nameof(FavoriteNoteItems));
        OnPropertyChanged(nameof(FilteredNoteItems));
        OnPropertyChanged(nameof(NoteTreeGroups));
        OnPropertyChanged(nameof(FavoriteNoteCount));
        OnPropertyChanged(nameof(HasFavoriteNoteItems));
        OnPropertyChanged(nameof(HasFilteredNoteItems));
        OnPropertyChanged(nameof(HasNoteTreeGroups));
        OnPropertyChanged(nameof(NoteTreeStatusText));
    }

    private void RefreshNoteTabState()
    {
        foreach (var tab in OpenNoteTabs)
        {
            tab.IsSelected = ReferenceEquals(tab, SelectedNoteTab);
        }

        SelectPreviousNoteTabCommand.NotifyCanExecuteChanged();
        SelectNextNoteTabCommand.NotifyCanExecuteChanged();
    }

    private IReadOnlyList<SecureItem> BuildFilteredNoteItems(bool favoritesOnly)
    {
        var query = NoteSearchText ?? "";
        return NoteItems
            .Where(item => (!favoritesOnly || item.IsFavorite) && MatchesNoteSearch(item, query))
            .OrderByDescending(item => item.IsFavorite)
            .ThenByDescending(item => item.UpdatedAt)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<NoteTreeGroup> BuildNoteTreeGroups(IReadOnlyList<SecureItem> notes)
    {
        var taggedGroups = new SortedDictionary<string, List<SecureItem>>(StringComparer.OrdinalIgnoreCase);
        var untagged = new List<SecureItem>();

        foreach (var note in notes)
        {
            var tags = GetNoteTreeTags(note);
            if (tags.Count == 0)
            {
                untagged.Add(note);
                continue;
            }

            foreach (var tag in tags)
            {
                if (!taggedGroups.TryGetValue(tag, out var bucket))
                {
                    bucket = [];
                    taggedGroups[tag] = bucket;
                }

                bucket.Add(note);
            }
        }

        var groups = taggedGroups
            .Select(pair => new NoteTreeGroup(pair.Key, pair.Value.Count, pair.Value, IsUntagged: false))
            .ToList();

        if (untagged.Count > 0)
        {
            groups.Add(new NoteTreeGroup("未分类", untagged.Count, untagged, IsUntagged: true));
        }

        return groups;
    }

    private static IReadOnlyList<string> GetNoteTreeTags(SecureItem item)
    {
        try
        {
            return NoteContentCodec.DecodeFromItem(item).Tags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static bool MatchesNoteSearch(SecureItem item, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var terms = query
            .Split([' ', '\t', '\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
        {
            return true;
        }

        var decodedContent = "";
        var decodedTags = "";
        try
        {
            var decoded = NoteContentCodec.DecodeFromItem(item);
            decodedContent = decoded.Content;
            decodedTags = string.Join(" ", decoded.Tags);
        }
        catch
        {
            // Keep the file tree usable even when a legacy note payload cannot be decoded.
        }

        return terms.All(term =>
            ContainsOrdinalIgnoreCase(item.Title, term) ||
            ContainsOrdinalIgnoreCase(item.Notes, term) ||
            ContainsOrdinalIgnoreCase(decodedContent, term) ||
            ContainsOrdinalIgnoreCase(decodedTags, term));
    }

    private static bool ContainsOrdinalIgnoreCase(string? source, string value) =>
        !string.IsNullOrEmpty(source) &&
        source.Contains(value, StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void InsertMarkdown(string? action)
    {
        var snippet = action switch
        {
            "h1" => "# Heading",
            "h2" => "## Heading",
            "bold" => "**bold text**",
            "italic" => "_italic text_",
            "quote" => "> Quote",
            "code" => "```\ncode\n```",
            "ul" => "- List item",
            "ol" => "1. List item",
            "todo" => "- [ ] Task",
            "table" => "| Column | Column |\n| --- | --- |\n| Value | Value |",
            "link" => "[link text](https://)",
            "hr" => "---",
            _ => ""
        };

        if (string.IsNullOrWhiteSpace(snippet))
        {
            return;
        }

        AppendNoteContentSnippet(snippet);
    }

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task InsertNoteImageAsync()
    {
        var markdown = await PickNoteImageMarkdownAsync();
        if (!string.IsNullOrWhiteSpace(markdown))
        {
            AppendNoteContentSnippet(markdown);
        }
    }

    public async Task<string?> PickNoteImageMarkdownAsync()
    {
        try
        {
            var file = await _fileSystemPickerService.OpenBinaryFileAsync("插入图片", NoteImageFileTypes);
            if (file is null)
            {
                return null;
            }

            var draft = await _passwordAttachmentFileService.StoreAttachmentAsync(
                file.FileName,
                file.Content,
                InferImageContentType(file.FileName));
            StatusMessage = $"已插入图片 {draft.FileName}";
            return NoteContentCodec.BuildInlineImageMarkdown(draft.StoragePath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"插入图片失败：{ex.Message}";
            return null;
        }
    }

    [RelayCommand]
    private async Task SaveNoteAsync()
    {
        if (SelectedNoteTab is not null)
        {
            CaptureNoteEditorState(SelectedNoteTab, markDirty: SelectedNoteTab.IsDirty);
            if (!CanSaveNoteTab(SelectedNoteTab))
            {
                StatusMessage = _localization.Get("NoteRequiresContent");
                return;
            }

            var savedNote = await SaveNoteTabAsync(SelectedNoteTab);
            SelectedNote = savedNote;
            await LoadTimelineAsync();
            RaiseCounts();
            StatusMessage = _localization.Format("SavedNoteFormat", savedNote.Title);
            return;
        }

        if (string.IsNullOrWhiteSpace(NoteTitle) && string.IsNullOrWhiteSpace(NoteContent))
        {
            StatusMessage = _localization.Get("NoteRequiresContent");
            return;
        }

        var sourceNote = SelectedNote;
        var payload = NoteContentCodec.BuildSavePayload(
            NoteTitle,
            NoteContent,
            NoteTagsText,
            NoteIsMarkdown,
            sourceNote is null ? [] : NoteContentCodec.DecodeImagePaths(sourceNote.ImagePaths));

        var item = sourceNote ?? new SecureItem
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
            OperationType = sourceNote is null ? "CREATE" : "UPDATE",
            DeviceName = Environment.MachineName
        });

        if (NoteItems.All(note => note.Id != item.Id))
        {
            NoteItems.Insert(0, item);
        }

        if (SelectedNoteTab is not null)
        {
            SelectedNoteTab.Source = item;
            SelectedNoteTab.Title = item.Title;
            CaptureNoteEditorState(SelectedNoteTab, markDirty: false);
            SelectedNoteTab.IsDirty = false;
        }

        SelectedNote = item;
        await LoadTimelineAsync();
        RaiseCounts();
        StatusMessage = _localization.Format("SavedNoteFormat", item.Title);
    }

    [RelayCommand]
    private async Task SaveAllNoteTabsAsync()
    {
        if (SelectedNoteTab is not null)
        {
            CaptureNoteEditorState(SelectedNoteTab, markDirty: SelectedNoteTab.IsDirty);
        }

        var dirtyTabs = OpenNoteTabs.Where(tab => tab.IsDirty).ToArray();
        if (dirtyTabs.Length == 0)
        {
            StatusMessage = "没有需要保存的笔记";
            return;
        }

        var savedCount = 0;
        var skippedCount = 0;
        foreach (var tab in dirtyTabs)
        {
            if (!CanSaveNoteTab(tab))
            {
                skippedCount++;
                continue;
            }

            await SaveNoteTabAsync(tab);
            savedCount++;
        }

        if (SelectedNoteTab?.Source is not null)
        {
            SelectedNote = SelectedNoteTab.Source;
        }

        if (savedCount > 0)
        {
            await LoadTimelineAsync();
            RaiseCounts();
        }

        StatusMessage = skippedCount == 0
            ? $"已保存 {savedCount} 个笔记"
            : $"已保存 {savedCount} 个笔记，{skippedCount} 个空笔记未保存";
    }

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task ImportMarkdownNoteAsync()
    {
        try
        {
            var file = await _fileSystemPickerService.OpenTextFileAsync("导入 Markdown", MarkdownFileTypes);
            if (file is null)
            {
                return;
            }

            var title = Path.GetFileNameWithoutExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(title))
            {
                title = _localization.Get("Untitled");
            }

            var tab = new NoteEditorTab(-DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), null, title)
            {
                IsDirty = true,
                DraftInitialized = true,
                DraftTitle = title,
                DraftContent = file.Content,
                DraftTagsText = "",
                DraftIsMarkdown = true,
                DraftIsFavorite = false,
                DraftPreviewMode = false,
                DraftSplitPreviewMode = false
            };

            OpenNoteTabs.Add(tab);
            NotifyNoteTabsChanged();
            SelectedNoteTab = tab;
            StatusMessage = $"已导入 Markdown 草稿 {file.FileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"导入 Markdown 失败：{ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task ExportCurrentNoteMarkdownAsync()
    {
        if (SelectedNoteTab is not null)
        {
            CaptureNoteEditorState(SelectedNoteTab, markDirty: SelectedNoteTab.IsDirty);
        }

        var title = string.IsNullOrWhiteSpace(NoteTitle)
            ? _localization.Get("Untitled")
            : NoteTitle.Trim();
        var suggestedFileName = $"{BuildSafeFileName(title)}.md";
        var content = NoteIsMarkdown
            ? NoteContent
            : NoteContentCodec.ToPlainPreview(NoteContent, NoteIsMarkdown);

        await SaveExportTextAsync("导出 Markdown", suggestedFileName, content, MarkdownFileTypes);
    }

    private static bool CanSaveNoteTab(NoteEditorTab tab) =>
        !string.IsNullOrWhiteSpace(tab.DraftTitle) ||
        !string.IsNullOrWhiteSpace(tab.DraftContent);

    private static string BuildSafeFileName(string title)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(title.Length);
        foreach (var character in title.Trim())
        {
            builder.Append(invalidCharacters.Contains(character) ? '_' : character);
        }

        var fileName = builder.ToString().Trim(' ', '.');
        return string.IsNullOrWhiteSpace(fileName) ? "untitled" : fileName;
    }

    private async Task<SecureItem> SaveNoteTabAsync(NoteEditorTab tab)
    {
        var sourceNote = tab.Source;
        var payload = NoteContentCodec.BuildSavePayload(
            tab.DraftTitle,
            tab.DraftContent,
            tab.DraftTagsText,
            tab.DraftIsMarkdown,
            sourceNote is null ? [] : NoteContentCodec.DecodeImagePaths(sourceNote.ImagePaths));

        var item = sourceNote ?? new SecureItem
        {
            ItemType = VaultItemType.Note,
            CreatedAt = DateTimeOffset.UtcNow
        };

        item.Title = payload.Title;
        item.Notes = payload.NotesCache;
        item.ItemData = payload.ItemData;
        item.ImagePaths = payload.ImagePaths;
        item.IsFavorite = tab.DraftIsFavorite;
        item.ItemType = VaultItemType.Note;
        item.SyncStatus = item.BitwardenVaultId is null ? SyncStatus.None : SyncStatus.Pending;

        await _repository.SaveSecureItemAsync(item);
        await _repository.LogAsync(new OperationLog
        {
            ItemType = "NOTE",
            ItemId = item.Id,
            ItemTitle = item.Title,
            OperationType = sourceNote is null ? "CREATE" : "UPDATE",
            DeviceName = Environment.MachineName
        });

        if (NoteItems.All(note => note.Id != item.Id))
        {
            NoteItems.Insert(0, item);
        }

        tab.Source = item;
        tab.Title = item.Title;
        tab.DraftTitle = item.Title;
        tab.DraftIsFavorite = item.IsFavorite;
        tab.IsDirty = false;
        return item;
    }

    [RelayCommand]
    private async Task ToggleNoteFavoriteAsync()
    {
        NoteIsFavorite = !NoteIsFavorite;
        if (SelectedNote is null)
        {
            MarkSelectedNoteTabDirty();
            return;
        }

        SelectedNote.IsFavorite = NoteIsFavorite;
        await _repository.SaveSecureItemAsync(SelectedNote);
        if (SelectedNoteTab is not null)
        {
            CaptureNoteEditorState(SelectedNoteTab, markDirty: false);
        }

        RaiseNoteTreeState();
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

        if (!await ConfirmMoveItemToRecycleBinAsync(item.Title))
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
        GeneratedPassword = GeneratorMode switch
        {
            GeneratorModePassphrase => GeneratePassphrase(),
            GeneratorModePin => GenerateFromAlphabet("0123456789", GeneratorLength),
            GeneratorModeUsername => GenerateUsername(),
            _ => GenerateRandomPasswordValue()
        };
        AddGeneratedPasswordHistory(GeneratedPassword);
        StatusMessage = _localization.Get("GeneratedPassword");
    }

    [RelayCommand]
    private void ResetGenerator()
    {
        GeneratorTemplate = GeneratorTemplateBalanced;
        ApplyGeneratorTemplate(GeneratorTemplateBalanced);
    }

    [RelayCommand]
    private void UseGeneratedPasswordHistoryItem(GeneratorHistoryItem? item)
    {
        if (item is null)
        {
            return;
        }

        GeneratedPassword = item.Value;
        StatusMessage = _localization.Get("GeneratedPasswordRestoredFromHistory");
    }

    [RelayCommand]
    private async Task CopyGeneratedPasswordHistoryItemAsync(GeneratorHistoryItem? item)
    {
        if (item is null)
        {
            return;
        }

        await _clipboardService.SetTextAsync(item.Value);
        StatusMessage = _localization.Get("CopiedGeneratedPassword");
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

    private string GenerateRandomPasswordValue()
    {
        if (!GeneratorExcludeSimilarCharacters)
        {
            return _passwordGenerator.GeneratePassword(
                GeneratorLength,
                GeneratorIncludeUppercase,
                GeneratorIncludeLowercase,
                GeneratorIncludeNumbers,
                GeneratorIncludeSymbols);
        }

        var groups = BuildGeneratorCharacterGroups(
            GeneratorIncludeUppercase,
            GeneratorIncludeLowercase,
            GeneratorIncludeNumbers,
            GeneratorIncludeSymbols,
            excludeSimilar: true);
        return GenerateFromGroups(groups, GeneratorLength);
    }

    private string GeneratePassphrase()
    {
        var words = Enumerable
            .Range(0, GeneratorWordCount)
            .Select(_ => GeneratorPassphraseWords[RandomNumberGenerator.GetInt32(GeneratorPassphraseWords.Length)])
            .ToList();

        var result = string.Join("-", words);
        if (GeneratorIncludeNumbers)
        {
            result += RandomNumberGenerator.GetInt32(10, 100).ToString(CultureInfo.InvariantCulture);
        }

        if (GeneratorIncludeSymbols)
        {
            result += PickCharacter("!@#$%?");
        }

        return result;
    }

    private string GenerateUsername()
    {
        var words = Enumerable
            .Range(0, 2)
            .Select(_ => GeneratorPassphraseWords[RandomNumberGenerator.GetInt32(GeneratorPassphraseWords.Length)])
            .ToArray();
        var suffix = GeneratorIncludeNumbers
            ? RandomNumberGenerator.GetInt32(100, 1000).ToString(CultureInfo.InvariantCulture)
            : "";
        var value = $"{words[0]}.{words[1]}{suffix}";

        if (value.Length <= GeneratorLength)
        {
            return value;
        }

        return value[..GeneratorLength].TrimEnd('.');
    }

    private static string GenerateFromAlphabet(string alphabet, int length)
    {
        if (string.IsNullOrEmpty(alphabet))
        {
            alphabet = "abcdefghijklmnopqrstuvwxyz";
        }

        var chars = new char[Math.Max(1, length)];
        for (var index = 0; index < chars.Length; index++)
        {
            chars[index] = PickCharacter(alphabet);
        }

        return new string(chars);
    }

    private static string GenerateFromGroups(IReadOnlyList<string> groups, int length)
    {
        if (groups.Count == 0)
        {
            groups = ["abcdefghijklmnopqrstuvwxyz"];
        }

        var required = groups
            .Select(PickCharacter)
            .ToList();
        var alphabet = string.Concat(groups);
        while (required.Count < length)
        {
            required.Add(PickCharacter(alphabet));
        }

        for (var index = required.Count - 1; index > 0; index--)
        {
            var swapIndex = RandomNumberGenerator.GetInt32(index + 1);
            (required[index], required[swapIndex]) = (required[swapIndex], required[index]);
        }

        return new string(required.Take(length).ToArray());
    }

    private static char PickCharacter(string alphabet) =>
        alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

    private static IReadOnlyList<string> BuildGeneratorCharacterGroups(
        bool includeUppercase,
        bool includeLowercase,
        bool includeNumbers,
        bool includeSymbols,
        bool excludeSimilar)
    {
        var groups = new List<string>(4);
        AddGeneratorGroup(groups, "ABCDEFGHIJKLMNOPQRSTUVWXYZ", includeUppercase, excludeSimilar);
        AddGeneratorGroup(groups, "abcdefghijklmnopqrstuvwxyz", includeLowercase, excludeSimilar);
        AddGeneratorGroup(groups, "0123456789", includeNumbers, excludeSimilar);
        AddGeneratorGroup(groups, "!@#$%^&*()-_=+[]{};:,.?", includeSymbols, excludeSimilar);
        return groups;
    }

    private static void AddGeneratorGroup(List<string> groups, string alphabet, bool include, bool excludeSimilar)
    {
        if (!include)
        {
            return;
        }

        var value = excludeSimilar
            ? new string(alphabet.Where(character => !SimilarGeneratorCharacters.Contains(character)).ToArray())
            : alphabet;
        if (!string.IsNullOrEmpty(value))
        {
            groups.Add(value);
        }
    }

    private void AddGeneratedPasswordHistory(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var existing = GeneratedPasswordHistory.FirstOrDefault(item => item.Value == value);
        if (existing is not null)
        {
            GeneratedPasswordHistory.Remove(existing);
        }

        var strength = _passwordGenerator.Analyze(value);
        GeneratedPasswordHistory.Insert(0, new GeneratorHistoryItem(
            value,
            SelectedGeneratorModeLabel,
            PasswordStrengthLocalization.Label(_localization, strength.Label),
            DateTimeOffset.Now.ToString("HH:mm", CultureInfo.CurrentCulture)));

        while (GeneratedPasswordHistory.Count > MaxGeneratorHistoryItems)
        {
            GeneratedPasswordHistory.RemoveAt(GeneratedPasswordHistory.Count - 1);
        }

        OnPropertyChanged(nameof(HasGeneratedPasswordHistory));
    }

    private void ApplyGeneratorTemplate(string value)
    {
        switch (value)
        {
            case GeneratorTemplateMaximum:
                GeneratorMode = GeneratorModeRandom;
                GeneratorLength = 32;
                GeneratorWordCount = 4;
                GeneratorIncludeUppercase = true;
                GeneratorIncludeLowercase = true;
                GeneratorIncludeNumbers = true;
                GeneratorIncludeSymbols = true;
                GeneratorExcludeSimilarCharacters = false;
                break;
            case GeneratorTemplateMemorable:
                GeneratorMode = GeneratorModePassphrase;
                GeneratorLength = 24;
                GeneratorWordCount = 4;
                GeneratorIncludeUppercase = false;
                GeneratorIncludeLowercase = true;
                GeneratorIncludeNumbers = true;
                GeneratorIncludeSymbols = false;
                GeneratorExcludeSimilarCharacters = true;
                break;
            case GeneratorTemplatePin:
                GeneratorMode = GeneratorModePin;
                GeneratorLength = 6;
                GeneratorWordCount = 4;
                GeneratorIncludeUppercase = false;
                GeneratorIncludeLowercase = false;
                GeneratorIncludeNumbers = true;
                GeneratorIncludeSymbols = false;
                GeneratorExcludeSimilarCharacters = false;
                break;
            case GeneratorTemplateUsername:
                GeneratorMode = GeneratorModeUsername;
                GeneratorLength = 18;
                GeneratorWordCount = 2;
                GeneratorIncludeUppercase = false;
                GeneratorIncludeLowercase = true;
                GeneratorIncludeNumbers = true;
                GeneratorIncludeSymbols = false;
                GeneratorExcludeSimilarCharacters = true;
                break;
            default:
                GeneratorMode = GeneratorModeRandom;
                GeneratorLength = 24;
                GeneratorWordCount = 4;
                GeneratorIncludeUppercase = true;
                GeneratorIncludeLowercase = true;
                GeneratorIncludeNumbers = true;
                GeneratorIncludeSymbols = true;
                GeneratorExcludeSimilarCharacters = false;
                break;
        }
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
        SelectedArchivedPassword =
            ArchivedPasswords.FirstOrDefault(item => item.Id == SelectedArchivedPassword?.Id) ??
            ArchivedPasswords.FirstOrDefault();
        SelectedDeletedPassword =
            DeletedPasswords.FirstOrDefault(item => item.Id == SelectedDeletedPassword?.Id) ??
            DeletedPasswords.FirstOrDefault();

        OnPropertyChanged(nameof(PasswordCountText));
        OnPropertyChanged(nameof(ArchivedPasswordCountText));
        OnPropertyChanged(nameof(DeletedPasswordCountText));
        OnPropertyChanged(nameof(HasDeletedPasswords));
        OnPropertyChanged(nameof(FilteredArchivedPasswords));
        OnPropertyChanged(nameof(FilteredDeletedPasswords));
        OnPropertyChanged(nameof(HasFilteredArchivedPasswords));
        OnPropertyChanged(nameof(HasFilteredDeletedPasswords));
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


    private void RaiseTotpSelectionState()
    {
        OnPropertyChanged(nameof(SelectedTotpCount));
        OnPropertyChanged(nameof(SelectedTotpCountText));
        OnPropertyChanged(nameof(HasSelectedTotpItems));
    }

    private void RaiseTotpFilterState(bool reconcileSelection = true)
    {
        RefreshTotpFilterChoices();
        OnPropertyChanged(nameof(FilteredTotpItems));
        OnPropertyChanged(nameof(HasFilteredTotpItems));
        OnPropertyChanged(nameof(HasTotpFilterOrSearch));
        OnPropertyChanged(nameof(TotpExpiringSoonCount));
        OnPropertyChanged(nameof(TotpConsoleStatusText));
        OnPropertyChanged(nameof(TotpFilteredStatusText));
        OnPropertyChanged(nameof(TotpEmptyStateText));

        if (reconcileSelection)
        {
            ReconcileSelectedTotpItem();
        }
    }

    private void RefreshTotpFilterChoices()
    {
        var choices = new List<TotpFilterChoice>
        {
            BuildTotpFilterChoice(TotpFilterAll, _localization.Get("TotpFilterAll"), TotpItems.Count),
            BuildTotpFilterChoice(TotpFilterFavorites, _localization.Get("QuickFilterFavorite"), TotpItems.Count(item => item.IsFavorite)),
            BuildTotpFilterChoice(TotpFilterExpiringSoon, _localization.Get("TotpFilterExpiringSoon"), TotpItems.Count(IsTotpExpiringSoon)),
            BuildTotpFilterChoice(TotpFilterUnbound, _localization.Get("TotpFilterUnbound"), TotpItems.Count(item => item.BoundPasswordId is null))
        };

        var issuerChoices = TotpItems
            .GroupBy(ResolveTotpIssuer, StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Take(8)
            .Select(group => BuildTotpFilterChoice($"{TotpFilterIssuerPrefix}{group.Key}", group.Key, group.Count(), level: 1));

        choices.AddRange(issuerChoices);
        ReplaceItems(TotpFilterChoices, choices);
    }

    private TotpFilterChoice BuildTotpFilterChoice(string key, string label, int count, int level = 0) =>
        new(key, label, count, level, string.Equals(SelectedTotpFilterKey, key, StringComparison.OrdinalIgnoreCase));

    private void ReconcileSelectedTotpItem()
    {
        var visibleItems = FilteredTotpItems;
        SelectedTotpItem =
            visibleItems.FirstOrDefault(item => item.Id == SelectedTotpItem?.Id) ??
            visibleItems.FirstOrDefault();
    }

    private void RaiseWalletSelectionState()
    {
        OnPropertyChanged(nameof(SelectedWalletCount));
        OnPropertyChanged(nameof(SelectedWalletCountText));
        OnPropertyChanged(nameof(HasSelectedWalletItems));
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


    private async Task LoadTotpItemsAsync(IReadOnlyList<SecureItem>? preloadedTotps = null)
    {
        var selectedId = SelectedTotpItem?.Id;
        var storedTotps = preloadedTotps ?? await AppDiagnostics.MeasureAsync(
            "Load TOTP secure items",
            () => _repository.GetSecureItemsAsync(VaultItemType.Totp));
        var activePasswordIds = Passwords.Select(item => item.Id).ToHashSet();
        var seenVirtualPasswordIds = new HashSet<long>();
        var nextItems = new List<SecureItem>();

        foreach (var item in storedTotps)
        {
            if (item.BoundPasswordId is { } boundPasswordId && !activePasswordIds.Contains(boundPasswordId))
            {
                continue;
            }

            TrackTotpSelection(item);
            RefreshTotpDisplay(item);
            nextItems.Add(item);
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
            nextItems.Add(virtualItem);
        }

        ReplaceItems(TotpItems, nextItems);
        SelectedTotpItem = nextItems.FirstOrDefault(item => item.Id == selectedId)
            ?? nextItems.FirstOrDefault();
        OnPropertyChanged(nameof(HasTotpItems));
        RaiseTotpFilterState();
        RaiseTotpSelectionState();
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

        SelectedSecurityIssue =
            SecurityIssueItems.FirstOrDefault(item => item.PasswordId == SelectedSecurityIssue?.PasswordId) ??
            SecurityIssueItems.FirstOrDefault();

        OnPropertyChanged(nameof(SecurityIssueCountText));
        OnPropertyChanged(nameof(HasSecurityIssues));
    }

    private async Task RefreshSecurityAnalysisDeferredAsync()
    {
        try
        {
            await Task.Delay(750);
            if (!IsUnlocked)
            {
                return;
            }

            AppDiagnostics.Measure("Refresh security analysis deferred", RefreshSecurityAnalysis);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error("Deferred security analysis failed", ex);
        }
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
                _localization.Format(
                    "WeakPasswordIssueFormat",
                    PasswordStrengthLocalization.Label(_localization, strength.Label)),
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
        if (SelectedTotpItem?.Id == item.Id)
        {
            SelectedTotpItem = TotpItems.FirstOrDefault();
        }

        await _repository.LogAsync(new OperationLog
        {
            ItemType = "TOTP",
            ItemId = item.Id,
            ItemTitle = item.Title,
            OperationType = "DELETE",
            DeviceName = Environment.MachineName
        });
        RaiseCounts();
        RaiseTotpFilterState();
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
        if (SelectedPasswordFolderFilter is { } folderFilter)
        {
            if (!MatchesPasswordFolderFilter(item, folderFilter))
            {
                return false;
            }
        }

        return MatchesPasswordNonFolderFilters(item);
    }

    private bool MatchesPasswordFolderFilter(PasswordEntry item, PasswordFolderFilterChoice folderFilter)
    {
        if (folderFilter.Id == -2)
        {
            return item.IsFavorite;
        }

        if (folderFilter.Id == -1)
        {
            return item.CategoryId is null;
        }

        if (!string.IsNullOrWhiteSpace(folderFilter.PathPrefix))
        {
            return PasswordMatchesFolderPath(item, folderFilter.PathPrefix);
        }

        return folderFilter.Id is not { } folderId || item.CategoryId == folderId;
    }

    private bool MatchesPasswordNonFolderFilters(PasswordEntry item)
    {
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

        return MatchesPasswordSearch(item, GetEffectivePasswordSearchQuery());
    }

    private string GetEffectivePasswordSearchQuery() =>
        string.IsNullOrWhiteSpace(PasswordSearchQuery) ? SearchText : PasswordSearchQuery;

    private bool PasswordMatchesFolderPath(PasswordEntry item, string pathPrefix)
    {
        if (item.CategoryId is null)
        {
            return false;
        }

        var category = Categories.FirstOrDefault(category => category.Id == item.CategoryId.Value);
        if (category is null)
        {
            return false;
        }

        var categoryPath = string.Join("/", SplitFolderPath(category.Name));
        return string.Equals(categoryPath, pathPrefix, StringComparison.OrdinalIgnoreCase) ||
               categoryPath.StartsWith($"{pathPrefix}/", StringComparison.OrdinalIgnoreCase);
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

    private bool MatchesTotpFilters(SecureItem item)
    {
        var filterKey = string.IsNullOrWhiteSpace(SelectedTotpFilterKey) ? TotpFilterAll : SelectedTotpFilterKey;
        if (filterKey.StartsWith(TotpFilterIssuerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var issuer = filterKey[TotpFilterIssuerPrefix.Length..];
            if (!string.Equals(ResolveTotpIssuer(item), issuer, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        else
        {
            switch (filterKey)
            {
                case TotpFilterFavorites when !item.IsFavorite:
                case TotpFilterExpiringSoon when !IsTotpExpiringSoon(item):
                case TotpFilterUnbound when item.BoundPasswordId is not null:
                    return false;
            }
        }

        return MatchesTotpSearch(item, SearchText);
    }

    private static bool MatchesTotpSearch(SecureItem item, string query)
    {
        var term = query.Trim();
        if (term.Length == 0)
        {
            return true;
        }

        var data = ResolveTotpData(item);
        return ContainsAny(
            term,
            item.Title,
            item.Notes,
            item.TotpCode,
            data?.Issuer ?? "",
            data?.AccountName ?? "",
            data?.OtpType ?? "");
    }

    private bool IsTotpExpiringSoon(SecureItem item)
    {
        var data = ResolveTotpData(item);
        if (data is not null)
        {
            return _totpService.GetRemainingSeconds(data.Period) <= 10;
        }

        return item.TotpProgress >= 66;
    }

    private static string ResolveTotpIssuer(SecureItem item)
    {
        var data = ResolveTotpData(item);
        if (!string.IsNullOrWhiteSpace(data?.Issuer))
        {
            return data.Issuer.Trim();
        }

        return string.IsNullOrWhiteSpace(item.Title) ? "TOTP" : item.Title.Trim();
    }

    private static TotpData? ResolveTotpData(SecureItem item) =>
        TotpDataResolver.ParseStoredItemData(item.ItemData, item.Title, item.Notes);

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

        if (entry.BitwardenVaultId is not null && !string.IsNullOrWhiteSpace(entry.BitwardenCipherId))
        {
            return $"bw:{entry.BitwardenVaultId}:{entry.BitwardenCipherId.Trim()}";
        }

        if (entry.KeepassDatabaseId is not null && !string.IsNullOrWhiteSpace(entry.KeepassEntryUuid))
        {
            return $"kp:{entry.KeepassDatabaseId}:{entry.KeepassEntryUuid.Trim()}";
        }

        return $"entry:{entry.Id}";
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
        OnPropertyChanged(nameof(GeneratorLengthText));
        OnPropertyChanged(nameof(GeneratedPasswordStrengthText));
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

        ReplaceOptions(GeneratorModeOptions,
            new(GeneratorModeRandom, _localization.Get("GeneratorModeRandom")),
            new(GeneratorModePassphrase, _localization.Get("GeneratorModePassphrase")),
            new(GeneratorModePin, _localization.Get("GeneratorModePin")),
            new(GeneratorModeUsername, _localization.Get("GeneratorModeUsername")));

        ReplaceOptions(GeneratorTemplateOptions,
            new(GeneratorTemplateBalanced, _localization.Get("GeneratorTemplateBalanced")),
            new(GeneratorTemplateMaximum, _localization.Get("GeneratorTemplateMaximum")),
            new(GeneratorTemplateMemorable, _localization.Get("GeneratorTemplateMemorable")),
            new(GeneratorTemplatePin, _localization.Get("GeneratorTemplatePin")),
            new(GeneratorTemplateUsername, _localization.Get("GeneratorTemplateUsername")));

        ReplaceOptions(
            SecurityQuestionOptions,
            _securityQuestionService.PredefinedQuestions
                .Select(question => new SettingsChoice(question.Id, question.Text))
                .ToArray());

        RaiseGeneratorState();
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
        OpenNoteReferenceCommand.NotifyCanExecuteChanged();
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
        ImportMarkdownNoteCommand.NotifyCanExecuteChanged();
        ExportCurrentNoteMarkdownCommand.NotifyCanExecuteChanged();
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

    private void LoadNoteIntoEditor(SecureItem? item)
    {
        if (item is null)
        {
            ResetNoteEditor();
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

    private void OpenNoteTab(SecureItem item)
    {
        var tab = OpenNoteTabs.FirstOrDefault(openTab => openTab.Source?.Id == item.Id);
        if (tab is null)
        {
            tab = new NoteEditorTab(item.Id, item, item.Title);
            OpenNoteTabs.Add(tab);
            NotifyNoteTabsChanged();
            RefreshNoteTabState();
        }

        SelectedNoteTab = tab;
    }

    private void LoadNoteTab(NoteEditorTab? tab)
    {
        _isLoadingNoteEditor = true;
        try
        {
            if (tab is null)
            {
                SelectedNote = null;
                ResetNoteEditor();
                return;
            }

            EnsureNoteTabDraftInitialized(tab);
            SelectedNote = tab.Source;
            LoadNoteTabDraftIntoEditor(tab);
        }
        finally
        {
            _isLoadingNoteEditor = false;
        }
    }

    private void EnsureNoteTabDraftInitialized(NoteEditorTab tab)
    {
        if (tab.DraftInitialized)
        {
            return;
        }

        if (tab.Source is null)
        {
            tab.DraftTitle = tab.Title;
            tab.DraftContent = "";
            tab.DraftTagsText = "";
            tab.DraftIsMarkdown = true;
            tab.DraftIsFavorite = false;
            tab.DraftPreviewMode = false;
            tab.DraftSplitPreviewMode = false;
            tab.DraftInitialized = true;
            return;
        }

        var decoded = NoteContentCodec.DecodeFromItem(tab.Source);
        tab.DraftTitle = tab.Source.Title;
        tab.DraftContent = decoded.Content;
        tab.DraftTagsText = string.Join(", ", decoded.Tags);
        tab.DraftIsMarkdown = decoded.IsMarkdown;
        tab.DraftIsFavorite = tab.Source.IsFavorite;
        tab.DraftPreviewMode = decoded.IsMarkdown;
        tab.DraftSplitPreviewMode = false;
        tab.Title = string.IsNullOrWhiteSpace(tab.Source.Title) ? _localization.Get("Untitled") : tab.Source.Title.Trim();
        tab.IsDirty = false;
        tab.DraftInitialized = true;
    }

    private void LoadNoteTabDraftIntoEditor(NoteEditorTab tab)
    {
        NoteTitle = tab.DraftTitle;
        NoteContent = tab.DraftContent;
        NoteTagsText = tab.DraftTagsText;
        NoteIsMarkdown = tab.DraftIsMarkdown;
        NoteIsFavorite = tab.DraftIsFavorite;
        NotePreviewMode = tab.DraftPreviewMode;
        NoteSplitPreviewMode = tab.DraftSplitPreviewMode;
        StatusMessage = tab.Source is null
            ? _localization.Get("EditingNewSecureNote")
            : _localization.Format("EditingNoteFormat", tab.Title);
    }

    private void CaptureSelectedNoteTabViewState()
    {
        if (_isLoadingNoteEditor || SelectedNoteTab is null)
        {
            return;
        }

        CaptureNoteEditorState(SelectedNoteTab, markDirty: false);
    }

    private void CaptureNoteEditorState(NoteEditorTab tab, bool markDirty)
    {
        tab.DraftTitle = NoteTitle;
        tab.DraftContent = NoteContent;
        tab.DraftTagsText = NoteTagsText;
        tab.DraftIsMarkdown = NoteIsMarkdown;
        tab.DraftIsFavorite = NoteIsFavorite;
        tab.DraftPreviewMode = NotePreviewMode;
        tab.DraftSplitPreviewMode = NoteSplitPreviewMode;
        tab.DraftInitialized = true;
        tab.DraftSelectionStart = Math.Clamp(tab.DraftSelectionStart, 0, NoteContent.Length);
        tab.DraftSelectionEnd = Math.Clamp(tab.DraftSelectionEnd, 0, NoteContent.Length);
        tab.Title = string.IsNullOrWhiteSpace(NoteTitle) ? _localization.Get("Untitled") : NoteTitle.Trim();
        if (markDirty)
        {
            tab.IsDirty = true;
        }
    }

    private void AppendNoteContentSnippet(string snippet)
    {
        var prefix = string.IsNullOrWhiteSpace(NoteContent)
            ? ""
            : NoteContent.EndsWith('\n') ? "\n" : "\n\n";
        NoteContent += prefix + snippet;
    }

    private void MarkSelectedNoteTabDirty()
    {
        if (_isLoadingNoteEditor || SelectedNoteTab is null)
        {
            return;
        }

        CaptureNoteEditorState(SelectedNoteTab, markDirty: true);
    }

    public void UpdateNoteEditorStatus(int caretIndex, int selectionStart, int selectionEnd)
    {
        var text = NoteContent ?? "";
        caretIndex = Math.Clamp(caretIndex, 0, text.Length);
        selectionStart = Math.Clamp(selectionStart, 0, text.Length);
        selectionEnd = Math.Clamp(selectionEnd, 0, text.Length);
        var line = 1;
        var column = 1;
        for (var index = 0; index < caretIndex; index++)
        {
            if (text[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        NoteCaretLine = line;
        NoteCaretColumn = column;
        NoteSelectedCharacterCount = Math.Abs(selectionEnd - selectionStart);
        if (!_isLoadingNoteEditor && SelectedNoteTab is not null)
        {
            SelectedNoteTab.DraftSelectionStart = selectionStart;
            SelectedNoteTab.DraftSelectionEnd = selectionEnd;
        }
    }

    private async Task RefreshNoteImagePreviewsAsync(string content)
    {
        var version = Interlocked.Increment(ref _noteImagePreviewVersion);
        var imagePaths = NoteContentCodec.ExtractInlineImageIds(content)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (imagePaths.Length == 0)
        {
            if (version == _noteImagePreviewVersion)
            {
                ReplaceNoteImagePreviews([]);
            }

            return;
        }

        var previews = new List<NoteImagePreviewItem>();
        foreach (var imagePath in imagePaths)
        {
            try
            {
                var attachment = CreateNoteImageAttachment(imagePath);
                var contentBytes = await _repository.TryReadAttachmentContentAsync(attachment);
                if (contentBytes is null || contentBytes.Length == 0)
                {
                    continue;
                }

                using var stream = new MemoryStream(contentBytes);
                previews.Add(new NoteImagePreviewItem(
                    imagePath,
                    BuildNoteImagePreviewName(imagePath, previews.Count + 1),
                    FormatByteSize(contentBytes.LongLength),
                    new Bitmap(stream)));
            }
            catch (Exception ex)
            {
                AppDiagnostics.Error($"Note image preview failed for {imagePath}", ex);
            }
        }

        if (version == _noteImagePreviewVersion)
        {
            ReplaceNoteImagePreviews(previews);
        }
        else
        {
            foreach (var preview in previews)
            {
                preview.Image.Dispose();
            }
        }
    }

    private Attachment CreateNoteImageAttachment(string imagePath)
    {
        var ownerId = SelectedNoteTab?.Source?.Id ?? SelectedNote?.Id ?? 0;
        return new Attachment
        {
            OwnerType = "SECURE_ITEM",
            OwnerId = ownerId,
            FileName = BuildNoteImagePreviewName(imagePath, 0),
            ContentType = InferImageContentType(imagePath),
            StoragePath = imagePath,
            SizeBytes = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private void ReplaceNoteImagePreviews(IReadOnlyList<NoteImagePreviewItem> previews)
    {
        foreach (var preview in NoteImagePreviewItems)
        {
            preview.Image.Dispose();
        }

        ReplaceItems(NoteImagePreviewItems, previews);
        OnPropertyChanged(nameof(NoteImagePreviewCount));
        OnPropertyChanged(nameof(HasNoteImagePreviewItems));
    }

    private static string BuildNoteImagePreviewName(string imagePath, int fallbackIndex)
    {
        var normalized = imagePath.Trim();
        if (normalized.StartsWith("mdbx:", StringComparison.OrdinalIgnoreCase))
        {
            return fallbackIndex > 0 ? $"MDBX image {fallbackIndex}" : "MDBX image";
        }

        var fileName = Path.GetFileName(normalized.Replace('\\', '/'));
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            return fileName;
        }

        return fallbackIndex > 0 ? $"Image {fallbackIndex}" : "Image";
    }

    private static string BuildLineNumbersText(string content)
    {
        return string.Join(Environment.NewLine, Enumerable.Range(1, CountNoteLines(content)));
    }

    private static int CountNoteLines(string content) =>
        string.IsNullOrEmpty(content)
            ? 1
            : content.Count(character => character == '\n') + 1;

    private static int CountNoteWords(string content)
    {
        var count = 0;
        var inAsciiWord = false;
        foreach (var character in content)
        {
            if (IsCjkCharacter(character))
            {
                count++;
                inAsciiWord = false;
            }
            else if (char.IsLetterOrDigit(character))
            {
                if (!inAsciiWord)
                {
                    count++;
                    inAsciiWord = true;
                }
            }
            else
            {
                inAsciiWord = false;
            }
        }

        return count;
    }

    private static bool IsCjkCharacter(char character) =>
        character is >= '\u3400' and <= '\u9fff' or >= '\uf900' and <= '\ufaff';

    private static string BuildNotePreviewMarkdown(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "";
        }

        var builder = new StringBuilder(content.Length);
        var inCodeFence = false;
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCodeFence = !inCodeFence;
                AppendPreviewMarkdownLine(builder, line, index < lines.Length - 1);
                continue;
            }

            var previewLine = inCodeFence
                ? line
                : MarkdownLinkRegex().Replace(line, match =>
                {
                    var isImage = match.Groups[1].Value == "!";
                    var label = match.Groups[2].Value.Trim();
                    var target = match.Groups[3].Value.Trim();
                    if (!isImage || !target.StartsWith("monica-image://", StringComparison.OrdinalIgnoreCase))
                    {
                        return match.Value;
                    }

                    return string.IsNullOrWhiteSpace(label)
                        ? "[图片附件]"
                        : $"[图片附件: {label}]";
                });
            AppendPreviewMarkdownLine(builder, previewLine, index < lines.Length - 1);
        }

        return builder.ToString();
    }

    private static void AppendPreviewMarkdownLine(StringBuilder builder, string line, bool appendLineBreak)
    {
        builder.Append(line);
        if (appendLineBreak)
        {
            builder.Append('\n');
        }
    }

    private static IReadOnlyList<NoteOutlineItem> BuildNoteOutlineItems(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var items = new List<NoteOutlineItem>();
        var inCodeFence = false;
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCodeFence = !inCodeFence;
                continue;
            }

            if (inCodeFence)
            {
                continue;
            }

            var match = HeadingRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var level = match.Groups[1].Value.Length;
            var title = match.Groups[2].Value.Trim();
            if (title.Length == 0)
            {
                continue;
            }

            items.Add(new NoteOutlineItem(
                level,
                title,
                index + 1,
                new Thickness(Math.Min(level - 1, 5) * 12, 0, 0, 0)));
        }

        return items;
    }

    private static IReadOnlyList<NoteReferenceItem> BuildNoteReferenceItems(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var items = new List<NoteReferenceItem>();
        var inCodeFence = false;
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCodeFence = !inCodeFence;
                continue;
            }

            if (inCodeFence)
            {
                continue;
            }

            var markdownLinkRanges = new List<(int Start, int End)>();
            foreach (Match match in MarkdownLinkRegex().Matches(line))
            {
                var isImage = match.Groups[1].Value == "!";
                var label = match.Groups[2].Value.Trim();
                var target = match.Groups[3].Value.Trim();
                if (string.IsNullOrWhiteSpace(target))
                {
                    continue;
                }

                markdownLinkRanges.Add((match.Index, match.Index + match.Length));
                items.Add(new NoteReferenceItem(
                    string.IsNullOrWhiteSpace(label) ? (isImage ? "Image" : target) : label,
                    target,
                    index + 1,
                    isImage));
            }

            foreach (Match match in BareUrlRegex().Matches(line))
            {
                var start = match.Index;
                if (markdownLinkRanges.Any(range => start >= range.Start && start < range.End))
                {
                    continue;
                }

                var target = match.Value.TrimEnd('.', ',', ';', ':');
                items.Add(new NoteReferenceItem(target, target, index + 1, IsImageUrl(target)));
            }
        }

        return items
            .DistinctBy(item => (item.Target, item.LineNumber))
            .ToArray();
    }

    private static bool IsImageUrl(string target) =>
        target.StartsWith("monica-image://", StringComparison.OrdinalIgnoreCase) ||
        target.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
        target.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
        target.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
        target.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
        target.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

    private static bool TryCreateExternalReferenceUri(string? target, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(target) ||
            !Uri.TryCreate(target.Trim(), UriKind.Absolute, out var candidate) ||
            candidate.Scheme is not ("http" or "https"))
        {
            return false;
        }

        uri = candidate;
        return true;
    }

    [GeneratedRegex("^\\s{0,3}(#{1,6})\\s+(.+?)\\s*#*\\s*$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex("(!?)\\[([^\\]]*)\\]\\(([^\\)\\s]+)\\)")]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex("https?://[^\\s<>()]+")]
    private static partial Regex BareUrlRegex();

    private static string InferImageContentType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
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

    private void ResetNoteEditor()
    {
        NoteTitle = "";
        NoteContent = "";
        NoteTagsText = "";
        NoteIsMarkdown = true;
        NotePreviewMode = false;
        NoteSplitPreviewMode = false;
        NoteIsFavorite = false;
    }
}
