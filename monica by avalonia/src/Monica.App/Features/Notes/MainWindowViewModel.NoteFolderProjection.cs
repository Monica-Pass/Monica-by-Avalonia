using Avalonia;
using Monica.Core.Categories;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private IReadOnlyList<NoteTreeEntry> BuildNoteFolderEntries(
        IReadOnlyList<NoteTreeProjectionItem> notes,
        bool revealMatches)
    {
        var options = LocalCategoryPath.BuildOptions(Categories, includeVirtualParents: true);
        var nodes = options.ToDictionary(
            option => option.Path,
            option => new NoteFolderProjectionNode(option),
            StringComparer.OrdinalIgnoreCase);
        var roots = new List<NoteFolderProjectionNode>();

        foreach (var option in options)
        {
            var node = nodes[option.Path];
            if (string.IsNullOrWhiteSpace(node.Option.ParentPath) ||
                !nodes.TryGetValue(node.Option.ParentPath, out var parent))
            {
                roots.Add(node);
            }
            else
            {
                parent.Children.Add(node);
            }
        }

        var categoryPathById = Categories.ToDictionary(
            category => category.Id,
            category => LocalCategoryPath.Normalize(category.Name));
        var noFolderNotes = new List<SecureItem>();
        foreach (var note in notes)
        {
            if (note.Item.CategoryId is long categoryId &&
                categoryPathById.TryGetValue(categoryId, out var categoryPath) &&
                nodes.TryGetValue(categoryPath, out var node))
            {
                node.Notes.Add(note.Item);
                continue;
            }

            noFolderNotes.Add(note.Item);
        }

        foreach (var root in roots)
        {
            UpdateNoteFolderCount(root);
        }

        var entries = new List<NoteTreeEntry>(notes.Count + nodes.Count + 1);
        foreach (var root in roots)
        {
            AddNoteFolderEntries(root, entries, revealMatches);
        }

        AddNoFolderEntries(noFolderNotes, entries, revealMatches);
        return entries;
    }

    private void AddNoFolderEntries(
        IReadOnlyList<SecureItem> notes,
        ICollection<NoteTreeEntry> entries,
        bool revealMatches)
    {
        if (notes.Count == 0)
        {
            return;
        }

        const string key = "folder:none";
        var expanded = revealMatches || !_collapsedNoteFolderKeys.Contains(key);
        entries.Add(new NoteTreeEntry(
            _localization.Get("NoFolder"),
            notes.Count,
            null,
            default,
            NoteTreeEntryKind.NoFolder,
            key,
            HasChildren: true,
            IsExpanded: expanded,
            IsSelected: key.Equals(SelectedNoteFolderKey, StringComparison.OrdinalIgnoreCase)));
        if (expanded)
        {
            foreach (var note in notes)
            {
                entries.Add(CreateNoteEntry(note, level: 1));
            }
        }
    }

    private void AddNoteFolderEntries(
        NoteFolderProjectionNode node,
        ICollection<NoteTreeEntry> entries,
        bool revealMatches)
    {
        var key = $"folder:{node.Option.Path}";
        var hasChildren = node.Children.Count > 0 || node.Notes.Count > 0;
        var expanded = revealMatches || !_collapsedNoteFolderKeys.Contains(key);
        entries.Add(new NoteTreeEntry(
            node.Option.DisplayName,
            node.DescendantNoteCount,
            null,
            new Thickness(node.Option.Depth * 14, 0, 0, 0),
            NoteTreeEntryKind.Folder,
            key,
            hasChildren,
            expanded,
            IsSelected: key.Equals(SelectedNoteFolderKey, StringComparison.OrdinalIgnoreCase)));

        if (!expanded)
        {
            return;
        }

        foreach (var note in node.Notes)
        {
            entries.Add(CreateNoteEntry(note, node.Option.Depth + 1));
        }

        foreach (var child in node.Children)
        {
            AddNoteFolderEntries(child, entries, revealMatches);
        }
    }

    private static int UpdateNoteFolderCount(NoteFolderProjectionNode node)
    {
        node.DescendantNoteCount = node.Notes.Count + node.Children.Sum(UpdateNoteFolderCount);
        return node.DescendantNoteCount;
    }

    private sealed class NoteFolderProjectionNode(LocalCategoryPathOption option)
    {
        public LocalCategoryPathOption Option { get; } = option;
        public List<NoteFolderProjectionNode> Children { get; } = [];
        public List<SecureItem> Notes { get; } = [];
        public int DescendantNoteCount { get; set; }
    }
}
