using System.Security.Cryptography;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task<PasswordAttachmentSaveResult> SavePasswordAttachmentAsync(Attachment attachment)
    {
        if (!await AuthorizeFileExportAsync())
        {
            return new PasswordAttachmentSaveResult(PasswordAttachmentSaveOutcome.AuthorizationFailed);
        }

        byte[]? content = null;
        try
        {
            content = await _repository.TryReadAttachmentContentAsync(attachment);
            if (content is null)
            {
                return new PasswordAttachmentSaveResult(PasswordAttachmentSaveOutcome.ContentUnavailable);
            }

            var suggestedFileName = GetAttachmentExportFileName(attachment.FileName);
            var savedFileName = await _fileSystemPickerService.SaveBinaryFileAsync(
                _localization.Get("SaveAttachment"),
                suggestedFileName,
                content,
                []);
            return new PasswordAttachmentSaveResult(
                savedFileName is null
                    ? PasswordAttachmentSaveOutcome.Cancelled
                    : PasswordAttachmentSaveOutcome.Saved);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error($"Password attachment export failed. attachmentId={attachment.Id}", ex);
            return new PasswordAttachmentSaveResult(PasswordAttachmentSaveOutcome.Failed);
        }
        finally
        {
            if (content is not null)
            {
                CryptographicOperations.ZeroMemory(content);
            }
        }
    }

    private string GetAttachmentExportFileName(string fileName)
    {
        var normalized = fileName.Replace('\\', '/');
        var safeName = Path.GetFileName(normalized).Trim();
        return string.IsNullOrWhiteSpace(safeName)
            ? _localization.Get("Attachment")
            : safeName;
    }
}
