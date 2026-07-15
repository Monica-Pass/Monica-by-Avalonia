using CommunityToolkit.Mvvm.Input;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private void ClearRecycleBinSearch() => RecycleBinSearchText = "";

    [RelayCommand]
    private void CloseDeletedPasswordDetails() => RecycleBinNarrowShowsList = true;

    [RelayCommand]
    private async Task RestorePasswordAsync(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        var siblings = GetDeletedPasswordSiblings(entry).ToList();
        foreach (var item in siblings)
        {
            await _repository.RestorePasswordAsync(item.Id);
            await LogOperationAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemId = item.Id,
                ItemTitle = item.Title,
                OperationType = "RESTORE",
                DeviceName = Environment.MachineName
            });
            DeletedPasswords.Remove(item);
            var current = DeletedPasswords.FirstOrDefault(password => password.Id == item.Id);
            if (current is not null)
            {
                DeletedPasswords.Remove(current);
            }
            item.IsDeleted = false;
            item.DeletedAt = null;
            item.IsSelected = false;
            RefreshPasswordTotpDisplay(item);
        }

        ReplacePasswordGroup([], siblings);
        RefreshBoundTotpPresentation(siblings);
        RaisePasswordCountState();
        RaiseFilteredPasswordsChanged();
        RecycleBinNarrowShowsList = true;
        InvalidateSecurityAnalysis();
        StatusMessage = _localization.Format("RestoredPasswordFormat", entry.Title);
    }

    [RelayCommand]
    private async Task DeletePasswordPermanentlyAsync(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        if (!await ConfirmPermanentDeleteAsync(entry.Title))
        {
            return;
        }

        var siblings = await PurgeDeletedPasswordGroupAsync(entry);

        RefreshBoundTotpPresentation(siblings);
        RaisePasswordCountState();
        InvalidateSecurityAnalysis();
        RecycleBinNarrowShowsList = true;
        StatusMessage = _localization.Format("DeletedPasswordPermanentlyFormat", entry.Title);
    }

    private async Task<IReadOnlyList<PasswordEntry>> PurgeDeletedPasswordGroupAsync(PasswordEntry entry)
    {
        var siblings = GetDeletedPasswordSiblings(entry).ToList();
        foreach (var item in siblings)
        {
            item.IsSelected = false;
            await _repository.DeletePasswordPermanentlyAsync(item.Id);
            await LogOperationAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemId = item.Id,
                ItemTitle = item.Title,
                OperationType = "PURGE",
                DeviceName = Environment.MachineName
            });
            DeletedPasswords.Remove(item);
            var current = DeletedPasswords.FirstOrDefault(password => password.Id == item.Id);
            if (current is not null)
            {
                DeletedPasswords.Remove(current);
            }
        }

        return siblings;
    }

    [RelayCommand]
    private async Task EmptyRecycleBinAsync()
    {
        var items = DeletedPasswords.ToArray();
        if (items.Length == 0)
        {
            return;
        }

        if (!await ConfirmEmptyRecycleBinAsync(items.Length))
        {
            return;
        }

        foreach (var item in items)
        {
            await _repository.DeletePasswordPermanentlyAsync(item.Id);
            await LogOperationAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemId = item.Id,
                ItemTitle = item.Title,
                OperationType = "PURGE",
                DeviceName = Environment.MachineName
            });
        }

        DeletedPasswords.Clear();
        RefreshBoundTotpPresentation(items);
        RaisePasswordCountState();
        InvalidateSecurityAnalysis();
        StatusMessage = _localization.Format("EmptiedRecycleBinFormat", items.Length);
    }

    [RelayCommand]
    private void ShowDeletedPasswordDetails(PasswordEntry? entry)
    {
        if (entry is not null)
        {
            SelectedDeletedPassword = entry;
            RecycleBinNarrowShowsList = false;
        }
    }

    private Task<bool> ConfirmPermanentDeleteAsync(string itemTitle) =>
        _confirmationDialogService.ConfirmTypedAsync(
            _localization.Get("DeletePermanentlyConfirmationTitle"),
            _localization.Format("DeletePermanentlyConfirmationMessageFormat", itemTitle),
            _localization.Get("PermanentDeleteConfirmationPhrase"),
            _localization.Format(
                "PermanentDeleteConfirmationInstructionFormat",
                _localization.Get("PermanentDeleteConfirmationPhrase")),
            _localization.Get("DeletePermanently"),
            _localization.Cancel);

    private Task<bool> ConfirmEmptyRecycleBinAsync(int count) =>
        _confirmationDialogService.ConfirmTypedAsync(
            _localization.Get("EmptyRecycleBinConfirmationTitle"),
            _localization.Format("EmptyRecycleBinConfirmationMessageFormat", count),
            _localization.Get("EmptyRecycleBinConfirmationPhrase"),
            _localization.Format(
                "EmptyRecycleBinConfirmationInstructionFormat",
                _localization.Get("EmptyRecycleBinConfirmationPhrase")),
            _localization.Get("EmptyRecycleBin"),
            _localization.Cancel);
}
