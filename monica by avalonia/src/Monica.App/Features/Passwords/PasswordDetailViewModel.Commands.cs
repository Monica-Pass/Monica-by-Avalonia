using CommunityToolkit.Mvvm.Input;

namespace Monica.App.ViewModels;

public sealed partial class PasswordDetailViewModel
{
    [RelayCommand]
    private async Task CopyFieldAsync(PasswordDetailField? field)
    {
        if (IsSensitiveStateCleared || field is null || !field.CanCopy || string.IsNullOrWhiteSpace(field.CopyValue))
        {
            return;
        }

        await _clipboardService.SetSensitiveTextAsync(field.CopyValue);
        StatusText = L.Format("CopiedFieldFormat", field.Label);
    }

    [RelayCommand]
    private void ToggleFieldVisibility(PasswordDetailField? field)
    {
        if (IsSensitiveStateCleared || field is null || !field.CanToggleVisibility)
        {
            return;
        }

        field.IsVisible = !field.IsVisible;
    }

    [RelayCommand(CanExecute = nameof(CanAddAttachment))]
    private async Task AddAttachmentAsync()
    {
        if (!CanAddAttachment())
        {
            return;
        }

        IsAddingAttachment = true;
        try
        {
            var result = await _addAttachment!(Entry);
            if (IsSensitiveStateCleared)
            {
                return;
            }

            if (result.Outcome == PasswordAttachmentAddOutcome.Added && result.Attachment is not null)
            {
                InsertAttachmentIfMissing(result.Attachment);
            }

            if (!string.IsNullOrWhiteSpace(result.StatusText))
            {
                StatusText = result.StatusText;
            }
        }
        finally
        {
            IsAddingAttachment = false;
        }
    }

    private bool CanAddAttachment() =>
        !IsSensitiveStateCleared && !IsAddingAttachment && _addAttachment is not null;

    private void InsertAttachmentIfMissing(Monica.Core.Models.Attachment attachment)
    {
        var alreadyPresent = Attachments.Any(item =>
            attachment.Id > 0 && item.Attachment.Id == attachment.Id ||
            attachment.Id <= 0 &&
            string.Equals(item.Attachment.StoragePath, attachment.StoragePath, StringComparison.Ordinal) &&
            string.Equals(item.FileName, attachment.FileName, StringComparison.Ordinal));
        if (alreadyPresent)
        {
            return;
        }

        Attachments.Insert(0, new PasswordAttachmentItem(L, attachment));
        OnPropertyChanged(nameof(HasAttachments));
    }

    [RelayCommand]
    private async Task SaveAttachmentAsync(PasswordAttachmentItem? item)
    {
        if (IsSensitiveStateCleared || item is null || _saveAttachment is null)
        {
            return;
        }

        var fileName = item.FileName;
        var result = await _saveAttachment(item.Attachment);
        StatusText = result.Outcome switch
        {
            PasswordAttachmentSaveOutcome.Saved => L.Format("SavedAttachmentFormat", fileName),
            PasswordAttachmentSaveOutcome.AuthorizationFailed => L.Get("AttachmentSaveAuthorizationFailed"),
            PasswordAttachmentSaveOutcome.ContentUnavailable => L.Format("AttachmentContentUnavailableFormat", fileName),
            PasswordAttachmentSaveOutcome.Failed => L.Format("AttachmentSaveFailedFormat", fileName),
            _ => StatusText
        };
    }

    [RelayCommand]
    private async Task DeleteAttachmentAsync(PasswordAttachmentItem? item)
    {
        if (IsSensitiveStateCleared || item is null || _deleteAttachment is null)
        {
            return;
        }

        if (!await _deleteAttachment(item.Attachment))
        {
            return;
        }

        var fileName = item.FileName;
        Attachments.Remove(item);
        item.ClearSensitiveState();
        OnPropertyChanged(nameof(HasAttachments));
        StatusText = L.Format("DeletedAttachmentFormat", fileName);
    }

    [RelayCommand]
    private void ToggleHistoryPassword(PasswordHistoryItemViewModel? item)
    {
        if (IsSensitiveStateCleared || item is null || !item.CanCopy)
        {
            return;
        }

        item.IsVisible = !item.IsVisible;
    }

    [RelayCommand]
    private async Task CopyHistoryPasswordAsync(PasswordHistoryItemViewModel? item)
    {
        if (IsSensitiveStateCleared || item is null || !item.CanCopy || string.IsNullOrWhiteSpace(item.Password))
        {
            return;
        }

        await _clipboardService.SetSensitiveTextAsync(item.Password);
        StatusText = L.Get("CopiedPasswordHistory");
    }

    [RelayCommand]
    private async Task DeleteHistoryPasswordAsync(PasswordHistoryItemViewModel? item)
    {
        if (IsSensitiveStateCleared || item is null || _deletePasswordHistory is null)
        {
            return;
        }

        if (!await _deletePasswordHistory(item.Entry))
        {
            return;
        }

        PasswordHistory.Remove(item);
        item.ClearSensitiveState();
        OnPropertyChanged(nameof(HasPasswordHistory));
        StatusText = L.Get("DeletedPasswordHistoryEntry");
    }

    [RelayCommand]
    private async Task ClearPasswordHistoryAsync()
    {
        if (IsSensitiveStateCleared || _clearPasswordHistory is null || PasswordHistory.Count == 0)
        {
            return;
        }

        if (!await _clearPasswordHistory(Entry.Id))
        {
            return;
        }

        ClearPasswordHistoryPresentation();
        StatusText = L.Get("ClearedPasswordHistory");
    }
}
