using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using Monica.Core.ImportExport;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task ExportDataAsync()
    {
        if (!await AuthorizeSensitiveExportAsync())
        {
            return;
        }

        ExportPreview = await BuildMonicaJsonExportAsync(
            includePasswords: true,
            includeTotp: true,
            includeNotes: true,
            includeCards: true,
            includeDocuments: true,
            includeImages: true,
            includeCategories: true);
        StatusMessage = _localization.Get("ExportPrepared");
    }

    [RelayCommand]
    private async Task ExportPasswordCsvAsync()
    {
        if (!await AuthorizeSensitiveExportAsync())
        {
            return;
        }

        var exportPasswords = (await _repository.GetPasswordsAsync())
            .Select(item => ClonePasswordForExport(item))
            .ToArray();
        ExportCsvPreview = _importExportService.ExportPasswordCsv(exportPasswords);
        StatusMessage = _localization.Get("ExportedPasswordCsv");
    }

    [RelayCommand]
    private async Task ExportNoteCsvAsync()
    {
        if (!await AuthorizeSensitiveExportAsync())
        {
            return;
        }

        ExportNoteCsvPreview = await BuildNoteCsvExportAsync();
        StatusMessage = _localization.Get("ExportedNoteCsv");
    }

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task ImportMonicaJsonFileAsync()
    {
        try
        {
            var file = await _fileSystemPickerService.OpenTextFileAsync(_localization.Get("ImportMonicaJson"), MonicaJsonFileTypes);
            if (file is null)
            {
                return;
            }

            ImportJsonText = file.Content;
            await ImportDataAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ImportFailedFormat", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task ImportPasswordCsvFileAsync()
    {
        try
        {
            var file = await _fileSystemPickerService.OpenTextFileAsync(_localization.Get("ImportPasswordCsv"), PasswordCsvFileTypes);
            if (file is null)
            {
                return;
            }

            ImportCsvText = file.Content;
            await ImportPasswordCsvAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ImportFailedFormat", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task ImportNoteCsvFileAsync()
    {
        try
        {
            var file = await _fileSystemPickerService.OpenTextFileAsync(_localization.Get("ImportNoteCsv"), NoteCsvFileTypes);
            if (file is null)
            {
                return;
            }

            ImportNoteCsvText = file.Content;
            await ImportNoteCsvAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ImportFailedFormat", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task SaveMonicaJsonExportAsync()
    {
        if (string.IsNullOrWhiteSpace(ExportPreview))
        {
            await ExportDataAsync();
        }

        await SaveExportTextAsync(
            _localization.Get("ExportData"),
            $"monica_export_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.json",
            ExportPreview,
            MonicaJsonFileTypes);
    }

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task SavePasswordCsvExportAsync()
    {
        if (string.IsNullOrWhiteSpace(ExportCsvPreview))
        {
            await ExportPasswordCsvAsync();
        }

        await SaveExportTextAsync(
            _localization.Get("ExportPasswordCsv"),
            $"monica_passwords_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.csv",
            ExportCsvPreview,
            PasswordCsvFileTypes);
    }

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task SaveNoteCsvExportAsync()
    {
        if (string.IsNullOrWhiteSpace(ExportNoteCsvPreview))
        {
            await ExportNoteCsvAsync();
        }

        await SaveExportTextAsync(
            _localization.Get("ExportNoteCsv"),
            $"monica_notes_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.csv",
            ExportNoteCsvPreview,
            NoteCsvFileTypes);
    }

    private async Task SaveExportTextAsync(
        string title,
        string suggestedFileName,
        string content,
        IReadOnlyList<PlatformFilePickerFileType> fileTypes)
    {
        if (!await AuthorizeFileExportAsync())
        {
            return;
        }

        try
        {
            var fileName = await _fileSystemPickerService.SaveTextFileAsync(title, suggestedFileName, content, fileTypes);
            if (fileName is not null)
            {
                StatusMessage = _localization.Format("SavedExportFileFormat", fileName);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("SaveExportFileFailedFormat", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ImportDataAsync()
    {
        if (string.IsNullOrWhiteSpace(ImportJsonText))
        {
            StatusMessage = _localization.Get("ImportJsonRequired");
            return;
        }

        try
        {
            var result = await ImportMonicaJsonAsync(ImportJsonText);
            ImportJsonText = "";
            StatusMessage = FormatMonicaJsonImportStatus(result);
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ImportFailedFormat", ex.Message);
        }
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
                (item.ItemType == VaultItemType.BankCard && includeCards) ||
                (item.ItemType == VaultItemType.Document && includeDocuments)))
            .Where(item => item.Id > 0)
            .Select(item => CloneSecureItemForExport(item, includeCategories, includeImages))
            .ToArray();
        var exportCategories = includeCategories
            ? categories.Select(CloneCategory).ToArray()
            : Array.Empty<Category>();
        var customFieldsByPasswordId = includePasswords
            ? await _repository.GetCustomFieldsByEntryIdsAsync(exportPasswords.Select(item => item.Id).ToArray())
            : new Dictionary<long, IReadOnlyList<CustomField>>();
        var passwordHistoryByPasswordId = includePasswords
            ? await GetPasswordHistoryForExportAsync(exportPasswords.Select(item => item.Id).ToArray())
            : new Dictionary<long, IReadOnlyList<PasswordHistoryEntry>>();
        var passwordAttachmentsByPasswordId = includePasswords
            ? await GetPasswordAttachmentsForExportAsync(exportPasswords.Select(item => item.Id).ToArray())
            : new Dictionary<long, IReadOnlyList<PasswordAttachmentExport>>();
        var secureItemAttachmentsByItemId = includeImages
            ? await GetSecureItemAttachmentsForExportAsync(exportSecureItems)
            : new Dictionary<long, IReadOnlyList<SecureItemAttachmentExport>>();

        return _importExportService.ExportJson(
            exportPasswords,
            exportSecureItems,
            exportCategories,
            customFieldsByPasswordId,
            passwordHistoryByPasswordId,
            passwordAttachmentsByPasswordId,
            secureItemAttachmentsByItemId);
    }

    private async Task<string> BuildNoteCsvExportAsync()
    {
        var exportNotes = (await _repository.GetSecureItemsAsync(VaultItemType.Note))
            .Select(item => CloneSecureItemForExport(item))
            .ToArray();

        return _importExportService.ExportNoteCsv(exportNotes);
    }

    private async Task<IReadOnlyDictionary<long, IReadOnlyList<PasswordHistoryEntry>>> GetPasswordHistoryForExportAsync(IReadOnlyList<long> passwordIds)
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

    private async Task<IReadOnlyDictionary<long, IReadOnlyList<PasswordAttachmentExport>>> GetPasswordAttachmentsForExportAsync(IReadOnlyList<long> passwordIds)
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
                if (content is null)
                {
                    continue;
                }

                exports.Add(new PasswordAttachmentExport(
                    CloneAttachmentForExport(attachment),
                    Convert.ToBase64String(content)));
            }

            if (exports.Count > 0)
            {
                result[group.Key] = exports;
            }
        }

        return result;
    }

    private async Task<IReadOnlyDictionary<long, IReadOnlyList<SecureItemAttachmentExport>>> GetSecureItemAttachmentsForExportAsync(IReadOnlyList<SecureItem> secureItems)
    {
        var result = new Dictionary<long, IReadOnlyList<SecureItemAttachmentExport>>();
        foreach (var item in secureItems.Where(item => item.Id > 0).OrderBy(item => item.Id))
        {
            var exports = new List<SecureItemAttachmentExport>();
            var imagePaths = DecodeSecureItemImagePaths(item);
            for (var index = 0; index < imagePaths.Count; index++)
            {
                var imagePath = imagePaths[index];
                var attachment = CreateSecureItemImageAttachmentForExport(item, imagePath, index);
                var content = await _repository.TryReadAttachmentContentAsync(attachment);
                if (content is null)
                {
                    continue;
                }

                exports.Add(new SecureItemAttachmentExport(
                    CloneAttachmentForExport(attachment),
                    Convert.ToBase64String(content)));
            }

            if (exports.Count > 0)
            {
                result[item.Id] = exports;
            }
        }

        return result;
    }

    private async Task<MonicaJsonImportResult> ImportMonicaJsonAsync(string json)
    {
        var package = _importExportService.ImportJson(json);
        var categoryIdMap = new Dictionary<long, long>();
        var importedCategories = 0;

        if (package.Categories.Count > 0)
        {
            var existingCategories = (await _repository.GetCategoriesAsync())
                .ToDictionary(item => item.Name, item => item, StringComparer.OrdinalIgnoreCase);
            foreach (var source in package.Categories.OrderBy(item => item.SortOrder).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(source.Name))
                {
                    continue;
                }

                var name = source.Name.Trim();
                if (existingCategories.TryGetValue(name, out var existing))
                {
                    if (source.Id != 0)
                    {
                        categoryIdMap[source.Id] = existing.Id;
                    }

                    continue;
                }

                var imported = CloneCategory(source);
                imported.Id = 0;
                imported.Name = name;
                imported.MdbxDatabaseId = null;
                imported.MdbxFolderId = null;
                await _repository.SaveCategoryAsync(imported);
                existingCategories[imported.Name] = imported;
                if (source.Id != 0)
                {
                    categoryIdMap[source.Id] = imported.Id;
                }

                importedCategories++;
            }
        }

        var passwordIdMap = new Dictionary<long, long>();
        var importedPasswords = 0;
        foreach (var source in package.Passwords)
        {
            var imported = ClonePasswordForImport(source, categoryIdMap);
            var sourceId = source.Id;
            await _repository.SavePasswordAsync(imported);
            if (sourceId != 0)
            {
                passwordIdMap[sourceId] = imported.Id;
            }

            importedPasswords++;
        }

        foreach (var group in package.PasswordCustomFields)
        {
            if (!passwordIdMap.TryGetValue(group.PasswordId, out var importedPasswordId))
            {
                continue;
            }

            await _repository.ReplaceCustomFieldsAsync(
                importedPasswordId,
                group.Fields.Select(field => CloneCustomFieldForImport(field, importedPasswordId)).ToArray());
        }

        foreach (var group in package.PasswordHistory)
        {
            if (!passwordIdMap.TryGetValue(group.PasswordId, out var importedPasswordId))
            {
                continue;
            }

            foreach (var source in group.Entries.OrderBy(item => item.LastUsedAt))
            {
                await _repository.SavePasswordHistoryAsync(ClonePasswordHistoryForImport(source, importedPasswordId));
            }
        }

        foreach (var group in package.PasswordAttachments)
        {
            if (!passwordIdMap.TryGetValue(group.PasswordId, out var importedPasswordId))
            {
                continue;
            }

            foreach (var source in group.Attachments)
            {
                if (!TryDecodeAttachmentContent(source.ContentBase64, out var content))
                {
                    continue;
                }

                await ImportPasswordAttachmentAsync(source.Metadata, importedPasswordId, content);
            }
        }

        var importedSecureItems = 0;
        foreach (var source in package.SecureItems)
        {
            var imported = CloneSecureItemForImport(source, passwordIdMap, categoryIdMap);
            await _repository.SaveSecureItemAsync(imported);
            if (source.Id > 0)
            {
                var restoredImagePaths = await ImportSecureItemAttachmentsAsync(
                    imported,
                    package.SecureItemAttachments.FirstOrDefault(group => group.SecureItemId == source.Id)?.Attachments ?? []);
                if (restoredImagePaths.Count > 0)
                {
                    ApplySecureItemImagePaths(imported, restoredImagePaths);
                    await _repository.SaveSecureItemAsync(imported);
                }
            }

            importedSecureItems++;
        }

        await LogOperationAsync(new OperationLog
        {
            ItemType = "VAULT",
            ItemTitle = _localization.Get("MonicaJson"),
            OperationType = "IMPORT",
            ChangesJson = JsonSerializer.Serialize(new { importedPasswords, importedSecureItems, importedCategories }),
            DeviceName = Environment.MachineName
        });

        await LoadAsync();
        return new MonicaJsonImportResult(importedPasswords, importedSecureItems, importedCategories);
    }

    private async Task ImportPasswordAttachmentAsync(Attachment source, long importedPasswordId, byte[] content)
    {
        var attachment = CloneAttachmentForImport(source, importedPasswordId);
        var draft = await _passwordAttachmentFileService.StoreAttachmentAsync(
            attachment.FileName,
            content,
            attachment.ContentType);
        attachment.StoragePath = draft.StoragePath;
        attachment.SizeBytes = draft.SizeBytes;
        if (string.IsNullOrWhiteSpace(attachment.ContentType))
        {
            attachment.ContentType = draft.ContentType;
        }

        var originalStoragePath = attachment.StoragePath;
        await _repository.SaveAttachmentAsync(attachment, content);
        if (!string.Equals(originalStoragePath, attachment.StoragePath, StringComparison.Ordinal) &&
            !originalStoragePath.StartsWith("mdbx:", StringComparison.OrdinalIgnoreCase))
        {
            await _passwordAttachmentFileService.DeleteStoredAttachmentAsync(originalStoragePath);
        }
    }

    private string FormatMonicaJsonImportStatus(MonicaJsonImportResult result)
    {
        return result.Categories > 0
            ? _localization.Format("ImportedMonicaJsonWithCategoriesFormat", result.Passwords, result.SecureItems, result.Categories)
            : _localization.Format("ImportedMonicaJsonFormat", result.Passwords, result.SecureItems);
    }


    [RelayCommand]
    private async Task ImportPasswordCsvAsync()
    {
        if (string.IsNullOrWhiteSpace(ImportCsvText))
        {
            StatusMessage = _localization.Get("ImportCsvRequired");
            return;
        }

        try
        {
            var entries = _importExportService.ImportPasswordCsv(ImportCsvText);
            var importedPasswords = 0;
            foreach (var source in entries)
            {
                var imported = ClonePasswordForImport(source);
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

            ImportCsvText = "";
            await LoadAsync();
            StatusMessage = _localization.Format("ImportedPasswordCsvFormat", importedPasswords);
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ImportFailedFormat", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ImportNoteCsvAsync()
    {
        if (string.IsNullOrWhiteSpace(ImportNoteCsvText))
        {
            StatusMessage = _localization.Get("ImportNoteCsvRequired");
            return;
        }

        try
        {
            var entries = _importExportService.ImportNoteCsv(ImportNoteCsvText);
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

            ImportNoteCsvText = "";
            await LoadAsync();
            StatusMessage = _localization.Format("ImportedNoteCsvFormat", importedNotes, skippedNotes);
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ImportFailedFormat", ex.Message);
        }
    }

}
