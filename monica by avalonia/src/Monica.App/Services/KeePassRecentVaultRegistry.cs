using System.Globalization;

namespace Monica.App.Services;

/// <summary>
/// One remembered database: enough to name it, find it again and say when it was last opened.
/// Recency is carried by the order of the list, not by these strings - they are only shown - and the
/// round-trip "o" spelling in UTC keeps a file written under one culture readable under another.
/// </summary>
public sealed record KeePassRecentVaultSetting
{
    public string Path { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string AddedAtUtc { get; init; } = "";
    public string LastOpenedAtUtc { get; init; } = "";
}

/// <summary>
/// What the list of .kdbx files this machine has opened is allowed to hold. Both the settings file
/// and the KeePass page read it through here, so the cap and the order cannot drift apart and only
/// one place decides what a duplicate is. A database whose file is not where it was recorded stays
/// in the list: a vault on a drive that is not mounted right now is still the user's vault.
/// </summary>
public static class KeePassRecentVaultRegistry
{
    public const int Limit = 12;

    public static void Remember(
        List<KeePassRecentVaultSetting>? vaults,
        string? path,
        string? displayName,
        DateTimeOffset timestamp)
    {
        if (vaults is null || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var target = path.Trim();
        var stamp = UtcStamp(timestamp);
        var previouslyAdded = Find(vaults, target);
        var addedAtUtc = previouslyAdded?.AddedAtUtc;
        if (string.IsNullOrEmpty(addedAtUtc))
        {
            addedAtUtc = stamp;
        }

        Forget(vaults, target);
        // Newest first is the list's own order, not something read back off the clock: two files
        // opened inside one clock tick would otherwise trade places between one run and the next.
        vaults.Insert(0, new KeePassRecentVaultSetting
        {
            Path = target,
            DisplayName = DisplayNameFor(displayName, target),
            AddedAtUtc = addedAtUtc,
            LastOpenedAtUtc = stamp
        });
    }

    public static void Forget(List<KeePassRecentVaultSetting>? vaults, string? path)
    {
        if (vaults is null || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var target = path.Trim();
        vaults.RemoveAll(item => string.Equals(item?.Path, target, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<KeePassRecentVaultSetting> Ordered(IEnumerable<KeePassRecentVaultSetting?>? vaults)
    {
        var byPath = new Dictionary<string, KeePassRecentVaultSetting>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in vaults ?? [])
        {
            if (string.IsNullOrWhiteSpace(item?.Path))
            {
                continue;
            }

            var target = item.Path.Trim();
            var candidate = item with
            {
                Path = target,
                DisplayName = DisplayNameFor(item.DisplayName, target)
            };
            if (byPath.TryGetValue(target, out var kept))
            {
                // The row that survives a duplicate keeps the name it was first shown under and the
                // span between its earliest arrival and its latest opening.
                byPath[target] = kept with
                {
                    AddedAtUtc = EarlierKnown(kept.AddedAtUtc, candidate.AddedAtUtc),
                    LastOpenedAtUtc = Later(kept.LastOpenedAtUtc, candidate.LastOpenedAtUtc)
                };
                continue;
            }

            byPath[target] = candidate;
        }

        return [.. byPath.Values.Take(Limit)];
    }

    /// <summary>
    /// Windows resolves these paths without regard to case, so two spellings of one file are one
    /// row here rather than two rows pointing at the same vault.
    /// </summary>
    public static KeePassRecentVaultSetting? Find(
        IEnumerable<KeePassRecentVaultSetting?>? vaults,
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var target = path.Trim();
        return vaults?
            .FirstOrDefault(item => string.Equals(item?.Path, target, StringComparison.OrdinalIgnoreCase));
    }

    private static string DisplayNameFor(string? displayName, string path) =>
        string.IsNullOrWhiteSpace(displayName) ? System.IO.Path.GetFileName(path) : displayName.Trim();

    private static string UtcStamp(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().UtcDateTime.ToString("o", CultureInfo.InvariantCulture);

    private static string EarlierKnown(string left, string right) =>
        left.Length == 0 ? right :
        right.Length == 0 ? left :
        string.CompareOrdinal(left, right) <= 0 ? left : right;

    private static string Later(string left, string right) =>
        string.CompareOrdinal(left, right) >= 0 ? left : right;
}
