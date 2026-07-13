using Monica.Data.Diagnostics;

namespace Monica.Tests;

public sealed class VaultBenchmarkRunnerTests
{
    [Fact]
    public async Task Runner_creates_measures_and_removes_deterministic_fixture()
    {
        var result = await VaultBenchmarkRunner.RunAsync(new VaultBenchmarkOptions(100));

        Assert.Equal("monica-vault-benchmark-v1", result.Schema);
        Assert.Equal(100, result.RequestedEntryCount);
        Assert.Equal(70, result.LoadedPasswordCount);
        Assert.Equal(30, result.LoadedSecureItemCount);
        Assert.Equal(50, result.SearchIterations);
        Assert.Equal(50, result.SearchMatches);
        Assert.True(result.DatabaseBytes > 0);
        Assert.False(result.DatabaseRetained);
        Assert.False(File.Exists(result.DatabasePath));
        Assert.True(result.Timings.InitializeMilliseconds >= 0);
        Assert.True(result.Timings.ColdLoadMilliseconds >= 0);
        Assert.True(result.Timings.CreateMilliseconds >= 0);
        Assert.True(result.Timings.UpdateMilliseconds >= 0);
        Assert.True(result.Timings.DeleteMilliseconds >= 0);
    }
}
