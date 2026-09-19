using System.ComponentModel;
using Avalonia;
using FluentIcons.Common;
using Monica.App.Controls;
using Monica.Core.Models;

namespace Monica.App.Features.Vault;

/// Which slice of the library a navigation preset keeps. The four vault pages are this enum,
/// not four views, so a preset has to be expressible without touching the tree algorithm.
public enum VaultEntryGroup
{
    All,
    Passwords,
    Notes,
    Totp,
    Cards
}

/// A filter that is active prunes the tree down to surviving entries and forces what remains
/// open, so a match can never hide behind a collapsed folder.
public sealed record VaultTreeFilter(
    VaultEntryGroup Group = VaultEntryGroup.All,
    string? Search = null,
    bool FavoritesOnly = false,
    string Sort = "updated-desc",
    VaultQuickFilters? QuickFilters = null)
{
    public bool HasSearch => !string.IsNullOrWhiteSpace(Search);

    public bool IsNarrowing =>
        HasSearch || FavoritesOnly || Group != VaultEntryGroup.All || (QuickFilters?.IsOn ?? false);

    /// IsNarrowing minus the group: a preset is navigation, not a filter the user applied, so the
    /// match count reads as "what did I narrow away" only within the slice already on screen.
    public bool NarrowsWithinGroup => HasSearch || FavoritesOnly || (QuickFilters?.IsOn ?? false);

    public bool Matches(VaultEntryKind kind) => Group switch
    {
        VaultEntryGroup.Passwords => kind is VaultEntryKind.Password or VaultEntryKind.Sso or
            VaultEntryKind.Wifi or VaultEntryKind.SshKey or VaultEntryKind.Barcode,
        VaultEntryGroup.Notes => kind == VaultEntryKind.Note,
        VaultEntryGroup.Totp => kind == VaultEntryKind.Totp,
        VaultEntryGroup.Cards => kind is VaultEntryKind.BankCard or VaultEntryKind.Document or
            VaultEntryKind.BillingAddress or VaultEntryKind.PaymentAccount,
        _ => true
    };

    public bool MatchesPassword(PasswordEntry entry)
    {
        if (FavoritesOnly && !entry.IsFavorite)
        {
            return false;
        }

        if (!Matches(VaultEntryKinds.FromPassword(entry)))
        {
            return false;
        }

        if (QuickFilters is { } quickFilters && !quickFilters.Matches(entry))
        {
            return false;
        }

        return !HasSearch || Contains(entry.Title) || Contains(entry.Username) ||
               Contains(entry.Website) || Contains(entry.Email);
    }

    public bool MatchesSecureItem(SecureItem item)
    {
        if (VaultEntryKinds.FromSecureItem(item) is not { } kind)
        {
            return false;
        }

        if (FavoritesOnly && !item.IsFavorite)
        {
            return false;
        }

        if (!Matches(kind))
        {
            return false;
        }

        return !HasSearch || Contains(item.Title) || Contains(item.Notes);
    }

    private bool Contains(string value) =>
        value.Contains(Search!.Trim(), StringComparison.CurrentCultureIgnoreCase);
}

public sealed record VaultTreeFolderRow : IVaultTreeRow
{
    public required string Path { get; init; }

    public required string Label { get; init; }

    public required Thickness Indent { get; init; }

    public required bool HasChildren { get; init; }

    public required bool IsExpanded { get; init; }

    public long? CategoryId { get; init; }

    public string Key => VaultTreeKey.Folder(Path);

    public VaultTreeRowKind RowKind => VaultTreeRowKind.Folder;

    public bool IsEntryRow => false;

    public Symbol EntrySymbol => Symbol.Folder;

    public string EntryDetail => "";

    public bool IsBatchable => false;

    public bool CanCopyUsername => false;

    public bool CanCopySecret => false;

    public bool CanCopyCode => false;

    // A folder has no checkbox, so the only way this is ever reached is a stray write; dropping it
    // keeps the row template free of a per-kind guard.
    public bool IsSelected
    {
        get => false;
        set { }
    }
}

public sealed record VaultTreeEntryRow : IVaultTreeRow, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// A bulk selection changes every check mark without rebuilding the rows, so the host tells the
    /// rows already on screen to read the entry again.
    internal void RaiseIsSelectedChanged() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));

    public required VaultEntryKind Kind { get; init; }

    public required string Label { get; init; }

    public required Thickness Indent { get; init; }

    /// The counterpart row is the one the host acts on: passwords and secure items come from
    /// different tables, and only the owning collection can be written back.
    public PasswordEntry? Password { get; init; }

    public SecureItem? Item { get; init; }

    public string Key { get; init; } = "";

    public VaultTreeRowKind RowKind => VaultTreeRowKind.Entry;

    public bool IsEntryRow => true;

    public bool HasChildren => false;

    public bool IsExpanded => false;

    public Symbol EntrySymbol => VaultEntryKinds.SymbolFor(Kind);

    public string EntryDetail { get; init; } = "";

    /// Notes answer to no bulk command, so only the types with one get a checkbox.
    public bool IsBatchable =>
        Password is not null ||
        Kind is VaultEntryKind.Totp or VaultEntryKind.BankCard or VaultEntryKind.Document or
            VaultEntryKind.BillingAddress or VaultEntryKind.PaymentAccount;

    public bool CanCopyUsername => Password is { } password && !string.IsNullOrWhiteSpace(password.Username);

    public bool CanCopySecret => Password is not null;

    // A code lives either on the authenticator item itself or inside a password's TOTP seed.
    public bool CanCopyCode =>
        Kind == VaultEntryKind.Totp || Password is { HasAuthenticator: true };

    /// The entry, not the row, owns the check mark: the library tree and the detail pages have to
    /// report one selection, and a rebuild reads it back from here.
    public bool IsSelected
    {
        get => Password?.IsSelected ?? Item?.IsSelected ?? false;
        set
        {
            if (Password is { } password)
            {
                password.IsSelected = value;
            }
            else if (Item is { } item)
            {
                item.IsSelected = value;
            }
        }
    }
}
