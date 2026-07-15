using Monica.Core.Services;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed class SecurityBaselineTests
{
    [Fact]
    public void Security_baseline_session_uses_monotonic_activity_time()
    {
        var timeProvider = new ManualTimeProvider();
        var session = new VaultSessionService(timeProvider);

        session.MarkUnlocked();
        timeProvider.Advance(TimeSpan.FromMinutes(4));
        session.RecordActivity();
        timeProvider.Advance(TimeSpan.FromMinutes(4));

        Assert.False(session.IsExpired(TimeSpan.FromMinutes(5)));

        timeProvider.Advance(TimeSpan.FromMinutes(2));

        Assert.True(session.IsExpired(TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Security_baseline_session_reports_monotonic_remaining_inactivity()
    {
        var timeProvider = new ManualTimeProvider();
        var session = new VaultSessionService(timeProvider);

        Assert.Null(session.GetRemainingInactivity(TimeSpan.FromMinutes(5)));
        session.MarkUnlocked();
        Assert.Equal(TimeSpan.FromMinutes(5), session.GetRemainingInactivity(TimeSpan.FromMinutes(5)));

        timeProvider.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(TimeSpan.FromMinutes(3), session.GetRemainingInactivity(TimeSpan.FromMinutes(5)));

        session.RecordActivity();
        Assert.Equal(TimeSpan.FromMinutes(5), session.GetRemainingInactivity(TimeSpan.FromMinutes(5)));

        timeProvider.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(TimeSpan.Zero, session.GetRemainingInactivity(TimeSpan.FromMinutes(5)));
        session.MarkLocked();
        Assert.Null(session.GetRemainingInactivity(TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task Security_baseline_new_sensitive_copy_cancels_previous_expiry()
    {
        var adapter = new MemoryClipboardAdapter();
        var scheduler = new ManualClipboardExpiryScheduler();
        var clipboard = new SecureClipboardService(adapter, scheduler);
        clipboard.ConfigureSensitiveClear(TimeSpan.FromSeconds(30));

        await clipboard.SetSensitiveTextAsync("first-secret");
        var firstExpiry = Assert.Single(scheduler.Delays);
        await clipboard.SetSensitiveTextAsync("second-secret");

        Assert.True(firstExpiry.CancellationToken.IsCancellationRequested);
        Assert.Equal("second-secret", adapter.Text);
        Assert.Equal(2, scheduler.Delays.Count);
    }

    [Fact]
    public async Task Security_baseline_expiry_clears_only_owned_content()
    {
        var adapter = new MemoryClipboardAdapter();
        var scheduler = new ManualClipboardExpiryScheduler();
        var clipboard = new SecureClipboardService(adapter, scheduler);
        clipboard.ConfigureSensitiveClear(TimeSpan.FromSeconds(30));

        await clipboard.SetSensitiveTextAsync("monica-secret");
        adapter.Text = "content-from-another-application";
        await scheduler.ReleaseAsync(0);
        await WaitUntilAsync(() => adapter.GetTextCallCount == 1);

        Assert.Equal("content-from-another-application", adapter.Text);

        await clipboard.SetSensitiveTextAsync("owned-secret");
        await scheduler.ReleaseAsync(1);
        await WaitUntilAsync(() => adapter.Text is null);

        Assert.Null(adapter.Text);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.True(predicate());
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }

    private sealed class MemoryClipboardAdapter : IClipboardAdapter
    {
        public string? Text { get; set; }
        public int GetTextCallCount { get; private set; }

        public Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
        {
            GetTextCallCount++;
            return Task.FromResult(Text);
        }

        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            Text = text;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Text = null;
            return Task.CompletedTask;
        }
    }

    private sealed class ManualClipboardExpiryScheduler : IClipboardExpiryScheduler
    {
        public List<PendingDelay> Delays { get; } = [];

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            Delays.Add(new PendingDelay(completion, cancellationToken));
            return completion.Task;
        }

        public async Task ReleaseAsync(int index)
        {
            Delays[index].Completion.TrySetResult();
            for (var attempt = 0; attempt < 20; attempt++)
            {
                await Task.Yield();
            }
        }
    }

    private sealed record PendingDelay(
        TaskCompletionSource Completion,
        CancellationToken CancellationToken);
}
