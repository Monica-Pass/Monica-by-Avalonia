using Monica.App.Services.VaultOperations;
using Monica.Core.ImportExport;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private MonicaJsonExportUseCase? _monicaJsonExportUseCase;

    private MonicaJsonExportUseCase GetMonicaJsonExportUseCase()
    {
        return _monicaJsonExportUseCase ??= new MonicaJsonExportUseCase(_repository, _importExportService);
    }

    private async Task<string> BuildMonicaJsonExportAsync(
        bool includePasswords,
        bool includeTotp,
        bool includeNotes,
        bool includeCards,
        bool includeDocuments,
        bool includeImages,
        bool includeCategories)
    {
        var passwords = await _repository.GetPasswordsAsync();
        var secureItems = await _repository.GetSecureItemsAsync();
        var categories = includeCategories
            ? await _repository.GetCategoriesAsync()
            : Array.Empty<Category>();
        var totpItems = BuildStoredAndVirtualTotpItems(passwords, secureItems);
        var exportPasswords = includePasswords
            ? passwords.Select(item => ClonePasswordForExport(item, includeCategories)).ToArray()
            : Array.Empty<PasswordEntry>();
        var exportSecureItems = totpItems
            .Where(_ => includeTotp)
            .Concat(secureItems.Where(item => includeNotes && item.ItemType == VaultItemType.Note))
            .Concat(secureItems.Where(item =>
                (item.ItemType is VaultItemType.BankCard or VaultItemType.BillingAddress or VaultItemType.PaymentAccount && includeCards) ||
                (item.ItemType == VaultItemType.Document && includeDocuments)))
            .Where(item => item.Id > 0)
            .Select(item => ImportExportHelpers.CloneSecureItemForExport(item, includeCategories, includeImages))
            .ToArray();
        var exportCategories = includeCategories
            ? categories.Select(ImportExportHelpers.CloneCategory).ToArray()
            : Array.Empty<Category>();

        return await GetMonicaJsonExportUseCase().ExecuteAsync(
            exportPasswords,
            exportSecureItems,
            exportCategories);
    }

    private async Task<string> BuildNoteCsvExportAsync()
    {
        var exportNotes = (await _repository.GetSecureItemsAsync(VaultItemType.Note))
            .Select(item => ImportExportHelpers.CloneSecureItemForExport(item))
            .ToArray();
        return await Task.Run(() => _importExportService.ExportNoteCsv(exportNotes));
    }
}
