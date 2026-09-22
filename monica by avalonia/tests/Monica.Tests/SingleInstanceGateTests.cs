using Monica.App.Services;
using System.IO;
using System.Threading;
using Xunit;

namespace Monica.Tests;

/// <summary>
/// The lock itself is an operating system object, so the interesting failures are in the
/// orchestration: who is told they are second, what key they were judged against, and whether the
/// instance that stayed running actually hears the request to come forward.
/// </summary>
public class SingleInstanceGateTests
{
    [Fact]
    public void The_second_gate_on_one_directory_is_not_primary_and_hands_off_to_the_first()
    {
        var channel = new FakeChannel();
        var first = SingleInstanceGate.TryStartAsPrimary(@"C:\Vaults\Personal", channel);
        var second = SingleInstanceGate.TryStartAsPrimary(@"C:\Vaults\Personal", channel);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Equal(SingleInstanceGate.BuildInstanceKey(@"C:\Vaults\Personal"), channel.AcquiredKey);
        Assert.True(SingleInstanceGate.NotifyExistingInstance(@"C:\Vaults\Personal", channel).HandedOff);
        Assert.Equal(channel.AcquiredKey, channel.SignalledKey);
        Assert.True(channel.Signalled);

        first!.Dispose();
    }

    [Fact]
    public void Reopen_requests_are_delivered_to_the_instance_that_owns_the_directory()
    {
        var channel = new FakeChannel();
        var gate = SingleInstanceGate.TryStartAsPrimary(@"C:\Vaults\Personal", channel);
        var reopened = 0;
        gate!.ListenForReopen(() => Interlocked.Increment(ref reopened));

        channel.FireReopen();
        Assert.Equal(1, reopened);

        gate.Dispose();
        Assert.True(channel.Released);
        Assert.True(channel.ListenerDisposed);
        Assert.Null(SingleInstanceGate.Active);
    }

    [Fact]
    public void Two_data_directories_are_judged_independently()
    {
        Assert.NotEqual(
            SingleInstanceGate.BuildInstanceKey(Path.Combine(Path.GetTempPath(), "monica-vault-a")),
            SingleInstanceGate.BuildInstanceKey(Path.Combine(Path.GetTempPath(), "monica-vault-b")));
    }

