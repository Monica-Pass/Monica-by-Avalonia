namespace Monica.Data;

public static class MonicaAppDataPaths
{
    public const string OverrideEnvironmentVariable = "MONICA_APPDATA_DIR";
    public const string DefaultApplicationDataDirectoryName = "Monica by Avalonia";
    public const string DefaultDatabaseFileName = "monica.db";

    public static string GetRootDirectory()
    {
        var overrideRoot = Environment.GetEnvironmentVariable(OverrideEnvironmentVariable);
        var root = string.IsNullOrWhiteSpace(overrideRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                DefaultApplicationDataDirectoryName)
            : Environment.ExpandEnvironmentVariables(overrideRoot.Trim());
        root = Path.GetFullPath(root);
        Directory.CreateDirectory(root);
        return root;
    }

    public static string GetDatabasePath() =>
        Path.Combine(GetRootDirectory(), DefaultDatabaseFileName);

    public static string GetPath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(GetRootDirectory(), normalized);
    }
}
