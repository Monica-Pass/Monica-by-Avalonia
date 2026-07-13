using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static readonly PlatformFilePickerFileType[] WalletCsvFileTypes =
    [
        new("Cards and Documents CSV", ["*.csv"])
    ];

    [ObservableProperty]
    private string _exportWalletCsvPreview = "";

    [RelayCommand]
    private async Task ExportWalletCsvAsync()
    {
        if (!await AuthorizeSensitiveExportAsync())
        {
            return;
        }

        ExportWalletCsvPreview = await BuildWalletCsvExportAsync();
        StatusMessage = _localization.Get("ExportedWalletCsv");
    }

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task SaveWalletCsvExportAsync()
    {
        if (string.IsNullOrWhiteSpace(ExportWalletCsvPreview))
        {
            await ExportWalletCsvAsync();
        }

        await SaveExportTextAsync(
            _localization.Get("ExportWalletCsv"),
            $"monica_cards_documents_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.csv",
            ExportWalletCsvPreview,
            WalletCsvFileTypes);
    }

    private async Task<string> BuildWalletCsvExportAsync()
    {
        var exportWalletItems = (await _repository.GetSecureItemsAsync())
            .Where(item => item.ItemType is VaultItemType.BankCard or VaultItemType.Document)
            .Select(item => CloneSecureItemForExport(item))
            .ToArray();

        return _importExportService.ExportWalletCsv(exportWalletItems);
    }
}
