using Monica.Core.ImportExport;
using Monica.Core.Models;
using Monica.Data.Repositories;

namespace Monica.App.Services.VaultOperations;

public sealed class MonicaJsonExportUseCase
{
    private readonly IMonicaRepository _repository;
    private readonly IImportExportService _importExportService;

    public MonicaJsonExportUseCase(
        IMonicaRepository repository,
        IImportExportService importExportService)
    {
        _repository = repository;
        _importExportService = importExportService;
    }

    public async Task<string> ExecuteAsync(
        PasswordEntry[] exportPasswords,
        SecureItem[] exportSecureItems,
        Category[] exportCategories,
        IReadOnlyDictionary<long, IReadOnlyList<CustomField>>? precomputedCustomFields = null,
        IReadOnlyDictionary<long, IReadOnlyList<PasswordHistoryEntry>>? precomputedPasswordHistory = null,
        CancellationToken cancellationToken = default)
    {
        var passwordIds = exportPasswords.Select(item => item.Id).ToArray();

        var customFieldsByPasswordId = precomputedCustomFields
            ?? (exportPasswords.Length > 0
                ? await _repository.GetCustomFieldsByEntryIdsAsync(passwordIds)
                : new Dictionary<long, IReadOnlyList<CustomField>>());

        var passwordHistoryByPasswordId = precomputedPasswordHistory
            ?? (exportPasswords.Length > 0
                ? await GetPasswordHistoryForExportAsync(passwordIds)
                : new Dictionary<long, IReadOnlyList<PasswordHistoryEntry>>());

        var passwordAttachmentsByPasswordId = exportPasswords.Length > 0
            ? await GetPasswordAttachmentsForExportAsync(passwordIds)
            : new Dictionary<long, IReadOnlyList<PasswordAttachmentExport>>();

        var secureItemAttachmentsByItemId = await GetSecureItemAttachmentsForExportAsync(exportSecureItems);

        return await Task.Run(() => _importExportService.ExportJson(
            exportPasswords,
            exportSecureItems,
            exportCategories,
            customFieldsByPasswordId,
            passwordHistoryByPasswordId,
            passwordAttachmentsByPasswordId,
            secureItemAttachmentsByItemId), cancellationToken);
    }

    private async Task<IReadOnlyDictionary<long, IReadOnlyList<PasswordHistoryEntry>>> GetPasswordHistoryForExportAsync(
        IReadOnlyList<long> passwordIds)
    {
        var result = new Dictionary<long, IReadOnlyList<PasswordHistoryEntry>>();
        foreach (var passwordId in passwordIds.Where(id => id > 0).Distinct())
        {
            var history = (await _repository.GetPasswordHistoryAsync(passwordId)).ToArray();
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
                        ImportExportHelpers.CloneAttachmentForExport(attachment),
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
            var imagePaths = ImportExportHelpers.DecodeSecureItemImagePaths(item);
            for (var index = 0; index < imagePaths.Count; index++)
            {
                var attachment = ImportExportHelpers.CreateSecureItemImageAttachmentForExport(item, imagePaths[index], index);
                var content = await _repository.TryReadAttachmentContentAsync(attachment);
                if (content is not null)
                {
                    exports.Add(new SecureItemAttachmentExport(
                        ImportExportHelpers.CloneAttachmentForExport(attachment),
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
