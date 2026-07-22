using Monica.Core.Services;

namespace Monica.Core.Bitwarden;

public interface IBitwardenSessionManager
{
    bool HasSession(long accountId);
    void Open(long accountId, BitwardenAccountSecrets secrets, DateTimeOffset? accessTokenExpiresAt);
    bool TryCreateLease(long accountId, out BitwardenSessionLease? lease);
    void Clear();
}

public sealed class BitwardenSessionManager(IVaultSessionService vaultSessionService) :
    IBitwardenSessionManager,
    IDisposable
{
    private readonly object _sync = new();
    private readonly HashSet<BitwardenSessionLease> _leases = [];
    private CancellationTokenRegistration _vaultLockRegistration;
    private SessionState? _session;

    public bool HasSession(long accountId)
    {
        lock (_sync)
        {
            return _session?.AccountId == accountId;
        }
    }

    public void Open(
        long accountId,
        BitwardenAccountSecrets secrets,
        DateTimeOffset? accessTokenExpiresAt)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        if (accountId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId));
        }

        if (!vaultSessionService.IsUnlocked)
        {
            throw new InvalidOperationException("The Monica vault must be unlocked before opening a Bitwarden session.");
        }

        var sessionCancellation = vaultSessionService.SessionCancellationToken;
        lock (_sync)
        {
            ClearUnsafe(disposeRegistration: true);
            if (sessionCancellation.IsCancellationRequested)
            {
                throw new InvalidOperationException("The Monica vault session is no longer active.");
            }

            _session = new SessionState(accountId, secrets.Clone(), accessTokenExpiresAt);
            _vaultLockRegistration = sessionCancellation.Register(
                static state => ((BitwardenSessionManager)state!).ClearFromVaultLock(),
                this);
        }
    }

    public bool TryCreateLease(long accountId, out BitwardenSessionLease? lease)
    {
        lock (_sync)
        {
            if (_session is null || _session.AccountId != accountId)
            {
                lease = null;
                return false;
            }

            lease = new BitwardenSessionLease(
                _session.AccountId,
                _session.Secrets.Clone(),
                _session.AccessTokenExpiresAt,
                ReleaseLease);
            _leases.Add(lease);
            return true;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            ClearUnsafe(disposeRegistration: true);
        }
    }

    public void Dispose() => Clear();

    private void ClearFromVaultLock()
    {
        lock (_sync)
        {
            ClearUnsafe(disposeRegistration: false);
        }
    }

    private void ClearUnsafe(bool disposeRegistration)
    {
        foreach (var lease in _leases.ToArray())
        {
            lease.DisposeFromOwner();
        }

        _leases.Clear();
        _session?.Dispose();
        _session = null;
        if (disposeRegistration)
        {
            _vaultLockRegistration.Dispose();
        }

        _vaultLockRegistration = default;
    }

    private void ReleaseLease(BitwardenSessionLease lease)
    {
        lock (_sync)
        {
            _leases.Remove(lease);
        }
    }

    private sealed record SessionState(
        long AccountId,
        BitwardenAccountSecrets Secrets,
        DateTimeOffset? AccessTokenExpiresAt) : IDisposable
    {
        public void Dispose() => Secrets.Dispose();
    }
}

public sealed class BitwardenSessionLease : IDisposable
{
    private readonly object _sync = new();
    private readonly Action<BitwardenSessionLease> _release;
    private BitwardenAccountSecrets? _secrets;

    internal BitwardenSessionLease(
        long accountId,
        BitwardenAccountSecrets secrets,
        DateTimeOffset? accessTokenExpiresAt,
        Action<BitwardenSessionLease> release)
    {
        AccountId = accountId;
        _secrets = secrets;
        AccessTokenExpiresAt = accessTokenExpiresAt;
        _release = release;
    }

    public long AccountId { get; }
    public BitwardenAccountSecrets Secrets
    {
        get
        {
            lock (_sync)
            {
                return _secrets ?? throw new ObjectDisposedException(nameof(BitwardenSessionLease));
            }
        }
    }

    public DateTimeOffset? AccessTokenExpiresAt { get; }
    public bool IsAccessTokenExpired(TimeProvider? timeProvider = null) =>
        AccessTokenExpiresAt is { } expiry && expiry <= (timeProvider ?? TimeProvider.System).GetUtcNow();

    public void Dispose()
    {
        if (DisposeCore())
        {
            _release(this);
        }
    }

    internal void DisposeFromOwner() => DisposeCore();

    private bool DisposeCore()
    {
        lock (_sync)
        {
            if (_secrets is null)
            {
                return false;
            }

            _secrets.Dispose();
            _secrets = null;
            return true;
        }
    }
}
