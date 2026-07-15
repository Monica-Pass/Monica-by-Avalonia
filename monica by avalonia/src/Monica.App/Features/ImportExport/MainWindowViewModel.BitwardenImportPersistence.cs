using Monica.Core.ImportExport;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task ImportBitwardenSnapshotAsync(
        BitwardenJsonImportSnapshot snapshot,
        BitwardenImportAccumulator progress,
        CancellationToken cancellationToken)
    {
        var existingPasswords = await _repository.GetPasswordsAsync(
            includeDeleted: true,
            includeArchived: true,
            cancellationToken);
        var existingSecureItems = await _repository.GetSecureItemsAsync(
            includeDeleted: true,
            cancellationToken: cancellationToken);
        var sourceKeys = existingPasswords.Select(item => item.ReplicaGroupId)
            .Concat(existingSecureItems.Select(item => item.ReplicaGroupId))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var categoryIdByFolderId = await ImportBitwardenFoldersAsync(
            snapshot.Folders,
            progress,
            cancellationToken);
        var customFieldsBySourceId = snapshot.PasswordCustomFields
            .ToDictionary(item => item.PasswordId);
        var historyBySourceId = snapshot.PasswordHistory
            .ToDictionary(item => item.PasswordId);

        foreach (var source in snapshot.Passwords)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(source.ReplicaGroupId) || !sourceKeys.Add(source.ReplicaGroupId))
            {
                progress.Skipped++;
                AdvanceBitwardenImportProgress();
                continue;
            }

            var imported = CloneBitwardenPassword(source, categoryIdByFolderId);
            await _repository.SavePasswordAsync(imported, cancellationToken);
            if (customFieldsBySourceId.TryGetValue(source.Id, out var customFields))
            {
                await _repository.ReplaceCustomFieldsAsync(
                    imported.Id,
                    customFields.Fields
                        .Select(item => CloneCustomFieldForImport(item, imported.Id))
                        .ToArray(),
                    cancellationToken);
            }

            if (historyBySourceId.TryGetValue(source.Id, out var history))
            {
                foreach (var entry in history.Entries.OrderBy(item => item.LastUsedAt))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await _repository.SavePasswordHistoryAsync(
                        ClonePasswordHistoryForImport(entry, imported.Id),
                        cancellationToken);
                }
            }

            if (!string.IsNullOrWhiteSpace(imported.AuthenticatorKey))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await SynchronizeBoundTotpAsync(imported);
            }

            progress.Imported++;
            AdvanceBitwardenImportProgress();
        }

        foreach (var source in snapshot.SecureItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(source.ReplicaGroupId) || !sourceKeys.Add(source.ReplicaGroupId))
            {
                progress.Skipped++;
                AdvanceBitwardenImportProgress();
                continue;
            }

            var imported = CloneBitwardenSecureItem(source, categoryIdByFolderId);
            await _repository.SaveSecureItemAsync(imported, cancellationToken);
            progress.Imported++;
            AdvanceBitwardenImportProgress();
        }
    }

    private async Task<IReadOnlyDictionary<string, long>> ImportBitwardenFoldersAsync(
        IReadOnlyList<BitwardenFolderSnapshot> folders,
        BitwardenImportAccumulator progress,
        CancellationToken cancellationToken)
    {
        var categories = (await _repository.GetCategoriesAsync(cancellationToken))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = folder.Name.Trim();
            if (!categories.TryGetValue(name, out var category))
            {
                category = new Category { Name = name, SortOrder = categories.Count };
                await _repository.SaveCategoryAsync(category, cancellationToken);
                categories[name] = category;
                progress.CategoriesCreated++;
            }

            result[folder.Id] = category.Id;
        }

        return result;
    }

    private PasswordEntry CloneBitwardenPassword(
        PasswordEntry source,
        IReadOnlyDictionary<string, long> categoryIdByFolderId)
    {
        var clone = source.CreateDetachedCopy();
        clone.Id = 0;
        clone.Password = ProtectPassword(ReadPasswordSecretOrThrow(source.Password));
        clone.CategoryId = ResolveBitwardenCategory(source.BitwardenFolderId, categoryIdByFolderId);
        clone.BitwardenVaultId = null;
        clone.BitwardenLocalModified = false;
        clone.MdbxDatabaseId = null;
        clone.MdbxFolderId = null;
        return clone;
    }

    private static SecureItem CloneBitwardenSecureItem(
        SecureItem source,
        IReadOnlyDictionary<string, long> categoryIdByFolderId)
    {
        var clone = source.CreateDetachedCopy();
        clone.Id = 0;
        clone.CategoryId = ResolveBitwardenCategory(source.BitwardenFolderId, categoryIdByFolderId);
        clone.BitwardenVaultId = null;
        clone.BitwardenLocalModified = false;
        clone.SyncStatus = SyncStatus.None;
        clone.MdbxDatabaseId = null;
        clone.MdbxFolderId = null;
        return clone;
    }

    private static long? ResolveBitwardenCategory(
        string? folderId,
        IReadOnlyDictionary<string, long> categoryIdByFolderId) =>
        !string.IsNullOrWhiteSpace(folderId) && categoryIdByFolderId.TryGetValue(folderId, out var categoryId)
            ? categoryId
            : null;

    private sealed class BitwardenImportAccumulator
    {
        public int Imported { get; set; }
        public int Skipped { get; set; }
        public int CategoriesCreated { get; set; }
    }
}
