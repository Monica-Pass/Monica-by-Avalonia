using FluentIcons.Common;
using Monica.Core.Models;

namespace Monica.App.Features.Vault;

/// One display vocabulary for every leaf the vault tree can hold. <see cref="VaultItemType"/> and
/// <see cref="PasswordLoginType"/> are storage shapes — a WiFi credential is a password row and a
/// bank card is a secure item — so the tree needs a third name that covers both without either
/// model leaking into <c>Controls/</c>.
public enum VaultEntryKind
{
    Password,
    Sso,
    Wifi,
    SshKey,
    Barcode,
    Note,
    Totp,
    BankCard,
    Document,
    BillingAddress,
    PaymentAccount
}

public static class VaultEntryKinds
{
    public static VaultEntryKind FromPassword(PasswordEntry entry) => entry.LoginType switch
    {
        PasswordLoginType.Sso => VaultEntryKind.Sso,
        PasswordLoginType.Wifi => VaultEntryKind.Wifi,
        PasswordLoginType.SshKey => VaultEntryKind.SshKey,
        PasswordLoginType.Barcode => VaultEntryKind.Barcode,
        _ => VaultEntryKind.Password
    };

    public static VaultEntryKind? FromSecureItem(SecureItem item) => item.ItemType switch
    {
        VaultItemType.Note => VaultEntryKind.Note,
        VaultItemType.Totp => VaultEntryKind.Totp,
        VaultItemType.BankCard => VaultEntryKind.BankCard,
        VaultItemType.Document => VaultEntryKind.Document,
        VaultItemType.BillingAddress => VaultEntryKind.BillingAddress,
        VaultItemType.PaymentAccount => VaultEntryKind.PaymentAccount,
        // A password-typed secure item has no storage path; the tree must not invent a row for it.
        VaultItemType.Password => null,
        _ => null
    };

    /// Coarse glyphs on purpose: the five credential flavours share <see cref="Symbol.Key"/> and
    /// differ by label, which keeps the tree quiet. Whether each kind earns its own glyph is a
    /// call for the 库 screenshot review, not a data-model question.
    public static Symbol SymbolFor(VaultEntryKind kind) => kind switch
    {
        VaultEntryKind.Note => Symbol.Note,
        VaultEntryKind.Totp => Symbol.Fingerprint,
        VaultEntryKind.BankCard or VaultEntryKind.PaymentAccount => Symbol.WalletCreditCard,
        VaultEntryKind.Document or VaultEntryKind.BillingAddress => Symbol.Document,
        _ => Symbol.Key
    };

    public static string LabelFor(VaultEntryKind kind) => kind switch
    {
        VaultEntryKind.Sso => "SSO",
        VaultEntryKind.Wifi => "WiFi",
        VaultEntryKind.SshKey => "SSH",
        VaultEntryKind.Barcode => "Barcode",
        VaultEntryKind.Note => "Note",
        VaultEntryKind.Totp => "TOTP",
        VaultEntryKind.BankCard => "Card",
        VaultEntryKind.Document => "Document",
        VaultEntryKind.BillingAddress => "Address",
        VaultEntryKind.PaymentAccount => "Account",
        _ => "Password"
    };
}

/// Password ids and secure-item ids are both <c>long</c> and overlap, so a row key has to carry
/// which table it came from.
public static class VaultTreeKey
{
    public static string Folder(string folderId) => $"f:{folderId}";

    public static string Password(long entryId) => $"p:{entryId}";

    public static string SecureItem(long itemId) => $"s:{itemId}";
}
