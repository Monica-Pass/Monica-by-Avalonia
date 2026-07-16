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

    [Fact]
    public async Task Security_baseline_inflight_cleanup_survives_concurrent_disposal()
    {
        var adapter = new BlockingClipboardAdapter();
        var clipboard = new SecureClipboardService(adapter);
        await clipboard.SetSensitiveTextAsync("owned-secret");

        var cleanup = clipboard.ClearOwnedContentAsync();
        await adapter.GetTextStarted.WaitAsync(TimeSpan.FromSeconds(2));

        clipboard.Dispose();
        adapter.ReleaseGetText();

        await cleanup;
        Assert.Null(adapter.Text);
    }

    [Fact]
    public async Task Security_baseline_cleanup_after_disposal_is_safe_noop()
    {
        var adapter = new MemoryClipboardAdapter { Text = "external-content" };
        var clipboard = new SecureClipboardService(adapter);

        clipboard.Dispose();
        clipboard.Dispose();
        await clipboard.ClearOwnedContentAsync();

        Assert.Equal("external-content", adapter.Text);
        Assert.Equal(0, adapter.GetTextCallCount);
    }

    [Fact]
    public async Task Security_baseline_write_after_disposal_is_rejected()
    {
        var clipboard = new SecureClipboardService(new MemoryClipboardAdapter());
        clipboard.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => clipboard.SetTextAsync("late-write"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => clipboard.SetSensitiveTextAsync("late-secret"));
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

    private sealed class BlockingClipboardAdapter : IClipboardAdapter
    {
        private readonly TaskCompletionSource _getTextStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseGetText =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string? Text { get; private set; }
        public Task GetTextStarted => _getTextStarted.Task;

        public async Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
        {
            _getTextStarted.TrySetResult();
            await _releaseGetText.Task.WaitAsync(cancellationToken);
            return Text;
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

        public void ReleaseGetText() => _releaseGetText.TrySetResult();
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
