using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static readonly PlatformFilePickerFileType[] TotpCsvFileTypes =
    [
        new("TOTP CSV", ["*.csv"])
    ];

    private static readonly PlatformFilePickerFileType[] AegisJsonFileTypes =
    [
        new("Aegis JSON", ["*.json"])
    ];

    [ObservableProperty]
    private string _importAegisJsonText = "";

    [ObservableProperty]
    private string _importTotpCsvText = "";

    [ObservableProperty]
    private string _exportTotpCsvPreview = "";

    [ObservableProperty]
    private string _exportAegisPreview = "";

    [RelayCommand]
    private async Task ExportTotpCsvAsync()
    {
        ExportTotpCsvPreview = await BuildTotpCsvExportAsync();
        StatusMessage = _localization.Get("ExportedTotpCsv");
    }

    [RelayCommand]
    private async Task ExportAegisJsonAsync()
    {
        ExportAegisPreview = await BuildAegisJsonExportAsync();
        StatusMessage = _localization.Get("ExportedAegisJson");
    }

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task ImportTotpCsvFileAsync()
    {
        try
        {
            var file = await _fileSystemPickerService.OpenTextFileAsync(_localization.Get("ImportTotpCsv"), TotpCsvFileTypes);
            if (file is null)
            {
                return;
            }

            ImportTotpCsvText = file.Content;
            await ImportTotpCsvAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ImportFailedFormat", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task ImportAegisJsonFileAsync()
    {
        try
        {
            var file = await _fileSystemPickerService.OpenTextFileAsync(_localization.Get("ImportAegisJson"), AegisJsonFileTypes);
            if (file is null)
            {
                return;
            }

            ImportAegisJsonText = file.Content;
            await ImportAegisJsonAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ImportFailedFormat", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task SaveTotpCsvExportAsync()
    {
        if (string.IsNullOrWhiteSpace(ExportTotpCsvPreview))
        {
            await ExportTotpCsvAsync();
        }

        await SaveExportTextAsync(
            _localization.Get("ExportTotpCsv"),
            $"monica_totp_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.csv",
            ExportTotpCsvPreview,
            TotpCsvFileTypes);
    }

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task SaveAegisJsonExportAsync()
    {
        if (string.IsNullOrWhiteSpace(ExportAegisPreview))
        {
            await ExportAegisJsonAsync();
        }

        await SaveExportTextAsync(
            _localization.Get("ExportAegisJson"),
            $"monica_totp_aegis_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.json",
            ExportAegisPreview,
            AegisJsonFileTypes);
    }

    private async Task<string> BuildTotpCsvExportAsync()
    {
        var exportTotps = BuildStoredAndVirtualTotpItems(
                await _repository.GetPasswordsAsync(),
                await _repository.GetSecureItemsAsync(VaultItemType.Totp))
            .Select(item => CloneSecureItemForExport(item))
            .ToArray();

        return _importExportService.ExportTotpCsv(exportTotps);
    }

    private async Task<string> BuildAegisJsonExportAsync()
    {
        var exportTotps = BuildStoredAndVirtualTotpItems(
                await _repository.GetPasswordsAsync(),
                await _repository.GetSecureItemsAsync(VaultItemType.Totp))
            .Select(item => CloneSecureItemForExport(item))
            .ToArray();

        return _importExportService.ExportAegisJson(exportTotps);
    }

    private static IReadOnlyList<SecureItem> BuildStoredAndVirtualTotpItems(
        IEnumerable<PasswordEntry> passwords,
        IEnumerable<SecureItem> secureItems)
    {
        var activePasswords = passwords.ToArray();
        var activePasswordIds = activePasswords.Select(item => item.Id).ToHashSet();
        var seenVirtualPasswordIds = new HashSet<long>();
        var result = new List<SecureItem>();

        foreach (var item in secureItems.Where(item => item.ItemType == VaultItemType.Totp))
        {
            if (item.BoundPasswordId is { } boundPasswordId && !activePasswordIds.Contains(boundPasswordId))
            {
                continue;
            }

            result.Add(item);
            if (item.BoundPasswordId is { } passwordId)
            {
                seenVirtualPasswordIds.Add(passwordId);
            }
        }

        foreach (var password in activePasswords.Where(item => item.HasAuthenticator && !seenVirtualPasswordIds.Contains(item.Id)))
        {
            result.Add(BuildVirtualTotpItem(password));
        }

        return result;
    }

    [RelayCommand]
    private async Task ImportAegisJsonAsync()
    {
        if (string.IsNullOrWhiteSpace(ImportAegisJsonText))
        {
            StatusMessage = _localization.Get("ImportAegisJsonRequired");
            return;
        }

        try
        {
            var entries = _importExportService.ImportAegisJson(ImportAegisJsonText);
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

            await _repository.LogAsync(new OperationLog
            {
                ItemType = "TOTP",
                ItemTitle = _localization.Get("AegisJson"),
                OperationType = "IMPORT",
                DeviceName = Environment.MachineName
            });

            ImportAegisJsonText = "";
            await LoadAsync();
            StatusMessage = _localization.Format("ImportedAegisJsonFormat", importedTotps, skippedTotps);
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ImportFailedFormat", ex.Message);
        }
    }

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

            await _repository.LogAsync(new OperationLog
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
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ImportFailedFormat", ex.Message);
        }
    }
}
