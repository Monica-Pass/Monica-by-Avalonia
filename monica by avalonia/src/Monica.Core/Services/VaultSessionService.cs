namespace Monica.Core.Services;

public interface IVaultSessionService
{
    bool IsUnlocked { get; }
    bool IsExplicitlyLocked { get; }
    CancellationToken SessionCancellationToken { get; }
    void MarkUnlocked();
    void RecordActivity();
    TimeSpan? GetRemainingInactivity(TimeSpan inactivityTimeout);
    bool IsExpired(TimeSpan inactivityTimeout);
    void MarkLocked();
}

public sealed class VaultSessionService(TimeProvider? timeProvider = null) : IVaultSessionService, IDisposable
{
    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private CancellationTokenSource _sessionCancellation = new();
    private long _lastActivityTimestamp;

    public bool IsUnlocked { get; private set; }
    public bool IsExplicitlyLocked { get; private set; }

    public CancellationToken SessionCancellationToken
    {
        get
        {
            lock (_sync)
            {
                return _sessionCancellation.Token;
            }
        }
    }

    public void MarkUnlocked()
    {
        lock (_sync)
        {
            ReplaceSessionCancellationUnsafe();
            _lastActivityTimestamp = _timeProvider.GetTimestamp();
            IsUnlocked = true;
            IsExplicitlyLocked = false;
        }
    }

    public void RecordActivity()
    {
        lock (_sync)
        {
            if (IsUnlocked)
            {
                _lastActivityTimestamp = _timeProvider.GetTimestamp();
            }
        }
    }

    public bool IsExpired(TimeSpan inactivityTimeout)
    {
        lock (_sync)
        {
            return GetRemainingInactivityUnsafe(inactivityTimeout) is { } remaining &&
                remaining <= TimeSpan.Zero;
        }
    }

    public TimeSpan? GetRemainingInactivity(TimeSpan inactivityTimeout)
    {
        lock (_sync)
        {
            return GetRemainingInactivityUnsafe(inactivityTimeout);
        }
    }

    public void MarkLocked()
    {
        lock (_sync)
        {
            IsUnlocked = false;
            IsExplicitlyLocked = true;
            _sessionCancellation.Cancel();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _sessionCancellation.Cancel();
            _sessionCancellation.Dispose();
        }
    }

    private void ReplaceSessionCancellationUnsafe()
    {
        _sessionCancellation.Cancel();
        _sessionCancellation.Dispose();
        _sessionCancellation = new CancellationTokenSource();
    }

    private TimeSpan? GetRemainingInactivityUnsafe(TimeSpan inactivityTimeout)
    {
        if (!IsUnlocked)
        {
            return null;
        }

        var elapsed = _timeProvider.GetElapsedTime(
            _lastActivityTimestamp,
            _timeProvider.GetTimestamp());
        var remaining = inactivityTimeout - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
