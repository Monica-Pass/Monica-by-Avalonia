using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private const int PasswordHistoryLimit = 10;
    private static readonly TimeSpan SelectedPasswordDetailsCoalesceDelay = TimeSpan.FromMilliseconds(60);
    private static readonly TimeSpan SelectedPasswordDetailsLoadingDelay = TimeSpan.FromMilliseconds(120);

    private enum QuickAccessSort
    {
        Recent,
        Frequent
    }

    private readonly IPasswordAttachmentFileService _passwordAttachmentFileService;
    private readonly IPasswordEditorDialogService _passwordEditorDialogService;
    private readonly IPasswordDetailDialogService _passwordDetailDialogService;
    private readonly ICategoryPickerDialogService _categoryPickerDialogService;
    private readonly Avalonia.Threading.Dispatcher _viewModelDispatcher;
    private IReadOnlyDictionary<long, IReadOnlyList<CustomField>> _passwordCustomFields = new Dictionary<long, IReadOnlyList<CustomField>>();
    private IReadOnlySet<long> _passwordAttachmentOwnerIds = new HashSet<long>();
    private IReadOnlyDictionary<long, PasswordQuickAccessRecord> _passwordQuickAccessRecords = new Dictionary<long, PasswordQuickAccessRecord>();
    private IReadOnlySet<long> _passwordCustomFieldSearchMatches = new HashSet<long>();
    private IReadOnlySet<long> _passwordAttachmentSearchMatches = new HashSet<long>();
    private string _passwordCustomFieldSearchQuery = "";
    private string _passwordAttachmentSearchQuery = "";
    internal int PasswordQuickAccessRecordCacheCount => _passwordQuickAccessRecords.Count;
    private int _selectedPasswordCount;
    private bool _suppressPasswordSelectionStateNotifications;
    private CancellationTokenSource? _passwordSearchDebounceCts;
    private CancellationTokenSource? _selectedPasswordDetailsCts;
    private int _selectedPasswordDetailsVersion;

    [ObservableProperty]
    private string _newFolderName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordSortButtonTip))]
    [NotifyPropertyChangedFor(nameof(IsSortUpdatedSelected))]
    [NotifyPropertyChangedFor(nameof(IsSortTitleSelected))]
    [NotifyPropertyChangedFor(nameof(IsSortWebsiteSelected))]
    [NotifyPropertyChangedFor(nameof(IsSortUsernameSelected))]
    [NotifyPropertyChangedFor(nameof(IsSortCreatedSelected))]
    [NotifyPropertyChangedFor(nameof(IsSortFavoritesSelected))]
    [NotifyPropertyChangedFor(nameof(VaultMoreButtonTip))]
    private string _selectedPasswordSort = "updated-desc";

    public ObservableCollection<PasswordEntry> Passwords { get; } = new ObservableRangeCollection<PasswordEntry>();
    public ObservableCollection<Category> Categories { get; } = new ObservableRangeCollection<Category>();
    public ObservableCollection<SettingsChoice> PasswordSortOptions { get; } = [];

    [ObservableProperty]
    private bool _compactPasswordList;

    [ObservableProperty]
    private bool _showWebsiteIcons = true;

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
