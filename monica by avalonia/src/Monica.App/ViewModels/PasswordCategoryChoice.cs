using Avalonia;
using Monica.Core.Categories;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed record PasswordCategoryChoice(
    long? Id,
    string Name,
    string DisplayName = "",
    string ParentPath = "",
    int Level = 0)
{
    public string FolderDisplayName => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;
    public string FullPath => Name;
    public bool HasParentPath => !string.IsNullOrWhiteSpace(ParentPath);
    public Thickness Indent => new(Math.Max(0, Level) * 16, 0, 0, 0);

    public static IReadOnlyList<PasswordCategoryChoice> BuildOptions(
        IEnumerable<Category> categories,
        string noFolderLabel)
    {
        var result = new List<PasswordCategoryChoice>
        {
            new(null, noFolderLabel)
        };
        result.AddRange(LocalCategoryPath.BuildOptions(categories, includeVirtualParents: false)
            .Where(option => option.Category is not null)
            .Select(option => new PasswordCategoryChoice(
                option.Category!.Id,
                option.Path,
                option.DisplayName,
                option.ParentPath ?? "",
                option.Depth)));
        return result;
    }
}
