namespace Monica.Data;

public static class MonicaAppDataPaths
{
    public const string OverrideEnvironmentVariable = "MONICA_APPDATA_DIR";
    public const string DefaultApplicationDataDirectoryName = "Monica by Avalonia";
    public const string DefaultDatabaseFileName = "monica.db";

    // Owner-only: rwx------.
    private const UnixFileMode PrivateDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const UnixFileMode SharedBits =
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    private static string? s_securedRoot;

    public static string GetRootDirectory()
    {
        var overrideRoot = Environment.GetEnvironmentVariable(OverrideEnvironmentVariable);
        var root = string.IsNullOrWhiteSpace(overrideRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                DefaultApplicationDataDirectoryName)
            : ExpandOverride(overrideRoot);
        root = Path.GetFullPath(root);
        if (!string.Equals(root, Volatile.Read(ref s_securedRoot), StringComparison.Ordinal))
        {
            EnsurePrivateDirectory(root);
            Volatile.Write(ref s_securedRoot, root);
        }

        return root;
    }

    public static string GetDatabasePath() =>
        Path.Combine(GetRootDirectory(), DefaultDatabaseFileName);

    public static string GetPath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(GetRootDirectory(), normalized);
    }

    /// <summary>
    /// Expands <c>%VAR%</c> everywhere and, on Linux and macOS, a leading <c>~</c>, which shells do not
    /// expand inside quotes and <see cref="Environment.ExpandEnvironmentVariables"/> never does: without
    /// it <c>MONICA_APPDATA_DIR="~/vault"</c> silently became a directory named "~" under the working
    /// directory.
    /// </summary>
    public static string ExpandOverride(string value)
    {
        var expanded = Environment.ExpandEnvironmentVariables(value.Trim());
        if (OperatingSystem.IsWindows() ||
            !(expanded == "~" || expanded.StartsWith("~/", StringComparison.Ordinal)))
        {
            return expanded;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(home) ? expanded : home + expanded[1..];
    }

    /// <summary>
    /// The vault, its metadata database (which holds the master-password verifier), logs and caches
    /// all live under the data directory. Under the usual umask Unix creates it as 0755, so any local
    /// account could list it and copy monica.db for an offline guessing attack. Owner-only access on
    /// the directory covers every file inside, whatever mode each file was created with.
    /// </summary>
    public static void EnsurePrivateDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
            return;
        }

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory, PrivateDirectoryMode);
            return;
        }

        try
        {
            var mode = File.GetUnixFileMode(directory);
            if ((mode & SharedBits) != 0)
            {
                File.SetUnixFileMode(directory, mode & ~SharedBits);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // A directory this account does not own (a deliberately shared MONICA_APPDATA_DIR) keeps
            // the mode its owner chose; refusing to start over it would help nobody.
        }
    }
}
