namespace Monica.Tests;

public sealed class DesktopPublishingTests
{
    [Fact]
    public void Jit_desktop_publish_enables_ready_to_run_without_changing_aot_mode()
    {
        var script = File.ReadAllText(FindRepositoryFile("eng", "ci", "publish-desktop.ps1"));

        Assert.Contains(
            "$publishReadyToRun = if ($Mode -eq 'jit') { 'true' } else { 'false' }",
            script,
            StringComparison.Ordinal);
        Assert.Contains("/p:PublishReadyToRun=$publishReadyToRun", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Direct_jit_publish_defaults_to_ready_to_run()
    {
        var project = File.ReadAllText(
            FindRepositoryFile("src", "Monica.App", "Monica.App.csproj"));

        Assert.Contains(
            "<PublishReadyToRun Condition=\"'$(PublishAot)' != 'true' and " +
            "'$(PublishReadyToRun)' == ''\">true</PublishReadyToRun>",
            project,
            StringComparison.Ordinal);
    }

    // A referenced project builds without the publish's RID, so the engine copy has to be told which
    // platform it is shipping for; without this a linux-x64 artifact carried the Windows PE.
    [Fact]
    public void Desktop_publish_selects_the_native_engine_for_its_own_rid()
    {
        var script = File.ReadAllText(FindRepositoryFile("eng", "ci", "publish-desktop.ps1"));

        Assert.Contains("/p:MonicaNativeRid=$RuntimeIdentifier", script, StringComparison.Ordinal);
    }

    // Loose at that root means every artifact on every platform copies it.
    [Fact]
    public void Native_engines_live_in_one_folder_per_runtime_identifier()
    {
        var project = FindRepositoryFile("src", "Monica.Platform", "Monica.Platform.csproj");
        var runtimes = Path.Combine(Path.GetDirectoryName(project)!, "Mdbx", "runtimes");

        foreach (var looseFile in Directory.EnumerateFiles(runtimes))
        {
            Assert.EndsWith(".gitkeep", looseFile, StringComparison.Ordinal);
        }

        foreach (var rid in new[] { "win-x64", "linux-x64", "osx-x64", "osx-arm64" })
        {
            Assert.True(
                Directory.Exists(Path.Combine(runtimes, rid)),
                $"Mdbx/runtimes/{rid} is a published runtime and needs its own folder for the engine.");
        }
    }

    private static string FindRepositoryFile(params string[] pathSegments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. pathSegments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(pathSegments)}.");
    }
}
