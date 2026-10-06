namespace Monica.Core.Models;

public static class FeatureCatalog
{
    public static IReadOnlyList<PlatformCapability> AndroidParityFeatures { get; } =
    [
        new("passwords", "Passwords", "Login credentials with websites, app bindings, folders, favorites, archive, recycle bin and history.", PlatformFeatureStatus.Available),
        new("notes", "Secure Notes", "Encrypted notes and note binding for password entries.", PlatformFeatureStatus.Available),
        new("totp", "TOTP", "TOTP/HOTP/Steam-compatible authenticator records with QR import and copy actions.", PlatformFeatureStatus.Available),
        new("cards", "Wallet", "Bank cards, identity documents and images stored as secure items.", PlatformFeatureStatus.Available),
        new("passkeys", "Passkeys", "Local passkey record management, encrypted software keys and Windows WebAuthn client ceremonies; system-provider and vault sync integration remain in development.", PlatformFeatureStatus.PlatformLimited),
        new("api-key", "API Keys", "Android-compatible API key entries with encrypted secrets, provider websites and optional request URLs.", PlatformFeatureStatus.Available),
        new("wifi", "Wi-Fi", "Network settings, connection QR import and sharing, with preserved Android advanced metadata.", PlatformFeatureStatus.Available),
        new("ssh", "SSH Keys", "Structured SSH key records stored alongside password entries.", PlatformFeatureStatus.Available),
        new("security-analysis", "Security Analysis", "Weak, duplicate and stale password checks.", PlatformFeatureStatus.Available),
        new("generator", "Generator", "Password and passphrase generation.", PlatformFeatureStatus.Available),
        new("import-export", "Import / Export", "Monica JSON, CSV, Bitwarden JSON, KeePass KDBX and Aegis-oriented pipelines.", PlatformFeatureStatus.Available),
        new("trash", "Recycle Bin", "Soft-delete and restore flows.", PlatformFeatureStatus.Available),
        new("timeline", "Timeline", "Operation log and rollback metadata.", PlatformFeatureStatus.Available),
        new("categories", "Folders", "Local categories plus KeePass, Bitwarden and MDBX ownership metadata.", PlatformFeatureStatus.Available),
        new("customization", "Personalization", "Page, card, icon and list customization entry points.", PlatformFeatureStatus.DesktopEquivalent),
        new("plus", "Monica Plus", "Subscription/status page shell for parity with mobile.", PlatformFeatureStatus.DesktopEquivalent),
        new("bitwarden", "Bitwarden", "Account login, online two-way sync, pending operations and conflict recovery.", PlatformFeatureStatus.Available),
        new("keepass", "KeePass", "Local KDBX 3/4 unlock, review and import with groups, TOTP, custom fields, UUIDs and attachments.", PlatformFeatureStatus.DesktopEquivalent),
        new("mdbx", "MDBX", "Vault create/open/sync metadata and local file-stream management.", PlatformFeatureStatus.DesktopEquivalent),
        new("webdav", "WebDAV", "Remote backup and sync path handling.", PlatformFeatureStatus.Available),
        new("onedrive", "OneDrive", "Microsoft Graph/MSAL service boundary.", PlatformFeatureStatus.DesktopEquivalent),
        new("autofill", "Desktop Autofill", "Android Autofill/IME/Accessibility becomes a global auto-type shortcut that fills the focused app, plus quick search and clipboard.", PlatformFeatureStatus.PlatformLimited),
        new("credential-provider", "Credential Provider", "Android Credential Provider equivalent is platform-specific and exposed as limited status.", PlatformFeatureStatus.PlatformLimited)
    ];
}
