using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Monica.App;
using Monica.Core.Categories;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private Category? GetSelectedPasswordFolderCategory()
    {
        var selectedId = SelectedPasswordFolderFilter?.Id;
        return selectedId is > 0
            ? Categories.FirstOrDefault(item => item.Id == selectedId.Value)
            : null;
    }

    private string? GetSelectedPasswordFolderPath()
    {
        var selected = SelectedPasswordFolderFilter;
        return selected is { IsSystemNode: false } && !string.IsNullOrWhiteSpace(selected.PathPrefix)
            ? LocalCategoryPath.Normalize(selected.PathPrefix)
            : null;
    }

    private void RefreshPasswordFolderFilters(long? preferredCategoryId = null)
    {
        if (preferredCategoryId is > 0)
        {
            var preferredCategory = Categories.FirstOrDefault(category => category.Id == preferredCategoryId.Value);
            if (preferredCategory is not null)
            {
                ExpandPasswordFolderPath(preferredCategory.Name);
            }
        }

        var selectedKey = preferredCategoryId is not null
            ? CategorySelectionKey(preferredCategoryId.Value)
            : SelectedPasswordFolderFilter?.SelectionKey;
        var folderCountPasswords = Passwords.Where(MatchesPasswordNonFolderFilters).ToArray();
        var passwordCountByCategoryId = folderCountPasswords
            .Where(password => password.CategoryId is not null)
            .GroupBy(password => password.CategoryId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());
        var nextFilters = new List<PasswordFolderFilterChoice>
        {
            new(
            null,
            _localization.Get("AllFolders"),
            folderCountPasswords.Length,
            IsSystemNode: true,
            SelectionKey: "system:all"),
            new(
            -2,
            _localization.Get("QuickFilterFavorite"),
            folderCountPasswords.Count(password => password.IsFavorite),
            IsSystemNode: true,
            SelectionKey: "system:favorites")
        };

        foreach (var root in BuildPasswordFolderTree(passwordCountByCategoryId))
        {
            AddVisiblePasswordFolder(root, nextFilters);
        }

        nextFilters.Add(new PasswordFolderFilterChoice(
            -1,
            _localization.Get("NoFolder"),
            folderCountPasswords.Count(password => password.CategoryId is null),
            IsSystemNode: true,
            SelectionKey: "system:none"));
        ReplaceItems(PasswordFolderFilters, nextFilters);

        SelectedPasswordFolderFilter =
            PasswordFolderFilters.FirstOrDefault(item => string.Equals(item.SelectionKey, selectedKey, StringComparison.OrdinalIgnoreCase)) ??
            PasswordFolderFilters.FirstOrDefault(item => item.Id == preferredCategoryId) ??
            PasswordFolderFilters.FirstOrDefault();
        RaiseFilteredPasswordsChanged();
        OnPropertyChanged(nameof(CanManageSelectedPasswordFolder));
        RaisePasswordFolderFilterCollections();
        RaisePasswordFilterState();
    }

    private IReadOnlyList<PasswordFolderTreeNode> BuildPasswordFolderTree(
        IReadOnlyDictionary<long, int> passwordCountByCategoryId)
    {
        var roots = new List<PasswordFolderTreeNode>();
        var nodes = new Dictionary<string, PasswordFolderTreeNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var category in Categories.OrderBy(item => item.SortOrder).ThenBy(item => item.Name))
        {
            var pathParts = SplitFolderPath(category.Name);
            if (pathParts.Length == 0)
            {
                pathParts = [category.Name];
            }

            PasswordFolderTreeNode? parent = null;
            for (var index = 0; index < pathParts.Length; index++)
            {
                var key = string.Join("/", pathParts.Take(index + 1));
                if (!nodes.TryGetValue(key, out var node))
                {
                    node = new PasswordFolderTreeNode(key, pathParts[index], index);
                    nodes[key] = node;
                    if (parent is null)
                    {
                        roots.Add(node);
                    }
                    else
                    {
                        parent.Children.Add(node);
                    }
                }

                if (index == pathParts.Length - 1)
                {
                    node.Category = category;
                    node.ExactCount = passwordCountByCategoryId.GetValueOrDefault(category.Id);
                }

                parent = node;
            }
        }

        foreach (var root in roots)
        {
            UpdatePasswordFolderDescendantCount(root);
        }

        SortPasswordFolderNodes(roots);
        return roots;
    }

    private static int UpdatePasswordFolderDescendantCount(PasswordFolderTreeNode node)
    {
        node.DescendantCount = node.ExactCount + node.Children.Sum(UpdatePasswordFolderDescendantCount);
        return node.DescendantCount;
    }

    private static void SortPasswordFolderNodes(List<PasswordFolderTreeNode> nodes)
    {
        nodes.Sort((left, right) =>
        {
            var leftSort = left.Category?.SortOrder ?? int.MaxValue;
            var rightSort = right.Category?.SortOrder ?? int.MaxValue;
            var sortCompare = leftSort.CompareTo(rightSort);
            return sortCompare != 0
                ? sortCompare
                : string.Compare(left.DisplayName, right.DisplayName, StringComparison.CurrentCultureIgnoreCase);
        });

        foreach (var node in nodes)
        {
            SortPasswordFolderNodes(node.Children);
        }
    }

    private void AddVisiblePasswordFolder(
        PasswordFolderTreeNode node,
        ICollection<PasswordFolderFilterChoice> filters)
    {
        var hasChildren = node.Children.Count > 0;
        var isExpanded = hasChildren && !_collapsedPasswordFolderKeys.Contains(PathSelectionKey(node.Key));
        filters.Add(new PasswordFolderFilterChoice(
            node.Category?.Id,
            node.Category?.Name ?? node.Key,
            node.DescendantCount,
            node.DisplayName,
            node.Level,
            SelectionKey: node.Category is null ? PathSelectionKey(node.Key) : CategorySelectionKey(node.Category.Id),
            PathPrefix: node.Key,
            HasChildren: hasChildren,
            IsExpanded: isExpanded));

        if (!isExpanded)
        {
            return;
        }

        foreach (var child in node.Children)
        {
            AddVisiblePasswordFolder(child, filters);
        }
    }

    private static string CategorySelectionKey(long id) => $"category:{id}";

    private static string PathSelectionKey(string path) => $"path:{path}";

    private void ExpandPasswordFolderPath(string name)
    {
        var pathParts = SplitFolderPath(name);
        for (var index = 0; index < pathParts.Length - 1; index++)
        {
            _collapsedPasswordFolderKeys.Remove(PathSelectionKey(string.Join("/", pathParts.Take(index + 1))));
        }
    }

    private static string[] SplitFolderPath(string value) =>
        LocalCategoryPath.Normalize(value)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

}
