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
        catch (PasswordSecretUnavailableException ex)
        {
            ExportPreview = "";
            StatusMessage = GetPasswordSecretUnavailableMessage(ex);
            return false;
        }

        StatusMessage = _localization.Get("ExportPrepared");
        return true;
    }

    private async Task<bool> PreparePasswordCsvExportAsync()
    {
        if (!await AuthorizeSensitiveExportAsync())
        {
            return false;
        }

        PasswordEntry[] exportPasswords;
        try
        {
            exportPasswords = (await _repository.GetPasswordsAsync())
                .Select(item => ClonePasswordForExport(item))
                .ToArray();
        }
        catch (PasswordSecretUnavailableException ex)
        {
            ExportCsvPreview = "";
            StatusMessage = GetPasswordSecretUnavailableMessage(ex);
            return false;
        }

        ExportCsvPreview = await Task.Run(() => _importExportService.ExportPasswordCsv(exportPasswords));
        StatusMessage = _localization.Get("ExportedPasswordCsv");
        return true;
    }

    private async Task<bool> PrepareNoteCsvExportAsync()
    {
        if (!await AuthorizeSensitiveExportAsync())
        {
            return false;
        }

        ExportNoteCsvPreview = await BuildNoteCsvExportAsync();
        StatusMessage = _localization.Get("ExportedNoteCsv");
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
                StatusMessage = _localization.Format("SavedExportFileFormat", fileName);
            }
        }
        catch (Exception ex)
        {
            ReportImportExportFailure("Saving text export failed", "SaveExportFileFailed", ex);
        }
    }
}
