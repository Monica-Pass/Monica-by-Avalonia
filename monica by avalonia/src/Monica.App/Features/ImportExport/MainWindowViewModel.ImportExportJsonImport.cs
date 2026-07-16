using System.Text.Json;
using Monica.Core.ImportExport;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task ImportMonicaJsonTextAsync(string json, bool clearEditorOnSuccess)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            StatusMessage = _localization.Get("ImportJsonRequired");
            return;
        }

        try
        {
            var result = await ImportMonicaJsonAsync(json);
            if (clearEditorOnSuccess)
            {
                ImportJsonText = "";
            }

            StatusMessage = FormatMonicaJsonImportStatus(result);
        }
        catch (PasswordSecretUnavailableException error)
        {
            StatusMessage = GetPasswordSecretUnavailableMessage(error);
        }
        catch (MonicaJsonImportException error)
        {
            StatusMessage = _localization.Get(error.Error switch
            {
                MonicaJsonImportError.ResourceLimitExceeded => "ImportResourceLimitExceeded",
                _ => "ImportInvalidFormat"
            });
        }
        catch (Exception ex)
        {
            ReportImportExportFailure("Importing Monica JSON failed", "ImportUnexpectedFailure", ex);
        }
    }

    private async Task<MonicaJsonImportResult> ImportMonicaJsonAsync(string json)
    {
        var package = await Task.Run(() => _importExportService.ImportJson(json));
        ValidatePasswordSecretsForImport(package);
        var categoryIdMap = new Dictionary<long, long>();
        var importedCategories = await ImportCategoriesAsync(package.Categories, categoryIdMap);
        var passwordIdMap = new Dictionary<long, long>();
        var importedPasswords = await ImportPasswordsAsync(package, categoryIdMap, passwordIdMap);
        await ImportPasswordMetadataAsync(package, passwordIdMap);
        var importedSecureItems = await ImportSecureItemsAsync(package, passwordIdMap, categoryIdMap);

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

    private void ValidatePasswordSecretsForImport(MonicaExportPackage package)
    {
        foreach (var password in package.Passwords)
        {
            _ = ReadPasswordSecretOrThrow(password.Password);
        }

        foreach (var historyEntry in package.PasswordHistory.SelectMany(group => group.Entries))
        {
            _ = ReadPasswordSecretOrThrow(historyEntry.Password);
        }
    }

    private async Task<int> ImportCategoriesAsync(
        IReadOnlyList<Category> sources,
        IDictionary<long, long> categoryIdMap)
    {
        if (sources.Count == 0)
        {
            return 0;
        }

        var existingCategories = (await _repository.GetCategoriesAsync())
            .ToDictionary(item => item.Name, item => item, StringComparer.OrdinalIgnoreCase);
        var importedCount = 0;
        foreach (var source in sources.OrderBy(item => item.SortOrder).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
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

            importedCount++;
        }

        return importedCount;
    }

    private async Task<int> ImportPasswordsAsync(
        MonicaExportPackage package,
        IReadOnlyDictionary<long, long> categoryIdMap,
        IDictionary<long, long> passwordIdMap)
    {
        var importedCount = 0;
        foreach (var source in package.Passwords)
        {
            var imported = ClonePasswordForImport(source, categoryIdMap);
            await _repository.SavePasswordAsync(imported);
            if (source.Id != 0)
            {
                passwordIdMap[source.Id] = imported.Id;
            }

            importedCount++;
        }

        return importedCount;
    }

    private async Task ImportPasswordMetadataAsync(
        MonicaExportPackage package,
        IReadOnlyDictionary<long, long> passwordIdMap)
    {
        foreach (var group in package.PasswordCustomFields)
        {
            if (passwordIdMap.TryGetValue(group.PasswordId, out var importedPasswordId))
            {
                await _repository.ReplaceCustomFieldsAsync(
                    importedPasswordId,
                    group.Fields.Select(field => CloneCustomFieldForImport(field, importedPasswordId)).ToArray());
            }
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
                if (TryDecodeAttachmentContent(source.ContentBase64, out var content))
                {
                    await ImportPasswordAttachmentAsync(source.Metadata, importedPasswordId, content);
                }
            }
        }
    }

    private async Task<int> ImportSecureItemsAsync(
        MonicaExportPackage package,
        IReadOnlyDictionary<long, long> passwordIdMap,
        IReadOnlyDictionary<long, long> categoryIdMap)
    {
        var importedCount = 0;
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

            importedCount++;
        }

        return importedCount;
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

    private string FormatMonicaJsonImportStatus(MonicaJsonImportResult result) =>
        result.Categories > 0
            ? _localization.Format("ImportedMonicaJsonWithCategoriesFormat", result.Passwords, result.SecureItems, result.Categories)
            : _localization.Format("ImportedMonicaJsonFormat", result.Passwords, result.SecureItems);
}
