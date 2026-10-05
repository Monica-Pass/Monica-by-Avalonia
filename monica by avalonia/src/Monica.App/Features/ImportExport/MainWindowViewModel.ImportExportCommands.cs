using CommunityToolkit.Mvvm.Input;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private Task ExportDataAsync() => RunImportExportOperationAsync(PrepareMonicaJsonExportAsync);

    [RelayCommand]
    private Task ExportPasswordCsvAsync() => RunImportExportOperationAsync(PreparePasswordCsvExportAsync);

    [RelayCommand]
    private Task ExportNoteCsvAsync() => RunImportExportOperationAsync(PrepareNoteCsvExportAsync);

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private Task SaveMonicaJsonExportAsync() =>
        RunImportExportOperationAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(ExportPreview) && !await PrepareMonicaJsonExportAsync())
            {
                return;
            }

            await SaveExportTextAsync(
                _localization.Get("ExportData"),
                $"monica_export_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.json",
                ExportPreview,
                MonicaJsonFileTypes);
        });

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private Task SavePasswordCsvExportAsync() =>
        RunImportExportOperationAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(ExportCsvPreview) && !await PreparePasswordCsvExportAsync())
            {
                return;
            }

            await SaveExportTextAsync(
                _localization.Get("ExportPasswordCsv"),
                $"monica_passwords_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.csv",
                ExportCsvPreview,
                PasswordCsvFileTypes);
        });

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private Task SaveNoteCsvExportAsync() =>
        RunImportExportOperationAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(ExportNoteCsvPreview) && !await PrepareNoteCsvExportAsync())
            {
                return;
            }

            await SaveExportTextAsync(
                _localization.Get("ExportNoteCsv"),
                $"monica_notes_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.csv",
                ExportNoteCsvPreview,
                NoteCsvFileTypes);
        });

    private async Task<bool> PrepareMonicaJsonExportAsync()
    {
        if (!await AuthorizeSensitiveExportAsync())
        {
            return false;
        }

        try
        {
            ExportPreview = await BuildMonicaJsonExportAsync(
                includePasswords: true,
                includeTotp: true,
                includeNotes: true,
                includeCards: true,
                includeDocuments: true,
                includeImages: true,
                includeCategories: true);
        }
        catch (Monica.Data.Mdbx.MdbxVaultReadOnlyException ex) when (ex.ReasonCode == "unsupported-vault-objects")
        {
            ExportPreview = "";
            SetStatusFailure("MdbxUnsupportedObjectsProtected");
            return false;
        }
        catch (PasswordSecretUnavailableException ex)
        {
            ExportPreview = "";
            SetStatusFailure(PasswordSecretUnavailableKey(ex));
            return false;
        }

        SetStatusNotice("ExportPrepared");
        return true;
    }

    private async Task<bool> PreparePasswordCsvExportAsync()
    {
        if (!await AuthorizeSensitiveExportAsync())
        {
            return false;
        }

        PasswordEntry[] exportPasswords;
        IReadOnlyDictionary<long, IReadOnlyList<CustomField>> customFields;
        try
        {
            var sourcePasswords = (await _repository.GetPasswordsAsync()).ToArray();
            customFields = await _repository.GetCustomFieldsByEntryIdsAsync(
                sourcePasswords.Select(item => item.Id).ToArray());
            exportPasswords = sourcePasswords
                .Select(item => ClonePasswordForExport(item))
                .ToArray();
        }
        catch (PasswordSecretUnavailableException ex)
        {
            ExportCsvPreview = "";
            SetStatusFailure(PasswordSecretUnavailableKey(ex));
            return false;
        }

        ExportCsvPreview = await Task.Run(() => _importExportService.ExportPasswordCsv(exportPasswords, customFields));
        SetStatusNotice("ExportedPasswordCsv");
        return true;
    }

    private async Task<bool> PrepareNoteCsvExportAsync()
    {
        if (!await AuthorizeSensitiveExportAsync())
        {
            return false;
        }

        ExportNoteCsvPreview = await BuildNoteCsvExportAsync();
        SetStatusNotice("ExportedNoteCsv");
        return true;
    }

    private async Task SaveExportTextAsync(
        string title,
        string suggestedFileName,
        string content,
        IReadOnlyList<PlatformFilePickerFileType> fileTypes)
    {
        if (!await AuthorizeFileExportAsync())
        {
            return;
        }

        try
        {
            var fileName = await _fileSystemPickerService.SaveTextFileAsync(title, suggestedFileName, content, fileTypes);
            if (fileName is not null)
            {
                SetStatusNotice("SavedExportFileFormat", fileName);
            }
        }
        catch (Exception ex)
        {
            ReportImportExportFailure("Saving text export failed", "SaveExportFileFailed", ex);
        }
    }
}
