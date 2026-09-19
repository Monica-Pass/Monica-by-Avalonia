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
        if (_isApplyingPasswordSearchImmediately)
        {
            return;
        }

        QueuePasswordSearchQuery(value);
    }

    partial void OnPasswordSearchQueryChanged(string value)
    {
        ReconcilePasswordSearchQuery(value);
        RefreshPasswordFilters();
    }

    partial void OnQuickFilter2FaChanged(bool value) => RefreshPasswordAndVaultFilters();
    partial void OnQuickFilterNotesChanged(bool value) => RefreshPasswordAndVaultFilters();
    partial void OnQuickFilterPasskeyChanged(bool value) => RefreshPasswordAndVaultFilters();
    partial void OnQuickFilterBoundNoteChanged(bool value) => RefreshPasswordAndVaultFilters();
    partial void OnQuickFilterUncategorizedChanged(bool value) => RefreshPasswordAndVaultFilters();
    partial void OnQuickFilterLocalOnlyChanged(bool value) => RefreshPasswordAndVaultFilters();
    partial void OnQuickFilterAttachmentsChanged(bool value) => RefreshPasswordAndVaultFilters();
    partial void OnSelectedPasswordFolderFilterChanged(PasswordFolderFilterChoice? value)
    {
        RaiseFilteredPasswordsChanged();
        RaisePasswordSelectionState();
        ReconcileSelectedPasswordDetails();
        OnPropertyChanged(nameof(CanManageSelectedPasswordFolder));
        OnPropertyChanged(nameof(IsAllPasswordFoldersSelected));
    }
    partial void OnSelectedPasswordSortChanged(string value)
    {
        UpdateSettings(settings => settings.PasswordSortOrder = value);
        RaiseFilteredPasswordsChanged();
        RefreshPasswordSelectionStateFromPasswords();
        RaiseVaultFilterState();
    }

    // One filter switch, two projections: the password list and the library tree read the same state.
    private void RefreshPasswordAndVaultFilters()
    {
        RefreshPasswordFilters();
        RaiseVaultFilterState();
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
            _viewModelDispatcher.Post(() => oldValue?.Dispose(), DispatcherPriority.Background);
        }
    }

    partial void OnSelectedPasswordListRowChanged(PasswordListRow? value)
    {
        if (_isSyncingSelectedPasswordListRow)
        {
            return;
        }

        // A newly materialized ListBox can report null before its ItemsSource
        // has applied the existing selection. Preserve a still-visible VM
        // selection and let the binding converge on the next notification.
        if (value is null && SelectedPassword is { } selectedPassword &&
            FilteredPasswords.Any(entry => entry.Id == selectedPassword.Id))
        {
            SyncSelectedPasswordListRow(selectedPassword);
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
