using Monica.Core.Models;

namespace Monica.App.Features.Vault;

/// The password quick filters the library's more menu can switch on. Favorites is absent because
/// the header carries it as a chip of its own, and a filter that appears twice reads as a bug.
public sealed record VaultQuickFilters(
    bool TwoFactor = false,
    bool WithNotes = false,
    bool Passkey = false,
    bool BoundNote = false,
    bool Uncategorized = false,
    bool LocalOnly = false,
    bool WithAttachments = false)
{
    public static readonly VaultQuickFilters None = new();

    public bool IsOn =>
        TwoFactor || WithNotes || Passkey || BoundNote || Uncategorized || LocalOnly || WithAttachments;

    public bool Matches(PasswordEntry entry)
    {
        if (TwoFactor && !entry.HasAuthenticator)
        {
            return false;
        }

        if (WithNotes && string.IsNullOrWhiteSpace(entry.Notes))
        {
            return false;
        }

        if (Passkey && string.IsNullOrWhiteSpace(entry.PasskeyBindings))
        {
            return false;
        }

        if (BoundNote && entry.BoundNoteId is null)
        {
            return false;
        }

        if (Uncategorized && entry.CategoryId is not null)
        {
            return false;
        }

        if (LocalOnly && !IsLocalOnly(entry))
        {
            return false;
        }

        return !WithAttachments || entry.HasAttachments;
    }

    /// An entry that came from no external vault lives only in this install, so it can never be
    /// pulled back by sync. Both the password list and the library tree ask this one question.
    public static bool IsLocalOnly(PasswordEntry entry) =>
        entry.BitwardenVaultId is null &&
        entry.KeepassDatabaseId is null &&
        entry.MdbxDatabaseId is null;
}
