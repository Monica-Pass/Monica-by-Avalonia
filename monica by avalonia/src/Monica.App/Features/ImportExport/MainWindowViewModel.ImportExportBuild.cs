using Monica.Core.ImportExport;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
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
            .Select(item => CloneSecureItemForExport(item, includeCategories, includeImages))
            .ToArray();
        var exportCategories = includeCategories
            ? categories.Select(CloneCategory).ToArray()
            : Array.Empty<Category>();
        var passwordIds = exportPasswords.Select(item => item.Id).ToArray();
        var customFieldsByPasswordId = includePasswords
            ? await _repository.GetCustomFieldsByEntryIdsAsync(passwordIds)
            : new Dictionary<long, IReadOnlyList<CustomField>>();
        var passwordHistoryByPasswordId = includePasswords
            ? await GetPasswordHistoryForExportAsync(passwordIds)
            : new Dictionary<long, IReadOnlyList<PasswordHistoryEntry>>();
        var passwordAttachmentsByPasswordId = includePasswords
            ? await GetPasswordAttachmentsForExportAsync(passwordIds)
            : new Dictionary<long, IReadOnlyList<PasswordAttachmentExport>>();
        var secureItemAttachmentsByItemId = includeImages
            ? await GetSecureItemAttachmentsForExportAsync(exportSecureItems)
            : new Dictionary<long, IReadOnlyList<SecureItemAttachmentExport>>();

        return await Task.Run(() => _importExportService.ExportJson(
            exportPasswords,
            exportSecureItems,
            exportCategories,
            customFieldsByPasswordId,
            passwordHistoryByPasswordId,
            passwordAttachmentsByPasswordId,
            secureItemAttachmentsByItemId));
    }

    private async Task<string> BuildNoteCsvExportAsync()
    {
        var exportNotes = (await _repository.GetSecureItemsAsync(VaultItemType.Note))
            .Select(item => CloneSecureItemForExport(item))
            .ToArray();
        return await Task.Run(() => _importExportService.ExportNoteCsv(exportNotes));
    }

    private async Task<IReadOnlyDictionary<long, IReadOnlyList<PasswordHistoryEntry>>> GetPasswordHistoryForExportAsync(
        IReadOnlyList<long> passwordIds)
    {
        var result = new Dictionary<long, IReadOnlyList<PasswordHistoryEntry>>();
        foreach (var passwordId in passwordIds.Where(id => id > 0).Distinct())
        {
            var history = (await _repository.GetPasswordHistoryAsync(passwordId))
                .Select(ClonePasswordHistoryForExport)
                .ToArray();
            if (history.Length > 0)
            {
                result[passwordId] = history;
            }
        }

        return result;
    }

    private async Task<IReadOnlyDictionary<long, IReadOnlyList<PasswordAttachmentExport>>> GetPasswordAttachmentsForExportAsync(
        IReadOnlyList<long> passwordIds)
    {
        var ids = passwordIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<long, IReadOnlyList<PasswordAttachmentExport>>();
        }

        var result = new Dictionary<long, IReadOnlyList<PasswordAttachmentExport>>();
        var attachmentsByPasswordId = await _repository.GetAttachmentsByOwnerIdsAsync("PASSWORD", ids);
        foreach (var group in attachmentsByPasswordId.OrderBy(item => item.Key))
        {
            var exports = new List<PasswordAttachmentExport>();
            foreach (var attachment in group.Value)
            {
                var content = await _repository.TryReadAttachmentContentAsync(attachment);
                if (content is not null)
                {
                    exports.Add(new PasswordAttachmentExport(
                        CloneAttachmentForExport(attachment),
                        Convert.ToBase64String(content)));
                }
            }

            if (exports.Count > 0)
            {
                result[group.Key] = exports;
            }
        }

        return result;
    }

    private async Task<IReadOnlyDictionary<long, IReadOnlyList<SecureItemAttachmentExport>>> GetSecureItemAttachmentsForExportAsync(
        IReadOnlyList<SecureItem> secureItems)
    {
        var result = new Dictionary<long, IReadOnlyList<SecureItemAttachmentExport>>();
        foreach (var item in secureItems.Where(item => item.Id > 0).OrderBy(item => item.Id))
        {
            var exports = new List<SecureItemAttachmentExport>();
            var imagePaths = DecodeSecureItemImagePaths(item);
            for (var index = 0; index < imagePaths.Count; index++)
            {
                var attachment = CreateSecureItemImageAttachmentForExport(item, imagePaths[index], index);
                var content = await _repository.TryReadAttachmentContentAsync(attachment);
                if (content is not null)
                {
                    exports.Add(new SecureItemAttachmentExport(
                        CloneAttachmentForExport(attachment),
                        Convert.ToBase64String(content)));
                }
            }

            if (exports.Count > 0)
            {
                result[item.Id] = exports;
            }
        }

        return result;
    }
}
