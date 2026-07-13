using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public ObservableCollection<PasswordEntry> ArchivedPasswords { get; } = new ObservableRangeCollection<PasswordEntry>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedArchivedPassword))]
    private PasswordEntry? _selectedArchivedPassword;

    public string ArchivedPasswordCountText => _localization.Format("ArchivedPasswordCountFormat", ArchivedPasswords.Count);
    public bool HasSelectedArchivedPassword => SelectedArchivedPassword is not null;
    public IEnumerable<PasswordEntry> FilteredArchivedPasswords =>
        ArchivedPasswords.Where(item => MatchesPasswordSearch(item, SearchText));
    public bool HasFilteredArchivedPasswords => FilteredArchivedPasswords.Any();
}
