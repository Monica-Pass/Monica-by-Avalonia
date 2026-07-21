using CommunityToolkit.Mvvm.Input;
using Monica.Core.Categories;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task CreateNoteFolderAsync()
    {
        var result = await CreateLocalCategoryAsync(GetSelectedNoteFolderPath(), NewNoteFolderName);
        if (result is null)
        {
            return;
        }

        SelectAndExpandNoteFolder(result.Category.Name);
        NewNoteFolderName = "";
    }

    [RelayCommand]
    private async Task RenameSelectedNoteFolderAsync()
    {
        var category = GetSelectedNoteFolderCategory();
        var result = await RenameLocalCategoryAsync(category, NewNoteFolderName);
        if (result is null)
        {
            return;
        }

        RemapCollapsedNoteFolderKeys(result.OldPath, result.DestinationPath);
        SelectAndExpandNoteFolder(result.DestinationPath);
        NewNoteFolderName = "";
    }

    [RelayCommand]
    private async Task DeleteSelectedNoteFolderAsync()
    {
        var category = GetSelectedNoteFolderCategory();
        var parentPath = category is null ? null : LocalCategoryPath.ParentPath(category.Name);
        var result = await DeleteLocalCategoryAsync(category);
        if (result is null)
        {
            return;
        }

        SelectedNoteFolderKey = string.IsNullOrWhiteSpace(parentPath) ? "" : $"folder:{parentPath}";
        NewNoteFolderName = "";
        StatusMessage = _localization.Format("DeletedFolderItemsFormat", result.Name, result.AffectedCount);
    }

    private void SelectAndExpandNoteFolder(string path)
    {
        var normalizedPath = LocalCategoryPath.Normalize(path);
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            _collapsedNoteFolderKeys.Remove($"folder:{string.Join('/', segments.Take(index + 1))}");
        }

        SelectedNoteFolderKey = $"folder:{normalizedPath}";
        RaiseNoteTreeState();
    }

    private void RemapCollapsedNoteFolderKeys(string oldPath, string destinationPath)
    {
        var normalizedOldPath = LocalCategoryPath.Normalize(oldPath);
        var normalizedDestinationPath = LocalCategoryPath.Normalize(destinationPath);
        var affectedKeys = _collapsedNoteFolderKeys
            .Where(key => key.StartsWith("folder:", StringComparison.OrdinalIgnoreCase))
            .Where(key => LocalCategoryPath.IsDescendantOrSelf(normalizedOldPath, key["folder:".Length..]))
            .ToArray();
        foreach (var key in affectedKeys)
        {
            _collapsedNoteFolderKeys.Remove(key);
            var oldFolderPath = key["folder:".Length..];
            _collapsedNoteFolderKeys.Add($"folder:{normalizedDestinationPath}{oldFolderPath[normalizedOldPath.Length..]}");
        }
    }
}
