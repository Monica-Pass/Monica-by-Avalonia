using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.App.Services;

/// <summary>
/// Picks the vault entry an auto-type hotkey should send into the window that currently has focus.
/// The only signal available is the foreground window's title text, so an entry matches when one of
/// the host labels stored on it appears as a whole word in that text. Exactly one match types
/// straight away; anything else hands the choice back to the user as a list instead of guessing,
/// because a wrong credential landing in a live form (a payment field, a support chat) is worse than
/// one extra click.
/// </summary>
internal static class AutoTypeMatcher
{
    public static IReadOnlyList<PasswordEntry> Match(IEnumerable<PasswordEntry> entries, string windowText)
    {
        var words = SplitWords(windowText);
        if (words.Count == 0)
        {
            return [];
        }

        var titleHosts = CollectHostTokens(windowText);

        return entries
            .Where(IsTypeable)
            .Where(entry => EntryMatches(entry.Website, words, titleHosts))
            .ToArray();
    }

    /// <summary>
    /// Entries the hotkey could send into a form, which is the list the picker falls back to when the
    /// window title matched nothing. Title order keeps the list stable while the filter box is typed in.
    /// </summary>
    public static IReadOnlyList<AutoTypeCandidate> Candidates(IEnumerable<PasswordEntry> entries) =>
        entries
            .Where(IsTypeable)
            .OrderBy(entry => entry.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(CandidateOf)
            .ToArray();

    public static AutoTypeCandidate CandidateOf(PasswordEntry entry) =>
        new(entry.Id, entry.Title, entry.Username);

    private static bool IsTypeable(PasswordEntry entry) =>
        !entry.IsDeleted &&
        !entry.IsArchived &&
        (!string.IsNullOrWhiteSpace(entry.Username) || !string.IsNullOrWhiteSpace(entry.Password));

    public static IReadOnlyList<AutoTypeToken> BuildTokens(PasswordEntry entry)
    {
        var tokens = new List<AutoTypeToken>(3);
        var hasUsername = !string.IsNullOrWhiteSpace(entry.Username);
        var hasPassword = !string.IsNullOrWhiteSpace(entry.Password);
        if (hasUsername)
        {
            tokens.Add(AutoTypeToken.Text(entry.Username));
        }

        if (hasUsername && hasPassword)
        {
            // Tab moves to the next focused control instead of typing a literal tab, which is how a
            // username/password pair is reached in one pass. Enter is deliberately never sent: the
            // user submits the form themselves.
            tokens.Add(AutoTypeToken.Tab);
        }

        if (hasPassword)
        {
            tokens.Add(AutoTypeToken.Text(entry.Password));
        }

        return tokens;
    }

    internal static IReadOnlyList<string> HostLabels(string websites) =>
        BrowserCredentialMatcher.GetWebHosts(websites)
            .SelectMany(HostLabelsOf)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool EntryMatches(string websites, HashSet<string> words, IReadOnlyList<string> titleHosts)
    {
        var storedHosts = BrowserCredentialMatcher.GetWebHosts(websites);
        if (storedHosts.Count == 0)
        {
            return false;
        }

        // A title that names a host at all is held to that host: "github.com.phishing.test" must not
        // be read as GitHub just because the word github appears in it.
        if (titleHosts.Count > 0)
        {
            return storedHosts.Any(stored => titleHosts.Any(title => TitleHostCoversStoredHost(title, stored)));
        }

        foreach (var label in storedHosts.SelectMany(HostLabelsOf))
        {
            if (LabelAppearsInWords(label, words))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TitleHostCoversStoredHost(string titleHost, string storedHost) =>
        titleHost.Equals(storedHost, StringComparison.OrdinalIgnoreCase) ||
        titleHost.EndsWith("." + storedHost, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Pulls the host-shaped runs out of a window title ("mail.google.com", not "1.2" or "v2.0").
    /// Browsers put the origin in the title bar, so when one is present it is a far better signal
    /// than the page's own words, which the page itself chooses.
    /// </summary>
    internal static IReadOnlyList<string> CollectHostTokens(string value)
    {
        var hosts = new List<string>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return hosts;
        }

        var start = -1;
        for (var index = 0; index <= value.Length; index++)
        {
            var current = index < value.Length ? value[index] : '\0';
            var isPart = current is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '.' or '-';
            if (isPart)
            {
                if (start < 0)
                {
                    start = index;
                }

                continue;
            }

            if (start >= 0)
            {
                var run = value[start..index].Trim('.', '-');
                if (IsHostShape(run))
                {
                    hosts.Add(run);
                }
            }

            start = -1;
        }

        return hosts;
    }

    private static bool IsHostShape(string run)
    {
        if (run.Length < 4 || run.IndexOf('.') < 0 || !run.Any(char.IsLetter))
        {
            return false;
        }

        // Requiring an alphabetic final label keeps version numbers and dates out of the host list;
        // without it "Monica 2.0 release" would look like a host and suppress the word match.
        var lastDot = run.LastIndexOf('.');
        var topLabel = run[(lastDot + 1)..];
        return topLabel.Length is >= 2 and <= 24 && topLabel.All(char.IsLetter);
    }

    private static IEnumerable<string> HostLabelsOf(string host)
    {
        var labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (labels.Length == 0)
        {
            return [];
        }

        // "accounts.example.com" carries the service name and the organisation; the public suffix
        // ("com") is dropped so a title cannot match on it alone.
        return labels.Length == 1
            ? [labels[0]]
            : [labels[0], labels[^2]];
    }

    private static bool LabelAppearsInWords(string label, HashSet<string> words)
    {
        var segments = label.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length > 0 &&
               segments.All(segment => segment.Length > 1 && words.Contains(segment));
    }

    private static HashSet<string> SplitWords(string value)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
        {
            return words;
        }

        // Anything that is not a letter or a digit separates words, which covers the dashes,
        // brackets and pipes window titles are built from without keeping a punctuation table.
        var start = -1;
        for (var index = 0; index <= value.Length; index++)
        {
            var isPart = index < value.Length && char.IsLetterOrDigit(value[index]);
            if (isPart)
            {
                if (start < 0)
                {
                    start = index;
                }

                continue;
            }

            if (start >= 0 && index - start > 1)
            {
                words.Add(value[start..index]);
            }

            start = -1;
        }

        return words;
    }
}
