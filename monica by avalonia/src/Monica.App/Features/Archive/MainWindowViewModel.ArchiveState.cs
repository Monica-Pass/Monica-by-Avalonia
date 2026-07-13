using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private IEnumerable<PasswordEntry> GetArchivedPasswordSiblings(PasswordEntry entry)
    {
        var key = BuildSiblingGroupKey(entry);
        return ArchivedPasswords
            .Where(item => BuildSiblingGroupKey(item) == key)
            .OrderBy(item => item.Id == 0 ? long.MaxValue : item.Id);
    }

    private void RefreshArchiveSearchState()
    {
        OnPropertyChanged(nameof(FilteredArchivedPasswords));
        OnPropertyChanged(nameof(HasFilteredArchivedPasswords));
        SelectedArchivedPassword =
            FilteredArchivedPasswords.FirstOrDefault(item => item.Id == SelectedArchivedPassword?.Id) ??
            FilteredArchivedPasswords.FirstOrDefault();
    }

    private void RefreshArchiveCountState()
    {
        SelectedArchivedPassword =
            ArchivedPasswords.FirstOrDefault(item => item.Id == SelectedArchivedPassword?.Id) ??
            ArchivedPasswords.FirstOrDefault();
        OnPropertyChanged(nameof(ArchivedPasswordCountText));
        OnPropertyChanged(nameof(FilteredArchivedPasswords));
        OnPropertyChanged(nameof(HasFilteredArchivedPasswords));
    }
}
