using Monica.Core.Models;
using Monica.App.Features.RecycleBin;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    partial void OnRecycleBinSearchTextChanged(string value) => RefreshRecycleBinSearchState();

    partial void OnSelectedRecycleBinItemChanged(RecycleBinDisplayItem? value)
    {
        SelectedDeletedPassword = value?.Password;
    }

    private IEnumerable<PasswordEntry> GetDeletedPasswordSiblings(PasswordEntry entry)
    {
        var key = BuildSiblingGroupKey(entry);
        return DeletedPasswords
            .Where(item => BuildSiblingGroupKey(item) == key)
            .OrderBy(item => item.Id == 0 ? long.MaxValue : item.Id);
    }

    private void RefreshRecycleBinSearchState()
    {
        RefreshRecycleBinProjection();
        _filteredDeletedPasswords = DeletedPasswords
            .Where(item => MatchesLifecyclePasswordSearch(item, RecycleBinSearchText))
            .ToArray();
        _filteredRecycleBinItems = RecycleBinItems
            .Where(item => MatchesRecycleBinSearch(item, RecycleBinSearchText))
            .ToArray();
        OnPropertyChanged(nameof(FilteredDeletedPasswords));
        OnPropertyChanged(nameof(HasFilteredDeletedPasswords));
        OnPropertyChanged(nameof(FilteredRecycleBinItems));
        OnPropertyChanged(nameof(HasFilteredRecycleBinItems));
        OnPropertyChanged(nameof(ShowClearRecycleBinSearchInEmptyState));
        OnPropertyChanged(nameof(RecycleBinEmptyStateText));
        RaiseRecycleBinSelectionState();
        SelectedRecycleBinItem =
            FilteredRecycleBinItems.FirstOrDefault(item => item.Key == SelectedRecycleBinItem?.Key) ??
            FilteredRecycleBinItems.FirstOrDefault();
    }

    private void RefreshRecycleBinCountState()
    {
        RefreshRecycleBinProjection();
        _filteredDeletedPasswords = DeletedPasswords
            .Where(item => MatchesLifecyclePasswordSearch(item, RecycleBinSearchText))
            .ToArray();
        _filteredRecycleBinItems = RecycleBinItems
            .Where(item => MatchesRecycleBinSearch(item, RecycleBinSearchText))
            .ToArray();
        SelectedRecycleBinItem =
            FilteredRecycleBinItems.FirstOrDefault(item => item.Key == SelectedRecycleBinItem?.Key) ??
            FilteredRecycleBinItems.FirstOrDefault();
        OnPropertyChanged(nameof(DeletedPasswordCountText));
        OnPropertyChanged(nameof(HasDeletedPasswords));
        OnPropertyChanged(nameof(FilteredDeletedPasswords));
        OnPropertyChanged(nameof(HasFilteredDeletedPasswords));
        OnPropertyChanged(nameof(FilteredRecycleBinItems));
        OnPropertyChanged(nameof(HasFilteredRecycleBinItems));
        OnPropertyChanged(nameof(ShowClearRecycleBinSearchInEmptyState));
        OnPropertyChanged(nameof(RecycleBinEmptyStateText));
        RaiseRecycleBinSelectionState();
    }

    private void RefreshRecycleBinProjection()
    {
        var items = DeletedPasswords
            .Where(item => item.DeletedAt != DateTimeOffset.UnixEpoch)
            .Select(item => RecycleBinDisplayItem.FromPassword(item, _localization.Passwords, ResolveSource(item), BuildRetentionText(item.DeletedAt)))
            .Concat(DeletedSecureItems
                .Where(item => item.IsDeleted && item.DeletedAt != DateTimeOffset.UnixEpoch)
                .Select(item => RecycleBinDisplayItem.FromSecureItem(item, ResolveItemType(item), ResolveSource(item), BuildRetentionText(item.DeletedAt))))
            .OrderByDescending(item => item.DeletedAt ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        foreach (var item in items)
        {
            item.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(RecycleBinDisplayItem.IsSelected))
                {
                    RaiseRecycleBinSelectionState();
                }
            };
        }

        ReplaceItems(RecycleBinItems, items);
        OnPropertyChanged(nameof(HasRecycleBinItems));
        OnPropertyChanged(nameof(DeletedPasswordCountText));
    }

    private bool MatchesRecycleBinSearch(RecycleBinDisplayItem item, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var normalized = query.Trim();
        return item.Title.Contains(normalized, StringComparison.CurrentCultureIgnoreCase)
            || item.ItemType.Contains(normalized, StringComparison.CurrentCultureIgnoreCase)
            || item.Source.Contains(normalized, StringComparison.CurrentCultureIgnoreCase);
    }

    private string BuildRetentionText(DateTimeOffset? deletedAt)
    {
        if (deletedAt is null) return _localization.Get("RecycleBinCleanupDateUnknown");
        var remaining = Math.Max(0, (int)Math.Ceiling((deletedAt.Value.AddDays(RecycleBinRetentionDays) - DateTimeOffset.UtcNow).TotalDays));
        return remaining == 0
            ? _localization.Get("RecycleBinCleanupDue")
            : _localization.Format("RecycleBinRemainingDaysFormat", remaining);
    }

    private string ResolveItemType(SecureItem item) => item.ItemType switch
    {
        VaultItemType.Totp => _localization.Totp,
        VaultItemType.BankCard => _localization.BankCard,
        VaultItemType.Document => _localization.Document,
        VaultItemType.Note => _localization.SecureNotes,
        _ => _localization.Get("SecureItem")
    };

    private string ResolveSource(PasswordEntry item) =>
        item.KeepassDatabaseId is not null ? _localization.Get("KeePassDatabase") :
        item.BitwardenVaultId is not null ? _localization.Get("BitwardenVault") :
        item.MdbxDatabaseId is not null ? _localization.Get("MdbxDatabase") :
        _localization.Get("LocalOnly");

    private string ResolveSource(SecureItem item) =>
        item.KeepassDatabaseId is not null ? _localization.Get("KeePassDatabase") :
        item.BitwardenVaultId is not null ? _localization.Get("BitwardenVault") :
        item.MdbxDatabaseId is not null ? _localization.Get("MdbxDatabase") :
        _localization.Get("LocalOnly");
}
