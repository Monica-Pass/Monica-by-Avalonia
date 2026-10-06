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

    /// <summary>Credential is stored by Windows Hello and cannot be exported into the vault.</summary>
    public const string WindowsHello = "WINDOWS_HELLO";

    /// <summary>Credential is stored by Android Credential Manager or its platform provider.</summary>
    public const string AndroidCredentialManager = "ANDROID_CREDENTIAL_MANAGER";

    /// <summary>Credential is stored by macOS AuthenticationServices/iCloud Keychain.</summary>
    public const string MacOsAuthenticationServices = "MACOS_AUTHENTICATION_SERVICES";

    /// <summary>Credential is stored by a Linux WebAuthn/FIDO2 provider.</summary>
    public const string LinuxFido2 = "LINUX_FIDO2";
}
