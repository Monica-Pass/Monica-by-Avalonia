using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    partial void OnRecycleBinSearchTextChanged(string value) => RefreshRecycleBinSearchState();

    private IEnumerable<PasswordEntry> GetDeletedPasswordSiblings(PasswordEntry entry)
    {
        var key = BuildSiblingGroupKey(entry);
        return DeletedPasswords
            .Where(item => BuildSiblingGroupKey(item) == key)
            .OrderBy(item => item.Id == 0 ? long.MaxValue : item.Id);
    }

    private void RefreshRecycleBinSearchState()
    {
        _filteredDeletedPasswords = DeletedPasswords
            .Where(item => MatchesLifecyclePasswordSearch(item, RecycleBinSearchText))
            .ToArray();
        OnPropertyChanged(nameof(FilteredDeletedPasswords));
        OnPropertyChanged(nameof(HasFilteredDeletedPasswords));
        OnPropertyChanged(nameof(ShowClearRecycleBinSearchInEmptyState));
        OnPropertyChanged(nameof(RecycleBinEmptyStateText));
        RaiseRecycleBinSelectionState();
        SelectedDeletedPassword =
            FilteredDeletedPasswords.FirstOrDefault(item => item.Id == SelectedDeletedPassword?.Id) ??
            FilteredDeletedPasswords.FirstOrDefault();
    }

    private void RefreshRecycleBinCountState()
    {
        _filteredDeletedPasswords = DeletedPasswords
            .Where(item => MatchesLifecyclePasswordSearch(item, RecycleBinSearchText))
            .ToArray();
        SelectedDeletedPassword =
            DeletedPasswords.FirstOrDefault(item => item.Id == SelectedDeletedPassword?.Id) ??
            DeletedPasswords.FirstOrDefault();
        OnPropertyChanged(nameof(DeletedPasswordCountText));
        OnPropertyChanged(nameof(HasDeletedPasswords));
        OnPropertyChanged(nameof(FilteredDeletedPasswords));
        OnPropertyChanged(nameof(HasFilteredDeletedPasswords));
        OnPropertyChanged(nameof(ShowClearRecycleBinSearchInEmptyState));
        OnPropertyChanged(nameof(RecycleBinEmptyStateText));
        RaiseRecycleBinSelectionState();
    }
}
