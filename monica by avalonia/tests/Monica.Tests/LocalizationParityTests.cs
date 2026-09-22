using System.Reflection;
using Monica.App.Services;

namespace Monica.Tests;

public sealed class LocalizationParityTests
{
    // LocalizationService.Get falls back to the English table and then to the raw key,
    // so a missing translation never throws — it silently renders English in a Chinese UI.
    [Fact]
    public void Chinese_table_covers_every_English_key()
    {
        var missing = LocalizationService.English.Keys
            .Where(key => !LocalizationService.Chinese.ContainsKey(key))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"{missing.Length} key(s) missing from the Chinese table: {string.Join(", ", missing)}");
    }

    [Fact]
    public void English_table_covers_every_Chinese_key()
    {
        var missing = LocalizationService.Chinese.Keys
            .Where(key => !LocalizationService.English.ContainsKey(key))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"{missing.Length} key(s) missing from the English table: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Every_localization_property_resolves_to_a_table_key()
    {
        var keys = new HashSet<string>(
            LocalizationService.English.Keys.Concat(LocalizationService.Chinese.Keys),
            StringComparer.Ordinal);

        // The typed accessors read their own name through CallerMemberName, so a property
        // with no entry in either table shows its identifier on screen.
        var nonTranslatable = new HashSet<string>(StringComparer.Ordinal)
        {
            "Item", // the string this[key] indexer
            "SelectedLanguage", // reports the active culture code, not UI text
        };

        var unresolved = typeof(ILocalizationService)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => property.Name)
            .Where(name => !nonTranslatable.Contains(name))
            .Where(name => !keys.Contains(name))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unresolved.Length == 0,
            $"{unresolved.Length} accessor(s) with no dictionary key: {string.Join(", ", unresolved)}");
    }

    [Fact]
    public void Every_referenced_localization_key_exists_in_a_table()
    {
        var keys = new HashSet<string>(
            LocalizationService.English.Keys.Concat(LocalizationService.Chinese.Keys),
            StringComparer.Ordinal);
        var sourceRoot = FindSourceRoot();
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        var unresolved = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in sourceRoot.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (file.Extension is not (".cs" or ".axaml") ||
                file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            foreach (var key in ReferencedKeys(file.Extension == ".cs" ? CsKeys : XamlKeys, file))
            {
                referenced.Add(key);
                if (!keys.Contains(key))
                {
                    unresolved.Add($"{key} ({Path.GetRelativePath(sourceRoot.FullName, file.FullName)})");
                }
            }
        }

        // Guards the guard: patterns that stop matching would otherwise pass with nothing scanned.
        Assert.True(
            referenced.Count >= 1000,
            $"Only {referenced.Count} localization key reference(s) scanned; the patterns have drifted.");

        Assert.True(
            unresolved.Count == 0,
            $"{unresolved.Count} referenced key(s) with no dictionary entry: {string.Join(", ", unresolved)}");
    }

    private static readonly System.Text.RegularExpressions.Regex[] CsKeys =
    [
        // The receiver whitelist keeps String.Format's format-string argument out of the results.
        new(@"\b(?:_?localization|L)\.(?:Get|Format)\(\s*""([A-Za-z][\w.\-]*)""",
            System.Text.RegularExpressions.RegexOptions.Compiled),
        // Keys also travel as bare arguments to failure-reporting helpers, where no Get() call
        // marks them out. The suffix set is what makes the shape specific enough to scan for.
        new(
            @"""([A-Z][A-Za-z0-9]*(?:Failed|Error|Unavailable|Unsupported|Required|Missing|Invalid" +
            @"|Denied|Mismatch|NotFound|Removed|Exists|Exceeded|Refused|TimedOut))""",
            System.Text.RegularExpressions.RegexOptions.Compiled),
    ];

    private static readonly System.Text.RegularExpressions.Regex[] XamlKeys =
    [
        new(@"\bL\[([A-Za-z][\w.\-]*)\]", System.Text.RegularExpressions.RegexOptions.Compiled),
        new(@"\bL\.([A-Za-z][\w.\-]*)", System.Text.RegularExpressions.RegexOptions.Compiled),
    ];

    private static IEnumerable<string> ReferencedKeys(
        System.Text.RegularExpressions.Regex[] patterns,
        FileInfo file)
    {
        foreach (var line in File.ReadLines(file.FullName))
        {
            foreach (var pattern in patterns)
            {
                foreach (System.Text.RegularExpressions.Match match in pattern.Matches(line))
                {
                    yield return match.Groups[1].Value;
                }
            }
        }
    }

    private static DirectoryInfo FindSourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src");
            if (File.Exists(Path.Combine(candidate, "Monica.App", "Services", "LocalizationService.cs")))
            {
                return new DirectoryInfo(candidate);
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate the src directory from " + AppContext.BaseDirectory +
            "; the key-reference guard needs the source tree.");
    }
}
