using CommunityToolkit.Mvvm.Input;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task RestoreSelectedDeletedPasswordsAsync()
    {
        var selected = RecycleBinItems.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        foreach (var entry in selected)
        {
            await RestoreRecycleBinItemAsync(entry);
        }

        ClearDeletedPasswordSelection();
        StatusMessage = _localization.Format(
            selected.All(item => item.Password is not null)
                ? "RestoredSelectedPasswordsFormat"
                : "RestoredSelectedRecycleBinItemsFormat",
            selected.Length);
    }

    [RelayCommand]
    private async Task DeleteSelectedDeletedPasswordsPermanentlyAsync()
    {
        var selected = RecycleBinItems.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0 || !await ConfirmSelectedPermanentDeleteAsync(selected.Length))
        {
            return;
        }

        foreach (var entry in selected)
        {
            await DeleteRecycleBinItemPermanentlyCoreAsync(entry);
        }

        ClearDeletedPasswordSelection();
        RaiseRecycleBinCountStateForUnifiedItems();
        InvalidateSecurityAnalysis();
        RecycleBinNarrowShowsList = true;
        StatusMessage = _localization.Format(
            selected.All(item => item.Password is not null)
                ? "DeletedSelectedPasswordsPermanentlyFormat"
                : "DeletedSelectedRecycleBinItemsPermanentlyFormat",
            selected.Length);
    }

    private Task<bool> ConfirmSelectedPermanentDeleteAsync(int count) =>
        _confirmationDialogService.ConfirmTypedAsync(
            _localization.Get("DeleteSelectedPermanentlyConfirmationTitle"),
            _localization.Format("DeleteSelectedPermanentlyConfirmationMessageFormat", count),
            _localization.Get("PermanentDeleteConfirmationPhrase"),
            _localization.Format(
                "PermanentDeleteConfirmationInstructionFormat",
                _localization.Get("PermanentDeleteConfirmationPhrase")),
            _localization.Get("DeletePermanently"),
            _localization.Cancel);
}
