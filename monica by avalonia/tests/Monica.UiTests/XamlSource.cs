namespace Monica.UiTests;

/// Locates desktop source files by file name instead of by folder, so that moving or merging a
/// view does not break the tests that describe it. File names are unique under src/Monica.App,
/// and <see cref="SourcePaths"/> enforces that so a collision fails loudly.
///
/// Rule for assertions: a positive claim about the UI ("this control exists", "this command is
/// wired") must go through the instantiated visual tree (preferred) or <see cref="All"/> /
/// <see cref="ContainsAnywhere"/>. Naming one file is only legitimate for a negative claim
/// ("this file must not contain a hardcoded colour").
internal static class XamlSource
{
    private static readonly Lazy<Dictionary<string, string>> Paths = new(BuildPaths, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<Dictionary<string, string>> Texts = new(
        () => Paths.Value.ToDictionary(pair => pair.Key, pair => File.ReadAllText(pair.Value), StringComparer.Ordinal),
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// Every desktop source file, keyed by file name.
    public static IReadOnlyDictionary<string, string> All => Texts.Value;

    public static string Text(string fileName) => File.ReadAllText(Resolve(fileName));

    /// Full path of a source file located by name. Prefer <see cref="Text"/>; this exists so tests
    /// that need a path (publish scripts, project files) keep working after a view moves.
    public static string PathOf(string fileName) => Resolve(fileName);

    /// Source of the <c>.axaml</c> belonging to a view type. Renaming the type breaks compilation,
    /// which is a harder guard than a path scan.
    public static string TextFor<TView>() => Text(typeof(TView).Name + ".axaml");

    public static string TextOfType<TType>() => Text(typeof(TType).Name + ".cs");

    /// File names of every desktop source file containing <paramref name="fragment"/>. Use for
    /// positive structural claims so the assertion survives whichever view hosts them today.
    public static IReadOnlyList<string> ContainsAnywhere(string fragment) =>
        All.Where(pair => pair.Value.Contains(fragment, StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

    public static bool AnyContains(string fragment) => ContainsAnywhere(fragment).Count > 0;

    public static IReadOnlyList<string> InDirectory(params string[] relativeDirectory)
    {
        var directory = AppRelativePath(relativeDirectory);
        return [.. Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))];
    }

    /// This test project's own sources, so a guard can police the tests without each file writing
    /// its own directory walk.
    public static IReadOnlyList<string> TestSources()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "Monica.UiTests");
            if (Directory.Exists(candidate))
            {
                return [.. Directory.EnumerateFiles(candidate, "*.cs", SearchOption.AllDirectories)];
            }
        }

        throw new DirectoryNotFoundException("Could not locate tests/Monica.UiTests.");
    }

    public static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string Resolve(string fileName) =>
        Paths.Value.TryGetValue(fileName, out var path)
            ? path
            : throw new FileNotFoundException(
                $"No {fileName} under src/Monica.App. If the view was merged into another one, assert " +
                "against the instantiated visual tree or XamlSource.ContainsAnywhere instead of one file.");

    private static string AppRelativePath(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, "src", "Monica.App", .. parts]);
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', parts)} under src/Monica.App.");
    }

    private static Dictionary<string, string> BuildPaths()
    {
        var root = AppRelativePath();
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        var collisions = new List<string>();
        foreach (var path in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            if (!path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileName = Path.GetFileName(path);
            if (paths.TryGetValue(fileName, out var existing))
            {
                collisions.Add($"{fileName}: {existing} and {path}");
                continue;
            }

            paths[fileName] = path;
        }

        if (collisions.Count > 0)
        {
            throw new InvalidOperationException(
                "By-name source lookups need unique file names under src/Monica.App. Duplicates: " +
                string.Join("; ", collisions));
        }

        return paths;
    }
}
