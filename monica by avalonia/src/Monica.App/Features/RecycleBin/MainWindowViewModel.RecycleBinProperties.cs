using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.App.Features.RecycleBin;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private IReadOnlyList<PasswordEntry> _filteredDeletedPasswords = [];

    public ObservableCollection<PasswordEntry> DeletedPasswords { get; } = new ObservableRangeCollection<PasswordEntry>();
    public ObservableCollection<SecureItem> DeletedSecureItems { get; } = new ObservableRangeCollection<SecureItem>();

    private IReadOnlyList<RecycleBinDisplayItem> _filteredRecycleBinItems = [];

    public ObservableCollection<RecycleBinDisplayItem> RecycleBinItems { get; } = new ObservableRangeCollection<RecycleBinDisplayItem>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDeletedPassword))]
    private PasswordEntry? _selectedDeletedPassword;

    [ObservableProperty]
    private RecycleBinDisplayItem? _selectedRecycleBinItem;

    [ObservableProperty]
    private int _recycleBinRetentionDays = 30;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecycleBinSearchText))]
    [NotifyPropertyChangedFor(nameof(ShowClearRecycleBinSearchInEmptyState))]
    private string _recycleBinSearchText = "";

    [ObservableProperty]
    private bool _recycleBinNarrowShowsList = true;

    public string DeletedPasswordCountText => _localization.Format("RecycleBinItemCountFormat", RecycleBinItems.Count);
    public bool HasDeletedPasswords => RecycleBinItems.Count > 0;
    public bool HasSelectedDeletedPassword => SelectedRecycleBinItem is not null;
    public bool HasRecycleBinSearchText => !string.IsNullOrWhiteSpace(RecycleBinSearchText);
    public IReadOnlyList<PasswordEntry> FilteredDeletedPasswords => _filteredDeletedPasswords;
    public bool HasFilteredDeletedPasswords => FilteredDeletedPasswords.Count > 0;
    public IReadOnlyList<RecycleBinDisplayItem> FilteredRecycleBinItems => _filteredRecycleBinItems;
    public bool HasRecycleBinItems => RecycleBinItems.Count > 0;
    public bool HasFilteredRecycleBinItems => FilteredRecycleBinItems.Count > 0;
    public string SelectRecycleBinItemsText => _localization.Get("SelectRecycleBinItems");
    public string SelectAllVisibleRecycleBinItemsText => _localization.Get("SelectAllVisibleRecycleBinItems");
    public string RecycleBinRetentionText => _localization.Format("RecycleBinRetentionFormat", RecycleBinRetentionDays);
    public bool ShowClearRecycleBinSearchInEmptyState =>
        RecycleBinItems.Count > 0 && HasRecycleBinSearchText && !HasFilteredRecycleBinItems;
    public string RecycleBinEmptyStateText => ShowClearRecycleBinSearchInEmptyState
        ? _localization.Format("RecycleBinNoSearchResultsFormat", RecycleBinSearchText.Trim())
        : _localization.Get("RecycleBinEmptyHint");
}
