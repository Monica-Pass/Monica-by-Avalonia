using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private IReadOnlyList<PasswordEntry> _filteredDeletedPasswords = [];

    public ObservableCollection<PasswordEntry> DeletedPasswords { get; } = new ObservableRangeCollection<PasswordEntry>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDeletedPassword))]
    private PasswordEntry? _selectedDeletedPassword;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecycleBinSearchText))]
    [NotifyPropertyChangedFor(nameof(ShowClearRecycleBinSearchInEmptyState))]
    private string _recycleBinSearchText = "";

    [ObservableProperty]
    private bool _recycleBinNarrowShowsList = true;

    public string DeletedPasswordCountText => _localization.Format("DeletedPasswordCountFormat", DeletedPasswords.Count);
    public bool HasDeletedPasswords => DeletedPasswords.Count > 0;
    public bool HasSelectedDeletedPassword => SelectedDeletedPassword is not null;
    public bool HasRecycleBinSearchText => !string.IsNullOrWhiteSpace(RecycleBinSearchText);
    public IReadOnlyList<PasswordEntry> FilteredDeletedPasswords => _filteredDeletedPasswords;
    public bool HasFilteredDeletedPasswords => FilteredDeletedPasswords.Count > 0;
    public bool ShowClearRecycleBinSearchInEmptyState =>
        DeletedPasswords.Count > 0 && HasRecycleBinSearchText && !HasFilteredDeletedPasswords;
    public string RecycleBinEmptyStateText => ShowClearRecycleBinSearchInEmptyState
        ? _localization.Format("RecycleBinNoSearchResultsFormat", RecycleBinSearchText.Trim())
        : _localization.Get("RecycleBinEmptyHint");
}
