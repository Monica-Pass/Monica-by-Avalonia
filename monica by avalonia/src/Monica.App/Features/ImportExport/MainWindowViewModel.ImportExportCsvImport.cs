using Monica.Core.ImportExport;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task ImportPasswordCsvTextAsync(string csv, bool clearEditorOnSuccess)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            StatusMessage = _localization.Get("ImportCsvRequired");
            return;
        }

        try
        {
            var entries = await Task.Run(() => _importExportService.ImportPasswordCsv(csv));
            var importedEntries = entries.Select(item => ClonePasswordForImport(item)).ToArray();
            var importedPasswords = 0;
            foreach (var imported in importedEntries)
            {
                await _repository.SavePasswordAsync(imported);
                importedPasswords++;
            }

            await LogOperationAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemTitle = _localization.Get("PasswordCsv"),
                OperationType = "IMPORT",
                DeviceName = Environment.MachineName
            });
            if (clearEditorOnSuccess)
            {
                ImportCsvText = "";
            }

            await LoadAsync();
            StatusMessage = _localization.Format("ImportedPasswordCsvFormat", importedPasswords);
        }
        catch (CsvImportException error)
        {
            StatusMessage = _localization.Get(error.Error switch
            {
                CsvImportError.ResourceLimitExceeded => "ImportResourceLimitExceeded",
                _ => "ImportCsvInvalidFormat"
            });
        }
        catch (Exception ex)
        {
            ReportImportExportFailure("Importing password CSV failed", "ImportUnexpectedFailure", ex);
        }
    }

    private async Task ImportNoteCsvTextAsync(string csv, bool clearEditorOnSuccess)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            StatusMessage = _localization.Get("ImportNoteCsvRequired");
            return;
        }

        try
        {
            var entries = await Task.Run(() => _importExportService.ImportNoteCsv(csv));
            var existingTitles = (await _repository.GetSecureItemsAsync(VaultItemType.Note))
                .Select(item => item.Title)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var importedNotes = 0;
            var skippedNotes = 0;
            foreach (var source in entries)
            {
                if (!existingTitles.Add(source.Title))
                {
                    skippedNotes++;
                    continue;
                }

                await _repository.SaveSecureItemAsync(source);
                importedNotes++;
            }

            await LogOperationAsync(new OperationLog
            {
                ItemType = "NOTE",
                ItemTitle = _localization.Get("NoteCsv"),
                OperationType = "IMPORT",
                DeviceName = Environment.MachineName
            });
            if (clearEditorOnSuccess)
            {
                ImportNoteCsvText = "";
            }

            await LoadAsync();
            StatusMessage = _localization.Format("ImportedNoteCsvFormat", importedNotes, skippedNotes);
        }
        catch (CsvImportException error)
        {
            StatusMessage = _localization.Get(error.Error switch
            {
                CsvImportError.ResourceLimitExceeded => "ImportResourceLimitExceeded",
                _ => "ImportCsvInvalidFormat"
            });
        }
        catch (Exception ex)
        {
            ReportImportExportFailure("Importing note CSV failed", "ImportUnexpectedFailure", ex);
        }
    }
}
