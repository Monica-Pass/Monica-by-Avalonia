using Monica.App.ViewModels;
using Monica.Core.ImportExport;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data.Repositories;

namespace Monica.App.Services.VaultOperations;

public sealed class MonicaJsonImportUseCase
{
    private readonly IMonicaRepository _repository;
    private readonly IImportExportService _importExportService;
    private readonly IPasswordAttachmentFileService _attachmentFileService;
    private readonly ICryptoService _cryptoService;

    public MonicaJsonImportUseCase(
        IMonicaRepository repository,
        IImportExportService importExportService,
        IPasswordAttachmentFileService attachmentFileService,
        ICryptoService cryptoService)
    {
        _repository = repository;
        _importExportService = importExportService;
        _attachmentFileService = attachmentFileService;
        _cryptoService = cryptoService;
    }

    public async Task<MonicaJsonImportResult> ExecuteAsync(string json, CancellationToken cancellationToken = default)
    {
        var package = await Task.Run(() => _importExportService.ImportJson(json), cancellationToken);
        return await ExecutePackageAsync(package, cancellationToken);
    }

    public async Task<MonicaJsonImportResult> ExecutePackageAsync(
        MonicaExportPackage package,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePasswordSecrets(package);

        var categoryIdMap = new Dictionary<long, long>();
        var importedCategories = await ImportCategoriesAsync(package.Categories, categoryIdMap);

        var passwordIdMap = new Dictionary<long, long>();
        var importedPasswords = await ImportPasswordsAsync(package, categoryIdMap, passwordIdMap);
        await ImportPasswordMetadataAsync(package, passwordIdMap);

        var importedSecureItems = await ImportSecureItemsAsync(package, passwordIdMap, categoryIdMap);

        return new MonicaJsonImportResult(importedPasswords, importedSecureItems, importedCategories);
    }

    private void ValidatePasswordSecrets(MonicaExportPackage package)
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

            var imported = ImportExportHelpers.CloneCategory(source);
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
                    group.Fields.Select(field => ImportExportHelpers.CloneCustomFieldForImport(field, importedPasswordId)).ToArray());
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
                if (ImportExportHelpers.TryDecodeAttachmentContent(source.ContentBase64, out var content))
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
            var imported = ImportExportHelpers.CloneSecureItemForImport(source, passwordIdMap, categoryIdMap);
            await _repository.SaveSecureItemAsync(imported);
            if (source.Id > 0)
            {
                var attachments = package.SecureItemAttachments
                    .FirstOrDefault(group => group.SecureItemId == source.Id)?.Attachments ?? [];
                var restoredImagePaths = await ImportSecureItemAttachmentsAsync(imported, attachments);
                if (restoredImagePaths.Count > 0)
                {
                    ImportExportHelpers.ApplySecureItemImagePaths(imported, restoredImagePaths);
                    await _repository.SaveSecureItemAsync(imported);
                }
            }

            importedCount++;
        }

        return importedCount;
    }

    private async Task ImportPasswordAttachmentAsync(Attachment source, long importedPasswordId, byte[] content)
    {
        var attachment = ImportExportHelpers.CloneAttachmentForImport(source, importedPasswordId);
        var draft = await _attachmentFileService.StoreAttachmentAsync(
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
            await _attachmentFileService.DeleteStoredAttachmentAsync(originalStoragePath);
        }
    }

    private async Task<IReadOnlyList<string>> ImportSecureItemAttachmentsAsync(
        SecureItem item,
        IReadOnlyList<SecureItemAttachmentExport> attachments)
    {
        if (attachments.Count == 0)
        {
            return [];
        }

        var restoredPaths = new List<string>();
        foreach (var source in attachments)
        {
            if (!ImportExportHelpers.TryDecodeAttachmentContent(source.ContentBase64, out var content))
            {
                continue;
            }

            var draft = await _attachmentFileService.StoreAttachmentAsync(
                source.Metadata.FileName,
                content,
                source.Metadata.ContentType);
            restoredPaths.Add(draft.StoragePath);
        }

        return restoredPaths;
    }

    private PasswordEntry ClonePasswordForImport(PasswordEntry source, IReadOnlyDictionary<long, long>? categoryIdMap)
    {
        var clone = source.CreateDetachedCopy();
        clone.Id = 0;
        clone.Password = ProtectPassword(ReadPasswordSecretOrThrow(source.Password));
        clone.MdbxDatabaseId = null;
        clone.MdbxFolderId = null;
        if (clone.CategoryId is { } categoryId)
        {
            clone.CategoryId = categoryIdMap?.TryGetValue(categoryId, out var importedCategoryId) == true
                ? importedCategoryId
                : null;
        }

        clone.IsDeleted = false;
        clone.DeletedAt = null;
        clone.IsArchived = false;
        clone.ArchivedAt = null;
        clone.BitwardenLocalModified = true;
        return clone;
    }

    private PasswordHistoryEntry ClonePasswordHistoryForImport(PasswordHistoryEntry source, long importedPasswordId)
    {
        return new PasswordHistoryEntry
        {
            Id = 0,
            EntryId = importedPasswordId,
            Password = ProtectPassword(ReadPasswordSecretOrThrow(source.Password)),
            LastUsedAt = source.LastUsedAt
        };
    }

    private string ReadPasswordSecretOrThrow(string storedPassword)
    {
        var result = PasswordSecretResolver.Read(storedPassword, _cryptoService);
        if (result.IsReadable)
        {
            return result.Value;
        }

        var reason = result.State == PasswordSecretState.Locked
            ? PasswordSecretUnavailableReason.VaultLocked
            : PasswordSecretUnavailableReason.UnreadableData;
        throw new PasswordSecretUnavailableException(reason);
    }

    private string ProtectPassword(string password)
    {
        return _cryptoService.IsUnlocked ? _cryptoService.EncryptString(password) : password;
    }
}

