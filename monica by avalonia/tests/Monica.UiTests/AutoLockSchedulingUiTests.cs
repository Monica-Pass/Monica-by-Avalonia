using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.ViewModels;
using Monica.Core.Services;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class AutoLockSchedulingUiTests
{
    public AutoLockSchedulingUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Auto_lock_scheduler_only_runs_for_unlocked_enabled_session()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();

        Assert.False(window.IsAutoLockCheckScheduled);

        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.AutoLockMinutes = 5;
            viewModel.AutoLockEnabled = true;
            viewModel.IsUnlocked = true;
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.IsAutoLockCheckScheduled);
            Assert.InRange(
                window.AutoLockCheckInterval,
                TimeSpan.FromMinutes(4.9),
                TimeSpan.FromMinutes(5));

            var scheduleCount = window.AutoLockScheduleCount;
            viewModel.RecordUserActivity();
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.AutoLockScheduleCount > scheduleCount);

            viewModel.AutoLockEnabled = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsAutoLockCheckScheduled);

            viewModel.AutoLockEnabled = true;
            viewModel.AutoLockMinutes = 1;
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.IsAutoLockCheckScheduled);
            Assert.InRange(
                window.AutoLockCheckInterval,
                TimeSpan.FromSeconds(59),
                TimeSpan.FromMinutes(1));

            viewModel.IsUnlocked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsAutoLockCheckScheduled);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public async Task Auto_lock_scheduler_locks_at_monotonic_deadline()
    {
        var timeProvider = new ManualTimeProvider();
        using var session = new VaultSessionService(timeProvider);
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(
            window,
            overrides => overrides.AddSingleton<IVaultSessionService>(session));
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.AutoLockMinutes = 1;
            viewModel.AutoLockEnabled = true;
            viewModel.IsUnlocked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.IsAutoLockCheckScheduled);

            timeProvider.Advance(TimeSpan.FromMinutes(2));
            await window.RunScheduledAutoLockCheckAsync();

            Assert.False(viewModel.IsUnlocked);
            Assert.False(window.IsAutoLockCheckScheduled);
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
