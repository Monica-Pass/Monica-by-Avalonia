using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed record NoteOutlineItem(int Level, string Title, int LineNumber, Thickness Indent);
public sealed record NoteReferenceItem(string Label, string Target, int LineNumber, bool IsImage);
public sealed record NoteImagePreviewItem(string StoragePath, string DisplayName, string SizeText, Bitmap Image);
public sealed record NoteTreeGroup(string Name, int Count, IReadOnlyList<SecureItem> Items, bool IsUntagged);
public enum NoteTreeEntryKind
{
    Folder,
    Tag,
    Note,
    NoFolder
}

public sealed record NoteTreeEntry(
    string Name,
    int Count,
    SecureItem? Item,
    Thickness Indent,
    NoteTreeEntryKind Kind,
    string Key = "",
    bool HasChildren = false,
    bool IsExpanded = false)
{
    public bool IsGroup => Item is null;
    public bool IsNote => Item is not null;
    public bool IsFolderGroup => Kind is NoteTreeEntryKind.Folder or NoteTreeEntryKind.NoFolder;
    public bool IsTagGroup => Kind == NoteTreeEntryKind.Tag;
    public bool IsNoFolderGroup => Kind == NoteTreeEntryKind.NoFolder;
    public bool IsCollapsed => HasChildren && !IsExpanded;
}

public sealed partial class NoteEditorTab : ObservableObject
{
    public NoteEditorTab(long id, SecureItem? source, string title)
    {
        Id = id;
        Source = source;
        Title = string.IsNullOrWhiteSpace(title) ? "New Note" : title.Trim();
        DraftTitle = Title;
    }

    public long Id { get; }
    public SecureItem? Source { get; set; }
    public bool DraftInitialized { get; set; }
    public string DraftTitle { get; set; } = "";
    public string DraftContent { get; set; } = "";
    public string DraftTagsText { get; set; } = "";
    public long? DraftCategoryId { get; set; }
    public bool DraftIsMarkdown { get; set; } = true;
    public bool DraftIsFavorite { get; set; }
    public bool DraftPreviewMode { get; set; }
    public bool DraftSplitPreviewMode { get; set; }
    public int DraftSelectionStart { get; set; }
    public int DraftSelectionEnd { get; set; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private bool _isSelected;
}
