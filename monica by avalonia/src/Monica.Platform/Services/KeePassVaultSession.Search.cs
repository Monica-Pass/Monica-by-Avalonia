using KeePassLib;
using KeePassLib.Security;
using KeePassLib.Utility;

namespace Monica.Platform.Services;

public sealed partial class KeePassVaultSession
{
    /// <summary>
    /// How many hits a search hands back. The count of what it did not show travels with the list, so
    /// a screen can say "the first 200 of 1.234" rather than quietly truncating a password list.
    /// </summary>
    public const int SearchResultCap = 200;

    /// <summary>
    /// Finds entries by what a person would type: the title, the user name, the web address, the notes,
    /// a custom field, or the folder they sit in - the same field set the Android browser searches.
    /// Two things stay out of reach. A protected value is never read, so searching for a password does
    /// not find the entry holding it; a secret is only ever resolved for the one entry the user opened.
    /// Entries in the recycle bin stay out too, because a search that resurfaces something already
    /// deleted reads like a bug rather than a feature.
    /// </summary>
    public async Task<KeePassSearchResults> SearchEntriesAsync(
        string? query,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var root = _root ?? throw new ObjectDisposedException(nameof(KeePassVaultSession));
        var needle = query?.Trim() ?? "";
        if (needle.Length == 0)
        {
            return KeePassSearchResults.Empty;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var ranked = new List<(int Rank, KeePassEntryRow Row)>();
            var total = 0;
            CollectSearchHits(root, root, needle, "", ranked, ref total, cancellationToken);
            ranked.Sort(static (left, right) =>
            {
                var byRank = left.Rank.CompareTo(right.Rank);
                if (byRank != 0)
                {
                    return byRank;
                }

                var byTitle = string.Compare(
                    left.Row.Title,
                    right.Row.Title,
                    StringComparison.CurrentCultureIgnoreCase);
                return byTitle != 0
                    ? byTitle
                    : string.CompareOrdinal(left.Row.EntryUuid, right.Row.EntryUuid);
            });

            return new KeePassSearchResults(
                ranked.Take(SearchResultCap).Select(hit => hit.Row).ToArray(),
                total);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Walks the tree depth-first with each folder's display path already resolved, so a hit knows
    /// where it came from without a second climb up the parents. Cancellation is checked every 256
    /// entries rather than per entry, because checking 20,000 times costs more than the compare.
    /// </summary>
    private void CollectSearchHits(
        PwGroup group,
        PwGroup root,
        string needle,
        string path,
        List<(int Rank, KeePassEntryRow Row)> ranked,
        ref int total,
        CancellationToken cancellationToken)
    {
        if (IsInsideRecycleBin(group))
        {
            return;
        }

        var index = 0;
        foreach (var entry in group.Entries)
        {
            if ((index++ & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var rank = RankSearchHit(entry, path, needle);
            if (rank < 0)
            {
                continue;
            }

            total++;
            // Past the cap the walk keeps counting but stops building rows: the number on screen has to
            // be the number of matches, not the number of rows that happened to fit.
            if (ranked.Count < SearchResultCap)
            {
                ranked.Add((rank, CreateRow(entry, path)));
            }
        }

        foreach (var child in group.Groups)
        {
            CollectSearchHits(
                child,
                root,
                needle,
                KeePassVaultText.GroupPathOf(child, root),
                ranked,
                ref total,
                cancellationToken);
        }
    }

    /// <summary>
    /// Lower is nearer the top of the list: what a person typed is most often the name of the thing.
    /// Returns -1 for no match. The title is checked before the strings are walked, because it answers
    /// the common case without a second pass over every field of the entry.
    /// </summary>
    private static int RankSearchHit(PwEntry entry, string groupPath, string needle)
    {
        var title = entry.Strings.ReadSafe(PwDefs.TitleField);
        if (title.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (title.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        foreach (var field in entry.Strings)
        {
            if (!IsSearchableField(field.Key, field.Value))
            {
                continue;
            }

            if (field.Value.ReadString().Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            // The label counts as well, so a field someone named after the system it belongs to finds
            // the entry even when its value is empty. A protected field never gets this far: the Android
            // browser skips the whole field rather than only its value, and one rule is easier to
            // reason about than a field that is half searchable.
            if (KeePassVaultText.IsCustomField(field.Key) &&
                field.Key.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }
        }

        return groupPath.Contains(needle, StringComparison.OrdinalIgnoreCase) ? 3 : -1;
    }

    /// <summary>
    /// Whether a string may be read for a match. The password never is, whatever its flag; everything
    /// else is fair game only while it sits in the file as plain text.
    /// </summary>
    private static bool IsSearchableField(string name, ProtectedString value)
    {
        if (value.IsProtected)
        {
            return false;
        }

        return !name.Equals(PwDefs.PasswordField, StringComparison.OrdinalIgnoreCase);
    }
}
