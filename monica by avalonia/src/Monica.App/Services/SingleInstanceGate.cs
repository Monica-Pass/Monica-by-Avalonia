using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
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

    /// <summary>
    /// Asks Windows to let the instance that owns <paramref name="key"/> raise its own window. Has to be
    /// done by the launch the user acted on, and before the signal, so the owner can act on it.
    /// </summary>
    bool TryGrantForegroundToOwner(string key);
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
    public static HandoffResult NotifyExistingInstance(string dataRootDirectory, ISingleInstanceChannel? channel = null)
    {
        var ownedChannel = channel ?? new NamedSyncSingleInstanceChannel();
        try
        {
            var key = BuildInstanceKey(dataRootDirectory);

            // Grant first, and here rather than at the call site, because there is no ordering a caller
            // could get wrong: the right belongs to this process alone, this process is about to exit with
            // it, and the owner only raises its window once it hears the signal. Reversing these two lines
            // is what the ordering test above catches.
            var granted = ownedChannel.TryGrantForegroundToOwner(key);
            var signalled = ownedChannel.Signal(key);
            return new HandoffResult(signalled, granted);
        }
        finally
        {
            ownedChannel.Dispose();
        }
    }

    /// <param name="HandedOff">Whether the running instance was told to come forward.</param>
    /// <param name="GrantedForeground">
    /// Whether the running instance was handed the right to put itself on top. A handoff without it still
    /// brings the window back, just underneath whatever the user was looking at.
    /// </param>
    public readonly record struct HandoffResult(bool HandedOff, bool GrantedForeground);

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
    private const int OwnerPidBytes = sizeof(int);

    private Mutex? _mutex;
    private EventWaitHandle? _reopenEvent;
    private MemoryMappedFile? _ownerPidStore;

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

        // And publish which process that is, the same way: a second launch cannot raise the first one's
        // window itself, but it can hand over the right to do so, and for that it needs to know who.
        _ownerPidStore = TryCreateOwnerPidStore(key);
        WriteOwnerPid();
        return true;
    }

    public void Release()
    {
        _mutex?.Dispose();
        _mutex = null;
        _reopenEvent?.Dispose();
        _reopenEvent = null;
        _ownerPidStore?.Dispose();
        _ownerPidStore = null;
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

    public bool TryGrantForegroundToOwner(string key)
    {
        // Windows refuses SetForegroundWindow to a process the user did not act on, and the instance that
        // stayed running is not the one they double-clicked. Measured on the published artifact: the
        // window came back in 0.01-0.13s but sat at z-order 150 behind the app the user was looking at,
        // and never took the foreground in 6.6s of watching - so it stayed their "nothing happened".
        //
        // A process that holds the right can pass it on, and this launch does hold it (it was started by
        // the foreground process, which is the whole reason the rival has to be the one starting it).
        // A recycled pid could in principle get the right by mistake; the window is raised either way,
        // and the alternative is to leave the promise unkept whenever the owner is slow to die.
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var ownerPid = ReadOwnerPid(key);
        return ownerPid > 0 && AllowSetForegroundWindow(ownerPid);
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

    private static MemoryMappedFile? TryCreateOwnerPidStore(string key)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            return MemoryMappedFile.CreateOrOpen(OwnerPidStoreName(key), OwnerPidBytes);
        }
        catch (IOException)
        {
            // A mapping left behind at a different size than this build asks for. Losing the foreground
            // handover is the cost; refusing to start would be a worse one.
            return null;
        }
    }

    private void WriteOwnerPid()
    {
        if (_ownerPidStore is null)
        {
            return;
        }

        using var view = _ownerPidStore.CreateViewStream(0, OwnerPidBytes, MemoryMappedFileAccess.Write);
        view.Write(BitConverter.GetBytes(Environment.ProcessId));
    }

    internal static int ReadOwnerPid(string key)
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }

        try
        {
            using var store = MemoryMappedFile.OpenExisting(OwnerPidStoreName(key));
            using var view = store.CreateViewStream(0, OwnerPidBytes, MemoryMappedFileAccess.Read);

            Span<byte> buffer = stackalloc byte[OwnerPidBytes];
            return view.Read(buffer) == buffer.Length ? BitConverter.ToInt32(buffer) : 0;
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException)
        {
            // No store means nobody published one, which is the same answer as no owner.
            return 0;
        }
    }

    private static string MutexName(string key) => $"Monica.SingleInstance.{key}";

    private static string EventName(string key) => $"Monica.SingleInstance.Reopen.{key}";

    private static string OwnerPidStoreName(string key) => $"Monica.SingleInstance.Owner.{key}";

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    private sealed class Listener(RegisteredWaitHandle registration) : IDisposable
    {
        public void Dispose() => registration.Unregister(null);
    }
}
