using CommunityToolkit.Mvvm.Input;
using Monica.Core.ImportExport;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private Task ImportMonicaJsonFileAsync() =>
        RunImportExportOperationAsync(async () =>
        {
            try
            {
                var file = await _fileSystemPickerService.OpenTextFileAsync(
                    _localization.Get("ImportMonicaJson"),
                    MonicaJsonFileTypes);
                if (file is not null)
                {
                    await ImportMonicaJsonTextAsync(file.Content, clearEditorOnSuccess: false);
                }
            }
            catch (Exception ex)
            {
                ReportImportExportFailure("Opening Monica JSON import failed", "ImportFileSelectionFailed", ex);
            }
        });

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private Task ImportMonicaZipFileAsync() =>
        RunImportExportOperationAsync(async () =>
        {
            try
            {
                var file = await _fileSystemPickerService.OpenBinaryFileAsync(
                    _localization.Get("ImportMonicaJson"), MonicaZipFileTypes);
                if (file is null)
                    return;

                var package = await Task.Run(() => MonicaZipImportParser.Import(file.Content));
                var result = await GetMonicaJsonImportUseCase().ExecutePackageAsync(package);
                await LogOperationAsync(new OperationLog
                {
                    ItemType = "VAULT",
                    ItemTitle = _localization.Get("ImportMonicaJson"),
                    OperationType = "IMPORT",
                    ChangesJson = System.Text.Json.JsonSerializer.Serialize(new { result.Passwords, result.SecureItems, result.Categories }),
                    DeviceName = Environment.MachineName
                });
                await LoadAsync();
                ReportMonicaJsonImportResult(result);
            }
            catch (MonicaJsonImportException error)
            {
                SetStatusFailure(error.Error == MonicaJsonImportError.ResourceLimitExceeded
                    ? "ImportResourceLimitExceeded"
                    : "ImportInvalidFormat");
            }
            catch (PasswordSecretUnavailableException error)
            {
                SetStatusFailure(PasswordSecretUnavailableKey(error));
            }
            catch (Exception ex)
            {
                ReportImportExportFailure("Importing Monica ZIP failed", "ImportUnexpectedFailure", ex);
            }
        });

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private Task ImportPasswordCsvFileAsync() =>
        RunImportExportOperationAsync(async () =>
        {
            try
            {
                var file = await _fileSystemPickerService.OpenTextFileAsync(
                    _localization.Get("ImportPasswordCsv"),
                    PasswordCsvFileTypes);
                if (file is not null)
                {
                    await ImportPasswordCsvTextAsync(file.Content, clearEditorOnSuccess: false);
                }
            }
            catch (Exception ex)
            {
                ReportImportExportFailure("Opening password CSV import failed", "ImportFileSelectionFailed", ex);
            }
        });

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private Task ImportNoteCsvFileAsync() =>
        RunImportExportOperationAsync(async () =>
        {
            try
            {
                var file = await _fileSystemPickerService.OpenTextFileAsync(
                    _localization.Get("ImportNoteCsv"),
                    NoteCsvFileTypes);
                if (file is not null)
                {
                    await ImportNoteCsvTextAsync(file.Content, clearEditorOnSuccess: false);
                }
            }
            catch (Exception ex)
            {
                ReportImportExportFailure("Opening note CSV import failed", "ImportFileSelectionFailed", ex);
            }
        });

    [RelayCommand]
    private Task ImportDataAsync() =>
        RunImportExportOperationAsync(() => ImportMonicaJsonTextAsync(ImportJsonText, clearEditorOnSuccess: true));

    [RelayCommand]
    private Task ImportPasswordCsvAsync() =>
        RunImportExportOperationAsync(() => ImportPasswordCsvTextAsync(ImportCsvText, clearEditorOnSuccess: true));

    [RelayCommand]
    private Task ImportNoteCsvAsync() =>
        RunImportExportOperationAsync(() => ImportNoteCsvTextAsync(ImportNoteCsvText, clearEditorOnSuccess: true));
}
