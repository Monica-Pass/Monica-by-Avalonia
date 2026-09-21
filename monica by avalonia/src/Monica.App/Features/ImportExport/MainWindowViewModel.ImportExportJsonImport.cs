using System.Text.Json;
using Monica.Core.ImportExport;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task ImportMonicaJsonTextAsync(string json, bool clearEditorOnSuccess)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            StatusMessage = _localization.Get("ImportJsonRequired");
            return;
        }

        try
        {
            var result = await ImportMonicaJsonAsync(json);
            if (clearEditorOnSuccess)
            {
                ImportJsonText = "";
            }

            StatusMessage = FormatMonicaJsonImportStatus(result);
        }
        catch (PasswordSecretUnavailableException error)
        {
            StatusMessage = GetPasswordSecretUnavailableMessage(error);
        }
        catch (MonicaJsonImportException error)
        {
            StatusMessage = _localization.Get(error.Error switch
            {
                MonicaJsonImportError.ResourceLimitExceeded => "ImportResourceLimitExceeded",
                _ => "ImportInvalidFormat"
            });
        }
        catch (Exception ex)
        {
            ReportImportExportFailure("Importing Monica JSON failed", "ImportUnexpectedFailure", ex);
        }
    }

    private async Task<MonicaJsonImportResult> ImportMonicaJsonAsync(string json)
    {
        var result = await GetMonicaJsonImportUseCase().ExecuteAsync(json);

        await LogOperationAsync(new OperationLog
        {
            ItemType = "VAULT",
            ItemTitle = _localization.Get("MonicaJson"),
            OperationType = "IMPORT",
            ChangesJson = JsonSerializer.Serialize(new { result.Passwords, result.SecureItems, result.Categories }),
            DeviceName = Environment.MachineName
        });
        await LoadAsync();
        return result;
    }

    private async Task ImportPasswordAttachmentAsync(Attachment source, long importedPasswordId, byte[] content)
    {
        var attachment = CloneAttachmentForImport(source, importedPasswordId);
        var draft = await _passwordAttachmentFileService.StoreAttachmentAsync(
            attachment.FileName,
            content,
            attachment.ContentType);
        attachment.StoragePath = draft.StoragePath;
        attachment.SizeBytes = draft.SizeBytes;
        if (string.IsNullOrWhiteSpace(attachment.ContentType))
        {
            attachment.ContentType = draft.ContentType;
        }

        var originalStoragePath = attachment.StoragePath;
        await _repository.SaveAttachmentAsync(attachment, content);
        if (!string.Equals(originalStoragePath, attachment.StoragePath, StringComparison.Ordinal) &&
            !originalStoragePath.StartsWith("mdbx:", StringComparison.OrdinalIgnoreCase))
        {
            await _passwordAttachmentFileService.DeleteStoredAttachmentAsync(originalStoragePath);
        }
    }

    private string FormatMonicaJsonImportStatus(MonicaJsonImportResult result) =>
        result.Categories > 0
            ? _localization.Format("ImportedMonicaJsonWithCategoriesFormat", result.Passwords, result.SecureItems, result.Categories)
            : _localization.Format("ImportedMonicaJsonFormat", result.Passwords, result.SecureItems);
}
