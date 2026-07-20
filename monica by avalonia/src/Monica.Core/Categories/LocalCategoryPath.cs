using Monica.Core.Models;

namespace Monica.Core.Categories;

public sealed record LocalCategoryPathOption(
    string Path,
    string DisplayName,
    string? ParentPath,
    int Depth,
    Category? Category);

public sealed record LocalCategoryRenamePlan(
    string DestinationPath,
    IReadOnlyDictionary<long, string> UpdatedPaths,
    string? ConflictPath)
{
    public bool HasConflict => !string.IsNullOrWhiteSpace(ConflictPath);
}

public static class LocalCategoryPath
{
    public static string Normalize(string path) =>
        string.Join('/', path
            .Split(['/', '\\'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

    public static string LeafName(string path)
    {
        var normalized = Normalize(path);
        var separator = normalized.LastIndexOf('/');
        return separator < 0 ? normalized : normalized[(separator + 1)..];
    }

    public static string? ParentPath(string path)
    {
        var normalized = Normalize(path);
        var separator = normalized.LastIndexOf('/');
        return separator < 0 ? null : normalized[..separator];
    }

    public static string Build(string? parentPath, string name)
    {
        var child = Normalize(name);
        var parent = Normalize(parentPath ?? "");
        return string.IsNullOrWhiteSpace(parent)
            ? child
            : string.IsNullOrWhiteSpace(child) ? "" : $"{parent}/{child}";
    }

    public static bool IsDescendantOrSelf(string parentPath, string candidatePath)
    {
        var parent = Normalize(parentPath);
        var candidate = Normalize(candidatePath);
        return string.Equals(parent, candidate, StringComparison.OrdinalIgnoreCase) ||
               candidate.StartsWith($"{parent}/", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<LocalCategoryPathOption> BuildOptions(
        IEnumerable<Category> categories,
        bool includeVirtualParents = true)
    {
        var categoryByPath = categories
            .Select(category => (Path: Normalize(category.Name), Category: category))
            .Where(item => item.Path.Length > 0)
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Category, StringComparer.OrdinalIgnoreCase);
        var paths = includeVirtualParents
            ? categoryByPath.Keys.SelectMany(EnumerateParentPaths)
            : categoryByPath.Keys;

        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var segments = path.Split('/');
                return new LocalCategoryPathOption(
                    path,
                    segments[^1],
                    segments.Length > 1 ? string.Join('/', segments[..^1]) : null,
                    Math.Max(0, segments.Length - 1),
                    categoryByPath.GetValueOrDefault(path));
            })
            .OrderBy(option => option.Path, CategoryPathComparer.Instance)
            .ToArray();
    }

    public static LocalCategoryRenamePlan PlanSubtreeRename(
        IEnumerable<Category> categories,
        Category sourceCategory,
        string newName)
    {
        var allCategories = categories.ToArray();
        var sourcePath = Normalize(sourceCategory.Name);
        var destinationPath = Build(ParentPath(sourcePath), LeafName(newName));
        var moving = allCategories
            .Where(category => IsDescendantOrSelf(sourcePath, category.Name))
            .ToArray();
        var movingIds = moving.Select(category => category.Id).ToHashSet();
        var updatedPaths = moving.ToDictionary(
            category => category.Id,
            category => destinationPath + Normalize(category.Name)[sourcePath.Length..]);
        var occupiedPaths = allCategories
            .Where(category => !movingIds.Contains(category.Id))
            .Select(category => Normalize(category.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var conflictPath = updatedPaths.Values
            .FirstOrDefault(path => occupiedPaths.Contains(path));

        return new LocalCategoryRenamePlan(destinationPath, updatedPaths, conflictPath);
    }

    private static IEnumerable<string> EnumerateParentPaths(string path)
    {
        var segments = path.Split('/');
        for (var index = 0; index < segments.Length; index++)
        {
            yield return string.Join('/', segments.Take(index + 1));
        }
    }

    private sealed class CategoryPathComparer : IComparer<string>
    {
        public static CategoryPathComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            var leftSegments = (left ?? "").Split('/');
            var rightSegments = (right ?? "").Split('/');
            for (var index = 0; index < Math.Min(leftSegments.Length, rightSegments.Length); index++)
            {
                var result = StringComparer.CurrentCultureIgnoreCase.Compare(leftSegments[index], rightSegments[index]);
                if (result != 0)
                {
                    return result;
                }
            }

            return leftSegments.Length.CompareTo(rightSegments.Length);
        }
    }
}
