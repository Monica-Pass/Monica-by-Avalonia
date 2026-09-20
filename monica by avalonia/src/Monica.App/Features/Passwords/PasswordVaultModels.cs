using Avalonia;
using FluentIcons.Common;
using Monica.App.Controls;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed record PasswordHistoryDisplayItem(PasswordHistoryEntry Entry, string DisplayPassword, bool CanCopy);
public sealed record PasswordQuickAccessItem(PasswordEntry Entry, int OpenCount, string LastOpenedText, string Subtitle);
internal sealed record PasswordDetailSnapshot(
    PasswordEntry Entry,
    IReadOnlyList<PasswordEntry> Siblings,
    Category? Category,
    SecureItem? BoundNote,
    IReadOnlyList<Attachment> Attachments,
    IReadOnlyList<CustomField> CustomFields,
    IReadOnlyList<PasswordHistoryDisplayItem> History);
internal sealed record PasswordDetailSourceSnapshot(
    PasswordEntry Entry,
    IReadOnlyList<PasswordEntry> Siblings,
    Category? Category,
    SecureItem? BoundNote,
    IReadOnlyDictionary<long, IReadOnlyList<CustomField>> PasswordCustomFields);
public sealed record PasswordFolderFilterChoice(
    long? Id,
    string Name,
    int Count,
    string DisplayName = "",
    int Level = 0,
    bool IsSystemNode = false,
    string SelectionKey = "",
    string? PathPrefix = null,
    bool HasChildren = false,
    bool IsExpanded = false) : IFolderTreeRow, IVaultTreeRow
{
    public string FolderDisplayName => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;
    public string Key => SelectionKey;
    public string Label => FolderDisplayName;
    public Thickness Indent => FolderTreeLayout.IndentFor(Level);
    public bool IsCollapsed => HasChildren && !IsExpanded;

    public VaultTreeRowKind RowKind => VaultTreeRowKind.Folder;
    public bool IsEntryRow => false;
    public Symbol EntrySymbol => Symbol.Folder;
    public string EntryDetail => "";

    // The folder rail holds no entries, so none of its rows can be checked or copied.
    public bool IsBatchable => false;
    public bool CanCopyUsername => false;
    public bool CanCopySecret => false;
    public bool CanCopyCode => false;
    public bool IsSelected
    {
        get => false;
        set { }
    }
}
