using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public string SortUpdatedText => _localization.Get("SortUpdated");
    public string SortTitleText => _localization.Get("SortTitle");
    public string SortWebsiteText => _localization.Get("SortWebsite");
    public string SortUsernameText => _localization.Get("SortUsername");
    public string SortCreatedText => _localization.Get("SortCreated");
    public string SortFavoritesText => _localization.Get("SortFavorites");
    public string PasswordSortButtonTip => $"{_localization.SortPasswords}: {GetPasswordSortLabel(SelectedPasswordSort)}";
    public bool IsSortUpdatedSelected => string.Equals(SelectedPasswordSort, "updated-desc", StringComparison.Ordinal);
    public bool IsSortTitleSelected => string.Equals(SelectedPasswordSort, "title-asc", StringComparison.Ordinal);
    public bool IsSortWebsiteSelected => string.Equals(SelectedPasswordSort, "website-asc", StringComparison.Ordinal);
    public bool IsSortUsernameSelected => string.Equals(SelectedPasswordSort, "username-asc", StringComparison.Ordinal);
    public bool IsSortCreatedSelected => string.Equals(SelectedPasswordSort, "created-desc", StringComparison.Ordinal);
    public bool IsSortFavoritesSelected => string.Equals(SelectedPasswordSort, "favorites-first", StringComparison.Ordinal);
    public bool CanStackSelectedPasswords => SelectedPasswordCount > 1;
    public bool CanManageSelectedPasswordFolder => SelectedPasswordFolderFilter?.Id is > 0;
    public Thickness PasswordListCardPadding => CompactPasswordList ? new Thickness(12, 8) : new Thickness(16);
    public double PasswordListAvatarSize => CompactPasswordList ? 36 : 48;
    public double PasswordListAvatarFontSize => CompactPasswordList ? 14 : 18;
    public double PasswordListRowMinHeight => CompactPasswordList ? 40 : 54;
    public CornerRadius PasswordListAvatarCornerRadius => new(PasswordListAvatarSize / 2);
    public Thickness PasswordListContentMargin => CompactPasswordList ? new Thickness(10, 0, 0, 0) : new Thickness(14, 0, 0, 0);
    public bool ShowPasswordListDetails => !CompactPasswordList;
    public int SelectedPasswordCount => _selectedPasswordCount;
    public bool HasSelectedPasswords => SelectedPasswordCount > 0;
    public bool HasSelectedPassword => SelectedPassword is not null;
    public bool HasNoSelectedPassword => SelectedPassword is null;
    public bool HasSelectedPasswordDetails => SelectedPasswordDetails is not null;
    public bool HasCurrentSelectedPasswordDetails =>
        SelectedPassword is not null &&
        SelectedPasswordDetails?.Entry.Id == SelectedPassword.Id;
    public bool HasSelectedPasswordLoadingState =>
        SelectedPassword is not null &&
        IsLoadingSelectedPasswordDetails;
    public bool HasSelectedPasswordDetailsError =>
        SelectedPassword is not null &&
        !string.IsNullOrWhiteSpace(SelectedPasswordDetailsError);
    public bool HasRecoverableStatusMessage =>
        IsUnlocked &&
        !IsLoadingVault &&
        (HasPendingLegacyBusinessData || IsRecoverableStatusMessage(StatusMessage));
    public bool AreAllFilteredPasswordsSelected
    {
        get
        {
            var filtered = FilteredPasswords.ToArray();
            return filtered.Length > 0 && filtered.All(item => item.IsSelected);
        }
        set
        {
            UpdatePasswordSelectionsInBatch(() =>
            {
                foreach (var item in FilteredPasswords)
                {
                    item.IsSelected = value;
                }
            });
        }
    }

    public IEnumerable<PasswordQuickAccessItem> RecentPasswordQuickAccessItems =>
        BuildQuickAccessItems(QuickAccessSort.Recent);

    public IEnumerable<PasswordQuickAccessItem> FrequentPasswordQuickAccessItems =>
        BuildQuickAccessItems(QuickAccessSort.Frequent);

    public bool HasPasswordQuickAccessItems => RecentPasswordQuickAccessItems.Any() || FrequentPasswordQuickAccessItems.Any();

    public IReadOnlyList<PasswordEntry> FilteredPasswords => GetFilteredPasswords();
    public IReadOnlyList<PasswordListRow> FilteredPasswordRows => GetFilteredPasswordRows();
    public IReadOnlyList<PasswordEntry> VisiblePasswordNavigationEntries =>
        FilteredPasswordRows
            .Where(row => row.IsPasswordEntryRow || row.IsStackHeader)
            .Select(row => row.Entry)
            .ToArray();
}
