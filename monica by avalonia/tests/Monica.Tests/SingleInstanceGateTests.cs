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
        Assert.True(SingleInstanceGate.NotifyExistingInstance(@"C:\Vaults\Personal", channel));
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
        Assert.False(SingleInstanceGate.NotifyExistingInstance(@"C:\Vaults\Personal", channel));
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

    private sealed class FakeChannel : ISingleInstanceChannel
    {
        public string? AcquiredKey { get; private set; }

        public string? SignalledKey { get; private set; }

        public bool Signalled { get; private set; }

        public bool Released { get; private set; }

        public bool ListenerDisposed { get; private set; }

        public bool AcceptsSignal { get; init; } = true;

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
            Signalled = AcceptsSignal;
            return AcceptsSignal;
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
