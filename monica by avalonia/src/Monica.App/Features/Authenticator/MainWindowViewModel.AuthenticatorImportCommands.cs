using CommunityToolkit.Mvvm.Input;
using Monica.Core.ImportExport;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task ImportAegisJsonAsync()
    {
        if (string.IsNullOrWhiteSpace(ImportAegisJsonText))
        {
            AegisImportPassword = "";
            IsAegisImportPasswordRequired = false;
            StatusMessage = _localization.Get("ImportAegisJsonRequired");
            return;
        }

        var encrypted = _importExportService.IsEncryptedAegisJson(ImportAegisJsonText);
        IsAegisImportPasswordRequired = encrypted;
        if (encrypted && string.IsNullOrWhiteSpace(AegisImportPassword))
        {
            StatusMessage = _localization.AegisImportPasswordRequired;
            return;
        }

        var json = ImportAegisJsonText;
        var password = encrypted ? AegisImportPassword : null;
        try
        {
            // Aegis scrypt is intentionally CPU- and memory-hard, so it must not run on the interaction thread.
            var entries = await Task.Run(() => _importExportService.ImportAegisJson(json, password));
            var existingTitles = (await _repository.GetSecureItemsAsync(VaultItemType.Totp))
                .Select(item => item.Title)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var importedTotps = 0;
            var skippedTotps = 0;

            foreach (var source in entries)
            {
                if (!existingTitles.Add(source.Title))
                {
                    skippedTotps++;
                    continue;
                }

                await _repository.SaveSecureItemAsync(source);
                importedTotps++;
            }

            await LogOperationAsync(new OperationLog
            {
                ItemType = "TOTP",
                ItemTitle = _localization.Get("AegisJson"),
                OperationType = "IMPORT",
                DeviceName = Environment.MachineName
            });

            ImportAegisJsonText = "";
            IsAegisImportPasswordRequired = false;
            await LoadAsync();
            StatusMessage = _localization.Format("ImportedAegisJsonFormat", importedTotps, skippedTotps);
        }
        catch (AegisImportException ex)
        {
            IsAegisImportPasswordRequired = ex.Reason == AegisImportFailureReason.PasswordRequired || encrypted;
            StatusMessage = LocalizeAegisImportFailure(ex.Reason);
        }
        catch (Exception ex)
        {
            ReportImportExportFailure("Importing Aegis JSON failed", "ImportUnexpectedFailure", ex);
        }
        finally
        {
            AegisImportPassword = "";
        }
    }

    private string LocalizeAegisImportFailure(AegisImportFailureReason reason) => reason switch
    {
        AegisImportFailureReason.PasswordRequired => _localization.AegisImportPasswordRequired,
        AegisImportFailureReason.DecryptionFailed => _localization.AegisImportDecryptionFailed,
        AegisImportFailureReason.UnsupportedKeySlot => _localization.AegisImportUnsupportedKeySlot,
        AegisImportFailureReason.UnsafeKeyDerivationParameters => _localization.AegisImportUnsafeParameters,
        _ => _localization.AegisImportInvalidFormat
    };

    [RelayCommand]
    private async Task ImportTotpCsvAsync()
    {
        if (string.IsNullOrWhiteSpace(ImportTotpCsvText))
        {
            StatusMessage = _localization.Get("ImportTotpCsvRequired");
            return;
        }

        try
        {
            var entries = _importExportService.ImportTotpCsv(ImportTotpCsvText);
            var existingTitles = (await _repository.GetSecureItemsAsync(VaultItemType.Totp))
                .Select(item => item.Title)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var importedTotps = 0;
            var skippedTotps = 0;

            foreach (var source in entries)
            {
                if (!existingTitles.Add(source.Title))
                {
                    skippedTotps++;
                    continue;
                }

                await _repository.SaveSecureItemAsync(source);
                importedTotps++;
            }

            await LogOperationAsync(new OperationLog
            {
                ItemType = "TOTP",
                ItemTitle = _localization.Get("TotpCsv"),
                OperationType = "IMPORT",
                DeviceName = Environment.MachineName
            });

            ImportTotpCsvText = "";
            await LoadAsync();
            StatusMessage = _localization.Format("ImportedTotpCsvFormat", importedTotps, skippedTotps);
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
            ReportImportExportFailure("Importing TOTP CSV failed", "ImportUnexpectedFailure", ex);
        }
    }
}
