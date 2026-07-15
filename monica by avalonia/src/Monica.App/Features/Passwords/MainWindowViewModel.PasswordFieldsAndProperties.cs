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
    private int _passwordProjectionNotificationDeferralDepth;
    private bool _filteredPasswordsNotificationPending;
    private bool _filteredPasswordRowsNotificationPending;
    private bool _passwordSelectionReconciliationPending;
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
    public ObservableCollection<PasswordFolderFilterChoice> PasswordFolderFilters { get; } =
        new ObservableRangeCollection<PasswordFolderFilterChoice>();
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPasswordDetailsError))]
    private string? _selectedPasswordDetailsError;
}
