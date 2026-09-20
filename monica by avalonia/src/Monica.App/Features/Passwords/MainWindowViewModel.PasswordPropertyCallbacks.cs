using Avalonia;
using Avalonia.Threading;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    partial void OnQuickFilter2FaChanged(bool value) => RaiseVaultFilterState();
    partial void OnQuickFilterNotesChanged(bool value) => RaiseVaultFilterState();
    partial void OnQuickFilterPasskeyChanged(bool value) => RaiseVaultFilterState();
    partial void OnQuickFilterBoundNoteChanged(bool value) => RaiseVaultFilterState();
    partial void OnQuickFilterUncategorizedChanged(bool value) => RaiseVaultFilterState();
    partial void OnQuickFilterLocalOnlyChanged(bool value) => RaiseVaultFilterState();
    partial void OnQuickFilterAttachmentsChanged(bool value) => RaiseVaultFilterState();

    partial void OnSelectedPasswordSortChanged(string value)
    {
        UpdateSettings(settings => settings.PasswordSortOrder = value);
        RaiseVaultFilterState();
    }

    partial void OnSelectedPasswordChanged(PasswordEntry? value)
    {
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