    [Fact]
    public void The_same_directory_written_differently_is_one_directory()
    {
        var plain = Path.Combine(Path.GetTempPath(), "monica-vault-a");
        Assert.Equal(
            SingleInstanceGate.BuildInstanceKey(plain),
            SingleInstanceGate.BuildInstanceKey(plain + Path.DirectorySeparatorChar));
        Assert.Equal(
            SingleInstanceGate.BuildInstanceKey(plain),
            SingleInstanceGate.BuildInstanceKey(Path.GetFullPath(plain + "\\..\\monica-vault-a")));

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(
                SingleInstanceGate.BuildInstanceKey(plain),
                SingleInstanceGate.BuildInstanceKey(plain.ToUpperInvariant()));
        }
    }

    [Fact]
    public void A_handoff_with_no_listener_reports_itself_instead_of_looking_successful()
    {
        var channel = new FakeChannel { AcceptsSignal = false };
        Assert.False(SingleInstanceGate.NotifyExistingInstance(@"C:\Vaults\Personal", channel).HandedOff);
    }

    /// <summary>
    /// The right to come forward only exists while the launch the user acted on is alive, and it has to be
    /// handed over before that launch signals the owner to move - the other way round is a race the owner
    /// loses whenever it is quick.
    /// </summary>
    [Fact]
    public void The_foreground_right_is_handed_over_before_the_owner_is_told_to_move()
    {
        var channel = new FakeChannel();

        var handoff = SingleInstanceGate.NotifyExistingInstance(@"C:\Vaults\Personal", channel);

        Assert.True(handoff.HandedOff);
        Assert.True(handoff.GrantedForeground);
        Assert.Equal(0, channel.GrantIndex);
        Assert.Equal(1, channel.SignalIndex);

        // The right has to go to whoever owns *this* directory, so the key it is looked up by matters.
        var key = SingleInstanceGate.BuildInstanceKey(@"C:\Vaults\Personal");
        Assert.Equal(key, channel.GrantedKey);
        Assert.Equal(key, channel.SignalledKey);
    }

    /// <summary>
    /// A refused grant is a window that comes back underneath, which is worth having; a grant that throws
    /// and takes the handoff down with it is a window that does not come back at all.
    /// </summary>
    [Fact]
    public void A_handoff_still_lands_when_the_foreground_right_cannot_be_handed_over()
    {
        var channel = new FakeChannel { GrantsForeground = false };

        var handoff = SingleInstanceGate.NotifyExistingInstance(@"C:\Vaults\Personal", channel);

        Assert.True(handoff.HandedOff);
        Assert.False(handoff.GrantedForeground);
        Assert.True(channel.Signalled);
    }

    /// <summary>
    /// Runs the real named primitives, because everything above trusts that a second handle to the
    /// same name is refused. If the operating system ever answers differently, this is where it shows.
    /// </summary>
    [Fact]
    public void A_second_real_lock_on_one_key_is_refused_and_reaches_the_first_listener()
    {
        var key = "test." + Guid.NewGuid().ToString("N");
        using var owner = new NamedSyncSingleInstanceChannel();
        var rival = new NamedSyncSingleInstanceChannel();
        Assert.True(owner.TryAcquire(key));
        Assert.False(rival.TryAcquire(key));

        var delivered = new ManualResetEventSlim(false);
        using var listener = owner.Listen(key, () => delivered.Set());

        Assert.True(rival.Signal(key));
        Assert.True(delivered.Wait(TimeSpan.FromSeconds(5)));

        // Nobody owns the directory any more, so the next ask must not look like it reached someone.
        // A real second launch is its own process and its handle dies with it; inside one process the
        // name stays alive while any handle is open, so that handle has to go first.
        listener.Dispose();
        owner.Release();
        rival.Dispose();
        using var afterRelease = new NamedSyncSingleInstanceChannel();
        Assert.True(afterRelease.TryAcquire(key));
    }

    /// <summary>
    /// The window between taking the lock and the shell subscribing to it is exactly when a user
    /// who double-clicked twice would be answered with silence.
    /// </summary>
    [Fact]
    public void A_reopen_that_arrives_before_anyone_is_listening_is_still_delivered()
    {
        var key = "test." + Guid.NewGuid().ToString("N");
        using var owner = new NamedSyncSingleInstanceChannel();
        Assert.True(owner.TryAcquire(key));

        using var sender = new NamedSyncSingleInstanceChannel();
        Assert.True(sender.Signal(key));

        var delivered = new ManualResetEventSlim(false);
        using var listener = owner.Listen(key, () => delivered.Set());
        Assert.True(delivered.Wait(TimeSpan.FromSeconds(5)));
    }

    /// <summary>
    /// The tests above stub the handover, which is the part that can be stubbed. Who owns a directory is
    /// not: in real use the reader is a different process from the writer, so this runs the named store.
    /// Whether the handover then puts the window on top of what the user was looking at is a desktop
    /// question, and artifacts/autotype/verify-handoff-foreground.ps1 is what answers it.
    /// </summary>
    [Fact]
    public void The_owner_is_findable_by_process_id_and_only_while_it_holds_the_lock()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var key = "test." + Guid.NewGuid().ToString("N");
        using var owner = new NamedSyncSingleInstanceChannel();
        Assert.True(owner.TryAcquire(key));
        Assert.Equal(Environment.ProcessId, NamedSyncSingleInstanceChannel.ReadOwnerPid(key));

        // A directory nobody owns must not answer with a process id, or the grant would go to a stranger.
        Assert.Equal(0, NamedSyncSingleInstanceChannel.ReadOwnerPid("test." + Guid.NewGuid().ToString("N")));

        owner.Release();
        Assert.Equal(0, NamedSyncSingleInstanceChannel.ReadOwnerPid(key));
    }

    private sealed class FakeChannel : ISingleInstanceChannel
    {
        public string? AcquiredKey { get; private set; }

        public string? SignalledKey { get; private set; }

        public bool Signalled { get; private set; }

        public bool Released { get; private set; }

        public bool ListenerDisposed { get; private set; }

        public bool AcceptsSignal { get; init; } = true;

        public bool GrantsForeground { get; init; } = true;

        public string? GrantedKey { get; private set; }

        /// <summary>Position in the order the handoff made its calls, so the sequence is assertable.</summary>
        public int GrantIndex { get; private set; } = -1;

        public int SignalIndex { get; private set; } = -1;

        private int _callCount;

        private Action? _onReopen;

        public bool TryAcquire(string key)
        {
            if (AcquiredKey is not null)
            {
                return false;
            }

            AcquiredKey = key;
            return true;
        }

        public void Release() => Released = true;

        public bool Signal(string key)
        {
            SignalledKey = key;
            SignalIndex = _callCount++;
            Signalled = AcceptsSignal;
            return AcceptsSignal;
        }

        public bool TryGrantForegroundToOwner(string key)
        {
            GrantedKey = key;
            GrantIndex = _callCount++;
            return GrantsForeground;
        }

        public IDisposable Listen(string key, Action onReopen)
        {
            _onReopen = onReopen;
            return new Listener(this);
        }

        public void FireReopen() => _onReopen?.Invoke();

        public void Dispose() => Release();

        private sealed class Listener(FakeChannel channel) : IDisposable
        {
            public void Dispose() => channel.ListenerDisposed = true;
        }
    }
}
