using CommunityToolkit.Mvvm.ComponentModel;
using Monica.Core.Models;

namespace Monica.App.Features.RecycleBin;

/// <summary>
/// Safe, non-secret projection shared by password and secure-item recycle rows.
/// </summary>
public sealed partial class RecycleBinDisplayItem : ObservableObject
{
    private readonly PasswordEntry? _password;
    private readonly SecureItem? _secureItem;

    private RecycleBinDisplayItem(
        string key,
        string title,
        string itemType,
        string source,
        string retentionText,
        DateTimeOffset? deletedAt,
        PasswordEntry? password,
        SecureItem? secureItem)
    {
        Key = key;
        Title = title;
        ItemType = itemType;
        Source = source;
        RetentionText = retentionText;
        DeletedAt = deletedAt;
        _password = password;
        _secureItem = secureItem;
    }

    public string Key { get; }
    public string Title { get; }
    public string ItemType { get; }
    public string Source { get; }
    public string RetentionText { get; }
    public DateTimeOffset? DeletedAt { get; }
    public PasswordEntry? Password => _password;
    public SecureItem? SecureItem => _secureItem;
    public bool IsPassword => _password is not null;
    public long Id => _password?.Id ?? _secureItem?.Id ?? 0;
    public string AvatarText => string.IsNullOrWhiteSpace(Title) ? "?" : Title.Trim()[0].ToString().ToUpperInvariant();

    public bool IsSelected
    {
        get => _isSelected || (_password?.IsSelected ?? _secureItem?.IsSelected ?? false);
        set
        {
            if (_isSelected == value && (_password?.IsSelected ?? _secureItem?.IsSelected ?? false) == value)
            {
                return;
            }

            _isSelected = value;
            if (_password is not null) _password.IsSelected = value;
            if (_secureItem is not null) _secureItem.IsSelected = value;
            OnPropertyChanged();
        }
    }

    public static RecycleBinDisplayItem FromPassword(PasswordEntry item, string itemType, string source, string retentionText) =>
        new($"password:{item.Id}", item.Title, itemType, source, retentionText, item.DeletedAt, item, null);

    public static RecycleBinDisplayItem FromSecureItem(SecureItem item, string itemType, string source, string retentionText) =>
        new($"secure:{item.Id}", item.Title, itemType, source, retentionText, item.DeletedAt, null, item);

    private bool _isSelected;
}
