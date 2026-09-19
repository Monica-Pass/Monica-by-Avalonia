namespace Monica.App.Features.Vault;

/// The library is a single workspace and the four vault sections are presets of it, so one rail tag
/// decides both which page is shown and which slice of the tree it shows.
public static class VaultPresets
{
    public const string LibrarySection = "Vault";

    private static readonly string[] LibraryTags =
    [
        LibrarySection,
        "Passwords",
        "Notes",
        "Totp",
        "Cards"
    ];

    public static bool IsLibrarySection(string? section) =>
        section is not null && LibraryTags.Contains(section, StringComparer.OrdinalIgnoreCase);

    public static VaultEntryGroup GroupOf(string? section) => section switch
    {
        "Passwords" => VaultEntryGroup.Passwords,
        "Notes" => VaultEntryGroup.Notes,
        "Totp" => VaultEntryGroup.Totp,
        "Cards" => VaultEntryGroup.Cards,
        _ => VaultEntryGroup.All
    };
}
