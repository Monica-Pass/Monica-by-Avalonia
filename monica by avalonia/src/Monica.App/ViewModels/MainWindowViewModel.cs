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
    private const int PasswordHistoryLimit = 10;
    private const int PasswordQuickAccessLimit = 6;
    private static readonly TimeSpan SelectedPasswordDetailsCoalesceDelay = TimeSpan.FromMilliseconds(60);
    private static readonly TimeSpan SelectedPasswordDetailsLoadingDelay = TimeSpan.FromMilliseconds(120);
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

    private readonly IMonicaRepository _repository;
    private readonly ICryptoService _cryptoService;
    private readonly ITotpService _totpService;
    private readonly IPasswordGeneratorService _passwordGenerator;
    private readonly IPwnedPasswordService _pwnedPasswordService;
    private readonly IClipboardService _clipboardService;
    private readonly IPasswordAttachmentFileService _passwordAttachmentFileService;
    private readonly IPasswordEditorDialogService _passwordEditorDialogService;
    private readonly IPasswordDetailDialogService _passwordDetailDialogService;
    private readonly ICategoryPickerDialogService _categoryPickerDialogService;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly ITotpEditorDialogService _totpEditorDialogService;
    private readonly IWalletItemEditorDialogService _walletItemEditorDialogService;
    private readonly ILocalizationService _localization;
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
    private bool _isApplyingPasswordSearchImmediately;
    private CancellationTokenSource? _passwordSearchDebounceCts;
    private CancellationTokenSource? _selectedPasswordDetailsCts;
    private int _selectedPasswordDetailsVersion;
    private readonly HashSet<string> _collapsedPasswordFolderKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedPasswordStackKeys = new(StringComparer.OrdinalIgnoreCase);

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
    public ObservableCollection<SettingsChoice> PasswordSortOptions { get; } = [];
    public ObservableCollection<PasswordFolderFilterChoice> PasswordFolderFilters { get; } = [];
    public IEnumerable<PasswordFolderFilterChoice> SystemPasswordFolderFilters =>
        PasswordFolderFilters.Where(item => item.IsSystemNode);
    public IEnumerable<PasswordFolderFilterChoice> RegularPasswordFolderFilters =>
        PasswordFolderFilters.Where(item => !item.IsSystemNode);
    public bool HasRegularPasswordFolderFilters => PasswordFolderFilters.Any(item => !item.IsSystemNode);

    [ObservableProperty]
    private string _selectedSection = "Passwords";

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
    private bool _compactPasswordList;

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
    public int SmokeVaultLoadDelayMilliseconds { get; set; }
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





    private static bool IsWorkspacePageSelected(string selectedPage, string expectedPage) =>
        string.Equals(selectedPage, expectedPage, StringComparison.OrdinalIgnoreCase);






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













    private string FormatLocalDate(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy/MM/dd HH:mm", _localization.Culture);


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
