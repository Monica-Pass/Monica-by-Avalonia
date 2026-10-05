namespace Monica.Data.Mdbx;

/// <summary>
/// Quiesces the store's native handles while a caller replaces a vault file.
/// The lease must cover replacement and any rollback; it is not an OS file lock.
/// </summary>
public interface IMdbxVaultFileReplacementCoordinator
{
    Task<IDisposable> AcquireFileReplacementAsync(string path, CancellationToken cancellationToken = default);
    Task<IDisposable> AcquireSnapshotCommitAsync(string path, CancellationToken cancellationToken = default) =>
        AcquireFileReplacementAsync(path, cancellationToken);
}

public sealed partial class MdbxVaultStore : IMdbxVaultFileReplacementCoordinator
{
    public Task<IDisposable> AcquireFileReplacementAsync(string path, CancellationToken cancellationToken = default) =>
        AcquireFileGateAsync(path, true, cancellationToken);

    public Task<IDisposable> AcquireSnapshotCommitAsync(string path, CancellationToken cancellationToken = default) =>
        AcquireFileGateAsync(path, false, cancellationToken);

    private async Task<IDisposable> AcquireFileGateAsync(
        string path,
        bool replacement,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _ = Path.GetFullPath(path);

        var sessionToken = _vaultSessionService?.SessionCancellationToken ?? CancellationToken.None;
        using var linkedCancellation = CreateLinkedCancellation(cancellationToken, sessionToken);
        var effectiveCancellationToken = linkedCancellation?.Token ??
            (cancellationToken.CanBeCanceled ? cancellationToken : sessionToken);

        // Wait for the active CRUD lease before closing its native handle. All files
        // share this gate, so even a queued operation for a different vault must wait.
        await _vaultSessionGate.WaitAsync(effectiveCancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            effectiveCancellationToken.ThrowIfCancellationRequested();
            if (_vaultSessionService is { IsUnlocked: false })
            {
                throw new OperationCanceledException("The MDBX vault session is locked.", effectiveCancellationToken);
            }

            if (replacement)
            {
                CloseCachedVault();
                Interlocked.Increment(ref _fileReplacementVersion);
                Interlocked.Exchange(ref _vaultSessionReleaseRequested, 0);
            }
            effectiveCancellationToken.ThrowIfCancellationRequested();

            // Lock cancellation requests release but never frees this gate while
            // the caller is still replacing or rolling back the file.
            return new MdbxFileReplacementLease(this, replacement);
        }
        catch
        {
            ReleaseVaultLease();
            throw;
        }
    }

    private sealed class MdbxFileReplacementLease(MdbxVaultStore owner, bool replacement) : IDisposable
    {
        private MdbxVaultStore? _owner = owner;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null) return;
            if (replacement) Interlocked.Increment(ref owner._fileReplacementVersion);
            owner.ReleaseVaultLease();
        }
    }
}
