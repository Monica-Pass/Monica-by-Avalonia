using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private IReadOnlyList<PasswordEntry> _filteredArchivedPasswords = [];

    public ObservableCollection<PasswordEntry> ArchivedPasswords { get; } = new ObservableRangeCollection<PasswordEntry>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedArchivedPassword))]
    private PasswordEntry? _selectedArchivedPassword;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArchiveSearchText))]
    [NotifyPropertyChangedFor(nameof(ShowClearArchiveSearchInEmptyState))]
    private string _archiveSearchText = "";

    [ObservableProperty]
    private bool _archiveNarrowShowsList = true;

    public string ArchivedPasswordCountText => _localization.Format("ArchivedPasswordCountFormat", ArchivedPasswords.Count);
    public bool HasSelectedArchivedPassword => SelectedArchivedPassword is not null;
    public bool HasArchiveSearchText => !string.IsNullOrWhiteSpace(ArchiveSearchText);
    public IReadOnlyList<PasswordEntry> FilteredArchivedPasswords => _filteredArchivedPasswords;
    public bool HasFilteredArchivedPasswords => FilteredArchivedPasswords.Count > 0;
    public bool ShowClearArchiveSearchInEmptyState =>
        ArchivedPasswords.Count > 0 && HasArchiveSearchText && !HasFilteredArchivedPasswords;
    public string ArchiveEmptyStateText => ShowClearArchiveSearchInEmptyState
        ? _localization.Format("ArchiveNoSearchResultsFormat", ArchiveSearchText.Trim())
        : _localization.Get("ArchiveEmptyHint");
}
