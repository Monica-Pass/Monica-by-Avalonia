using Avalonia.Threading;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class ShutdownLifecycleUiTests
{
    public ShutdownLifecycleUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public async Task Main_window_waits_for_shutdown_cleanup_before_final_close()
    {
        var cleanupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupCallCount = 0;
        var closeCount = 0;
        var window = new Monica.App.MainWindow
        {
            ShutdownRequestedAsync = async () =>
            {
                cleanupCallCount++;
                cleanupStarted.TrySetResult();
                await releaseCleanup.Task;
            }
        };
        window.Closed += (_, _) => closeCount++;
        window.Show();

        window.Close();
        await cleanupStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.True(window.IsVisible);
        Assert.Equal(0, closeCount);
        window.Close();
        Assert.Equal(1, cleanupCallCount);

        releaseCleanup.TrySetResult();
        for (var attempt = 0; attempt < 50 && closeCount == 0; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }

        Assert.Equal(1, closeCount);
        Assert.False(window.IsVisible);
    }
}
