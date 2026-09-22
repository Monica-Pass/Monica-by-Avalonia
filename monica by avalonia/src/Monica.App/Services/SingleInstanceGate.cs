using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Monica.App.Services;

/// <summary>
/// The cross-process primitives the gate needs, separated so the orchestration can be tested
/// without two real processes: on Windows these names are session-local, and .NET emulates them
/// with files elsewhere, so the same code runs on every desktop platform.
/// </summary>
internal interface ISingleInstanceChannel : IDisposable
{
    bool TryAcquire(string key);

    void Release();

    bool Signal(string key);

    IDisposable Listen(string key, Action onSignal);
}

/// <summary>
/// Keeps one data directory to one running instance. A second launch against the same directory
/// would open a second window over the same vault, each holding its own decrypted copy with no
/// sight of the other's writes — and with the tray on by default, double-clicking the icon twice is
/// an easy way to end up there without noticing.
/// </summary>
internal sealed class SingleInstanceGate(ISingleInstanceChannel channel, string key) : IDisposable
{
    private IDisposable? _reopenListening;

    // Set for the lifetime of the primary process so the shell can subscribe once startup is done.
    public static SingleInstanceGate? Active { get; private set; }

    /// <summary>
    /// Returns the gate when this process is the only one on <paramref name="dataRootDirectory"/>,
    /// and null when another instance already holds it.
    /// </summary>
    public static SingleInstanceGate? TryStartAsPrimary(string dataRootDirectory, ISingleInstanceChannel? channel = null)
    {
        var ownedChannel = channel ?? new NamedSyncSingleInstanceChannel();
        var key = BuildInstanceKey(dataRootDirectory);
        if (!ownedChannel.TryAcquire(key))
        {
            ownedChannel.Dispose();
            return null;
        }

        var gate = new SingleInstanceGate(ownedChannel, key);
        Active = gate;
        return gate;
    }

    /// <summary>
    /// Asks the instance that does own the directory to bring itself forward. Returns false when no
    /// listener is there to hear it, which is the caller's cue to say something rather than exit
    /// into silence.
    /// </summary>
    public static bool NotifyExistingInstance(string dataRootDirectory, ISingleInstanceChannel? channel = null)
    {
        var ownedChannel = channel ?? new NamedSyncSingleInstanceChannel();
        try
        {
            return ownedChannel.Signal(BuildInstanceKey(dataRootDirectory));
        }
        finally
        {
            ownedChannel.Dispose();
        }
    }

    public static string BuildInstanceKey(string dataRootDirectory)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataRootDirectory));
        normalized = OperatingSystem.IsWindows() ? normalized.ToLowerInvariant() : normalized;
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(digest.AsSpan(0, 8)).ToLowerInvariant();
    }

    /// <summary>
    /// Runs <paramref name="onReopen"/> when another launch asks for this instance. The callback
    /// arrives off the UI thread, so the shell is responsible for marshalling.
    /// </summary>
    public void ListenForReopen(Action onReopen)
    {
        _reopenListening = channel.Listen(key, onReopen);
    }

    public void Dispose()
    {
        _reopenListening?.Dispose();
        _reopenListening = null;
        if (ReferenceEquals(Active, this))
        {
            Active = null;
        }

        channel.Release();
        channel.Dispose();
    }
}

internal sealed class NamedSyncSingleInstanceChannel : ISingleInstanceChannel
{
    private Mutex? _mutex;
    private EventWaitHandle? _reopenEvent;

    public bool TryAcquire(string key)
    {
        // Ownership is never waited on: the handle's lifetime is the lock, so a killed or crashed
        // process releases it the moment the operating system closes its handles.
        _mutex = new Mutex(initiallyOwned: false, MutexName(key), out var createdNew);
        if (!createdNew)
        {
            return false;
        }

        // Name the reopen channel at the same instant the lock is taken, not when the shell gets
        // round to listening. A double-click during startup would otherwise find no event at all
        // and be dropped; held here, the set signal waits for the registration to arrive.
        _reopenEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, EventName(key));
        return true;
    }

    public void Release()
    {
        _mutex?.Dispose();
        _mutex = null;
        _reopenEvent?.Dispose();
        _reopenEvent = null;
    }

    public bool Signal(string key)
    {
        // `createdNew` is the portable way to ask whether anyone is out here to receive this: the
        // primary names that event the moment it takes the lock, so an event nobody had named yet
        // means no instance is holding this directory. TryOpenExisting would say it more directly,
        // but the BCL only supports it on Windows.
        using var reopenEvent = new EventWaitHandle(
            initialState: false, EventResetMode.AutoReset, EventName(key), out var createdWhileNobodyListened);

        return !createdWhileNobodyListened && reopenEvent.Set();
    }

    public IDisposable Listen(string key, Action onReopen)
    {
        var reopenEvent = _reopenEvent ??= new EventWaitHandle(
            initialState: false, EventResetMode.AutoReset, EventName(key));

        var registration = ThreadPool.RegisterWaitForSingleObject(
            reopenEvent,
            (_, timedOut) =>
            {
                if (!timedOut)
                {
                    onReopen();
                }
            },
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);

        return new Listener(registration);
    }

    public void Dispose() => Release();

    private static string MutexName(string key) => $"Monica.SingleInstance.{key}";

    private static string EventName(string key) => $"Monica.SingleInstance.Reopen.{key}";

    private sealed class Listener(RegisteredWaitHandle registration) : IDisposable
    {
        public void Dispose() => registration.Unregister(null);
    }
}
