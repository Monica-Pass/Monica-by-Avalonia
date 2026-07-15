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
