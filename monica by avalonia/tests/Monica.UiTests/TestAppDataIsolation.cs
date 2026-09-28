using System.Diagnostics;
using System.Runtime.CompilerServices;
using Monica.App.Services;
using Monica.Data;

namespace Monica.UiTests;

// App.ConfigureServices registers the production SqliteConnectionFactory, whose default database
// path comes from MonicaAppDataPaths. Without this the whole suite opens, migrates and writes the
// developer's real vault directory, so one run can destroy a live vault.
//
// Each root is named after the owning process so leftovers stay attributable. The exit handler has
// never been observed to remove the root even from a run that finishes normally, so the sweep of
// dead process ids is the mechanism that reclaims them, and a run costs at most one directory until
// the next run clears it.
internal static class TestAppDataIsolation
{
    private static readonly string ParentPath = Path.Combine(Path.GetTempPath(), "monica-uitests");

    private static readonly string RootPath = Path.Combine(
        ParentPath,
        Environment.ProcessId.ToString());

    [ModuleInitializer]
    internal static void IsolateAppData()
    {
        DeleteExitedRoots();
        Directory.CreateDirectory(RootPath);
        Environment.SetEnvironmentVariable(MonicaAppDataPaths.OverrideEnvironmentVariable, RootPath);
        // The suite asserts on rows the tree realizes, so a real request per host would make every
        // assertion wait on the network; the rows keep the type glyph they were written against.
        WebsiteIconCache.SetAutomatedRunNetworkAllowed(false);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TryDelete(RootPath);
    }

    private static void DeleteExitedRoots()
    {
        try
        {
            if (!Directory.Exists(ParentPath))
            {
                return;
            }

            foreach (var path in Directory.EnumerateDirectories(ParentPath))
            {
                if (int.TryParse(Path.GetFileName(path), out var processId)
                    && processId != Environment.ProcessId
                    && !IsRunning(processId))
                {
                    TryDelete(path);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool IsRunning(int processId)
    {
        try
        {
            Process.GetProcessById(processId).Dispose();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
