using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    partial void OnPasswordSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasPasswordSearchText));
        RaisePasswordFilterState();
        if (_isApplyingPasswordSearchImmediately)
        {
            return;
        }

        QueuePasswordSearchQuery(value);
    }

    partial void OnPasswordSearchQueryChanged(string value)
    {
        RefreshPasswordFilters();
    }

    partial void OnQuickFilterFavoriteChanged(bool value) => RefreshPasswordFilters();
    partial void OnQuickFilter2FaChanged(bool value) => RefreshPasswordFilters();
    partial void OnQuickFilterNotesChanged(bool value) => RefreshPasswordFilters();
    partial void OnQuickFilterPasskeyChanged(bool value) => RefreshPasswordFilters();
    partial void OnQuickFilterBoundNoteChanged(bool value) => RefreshPasswordFilters();
    partial void OnQuickFilterUncategorizedChanged(bool value) => RefreshPasswordFilters();
    partial void OnQuickFilterLocalOnlyChanged(bool value) => RefreshPasswordFilters();
    partial void OnQuickFilterAttachmentsChanged(bool value) => RefreshPasswordFilters();
    partial void OnSelectedPasswordFolderFilterChanged(PasswordFolderFilterChoice? value)
    {
        RaiseFilteredPasswordsChanged();
        RaisePasswordFilterState();
        RaisePasswordSelectionState();
        ReconcileSelectedPasswordDetails();
        OnPropertyChanged(nameof(CanManageSelectedPasswordFolder));
    }
    partial void OnSelectedPasswordSortChanged(string value)
    {
        UpdateSettings(settings => settings.PasswordSortOrder = value);
        RaiseFilteredPasswordsChanged();
        RefreshPasswordSelectionStateFromPasswords();
    }

    partial void OnSelectedPasswordChanged(PasswordEntry? value)
    {
        SyncSelectedPasswordListRow(value);
        QueueSelectedPasswordDetailsRefresh(value);
    }

    partial void OnSelectedPasswordDetailsChanged(
        PasswordDetailViewModel? oldValue,
        PasswordDetailViewModel? newValue)
    {
        if (!ReferenceEquals(oldValue, newValue))
        {
            Dispatcher.UIThread.Post(() => oldValue?.Dispose(), DispatcherPriority.Background);
        }
    }

    partial void OnSelectedPasswordListRowChanged(PasswordListRow? value)
    {
        if (_isSyncingSelectedPasswordListRow)
        {
            return;
        }

        SelectedPassword = value?.Entry;
    }

    partial void OnCompactPasswordListChanged(bool value)
    {
        UpdateSettings(settings => settings.CompactPasswordList = value);
        OnPropertyChanged(nameof(PasswordListCardPadding));
        OnPropertyChanged(nameof(PasswordListAvatarSize));
        OnPropertyChanged(nameof(PasswordListAvatarFontSize));
        OnPropertyChanged(nameof(PasswordListRowMinHeight));
        OnPropertyChanged(nameof(PasswordListAvatarCornerRadius));
        OnPropertyChanged(nameof(PasswordListContentMargin));
        OnPropertyChanged(nameof(ShowPasswordListDetails));
    }

    private void RaisePasswordSortText()
    {
        OnPropertyChanged(nameof(SortUpdatedText));
        OnPropertyChanged(nameof(SortTitleText));
        OnPropertyChanged(nameof(SortWebsiteText));
        OnPropertyChanged(nameof(SortUsernameText));
        OnPropertyChanged(nameof(SortCreatedText));
        OnPropertyChanged(nameof(SortFavoritesText));
        OnPropertyChanged(nameof(PasswordSortButtonTip));
    }
}
