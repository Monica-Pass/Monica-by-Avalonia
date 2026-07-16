using CommunityToolkit.Mvvm.Input;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public async Task<long> AddPasswordAttachmentMetadataAsync(
        PasswordEntry entry,
        string fileName,
        string storagePath,
        long sizeBytes = 0,
        string contentType = "",
        byte[]? content = null,
        CancellationToken cancellationToken = default)
    {
        if (entry.Id == 0)
        {
            throw new ArgumentException("Password entry must be saved before adding attachments.", nameof(entry));
        }

        var attachment = new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = entry.Id,
            FileName = string.IsNullOrWhiteSpace(fileName) ? _localization.Get("Attachment") : fileName.Trim(),
            ContentType = contentType.Trim(),
            StoragePath = storagePath.Trim(),
            SizeBytes = Math.Max(0, sizeBytes),
            CreatedAt = DateTimeOffset.UtcNow,
            BitwardenVaultId = entry.BitwardenVaultId
        };

        var originalStoragePath = attachment.StoragePath;
        var id = content is null
            ? await _repository.SaveAttachmentAsync(attachment, cancellationToken)
            : await _repository.SaveAttachmentAsync(attachment, content, cancellationToken);
        if (content is not null &&
            !string.Equals(originalStoragePath, attachment.StoragePath, StringComparison.Ordinal) &&
            !originalStoragePath.StartsWith("mdbx:", StringComparison.OrdinalIgnoreCase))
        {
            await _passwordAttachmentFileService.DeleteStoredAttachmentAsync(originalStoragePath, cancellationToken);
        }

        SetPasswordAttachmentOwnerState(entry.Id, hasAttachments: true);
        AddPasswordAttachmentSearchMatch(entry.Id, attachment);
        RefreshPasswordAttachmentState(entry);
        RaiseFilteredPasswordsChanged();
        await LogOperationAsync(new OperationLog
        {
            ItemType = "PASSWORD",
            ItemId = entry.Id,
            ItemTitle = entry.Title,
            OperationType = "ATTACHMENT",
            DeviceName = Environment.MachineName
        }, cancellationToken);
        StatusMessage = _localization.Format("AddedAttachmentFormat", attachment.FileName, entry.Title);
        return id;
    }

    [RelayCommand]
    private async Task AddPasswordAttachmentAsync(PasswordEntry? entry)
    {
        if (entry is null || entry.Id <= 0 || entry.IsDeleted)
        {
            return;
        }

        var draft = await _passwordAttachmentFileService.PickAndStoreAttachmentAsync(entry);
        if (draft is null)
        {
            return;
        }

        await AddPasswordAttachmentMetadataAsync(entry, draft.FileName, draft.StoragePath, draft.SizeBytes, draft.ContentType, draft.Content);
    }

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
        var name = NewFolderName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = _localization.Get("FolderNameRequired");
            return;
        }

        var existing = Categories.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectedPasswordFolderFilter = PasswordFolderFilters.FirstOrDefault(item => item.Id == existing.Id);
            NewFolderName = "";
            StatusMessage = _localization.Format("SelectedFolderFormat", existing.Name);
            return;
        }

        var category = new Category
        {
            Name = name,
            SortOrder = Categories.Count == 0 ? 1 : Categories.Max(item => item.SortOrder) + 1
        };
        await _repository.SaveCategoryAsync(category);
        Categories.Add(category);
        RefreshPasswordFolderFilters(category.Id);
        NewFolderName = "";
        StatusMessage = _localization.Format("CreatedFolderFormat", category.Name);
    }

    [RelayCommand]
    private async Task RenameSelectedPasswordFolderAsync()
    {
        var category = GetSelectedPasswordFolderCategory();
        var name = NewFolderName.Trim();
        if (category is null)
        {
            StatusMessage = _localization.Get("SelectFolderToManage");
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = _localization.Get("FolderNameRequired");
            return;
        }

        var duplicate = Categories.FirstOrDefault(item =>
            item.Id != category.Id &&
            item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
        {
            StatusMessage = _localization.Format("FolderAlreadyExistsFormat", duplicate.Name);
            return;
        }

        var oldName = category.Name;
        category.Name = name;
        await _repository.SaveCategoryAsync(category);
        RefreshPasswordFolderFilters(category.Id);
        NewFolderName = "";
        await LogOperationAsync(new OperationLog
        {
            ItemType = "CATEGORY",
            ItemId = category.Id,
            ItemTitle = category.Name,
            OperationType = "UPDATE",
            DeviceName = Environment.MachineName
        });
        StatusMessage = _localization.Format("RenamedFolderFormat", oldName, category.Name);
    }

    [RelayCommand]
    private async Task DeleteSelectedPasswordFolderAsync()
    {
        var category = GetSelectedPasswordFolderCategory();
        if (category is null)
        {
            StatusMessage = _localization.Get("SelectFolderToManage");
            return;
        }

        var movedPasswords = Passwords.Count(item => item.CategoryId == category.Id);
        var name = category.Name;
        if (!await ConfirmDeleteFolderAsync(name, movedPasswords))
        {
            return;
        }

        await _repository.DeleteCategoryAsync(category.Id);
        Categories.Remove(category);
        foreach (var password in Passwords.Where(item => item.CategoryId == category.Id))
        {
            password.CategoryId = null;
        }

        foreach (var item in TotpItems.Where(item => item.CategoryId == category.Id))
        {
            item.CategoryId = null;
        }

        foreach (var item in NoteItems.Where(item => item.CategoryId == category.Id))
        {
            item.CategoryId = null;
        }

        foreach (var item in WalletItems.Where(item => item.CategoryId == category.Id))
        {
            item.CategoryId = null;
        }

        await LogOperationAsync(new OperationLog
        {
            ItemType = "CATEGORY",
            ItemId = category.Id,
            ItemTitle = name,
            OperationType = "DELETE",
            DeviceName = Environment.MachineName
        });
        RefreshPasswordFolderFilters(-1);
        StatusMessage = _localization.Format("DeletedFolderFormat", name, movedPasswords);
    }

}
