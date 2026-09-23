namespace Monica.Core.Passkeys;

/// <summary>
/// What a stored credential's key material is compatible with, using the same vocabulary as Monica for
/// Android so a vault that travels between the two does not have to re-interpret the column.
/// </summary>
public static class PasskeyModes
{
    /// <summary>A platform-keystore alias: the key cannot be exported, so nothing may re-wrap it.</summary>
    public const string Legacy = "LEGACY";

    /// <summary>Exportable PKCS#8 held by Monica itself, which is what Bitwarden-style sync expects.</summary>
    public const string BitwardenCompatible = "BW_COMPAT";

    /// <summary>Exportable key material filed into a KeePass database instead of the local vault.</summary>
    public const string KeePassCompatible = "KEEPASS_COMPAT";
}
