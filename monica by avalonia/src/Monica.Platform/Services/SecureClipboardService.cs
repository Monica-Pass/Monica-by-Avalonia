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

    public Task SetTextAsync(string text, CancellationToken cancellationToken = default) =>
        ReplaceTextAsync(text, isSensitive: false, cancellationToken);

    public Task SetSensitiveTextAsync(string text, CancellationToken cancellationToken = default) =>
        ReplaceTextAsync(text, isSensitive: true, cancellationToken);

    public void ConfigureSensitiveClear(TimeSpan? lifetime)
    {
        if (lifetime is { } value && value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }

        _sensitiveLifetime = lifetime;
    }

    public async Task ClearOwnedContentAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var expectedText = _ownedText;
            CancelExpiryUnsafe();
            _ownedText = null;
            _ownershipVersion++;
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
        CancelExpiryUnsafe();
        _gate.Dispose();
    }

    private async Task ReplaceTextAsync(
        string text,
        bool isSensitive,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            CancelExpiryUnsafe();
            _ownedText = null;
            var version = ++_ownershipVersion;
            await _adapter.SetTextAsync(text, cancellationToken);
            if (isSensitive && _sensitiveLifetime is { } lifetime)
            {
                ScheduleExpiryUnsafe(text, version, lifetime);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ScheduleExpiryUnsafe(string text, long version, TimeSpan lifetime)
    {
        _ownedText = text;
        _expiryCancellation = new CancellationTokenSource();
        _ = ClearAfterDelayAsync(text, version, lifetime, _expiryCancellation.Token);
    }

    private async Task ClearAfterDelayAsync(
        string expectedText,
        long version,
        TimeSpan lifetime,
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
    }

    private async Task ClearIfCurrentOwnerAsync(
        string expectedText,
        long version,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (version != _ownershipVersion || !string.Equals(_ownedText, expectedText, StringComparison.Ordinal))
            {
                return;
            }

            await ClearAdapterIfMatchingAsync(expectedText, cancellationToken);
            _ownedText = null;
            _expiryCancellation?.Dispose();
            _expiryCancellation = null;
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

    private void CancelExpiryUnsafe()
    {
        _expiryCancellation?.Cancel();
        _expiryCancellation?.Dispose();
        _expiryCancellation = null;
    }
}
