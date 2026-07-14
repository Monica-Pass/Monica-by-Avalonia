using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task ToggleFavoriteAsync(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        entry.IsFavorite = !entry.IsFavorite;
        await _repository.SavePasswordAsync(entry);
        await LogOperationAsync(new OperationLog
        {
            ItemType = "PASSWORD",
            ItemId = entry.Id,
            ItemTitle = entry.Title,
            OperationType = "FAVORITE",
            DeviceName = Environment.MachineName
        });
        InvalidateSecurityAnalysis();
        RaiseFilteredPasswordsChanged();
    }

    [RelayCommand]
    private async Task DeletePasswordAsync(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        if (!await _confirmationDialogService.ConfirmAsync(
            _localization.Get("DeletePasswordConfirmationTitle"),
            _localization.Format("DeletePasswordConfirmationMessageFormat", entry.Title),
            _localization.Get("MoveToRecycleBin"),
            _localization.Cancel))
        {
            return;
        }

        var siblings = entry.IsArchived
            ? GetArchivedPasswordSiblings(entry).ToList()
            : GetPasswordSiblings(entry).ToList();
        await DeletePasswordGroupAsync(entry, siblings, updateStatus: true);
    }

    private Task<bool> ConfirmMoveItemToRecycleBinAsync(string itemTitle) =>
        _confirmationDialogService.ConfirmAsync(
            _localization.Get("DeleteItemConfirmationTitle"),
            _localization.Format("DeleteItemConfirmationMessageFormat", itemTitle),
            _localization.Get("MoveToRecycleBin"),
            _localization.Cancel);

    private Task<bool> ConfirmMoveSelectedItemsToRecycleBinAsync(int count) =>
        _confirmationDialogService.ConfirmAsync(
            _localization.Get("DeleteSelectedItemsConfirmationTitle"),
            _localization.Format("DeleteSelectedItemsConfirmationMessageFormat", count),
            _localization.Get("MoveToRecycleBin"),
            _localization.Cancel);

    private Task<bool> ConfirmDeleteFolderAsync(string name, int affectedPasswordCount) =>
        _confirmationDialogService.ConfirmAsync(
            _localization.Get("DeleteFolderConfirmationTitle"),
            _localization.Format("DeleteFolderConfirmationMessageFormat", name, affectedPasswordCount),
            _localization.Get("DeleteFolder"),
            _localization.Cancel);

    private Task<bool> ConfirmDeleteAttachmentAsync(string fileName) =>
        _confirmationDialogService.ConfirmAsync(
            _localization.Get("DeleteAttachmentConfirmationTitle"),
            _localization.Format("DeleteAttachmentConfirmationMessageFormat", fileName),
            _localization.Get("Delete"),
            _localization.Cancel);

    private Task<bool> ConfirmDeletePasswordHistoryAsync() =>
        _confirmationDialogService.ConfirmAsync(
            _localization.Get("DeletePasswordHistoryConfirmationTitle"),
            _localization.Get("DeletePasswordHistoryConfirmationMessage"),
            _localization.Get("Delete"),
            _localization.Cancel);

    private Task<bool> ConfirmClearPasswordHistoryAsync() =>
        _confirmationDialogService.ConfirmAsync(
            _localization.Get("ClearPasswordHistoryConfirmationTitle"),
            _localization.Get("ClearPasswordHistoryConfirmationMessage"),
            _localization.Get("ClearPasswordHistory"),
            _localization.Cancel);

    [RelayCommand]
    private async Task ArchivePasswordAsync(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        var siblings = GetPasswordSiblings(entry).ToList();
        await ArchivePasswordGroupAsync(entry, siblings, updateStatus: true);
    }

    private async Task ArchivePasswordGroupAsync(PasswordEntry entry, IReadOnlyList<PasswordEntry> siblings, bool updateStatus)
    {
        foreach (var item in siblings)
        {
            item.IsArchived = true;
            item.ArchivedAt = DateTimeOffset.UtcNow;
            item.IsSelected = false;
            await _repository.SavePasswordAsync(item);
            await LogOperationAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemId = item.Id,
                ItemTitle = item.Title,
                OperationType = "ARCHIVE",
                DeviceName = Environment.MachineName
            });
            Passwords.Remove(item);
            var current = Passwords.FirstOrDefault(password => password.Id == item.Id);
            if (current is not null)
            {
                current.IsSelected = false;
                Passwords.Remove(current);
            }

            TrackPasswordSelection(item);
            ArchivedPasswords.Insert(0, item);
        }

        RefreshBoundTotpPresentation(siblings);
        RaiseCounts();
        RefreshPasswordSelectionStateFromPasswords();
        RaiseFilteredPasswordsChanged();
        InvalidateSecurityAnalysis();
        if (updateStatus)
        {
            StatusMessage = _localization.Format("ArchivedPasswordFormat", entry.Title);
        }
    }

    private async Task DeletePasswordGroupAsync(PasswordEntry entry, IReadOnlyList<PasswordEntry> siblings, bool updateStatus)
    {
        foreach (var item in siblings)
        {
            await _repository.SoftDeletePasswordAsync(item.Id);
            await LogOperationAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemId = item.Id,
                ItemTitle = item.Title,
                OperationType = "DELETE",
                DeviceName = Environment.MachineName
            });
            item.IsSelected = false;
            Passwords.Remove(item);
            var current = Passwords.FirstOrDefault(password => password.Id == item.Id);
            if (current is not null)
            {
                current.IsSelected = false;
                Passwords.Remove(current);
            }

            ArchivedPasswords.Remove(item);
            var archived = ArchivedPasswords.FirstOrDefault(password => password.Id == item.Id);
            if (archived is not null)
            {
                archived.IsSelected = false;
                ArchivedPasswords.Remove(archived);
            }

            item.IsDeleted = true;
            item.DeletedAt = DateTimeOffset.UtcNow;
            item.IsArchived = false;
            item.ArchivedAt = null;
            TrackPasswordSelection(item);
            DeletedPasswords.Insert(0, item);
        }

        RefreshBoundTotpPresentation(siblings);
        RaiseCounts();
        RefreshPasswordSelectionStateFromPasswords();
        RaiseFilteredPasswordsChanged();
        InvalidateSecurityAnalysis();
        if (updateStatus)
        {
            StatusMessage = _localization.Format("MovedToRecycleBinFormat", entry.Title);
        }
    }

}
