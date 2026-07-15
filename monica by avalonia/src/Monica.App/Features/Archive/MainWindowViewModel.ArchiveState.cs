using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    partial void OnArchiveSearchTextChanged(string value) => RefreshArchiveSearchState();

    private IEnumerable<PasswordEntry> GetArchivedPasswordSiblings(PasswordEntry entry)
    {
        var key = BuildSiblingGroupKey(entry);
        return ArchivedPasswords
            .Where(item => BuildSiblingGroupKey(item) == key)
            .OrderBy(item => item.Id == 0 ? long.MaxValue : item.Id);
    }

    private bool MatchesLifecyclePasswordSearch(PasswordEntry item, string query)
    {
        var term = query.Trim();
        return term.Length == 0 || ContainsAny(term,
            item.Title,
            item.Username,
            item.Website,
            item.AppName,
            item.AppPackageName,
            item.Email,
            item.Phone,
            item.AddressLine,
            item.City,
            item.State,
            item.Country,
            item.KeepassGroupPath ?? "",
            item.MdbxFolderId ?? "",
            item.BitwardenFolderId ?? "");
    }

    private void RefreshArchiveSearchState()
    {
        _filteredArchivedPasswords = ArchivedPasswords
            .Where(item => MatchesLifecyclePasswordSearch(item, ArchiveSearchText))
            .ToArray();
        OnPropertyChanged(nameof(FilteredArchivedPasswords));
        OnPropertyChanged(nameof(HasFilteredArchivedPasswords));
        OnPropertyChanged(nameof(ShowClearArchiveSearchInEmptyState));
        OnPropertyChanged(nameof(ArchiveEmptyStateText));
        RaiseArchiveSelectionState();
        SelectedArchivedPassword =
            FilteredArchivedPasswords.FirstOrDefault(item => item.Id == SelectedArchivedPassword?.Id) ??
            FilteredArchivedPasswords.FirstOrDefault();
    }

    private void RefreshArchiveCountState()
    {
        _filteredArchivedPasswords = ArchivedPasswords
            .Where(item => MatchesLifecyclePasswordSearch(item, ArchiveSearchText))
            .ToArray();
        SelectedArchivedPassword =
            ArchivedPasswords.FirstOrDefault(item => item.Id == SelectedArchivedPassword?.Id) ??
            ArchivedPasswords.FirstOrDefault();
        OnPropertyChanged(nameof(ArchivedPasswordCountText));
        OnPropertyChanged(nameof(FilteredArchivedPasswords));
        OnPropertyChanged(nameof(HasFilteredArchivedPasswords));
        OnPropertyChanged(nameof(ShowClearArchiveSearchInEmptyState));
        OnPropertyChanged(nameof(ArchiveEmptyStateText));
        RaiseArchiveSelectionState();
    }
}
