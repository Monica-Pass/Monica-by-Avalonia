using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public ObservableCollection<PasswordEntry> DeletedPasswords { get; } = new ObservableRangeCollection<PasswordEntry>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDeletedPassword))]
    private PasswordEntry? _selectedDeletedPassword;

    public string DeletedPasswordCountText => _localization.Format("DeletedPasswordCountFormat", DeletedPasswords.Count);
    public bool HasDeletedPasswords => DeletedPasswords.Count > 0;
    public bool HasSelectedDeletedPassword => SelectedDeletedPassword is not null;
    public IEnumerable<PasswordEntry> FilteredDeletedPasswords =>
        DeletedPasswords.Where(item => MatchesPasswordSearch(item, SearchText));
    public bool HasFilteredDeletedPasswords => FilteredDeletedPasswords.Any();
}
