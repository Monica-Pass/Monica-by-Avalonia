using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.App.Features.Vault;

/// What a library search reads, so the tree searches at least as wide as the four pages it replaced.
/// A query is split on spaces and punctuation and every term has to land somewhere, which is what the
/// note page already did and a superset of the one-substring match the other three did: a phrase that
/// used to match as a whole still matches, and its words can now come from different fields.
/// Short-circuiting chains rather than params arrays: a rebuild runs this over every entry.
/// Culture-aware casing matches the repository's metadata search, so the immediate in-memory match and
/// the delayed database match agree on what a term is.
internal static class VaultSearchFields
{
    private static readonly char[] TermSeparators = [' ', '\t', '\r', '\n', ',', ';'];

    internal static IReadOnlyList<string> ParseTerms(string? search) =>
        string.IsNullOrWhiteSpace(search)
            ? []
            : search.Split(
                TermSeparators,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    internal static bool MatchesPassword(PasswordEntry entry, IReadOnlyList<string> terms)
    {
        foreach (var term in terms)
        {
            if (!MatchesPassword(entry, term))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool MatchesSecureItem(
        SecureItem item,
        IReadOnlyList<string> terms,
        string payloadText)
    {
        foreach (var term in terms)
        {
            if (!Contains(item.Title, term) &&
                !Contains(item.Notes, term) &&
                !Contains(payloadText, term))
            {
                return false;
            }
        }

        return true;
    }

    /// Everything a payload holds that its row does not show: the issuer and account behind an OTP, the
    /// number inside wallet JSON, a note body and its tags. Flattened to one string per item so the
    /// decode happens once and can be memoized across the passes a single refresh makes.
    internal static string SecureItemPayloadText(SecureItem item) => item.ItemType switch
    {
        VaultItemType.Totp => TotpPayloadText(item),
        VaultItemType.BankCard => WalletPayloadText(WalletItemDataCodec.DecodeBankCard(item)),
        VaultItemType.Document => WalletPayloadText(WalletItemDataCodec.DecodeDocument(item)),
        VaultItemType.BillingAddress => WalletPayloadText(WalletItemDataCodec.DecodeBillingAddress(item)),
        VaultItemType.PaymentAccount => WalletPayloadText(WalletItemDataCodec.DecodePaymentAccount(item)),
        VaultItemType.Note => NotePayloadText(item),
        _ => ""
    };

    internal static bool MatchesPassword(PasswordEntry entry, string term) =>
        Contains(entry.Title, term) ||
        Contains(entry.Username, term) ||
        Contains(entry.Website, term) ||
        Contains(entry.Notes, term) ||
        Contains(entry.AuthenticatorKey, term) ||
        Contains(entry.AppName, term) ||
        Contains(entry.AppPackageName, term) ||
        Contains(entry.Email, term) ||
        Contains(entry.Phone, term) ||
        Contains(entry.AddressLine, term) ||
        Contains(entry.City, term) ||
        Contains(entry.State, term) ||
        Contains(entry.ZipCode, term) ||
        Contains(entry.Country, term) ||
        Contains(entry.CreditCardHolder, term) ||
        Contains(entry.CreditCardExpiry, term) ||
        Contains(entry.SsoProvider, term) ||
        Contains(entry.PasskeyBindings, term) ||
        Contains(entry.WifiMetadata, term) ||
        Contains(entry.SshKeyData, term) ||
        Contains(entry.KeepassGroupPath, term) ||
        Contains(entry.MdbxFolderId, term) ||
        Contains(entry.BitwardenFolderId, term);

    private static string TotpPayloadText(SecureItem item)
    {
        // The same resolution the authenticator page filters with, fallbacks included, so a code stored
        // as a bare otpauth URI stays findable by the issuer inside it.
        var data = TotpDataResolver.ParseStoredItemData(item.ItemData, item.Title, item.Notes);
        return data is null ? "" : $"{data.Issuer}\n{data.AccountName}\n{data.OtpType}";
    }

    private static string NotePayloadText(SecureItem item)
    {
        var decoded = NoteContentCodec.DecodeFromItem(item);
        return decoded.Tags.Count == 0
            ? decoded.Content
            : $"{decoded.Content}\n{string.Join('\n', decoded.Tags)}";
    }

    private static bool Contains(string? value, string term) =>
        value is not null && value.Contains(term, StringComparison.CurrentCultureIgnoreCase);

    private static string WalletPayloadText(BankCardWalletData data) =>
        $"{data.CardNumber}\n{data.CardholderName}\n{data.BankName}\n{data.Brand}\n{data.BillingAddress}";

    private static string WalletPayloadText(DocumentWalletData data) =>
        $"{data.DocumentNumber}\n{data.FullName}\n{data.IssuedBy}\n{data.Nationality}\n{data.AdditionalInfo}";

    private static string WalletPayloadText(BillingAddressWalletData data) =>
        $"{data.FullName}\n{data.Company}\n{data.StreetAddress}\n{data.City}\n{data.StateProvince}" +
        $"\n{data.PostalCode}\n{data.Country}\n{data.Phone}\n{data.Email}";

    private static string WalletPayloadText(PaymentAccountWalletData data) =>
        $"{data.Provider}\n{data.AccountName}\n{data.AccountHolderName}\n{data.Email}\n{data.Phone}" +
        $"\n{data.Username}\n{data.AccountId}\n{data.MaskedAccountNumber}\n{data.Iban}\n{data.SwiftBic}" +
        $"\n{data.Website}\n{data.Currency}";
}
