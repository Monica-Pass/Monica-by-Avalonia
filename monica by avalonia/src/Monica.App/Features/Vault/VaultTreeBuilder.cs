using Monica.App.Controls;
using Monica.Core.Categories;
using Monica.Core.Models;

namespace Monica.App.Features.Vault;

/// Projects the whole vault — folders plus every entry type — into the flat, depth-tagged row list
/// the shared tree control renders. Kept free of view-model and repository access so the ordering,
/// pruning and expansion rules can be exercised directly from tests.
public static class VaultTreeBuilder
{
    private const string UnfiledPath = "";

    public static IReadOnlyList<IVaultTreeRow> Build(
        IReadOnlyList<Category> categories,
        IReadOnlyList<PasswordEntry> passwords,
        IReadOnlyList<SecureItem> secureItems,
        IReadOnlyCollection<string> collapsedFolderKeys,
        VaultTreeFilter filter)
    {
        var roots = new List<FolderNode>();
        var nodes = new Dictionary<string, FolderNode>(StringComparer.OrdinalIgnoreCase);
        var folderPathByCategoryId = new Dictionary<long, string>();

        foreach (var category in categories.OrderBy(item => item.SortOrder).ThenBy(item => item.Name))
        {
            folderPathByCategoryId[category.Id] = Attach(roots, nodes, category);
        }

        var entriesByPath = new Dictionary<string, List<VaultTreeEntryRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in passwords.Where(filter.MatchesPassword))
        {
            AddEntry(entriesByPath, folderPathByCategoryId, entry.CategoryId, VaultTreeKey.Password(entry.Id),
                entry.Title, entry.SortOrder, VaultEntryKinds.FromPassword(entry),
                entry.Username.Length > 0 ? entry.Username : entry.Website,
                password: entry);
        }

        foreach (var item in secureItems.Where(filter.MatchesSecureItem))
        {
            var kind = VaultEntryKinds.FromSecureItem(item)!.Value;
            AddEntry(entriesByPath, folderPathByCategoryId, item.CategoryId, VaultTreeKey.SecureItem(item.Id),
                item.Title, item.SortOrder, kind, VaultEntryKinds.LabelFor(kind), item: item);
        }

        foreach (var root in roots)
        {
            root.MatchedEntryCount = CountMatches(root, entriesByPath);
        }

        var rows = new List<IVaultTreeRow>();
        foreach (var root in SortNodes(roots))
        {
            Emit(root, rows, entriesByPath, collapsedFolderKeys, filter);
        }

        foreach (var entry in SortedEntries(entriesByPath, UnfiledPath))
        {
            rows.Add(entry);
        }

        return rows;
    }

    private static string Attach(
        List<FolderNode> roots,
        Dictionary<string, FolderNode> nodes,
        Category category)
    {
        var pathParts = LocalCategoryPath.Normalize(category.Name)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathParts.Length == 0)
        {
            pathParts = [category.Name];
        }

        FolderNode? parent = null;
        for (var index = 0; index < pathParts.Length; index++)
        {
            var key = string.Join("/", pathParts.Take(index + 1));
            if (!nodes.TryGetValue(key, out var node))
            {
                node = new FolderNode(key, pathParts[index], index);
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
            }

            parent = node;
        }

        return string.Join("/", pathParts);
    }

    private static void AddEntry(
        Dictionary<string, List<VaultTreeEntryRow>> entriesByPath,
        Dictionary<long, string> folderPathByCategoryId,
        long? categoryId,
        string key,
        string title,
        int sortOrder,
        VaultEntryKind kind,
        string detail,
        PasswordEntry? password = null,
        SecureItem? item = null)
    {
        var path = categoryId is { } id && folderPathByCategoryId.TryGetValue(id, out var folderPath)
            ? folderPath
            : UnfiledPath;

        if (!entriesByPath.TryGetValue(path, out var bucket))
        {
            bucket = [];
            entriesByPath[path] = bucket;
        }

        bucket.Add(new VaultTreeEntryRow
        {
            Key = key,
            Kind = kind,
            Label = string.IsNullOrWhiteSpace(title) ? VaultEntryKinds.LabelFor(kind) : title.Trim(),
            Indent = FolderTreeLayout.IndentFor(Depth(path)),
            EntryDetail = detail,
            SortOrder = sortOrder,
            Password = password,
            Item = item
        });
    }

    private static void Emit(
        FolderNode node,
        List<IVaultTreeRow> rows,
        Dictionary<string, List<VaultTreeEntryRow>> entriesByPath,
        IReadOnlyCollection<string> collapsedFolderKeys,
        VaultTreeFilter filter)
    {
        if (filter.IsNarrowing && node.MatchedEntryCount == 0)
        {
            return;
        }

        var hasEntries = SortedEntries(entriesByPath, node.Path).Count > 0;
        var hasChildren = node.Children.Count > 0 || hasEntries;
        var isExpanded = (filter.IsNarrowing || !collapsedFolderKeys.Contains(node.Key)) && hasChildren;
        rows.Add(new VaultTreeFolderRow
        {
            Path = node.Path,
            CategoryId = node.Category?.Id,
            Label = node.DisplayName,
            Indent = FolderTreeLayout.IndentFor(node.Level),
            HasChildren = hasChildren,
            IsExpanded = isExpanded
        });

        if (!isExpanded)
        {
            return;
        }

        foreach (var child in SortNodes(node.Children))
        {
            Emit(child, rows, entriesByPath, collapsedFolderKeys, filter);
        }

        foreach (var entry in SortedEntries(entriesByPath, node.Path))
        {
            rows.Add(entry);
        }
    }

    private static IEnumerable<FolderNode> SortNodes(IEnumerable<FolderNode> nodes) => nodes
        .OrderBy(node => node.Category?.SortOrder ?? int.MaxValue)
        .ThenBy(node => node.DisplayName, StringComparer.CurrentCultureIgnoreCase);

    private static int CountMatches(FolderNode node, Dictionary<string, List<VaultTreeEntryRow>> entriesByPath)
    {
        var matches = SortedEntries(entriesByPath, node.Path).Count;
        foreach (var child in node.Children)
        {
            matches += CountMatches(child, entriesByPath);
        }

        node.MatchedEntryCount = matches;
        return matches;
    }

    private static IReadOnlyList<VaultTreeEntryRow> SortedEntries(
        Dictionary<string, List<VaultTreeEntryRow>> entriesByPath,
        string path)
    {
        if (!entriesByPath.TryGetValue(path, out var bucket))
        {
            return [];
        }

        return bucket
            .OrderBy(row => row.SortOrder)
            .ThenBy(row => row.Label, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private static int Depth(string path) =>
        path.Length == 0 ? 0 : path.Count(character => character == '/') + 1;

    private sealed class FolderNode(string path, string displayName, int level)
    {
        public string Path { get; } = path;

        public string DisplayName { get; } = displayName;

        public int Level { get; } = level;

        public Category? Category { get; set; }

        public List<FolderNode> Children { get; } = [];

        public int MatchedEntryCount { get; set; }

        public string Key => VaultTreeKey.Folder(Path);
    }
}
