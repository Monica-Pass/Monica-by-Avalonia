using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
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

    private readonly IPasswordAttachmentFileService _passwordAttachmentFileService;
    private readonly IPasswordEditorDialogService _passwordEditorDialogService;
    private readonly IPasswordDetailDialogService _passwordDetailDialogService;
    private readonly ICategoryPickerDialogService _categoryPickerDialogService;
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

    [ObservableProperty]
    private string _passwordSearchText = "";

    [ObservableProperty]
    private string _passwordSearchQuery = "";

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
    public string PasswordCountText => _localization.Format("PasswordCountFormat", Passwords.Count);
    public bool HasFilteredPasswordRows => FilteredPasswordRows.Count > 0;
    public bool HasPasswordSearchText => !string.IsNullOrEmpty(PasswordSearchText);
    public bool ShowAddPasswordInEmptyState => Passwords.Count == 0;
    public bool ShowClearPasswordFiltersInEmptyState => Passwords.Count > 0 && HasPasswordFilters;
    public string PasswordEmptyStateText => ShowClearPasswordFiltersInEmptyState
        ? _localization.Get("PasswordNoFilteredResults")
        : _localization.Get("PasswordEmptyHint");
    public string SelectPasswordItemsText => _localization.Get("SelectPasswordItems");
    public string SelectAllVisiblePasswordsText => _localization.Get("SelectAllVisiblePasswords");
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
        (HasPendingLegacyBusinessData || IsRecoverableStatusMessage(StatusMessage));
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
    partial void OnPasswordSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasPasswordSearchText));
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
}
