using System.Diagnostics;

namespace Monica.Tests;

public sealed class AppDiagnosticsPerformanceTests
{
    [Fact]
    [Trait("Category", "perf-budget")]
    public void Performance_budget_diagnostics_do_not_block_the_caller_on_file_io()
    {
        Monica.App.AppDiagnostics.Info("Performance diagnostic warmup");
        var stopwatch = Stopwatch.StartNew();

        for (var index = 0; index < 1_000; index++)
        {
            Monica.App.AppDiagnostics.Info($"Performance diagnostic event {index}");
        }

        stopwatch.Stop();
        Assert.True(
            stopwatch.ElapsedMilliseconds < 50,
            $"Enqueuing 1,000 diagnostic events took {stopwatch.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public void Diagnostic_log_roll_moves_an_overlong_log_into_one_backup()
    {
        var path = TestTempPaths.CreateFilePath(".log");
        File.WriteAllText(path, new string('a', 4_096));

        Monica.App.AppDiagnostics.RollLogIfOverlong(path, maxBytes: 2_048, maxBackupBytes: 8_192);

        Assert.False(File.Exists(path));
        Assert.Equal(4_096, new FileInfo(path + ".1").Length);
    }

    [Fact]
    public void Diagnostic_log_roll_discards_a_log_past_the_backup_ceiling()
    {
        var path = TestTempPaths.CreateFilePath(".log");
        File.WriteAllText(path, new string('a', 64 * 1_024));

        Monica.App.AppDiagnostics.RollLogIfOverlong(path, maxBytes: 2_048, maxBackupBytes: 8_192);

        // Rotating this into the backup would have preserved the disk usage the cap exists to bound.
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".1"));
    }

    [Fact]
    public void Diagnostic_log_roll_leaves_a_log_within_the_cap_alone()
    {
        var path = TestTempPaths.CreateFilePath(".log");
        File.WriteAllText(path, "short");

        Monica.App.AppDiagnostics.RollLogIfOverlong(path, maxBytes: 2_048, maxBackupBytes: 8_192);

        Assert.Equal("short", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".1"));
    }

    [Fact]
    public void Diagnostic_log_roll_replaces_the_previous_backup()
    {
        var path = TestTempPaths.CreateFilePath(".log");
        File.WriteAllText(path + ".1", new string('b', 3_000));
        File.WriteAllText(path, new string('a', 4_000));

        Monica.App.AppDiagnostics.RollLogIfOverlong(path, maxBytes: 2_048, maxBackupBytes: 8_192);

        Assert.Equal(new string('a', 4_000), File.ReadAllText(path + ".1"));
    }
}
