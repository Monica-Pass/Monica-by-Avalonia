using Monica.App.Services;

namespace Monica.Tests;

/// <summary>
/// How a .kdbx gets from a double-click into the one window that is allowed to open it. Two rules are
/// asked of here: which command line counts as a file being handed over - and which ones belong to the
/// application's own arguments - and whether a request really survives the gap between the launch that
/// left it and the instance that reads it. Only paths travel; the master password is typed by a person
/// into the window that shows the file.
/// </summary>
public sealed class KeePassOpenRequestQueueTests
{
    private const string LedgerPath = @"D:\vaults\ledger.kdbx";
    private const string WorkPath = @"D:\vaults\work.kdbx";

    [Fact]
    public void A_double_clicked_file_arrives_as_the_only_argument()
    {
        Assert.Equal(LedgerPath, KeePassOpenRequestQueue.TryReadCommandLinePath([LedgerPath]));
        // The association passes the extension in whatever case the file on disk happens to have.
        Assert.Equal(
            @"D:\vaults\work.KDBX",
            KeePassOpenRequestQueue.TryReadCommandLinePath([@"D:\vaults\work.KDBX"]));
    }

    [Fact]
    public void A_relative_file_is_named_the_way_the_window_will_open_it()
    {
        var name = $"monica-open-request-{Guid.NewGuid():N}.kdbx";

        var path = KeePassOpenRequestQueue.TryReadCommandLinePath([name]);

        Assert.Equal(Path.GetFullPath(name), path);
        Assert.True(Path.IsPathRooted(path));
    }

    [Fact]
    public void An_argument_that_is_not_a_file_being_handed_over_is_left_alone()
    {
        // Every argument this application defines for itself starts with a dash and takes the paths it
        // needs after that flag. Claiming the one after a flag would have the window jump to a database
        // the smoke runs only ever wanted to measure.
        Assert.Null(KeePassOpenRequestQueue.TryReadCommandLinePath(["--smoke-ui-keepass-file", LedgerPath]));
        Assert.Null(KeePassOpenRequestQueue.TryReadCommandLinePath(["--smoke-ui-unlock", "not-the-password"]));
        Assert.Null(KeePassOpenRequestQueue.TryReadCommandLinePath(["notes.txt"]));
        Assert.Null(KeePassOpenRequestQueue.TryReadCommandLinePath([]));
        Assert.Null(KeePassOpenRequestQueue.TryReadCommandLinePath(null));
    }

    [Fact]
    public void A_request_is_read_once_and_then_no_longer_waits()
    {
        var queue = NewQueue();

        Assert.True(queue.TryEnqueue(LedgerPath));
        Assert.Equal([LedgerPath], queue.Drain());
        Assert.Empty(queue.Drain());
        Assert.Empty(Directory.GetFiles(queue.RequestDirectory, "*.req"));
    }

    [Fact]
    public void A_request_left_before_the_window_was_listening_is_still_there_after()
    {
        var directory = NewDataRoot();
        var queue = new KeePassOpenRequestQueue(directory);
        Assert.True(queue.TryEnqueue(LedgerPath));

        // A second instance standing in for the launch that hands its file to somebody else to open.
        var reopened = new KeePassOpenRequestQueue(directory);

        Assert.Equal([LedgerPath], reopened.Drain());
    }

    [Fact]
    public async Task Two_double_clicks_both_arrive_and_the_newest_is_read_last()
    {
        var queue = NewQueue();
        Assert.True(queue.TryEnqueue(LedgerPath));
        await Task.Delay(15);
        Assert.True(queue.TryEnqueue(WorkPath));

        // The page shows one file at a time, so the caller works through these in order and the file left
        // on screen is the one asked for last - the double-click the person most recently made.
        Assert.Equal([LedgerPath, WorkPath], queue.Drain());
    }

    [Fact]
    public void A_request_still_being_written_is_not_read()
    {
        var queue = NewQueue();
        Directory.CreateDirectory(queue.RequestDirectory);
        // The name a writer uses before the path inside it is complete. A drain that mistook it for a
        // finished request would open a database nobody asked for.
        var pending = Path.Combine(queue.RequestDirectory, $"{Guid.NewGuid():N}.req.tmp");
        File.WriteAllText(pending, LedgerPath);

        Assert.Empty(queue.Drain());
        Assert.True(File.Exists(pending));
    }

    [Fact]
    public void A_request_that_came_in_empty_is_consumed_rather_than_retried()
    {
        var queue = NewQueue();
        Directory.CreateDirectory(queue.RequestDirectory);
        var spent = Path.Combine(queue.RequestDirectory, $"{DateTimeOffset.UtcNow.UtcTicks:x16}-spent.req");
        File.WriteAllText(spent, "   ");

        Assert.Empty(queue.Drain());
        Assert.False(File.Exists(spent));
    }

    [Fact]
    public void A_drop_zone_that_was_never_created_is_not_an_error()
    {
        var queue = new KeePassOpenRequestQueue(Path.Combine(Path.GetTempPath(), $"monica-{Guid.NewGuid():N}"));

        Assert.Empty(queue.Drain());
    }

    private static KeePassOpenRequestQueue NewQueue() => new(NewDataRoot());

    private static string NewDataRoot() =>
        Path.Combine(Path.GetTempPath(), "monica-kdbx-open-requests", Guid.NewGuid().ToString("N"));
}
