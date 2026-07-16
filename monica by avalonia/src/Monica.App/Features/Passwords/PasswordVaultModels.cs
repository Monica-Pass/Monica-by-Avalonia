using System.Globalization;
using Avalonia;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed class PasswordListRow
{
    public PasswordListRow(
        string key,
        PasswordEntry entry,
        IReadOnlyList<PasswordEntry> members,
        bool isStackHeader,
        bool isStackChild,
        bool isFirstStackChild,
        bool isLastStackChild,
        bool isExpanded)
    {
        Key = key;
        Entry = entry;
        Members = members;
        IsStackHeader = isStackHeader;
        IsStackChild = isStackChild;
        IsFirstStackChild = isFirstStackChild;
        IsLastStackChild = isLastStackChild;
        IsExpanded = isExpanded;
    }

    public string Key { get; }
    public PasswordEntry Entry { get; }
    public IReadOnlyList<PasswordEntry> Members { get; }
    public bool IsStackHeader { get; }
    public bool IsStackChild { get; }
    public bool IsFirstStackChild { get; }
    public bool IsLastStackChild { get; }
    public bool IsPasswordEntryRow => !IsStackHeader;
    public bool IsPlainPassword => IsPasswordEntryRow && !IsStackChild;
    public bool IsExpanded { get; }
    public bool IsCollapsed => IsStackHeader && !IsExpanded;
    public string StackCountText => Members.Count.ToString(CultureInfo.InvariantCulture);
    public string StackSubtitle => Members.Count == 1
        ? Entry.Username
        : string.Join(" / ", Members
            .Select(item => string.IsNullOrWhiteSpace(item.Username) ? item.Website : item.Username)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2));
    public bool HasGroupFavorite => Members.Any(item => item.IsFavorite);
    public bool HasGroupAuthenticator => Members.Any(item => item.HasAuthenticator);
    public bool HasGroupAttachments => Members.Any(item => item.HasAttachments);
    public Thickness RowMargin => IsStackChild ? new Thickness(22, 0, 0, 0) : new Thickness(0, 0, 0, 4);
    public double RowMinHeight => IsStackChild ? 50 : 58;
    public Thickness StackLineMargin => new(-5, IsFirstStackChild ? -8 : -34, 0, IsLastStackChild ? -8 : -34);
    public bool IsGroupSelected
    {
        get => Members.Count > 0 && Members.All(item => item.IsSelected);
        set
        {
            foreach (var member in Members)
            {
                member.IsSelected = value;
            }
        }
    }
}
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
    IReadOnlyDictionary<long, IReadOnlyList<Attachment>> PasswordAttachments,
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
    bool IsExpanded = false)
{
    public string FolderDisplayName => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;
    public Thickness Indent => new(Math.Max(0, Level) * 14, 0, 0, 0);
    public bool IsCollapsed => HasChildren && !IsExpanded;
}
