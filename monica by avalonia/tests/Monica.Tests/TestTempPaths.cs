using System.Runtime.CompilerServices;

namespace Monica.Tests;

/// <summary>
/// Owns temporary files created by the test assembly so SQLite WAL/SHM and MDBX
/// sidecars cannot accumulate in the shared system temp directory.
/// </summary>
internal static class TestTempPaths
{
    private const string RootName = "monica-tests";
    private static readonly string ParentPath = Path.Combine(Path.GetTempPath(), RootName);
    private static readonly string ParentPrefix = Path.TrimEndingDirectorySeparator(
        Path.GetFullPath(ParentPath)) + Path.DirectorySeparatorChar;
    private static readonly string CleanupManifestPath = Path.Combine(
        AppContext.BaseDirectory,
        "monica-test-temp-roots.txt");
    private static readonly string RootPath = Path.Combine(
        ParentPath,
        $"{Environment.ProcessId}-{Guid.NewGuid():N}");

    static TestTempPaths()
    {
        CleanupStaleRoots();
        Directory.CreateDirectory(RootPath);
        File.AppendAllText(CleanupManifestPath, RootPath + Environment.NewLine);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Cleanup();
    }

    public static string Root => EnsureRoot();

    public static string CreateFilePath(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        if (!extension.StartsWith(".", StringComparison.Ordinal))
        {
            extension = $".{extension}";
        }

        return Path.Combine(EnsureRoot(), $"{Guid.NewGuid():N}{extension}");
    }

    public static string CreateDirectoryPath()
    {
        var path = Path.Combine(EnsureRoot(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CleanupStaleRoots()
    {
        try
        {
            if (!Directory.Exists(ParentPath))
            {
                return;
            }

            var cutoff = DateTime.UtcNow - TimeSpan.FromDays(1);
            foreach (var path in Directory.EnumerateDirectories(ParentPath))
            {
                if (string.Equals(path, RootPath, StringComparison.OrdinalIgnoreCase) ||
                    Directory.GetLastWriteTimeUtc(path) > cutoff)
                {
                    continue;
                }

                DeleteOwnedDirectory(path);
            }
        }
        catch
        {
            // Stale cleanup is best effort and must not prevent tests from running.
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string EnsureRoot()
    {
        Directory.CreateDirectory(RootPath);
        return RootPath;
    }

    private static void Cleanup()
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                DeleteOwnedDirectory(RootPath);

                RemoveEmptyParent();
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(100 * (attempt + 1));
            }
            catch (UnauthorizedAccessException) when (attempt < 5)
            {
                Thread.Sleep(100 * (attempt + 1));
            }
            catch
            {
                return;
            }
        }
    }

    internal static void DeleteOwnedDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(ParentPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to delete a directory outside the Monica test temp root.");
        }

        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }

    private static void RemoveEmptyParent()
    {
        try
        {
            if (Directory.Exists(ParentPath) && !Directory.EnumerateFileSystemEntries(ParentPath).Any())
            {
                Directory.Delete(ParentPath);
            }
        }
        catch
        {
            // Cleanup must never mask the test result during process shutdown.
        }
    }
}
