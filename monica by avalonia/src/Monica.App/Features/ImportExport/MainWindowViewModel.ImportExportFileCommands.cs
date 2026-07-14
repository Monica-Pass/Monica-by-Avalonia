using CommunityToolkit.Mvvm.Input;

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
                StatusMessage = _localization.Format("ImportFailedFormat", ex.Message);
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
                StatusMessage = _localization.Format("ImportFailedFormat", ex.Message);
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
                StatusMessage = _localization.Format("ImportFailedFormat", ex.Message);
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
