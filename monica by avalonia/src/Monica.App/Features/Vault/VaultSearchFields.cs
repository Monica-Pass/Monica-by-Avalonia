using Monica.Core.Models;

namespace Monica.App.Features.Vault;

/// Every field a credential can be found by, so the library tree searches exactly as wide as the
/// password list did before the pages became one tree. A short-circuiting chain rather than a
/// params array: a rebuild runs this over every entry and the array would be allocated per entry.
/// Culture-aware casing matches the repository's metadata search, so the immediate in-memory match
/// and the delayed database match agree on what a term is.
internal static class VaultSearchFields
{
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

    private static bool Contains(string? value, string term) =>
        value is not null && value.Contains(term, StringComparison.CurrentCultureIgnoreCase);
}
