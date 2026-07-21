using CommunityToolkit.Mvvm.Input;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task<bool> DeletePasswordAttachmentAsync(Attachment attachment)
    {
        if (!await ConfirmDeleteAttachmentAsync(attachment.FileName))
        {
            return false;
        }

        await _repository.DeleteAttachmentAsync(attachment.Id, attachment);
        await _passwordAttachmentFileService.DeleteStoredAttachmentAsync(attachment.StoragePath);
        var remaining = await _repository.GetAttachmentsAsync("PASSWORD", attachment.OwnerId);
        SetPasswordAttachmentOwnerState(attachment.OwnerId, remaining.Count > 0);
        RefreshPasswordAttachmentSearchMatch(attachment.OwnerId, remaining);

        var entry = Passwords
            .Concat(ArchivedPasswords)
            .Concat(DeletedPasswords)
            .FirstOrDefault(item => item.Id == attachment.OwnerId);
        if (entry is not null)
        {
            RefreshPasswordAttachmentState(entry);
            RaiseFilteredPasswordsChanged();
        }

        StatusMessage = _localization.Format("DeletedAttachmentFormat", attachment.FileName);
        return true;
    }

    [RelayCommand]
    private void TogglePasswordFolderExpansion(PasswordFolderFilterChoice? item)
    {
        if (item is null || !item.HasChildren || string.IsNullOrWhiteSpace(item.SelectionKey))
        {
            return;
        }

        if (!_collapsedPasswordFolderKeys.Add(item.SelectionKey))
        {
            _collapsedPasswordFolderKeys.Remove(item.SelectionKey);
        }

        RefreshPasswordFolderFilters();
    }

    [RelayCommand]
    private async Task CreatePasswordFolderAsync()
    {
        var result = await CreateLocalCategoryAsync(GetSelectedPasswordFolderPath(), NewFolderName);
        if (result is null)
        {
            return;
        }

        RefreshPasswordFolderFilters(result.Category.Id);
        NewFolderName = "";
    }

    [RelayCommand]
    private async Task RenameSelectedPasswordFolderAsync()
    {
        var category = GetSelectedPasswordFolderCategory();
        var result = await RenameLocalCategoryAsync(category, NewFolderName);
        if (result is null)
        {
            return;
        }

        RefreshPasswordFolderFilters(result.Category.Id);
        NewFolderName = "";
    }

    [RelayCommand]
    private async Task DeleteSelectedPasswordFolderAsync()
    {
        var category = GetSelectedPasswordFolderCategory();
        var result = await DeleteLocalCategoryAsync(category);
        if (result is not null)
        {
            RefreshPasswordFolderFilters(-1);
            StatusMessage = _localization.Format("DeletedFolderFormat", result.Name, result.PasswordCount);
        }
    }

}
