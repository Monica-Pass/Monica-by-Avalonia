using Monica.Platform.Services;

namespace Monica.Tests;

/// <summary>
/// Two unlocked sessions over one file on disk, which is what "I opened this vault in two windows"
/// means to this client. The question is whose changes end up on the disk when both save, and the
/// answer had better not be "whoever wrote last". Everything here drives the same sessions the app
/// drives - nobody hand-copies bytes over the file to stand in for the other writer - so a save that
/// goes through is a save the app actually made.
/// </summary>
[Collection(KeePassVaultTestCollection.Name)]
public sealed class KeePassTwoSessionsOnOneFileTests : IDisposable
{
    private const string FixturePassword = "kdbx-parity-fixture-not-a-secret";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"monica-kdbx-two-sessions-{Guid.NewGuid():N}");

    /// <summary>
    /// The ordering, measured: the first session to save wins the file, and the one behind it is
    /// refused rather than allowed to overwrite. Nothing is discarded silently - the refused session
    /// keeps its edit and keeps saying it is unsaved.
    /// </summary>
    [Fact]
    public async Task The_second_session_to_save_is_refused_and_keeps_its_own_edit()
    {
        var path = await StageFixtureAsync("shared.kdbx");

        using var first = await OpenAsync(path);
        using var second = await OpenAsync(path);
        Assert.Equal(first.PayloadSha256, second.PayloadSha256);

        var firstTitles = (await first.DrainAsync()).OrderRows();
        var secondTitles = (await second.DrainAsync()).OrderRows();
        await first.EditTitleAsync(firstTitles[0], "Saved first");
        await second.EditTitleAsync(secondTitles[1], "Saved second");

        await first.SaveAsync();
        Assert.False(first.IsDirty);

        var error = await Assert.ThrowsAsync<KeePassVaultException>(() => second.SaveAsync());
        Assert.Equal(KeePassVaultError.ConcurrentChange, error.Error);

        var onDisk = await TitlesOfAsync(path);
        Assert.Contains("Saved first", onDisk);
        Assert.DoesNotContain("Saved second", onDisk);

        // The refused session is not left half-written: it still holds its edit and still calls
        // itself unsaved, so the screen has to keep telling the person so.
        Assert.True(second.IsDirty);
        Assert.Contains("Saved second", (await second.DrainAsync()).Select(detail => detail.Row.Title));
    }

    /// <summary>
    /// Saving to a different file is the way out of that dead end, and the guard must not become
    /// possible to bypass by pointing the same session back at its own source: the source path is
    /// still checked even when a path is spelled out.
    /// </summary>
    [Fact]
    public async Task A_refused_session_can_write_its_edit_elsewhere_but_not_back_onto_its_source()
    {
        var path = await StageFixtureAsync("shared.kdbx");
        var elsewhere = Path.Combine(_directory, "rescued.kdbx");

        using var winner = await OpenAsync(path);
        using var stale = await OpenAsync(path);
        await winner.EditTitleAsync((await winner.DrainAsync()).OrderRows()[0], "Kept on source");
        await stale.EditTitleAsync((await stale.DrainAsync()).OrderRows()[1], "Rescued elsewhere");
        await winner.SaveAsync();

        var samePath = await Assert.ThrowsAsync<KeePassVaultException>(
            () => stale.SaveToAsync(path));
        Assert.Equal(KeePassVaultError.ConcurrentChange, samePath.Error);

        await stale.SaveToAsync(elsewhere);

        Assert.Contains("Kept on source", await TitlesOfAsync(path));
        Assert.DoesNotContain("Rescued elsewhere", await TitlesOfAsync(path));
        var rescued = await TitlesOfAsync(elsewhere);
        Assert.Contains("Rescued elsewhere", rescued);
        Assert.DoesNotContain("Kept on source", rescued);

        // Recording what the platform does today: writing elsewhere clears the dirty flag even though
        // the file the session came from still lacks those changes. Anything that offers this as a
        // way out has to say so on screen rather than let the session read as saved.
        Assert.False(stale.IsDirty);
        Assert.Equal(path, stale.SourcePath);
    }

    /// <summary>
    /// The refusal is about the bytes, not a lock left on the file: once a session has the current
    /// payload in hand it saves normally, so the person who was refused is not locked out of a file
    /// nobody is holding open.
    /// </summary>
    [Fact]
    public async Task Refusing_a_stale_session_does_not_stop_a_current_one_from_saving()
    {
        var path = await StageFixtureAsync("shared.kdbx");

        using var first = await OpenAsync(path);
        using var second = await OpenAsync(path);
        await first.EditTitleAsync((await first.DrainAsync()).OrderRows()[0], "Saved first");
        await second.EditTitleAsync((await second.DrainAsync()).OrderRows()[1], "Saved second");
        await first.SaveAsync();
        await Assert.ThrowsAsync<KeePassVaultException>(() => second.SaveAsync());

        using var reopened = await OpenAsync(path);
        var reopenedRows = (await reopened.DrainAsync()).OrderRows();
        await reopened.EditTitleAsync(reopenedRows[1], "Saved third");
        await reopened.SaveAsync();

        var onDisk = await TitlesOfAsync(path);
        Assert.Contains("Saved first", onDisk);
        Assert.Contains("Saved third", onDisk);
        Assert.DoesNotContain("Saved second", onDisk);
    }

    private async Task<KeePassVaultSession> OpenAsync(string path)
    {
        var content = await File.ReadAllBytesAsync(path);
        return await new KeePassVaultService().OpenAsync(
            content,
            Path.GetFileName(path),
            FixturePassword,
            path);
    }

    /// <summary>
    /// Copies the vault another client authored into a fresh directory, so every test here starts from
    /// a file it did not write itself.
    /// </summary>
    private async Task<string> StageFixtureAsync(string fileName)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, fileName);
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Kdbx", "android-kotpass-v1.kdbx");
        await File.WriteAllBytesAsync(path, await File.ReadAllBytesAsync(fixture));
        return path;
    }

    private static async Task<List<string>> TitlesOfAsync(string path)
    {
        var content = await File.ReadAllBytesAsync(path);
        using var session = await new KeePassVaultService().OpenAsync(
            content,
            Path.GetFileName(path),
            FixturePassword);
        return (await session.DrainAsync()).Select(detail => detail.Row.Title).ToList();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a locked temp directory is not worth failing a green run over.
        }
    }
}

internal static class KeePassTwoSessionsTestExtensions
{
    /// <summary>
    /// The rows the browser shows, in a fixed order, so two sessions agree on which entry is which
    /// without either of them naming a row the other never saw.
    /// </summary>
    internal static List<KeePassEntryDetail> OrderRows(this List<KeePassEntryDetail> details) =>
        details.OrderBy(item => item.Row.Title, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Changes the title and nothing else. The edit carries the whole entry, so it is rebuilt from
    /// what this session just read - an edit that dropped a field would look like a lost field on
    /// disk and say nothing about the ordering being tested.
    /// </summary>
    internal static async Task EditTitleAsync(
        this KeePassVaultSession session,
        KeePassEntryDetail detail,
        string title)
    {
        var updated = await session.UpdateEntryAsync(new KeePassEntryEdit(
            detail.Row.EntryUuid,
            title,
            detail.Row.UserName,
            detail.Password,
            detail.Row.Url,
            detail.Notes,
            detail.AuthenticatorKey,
            detail.CustomFields));
        Assert.NotNull(updated);
    }
}
