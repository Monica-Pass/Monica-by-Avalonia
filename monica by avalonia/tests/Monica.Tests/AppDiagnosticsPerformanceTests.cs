using System.Diagnostics;

namespace Monica.Tests;

public sealed class AppDiagnosticsPerformanceTests
{
    [Fact]
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
}
