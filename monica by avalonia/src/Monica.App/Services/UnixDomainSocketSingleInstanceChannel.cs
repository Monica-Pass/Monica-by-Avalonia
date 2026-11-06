using System.Net.Sockets;
using System.Text;

namespace Monica.App.Services;

/// <summary>
/// The single-instance primitives for Linux and macOS. .NET has no named events there (constructing
/// one throws <see cref="PlatformNotSupportedException"/>), so ownership is an exclusive lock on a
/// file inside the data directory and the reopen request travels over a Unix domain socket.
/// </summary>
/// <remarks>
/// The lock lives in the guarded directory itself, so two spellings of the same directory (a
/// symlink, or different letter case on a case-insensitive macOS volume) still meet on one file.
/// The socket is bound and listening the moment the lock is taken: a connection that arrives before
/// the shell subscribes waits in the kernel's accept queue instead of being dropped.
/// </remarks>
internal sealed class UnixDomainSocketSingleInstanceChannel(string stateDirectory) : ISingleInstanceChannel
{
    private const string LockFileName = ".monica-instance.lock";
    private const string SocketFileName = ".monica-instance.sock";

    // sun_path holds 108 bytes on Linux and 104 on macOS, terminator included.
    private const int MaximumSocketPathBytes = 100;

    // flock(LOCK_EX | LOCK_NB) reports a held lock as EWOULDBLOCK, which .NET surfaces as an
    // IOException carrying the raw errno: 11 on Linux, 35 on macOS.
    private static readonly int[] LockHeldErrnos = [11, 35];

    private FileStream? _lock;
    private Socket? _listener;
    private string? _socketPath;

    public bool TryAcquire(string key)
    {
        Directory.CreateDirectory(stateDirectory);
        try
        {
            // FileShare.None makes .NET take an advisory flock on the descriptor. The kernel drops it
            // when the process dies, so a crashed owner never leaves the directory locked.
            _lock = new FileStream(
                Path.Combine(stateDirectory, LockFileName),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1);
        }
        catch (IOException ex) when (LockHeldErrnos.Contains(ex.HResult))
        {
            return false;
        }

        _socketPath = SocketPath(key);
        try
        {
            // Holding the lock means any socket file at this path belongs to an owner that is gone.
            DeleteIfPresent(_socketPath);
            var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
                listener.Listen(backlog: 16);
            }
            catch
            {
                listener.Dispose();
                throw;
            }

            _listener = listener;
        }
        catch (Exception ex) when (ex is SocketException or IOException or UnauthorizedAccessException)
        {
            // The lock alone keeps a second window off this vault. Without the socket a second launch
            // just exits without bringing this one forward, which is not worth refusing to start over.
            AppDiagnostics.Error("Single instance reopen channel is unavailable", ex);
            _socketPath = null;
        }

        return true;
    }

    public void Release()
    {
        _listener?.Dispose();
        _listener = null;

        // Remove the socket while the lock is still held, so it can never delete a successor's socket.
        if (_socketPath is not null)
        {
            DeleteIfPresent(_socketPath);
            _socketPath = null;
        }

        _lock?.Dispose();
        _lock = null;
    }

    public bool Signal(string key)
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            // A full accept queue makes a blocking connect wait indefinitely; a launch that is about
            // to exit anyway should give up instead.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath(key)), timeout.Token)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            socket.Send([1]);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
        {
            // No socket file, or a file nobody listens on: there is no owner to hand off to.
            return false;
        }
    }

    public IDisposable Listen(string key, Action onReopen)
    {
        if (_listener is not { } listener)
        {
            return new Subscription(null);
        }

        var cancellation = new CancellationTokenSource();
        _ = Task.Run(() => AcceptLoopAsync(listener, onReopen, cancellation.Token));
        return new Subscription(cancellation);
    }

    public bool TryGrantForegroundToOwner(string key) => false;

    public void Dispose() => Release();

    private static async Task AcceptLoopAsync(Socket listener, Action onReopen, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // The connection itself is the request; the byte the sender writes carries nothing.
                using var client = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
            {
                // Release() closed the listener underneath the pending accept.
                return;
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionAborted or SocketError.Interrupted)
            {
                continue;
            }
            catch (SocketException ex)
            {
                AppDiagnostics.Error("Single instance reopen listener stopped", ex);
                return;
            }

            try
            {
                onReopen();
            }
            catch (Exception ex)
            {
                AppDiagnostics.Error("Single instance reopen handler failed", ex);
            }
        }
    }

    private string SocketPath(string key)
    {
        var preferred = Path.Combine(stateDirectory, SocketFileName);
        if (Encoding.UTF8.GetByteCount(preferred) <= MaximumSocketPathBytes)
        {
            return preferred;
        }

        // A deep custom data directory would overflow sun_path. $XDG_RUNTIME_DIR is private to the user
        // by specification; the temporary directory is the last resort (per-user on macOS).
        var runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var shortRoot = !string.IsNullOrWhiteSpace(runtimeDirectory) && Directory.Exists(runtimeDirectory)
            ? runtimeDirectory
            : Path.GetTempPath();
        return Path.Combine(shortRoot, $"monica-{key}.sock");
    }

    private static void DeleteIfPresent(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private sealed class Subscription(CancellationTokenSource? cancellation) : IDisposable
    {
        private CancellationTokenSource? _cancellation = cancellation;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _cancellation, null) is not { } owned)
            {
                return;
            }

            owned.Cancel();
            owned.Dispose();
        }
    }
}
