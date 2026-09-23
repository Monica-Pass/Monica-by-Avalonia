namespace Monica.Core.Bitwarden;

/// <summary>
/// The queue row of a cipher the server has never seen needs an identity, and a Bitwarden cipher id is
/// a GUID this app does not own yet. Rather than invent one and hope the server accepts it, the pending
/// operation carries a key derived from the local row, which is also the only way back to that row once
/// the response comes with the real id. A Bitwarden id can never collide with these: they are not GUIDs.
/// </summary>
public static class BitwardenLocalCipherIdentity
{
    private const string PasswordPrefix = "local-password:";

    public static string ForPassword(long entryId) => PasswordPrefix + entryId.ToString();

    public static bool TryReadPasswordId(string cipherId, out long entryId)
    {
        entryId = 0;
        return cipherId is not null &&
               cipherId.StartsWith(PasswordPrefix, StringComparison.Ordinal) &&
               long.TryParse(cipherId.AsSpan(PasswordPrefix.Length), out entryId);
    }
}
