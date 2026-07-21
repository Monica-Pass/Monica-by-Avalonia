using Monica.Core.Categories;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task<LocalCategoryCreateResult?> CreateLocalCategoryAsync(
        string? parentPath,
        string input)
    {
        var path = LocalCategoryPath.Build(parentPath, input);
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusMessage = _localization.Get("FolderNameRequired");
            return null;
        }

        var existing = Categories.FirstOrDefault(category =>
            LocalCategoryPath.Normalize(category.Name).Equals(path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            StatusMessage = _localization.Format("SelectedFolderFormat", existing.Name);
            return new LocalCategoryCreateResult(existing);
        }

        var category = new Category
        {
            Name = path,
            SortOrder = Categories.Count == 0 ? 1 : Categories.Max(item => item.SortOrder) + 1
        };
        await _repository.SaveCategoryAsync(category);
        Categories.Add(category);
        await LogCategoryOperationAsync(category, "CREATE");
        RefreshCategoryConsumers();
        StatusMessage = _localization.Format("CreatedFolderFormat", category.Name);
        return new LocalCategoryCreateResult(category);
    }

    private async Task<LocalCategoryRenameResult?> RenameLocalCategoryAsync(
        Category? category,
        string input)
    {
        if (category is null)
        {
            StatusMessage = _localization.Get("SelectFolderToManage");
            return null;
        }

        var leafName = LocalCategoryPath.LeafName(input);
        if (string.IsNullOrWhiteSpace(leafName))
        {
            StatusMessage = _localization.Get("FolderNameRequired");
            return null;
        }

        var renamePlan = LocalCategoryPath.PlanSubtreeRename(Categories, category, leafName);
        if (renamePlan.HasConflict)
        {
            StatusMessage = _localization.Format("FolderAlreadyExistsFormat", renamePlan.ConflictPath ?? leafName);
            return null;
        }

        var oldPath = category.Name;
        foreach (var updatedCategory in Categories.Where(item => renamePlan.UpdatedPaths.ContainsKey(item.Id)))
        {
            updatedCategory.Name = renamePlan.UpdatedPaths[updatedCategory.Id];
            await _repository.SaveCategoryAsync(updatedCategory);
        }

        await LogCategoryOperationAsync(category, "UPDATE");
        RefreshCategoryConsumers();
        StatusMessage = _localization.Format("RenamedFolderFormat", oldPath, renamePlan.DestinationPath);
        return new LocalCategoryRenameResult(category, oldPath, renamePlan.DestinationPath);
    }

    private async Task<LocalCategoryDeleteResult?> DeleteLocalCategoryAsync(Category? category)
    {
        if (category is null)
        {
            StatusMessage = _localization.Get("SelectFolderToManage");
            return null;
        }

        var passwordCount = Passwords.Count(item => item.CategoryId == category.Id);
        var totpCount = TotpItems.Count(item => item.CategoryId == category.Id);
        var noteCount = NoteItems.Count(item => item.CategoryId == category.Id);
        var walletCount = WalletItems.Count(item => item.CategoryId == category.Id);
        var affectedCount = passwordCount + totpCount + noteCount + walletCount;
        if (!await ConfirmDeleteFolderAsync(category.Name, affectedCount))
        {
            return null;
        }

        await _repository.DeleteCategoryAsync(category.Id);
        Categories.Remove(category);
        ClearCategoryReferences(Passwords, category.Id);
        ClearCategoryReferences(TotpItems, category.Id);
        ClearCategoryReferences(NoteItems, category.Id);
        ClearCategoryReferences(WalletItems, category.Id);
        foreach (var tab in OpenNoteTabs.Where(tab => tab.DraftCategoryId == category.Id))
        {
            tab.DraftCategoryId = null;
            tab.IsDirty = true;
        }

        await LogCategoryOperationAsync(category, "DELETE");
        RefreshCategoryConsumers();
        return new LocalCategoryDeleteResult(category.Name, passwordCount, affectedCount);
    }

    private void RefreshCategoryConsumers(long? preferredPasswordCategoryId = null)
    {
        RefreshPasswordFolderFilters(preferredPasswordCategoryId);
        RefreshNoteCategoryOptions();
        RaiseNoteTreeState();
    }

    private static void ClearCategoryReferences(IEnumerable<SecureItem> items, long categoryId)
    {
        foreach (var item in items.Where(item => item.CategoryId == categoryId))
        {
            item.CategoryId = null;
        }
    }

    private static void ClearCategoryReferences(IEnumerable<PasswordEntry> items, long categoryId)
    {
        foreach (var item in items.Where(item => item.CategoryId == categoryId))
        {
            item.CategoryId = null;
        }
    }

    private Task LogCategoryOperationAsync(Category category, string operationType) =>
        LogOperationAsync(new OperationLog
        {
            ItemType = "CATEGORY",
            ItemId = category.Id,
            ItemTitle = category.Name,
            OperationType = operationType,
            DeviceName = Environment.MachineName
        });

    private sealed record LocalCategoryCreateResult(Category Category);
    private sealed record LocalCategoryRenameResult(Category Category, string OldPath, string DestinationPath);
    private sealed record LocalCategoryDeleteResult(string Name, int PasswordCount, int AffectedCount);
}
