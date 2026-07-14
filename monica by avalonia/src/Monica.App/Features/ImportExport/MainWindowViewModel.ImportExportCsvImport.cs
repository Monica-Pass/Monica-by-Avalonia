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
            var importedPasswords = 0;
            foreach (var source in entries)
            {
                await _repository.SavePasswordAsync(ClonePasswordForImport(source));
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
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ImportFailedFormat", ex.Message);
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
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ImportFailedFormat", ex.Message);
        }
    }
}
