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
        }

        SetStatusMessage("DeletedAttachmentFormat", attachment.FileName);
        return true;
    }
}
