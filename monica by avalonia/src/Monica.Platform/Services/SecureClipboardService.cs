namespace Monica.Platform.Services;

public sealed class ClipboardExpiryScheduler : IClipboardExpiryScheduler
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

public sealed class SecureClipboardService(
    IClipboardAdapter adapter,
    IClipboardExpiryScheduler? scheduler = null) : IClipboardService, IDisposable
{
    private readonly IClipboardAdapter _adapter = adapter;
    private readonly IClipboardExpiryScheduler _scheduler = scheduler ?? new ClipboardExpiryScheduler();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _expiryCancellation;
    private TimeSpan? _sensitiveLifetime = TimeSpan.FromSeconds(30);
    private string? _ownedText;
    private long _ownershipVersion;
    private int _disposeState;

    public Task SetTextAsync(string text, CancellationToken cancellationToken = default) =>
        ReplaceTextAsync(text, isSensitive: false, cancellationToken);

    public Task SetSensitiveTextAsync(string text, CancellationToken cancellationToken = default) =>
        ReplaceTextAsync(text, isSensitive: true, cancellationToken);

    public void ConfigureSensitiveClear(TimeSpan? lifetime)
    {
        ThrowIfDisposed();
        if (lifetime is { } value && value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }

        _sensitiveLifetime = lifetime;
    }

    public async Task ClearOwnedContentAsync(CancellationToken cancellationToken = default)
    {
        if (IsDisposed)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsDisposed)
            {
                return;
            }

            var expectedText = Interlocked.Exchange(ref _ownedText, null);
            CancelExpiry();
            Interlocked.Increment(ref _ownershipVersion);
            if (expectedText is not null)
            {
                await ClearAdapterIfMatchingAsync(expectedText, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        CancelExpiry();
        Interlocked.Exchange(ref _ownedText, null);
        Interlocked.Increment(ref _ownershipVersion);

        // Do not dispose the gate: an asynchronous cleanup may still own it while
        // the dependency injection container is tearing down this singleton.
    }

    private async Task ReplaceTextAsync(
        string text,
        bool isSensitive,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            CancelExpiry();
            Interlocked.Exchange(ref _ownedText, null);
            var version = Interlocked.Increment(ref _ownershipVersion);
            await _adapter.SetTextAsync(text, cancellationToken);
            if (isSensitive && _sensitiveLifetime is { } lifetime)
            {
                ScheduleExpiry(text, version, lifetime);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ScheduleExpiry(string text, long version, TimeSpan lifetime)
    {
        if (IsDisposed)
        {
            return;
        }

        var expiryCancellation = new CancellationTokenSource();
        var cancellationToken = expiryCancellation.Token;
        Interlocked.Exchange(ref _ownedText, text);
        CancelAndDispose(Interlocked.Exchange(ref _expiryCancellation, expiryCancellation));

        if (IsDisposed)
        {
            Interlocked.Exchange(ref _ownedText, null);
            CancelExpiry();
            return;
        }

        _ = ClearAfterDelayAsync(text, version, lifetime, expiryCancellation, cancellationToken);
    }

    private async Task ClearAfterDelayAsync(
        string expectedText,
        long version,
        TimeSpan lifetime,
        CancellationTokenSource expiryCancellation,
        CancellationToken cancellationToken)
    {
        try
        {
            await _scheduler.DelayAsync(lifetime, cancellationToken);
            await ClearIfCurrentOwnerAsync(expectedText, version, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            DisposeExpiryIfCurrent(expiryCancellation);
        }
    }

    private async Task ClearIfCurrentOwnerAsync(
        string expectedText,
        long version,
        CancellationToken cancellationToken)
    {
        if (IsDisposed)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsDisposed
                || version != Interlocked.Read(ref _ownershipVersion)
                || !string.Equals(Volatile.Read(ref _ownedText), expectedText, StringComparison.Ordinal))
            {
                return;
            }

            await ClearAdapterIfMatchingAsync(expectedText, cancellationToken);
            Interlocked.Exchange(ref _ownedText, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ClearAdapterIfMatchingAsync(
        string expectedText,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentText = await _adapter.GetTextAsync(cancellationToken);
            if (!string.Equals(currentText, expectedText, StringComparison.Ordinal))
            {
                return;
            }
        }
        catch
        {
            await _adapter.ClearAsync(cancellationToken);
            return;
        }

        await _adapter.ClearAsync(cancellationToken);
    }

    private bool IsDisposed => Volatile.Read(ref _disposeState) != 0;

    private void ThrowIfDisposed()
    {
        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(SecureClipboardService));
        }
    }

    private void CancelExpiry() =>
        CancelAndDispose(Interlocked.Exchange(ref _expiryCancellation, null));

    private void DisposeExpiryIfCurrent(CancellationTokenSource expected)
    {
        if (ReferenceEquals(
            Interlocked.CompareExchange(ref _expiryCancellation, null, expected),
            expected))
        {
            expected.Dispose();
        }
    }

    private static void CancelAndDispose(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        finally
        {
            cancellation.Dispose();
        }
    }
}
