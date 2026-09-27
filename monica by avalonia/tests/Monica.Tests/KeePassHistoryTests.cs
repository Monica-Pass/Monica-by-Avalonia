using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using Monica.Platform.Services;

namespace Monica.Tests;

/// <summary>
/// Whether the shapes a .kdbx remembers can be read and put back. The write funnel already files a
/// snapshot before every edit, so the question this suite answers is the other half: can a person see
/// those versions and revert to one, with the revert landing on the file as a version the next
/// KeePass client reads - and without the list ever showing a password.
/// </summary>
[Collection(KeePassVaultTestCollection.Name)]
public sealed class KeePassHistoryTests : IDisposable
{
    private const string FixturePassword = "history-fixture-not-a-secret";
    private const string ElderTitle = "Elder shape";
    private const string ElderPassword = "history-elder-secret";
    private const string ElderNotes = "Elder note";
    private const string ElderField = "Elder field";
    private const string ElderTotp = "otpauth://totp/Elder?secret=JBSWY3DPEHPK3PXP";
    private const string ElderAttachment = "elder.bin";
    private const string LiveTitle = "Live shape";
    private const string LivePassword = "history-live-secret";
    private const string LiveField = "Live field";
    private const string LiveAttachment = "live.bin";
    private const string EntryUrl = "https://history.example.com";

    // The two generations the fixture ships with, dated from the run rather than from the calendar:
    // the database carries a 365-day window on saved versions, so a fixed year would one day fall out
    // of it and the suite would report a version the writer pruned as a version it lost.
    private static readonly DateTime ElderTime = DateTime.UtcNow.AddDays(-6);
    private static readonly DateTime LiveTime = DateTime.UtcNow.AddDays(-2);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"monica-kdbx-history-{Guid.NewGuid():N}");

    [Fact]
    public async Task A_remembered_version_is_listed_without_the_secret_it_used_to_hold()
    {
        var path = await StageCopyAsync();
        using var session = await OpenAsync(path);
        var row = Assert.Single(await session.ReadGroupRowsAsync(null));

        var versions = await session.ReadHistoryAsync(row.EntryUuid);
        var version = Assert.Single(versions);
        Assert.Equal(0, version.Index);
        Assert.Equal(row.EntryUuid, version.EntryUuid);
        Assert.Equal(ElderTitle, version.Title);
        Assert.Equal(EntryUrl, version.Url);
        Assert.Equal(1, version.CustomFieldCount);
        Assert.Equal(1, version.AttachmentCount);

        // The list is a version picker, not a preview pane: nothing on it may be a secret field.
        var named = typeof(KeePassHistoryVersion).GetProperties().Select(property => property.Name);
        Assert.DoesNotContain(named, name => name.Contains("Password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(named, name => name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(named, name => name.Contains("Notes", StringComparison.OrdinalIgnoreCase));

        // That check is lexical: a field named `Hint` would slip past it while still carrying a
        // password. So what the record holds is checked by value as well - boolean, because a failed
        // string assertion prints the actual value and a red run must not put a secret in a log.
        var carried = typeof(KeePassHistoryVersion)
            .GetProperties()
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => (string?)property.GetValue(version) ?? "")
            .ToArray();
        foreach (var secret in new[] { ElderPassword, ElderNotes, ElderTotp, FixturePassword })
        {
            Assert.False(
                carried.Any(value => value.Contains(secret, StringComparison.Ordinal)),
                "a version row carried one of the secrets this entry used to hold");
        }
    }

    [Fact]
    public async Task Restoring_a_version_reverts_the_whole_entry_and_files_the_shape_it_replaced()
    {
        var path = await StageCopyAsync();
        var entryUuid = "";
        var groupUuid = "";

        using (var session = await OpenAsync(path))
        {
            var row = Assert.Single(await session.ReadGroupRowsAsync(null));
            entryUuid = row.EntryUuid;
            groupUuid = row.GroupUuid;

            var restored = await session.RestoreHistoryAsync(entryUuid, 0);
            Assert.NotNull(restored);
            Assert.Equal(ElderTitle, restored!.Row.Title);
            Assert.Equal(DigestOf(ElderPassword), DigestOf(restored.Password));
            Assert.Equal(ElderNotes, restored.Notes);
            Assert.Equal(DigestOf(ElderTotp), DigestOf(restored.AuthenticatorKey));
            Assert.Equal(ElderField, Assert.Single(restored.CustomFields).Name);

            // The attachment is part of the version too: the newer reference goes, the elder one lands.
            Assert.Equal(ElderAttachment, Assert.Single(restored.Attachments).Row.Name);

            // Reverting is itself an edit, so the shape that was live becomes the newest version.
            var versions = await session.ReadHistoryAsync(entryUuid);
            Assert.Equal(2, versions.Count);
            Assert.Equal(LiveTitle, versions[1].Title);

            await session.SaveAsync();
        }

        var written = await File.ReadAllBytesAsync(path);
        var database = ReadDatabase(written);
        try
        {
            var entry = Assert.Single(database.RootGroup.Entries);
            Assert.Equal(ElderTitle, entry.Strings.ReadSafe(PwDefs.TitleField));
            Assert.Equal(2, (int)entry.History.UCount);
            Assert.Equal([ElderAttachment], entry.Binaries.Select(binary => binary.Key));
        }
        finally
        {
            database.Close();
        }

        // The file that came out still unlocks and still says what the restore put into it.
        using var reopened = await new KeePassVaultService()
            .OpenAsync(written, Path.GetFileName(path), FixturePassword, path);
        var detail = await reopened.ReadDetailAsync(groupUuid, entryUuid);
        Assert.NotNull(detail);
        Assert.Equal(ElderTitle, detail!.Row.Title);
        Assert.Equal(DigestOf(ElderPassword), DigestOf(detail.Password));
    }

    [Fact]
    public async Task Versions_the_edit_funnel_produced_are_the_ones_the_list_shows_oldest_first()
    {
        var path = await StageCopyAsync();
        using var session = await OpenAsync(path);
        var row = Assert.Single(await session.ReadGroupRowsAsync(null));
        var detail = await session.ReadDetailAsync(row.GroupUuid, row.EntryUuid);
        Assert.NotNull(detail);

        var renamed = await session.UpdateEntryAsync(new KeePassEntryEdit(
            row.EntryUuid,
            "Renamed once",
            detail!.Row.UserName,
            "renamed-once-secret",
            detail.Row.Url,
            detail.Notes,
            detail.AuthenticatorKey,
            detail.CustomFields));
        Assert.NotNull(renamed);

        var versions = await session.ReadHistoryAsync(row.EntryUuid);
        Assert.Equal(2, versions.Count);

        // Index 0 is the elder shape the fixture shipped with, index 1 the one the rename replaced.
        Assert.Equal(ElderTitle, versions[0].Title);
        Assert.Equal(LiveTitle, versions[1].Title);

        var backToStart = await session.RestoreHistoryAsync(row.EntryUuid, 1);
        Assert.NotNull(backToStart);
        Assert.Equal(LiveTitle, backToStart!.Row.Title);
        Assert.Equal(DigestOf(LivePassword), DigestOf(backToStart.Password));
        Assert.Equal(LiveField, Assert.Single(backToStart.CustomFields).Name);
        Assert.Equal(LiveAttachment, Assert.Single(backToStart.Attachments).Row.Name);
    }

    [Fact]
    public async Task A_version_that_is_no_longer_there_is_refused_without_touching_the_entry()
    {
        var path = await StageCopyAsync();
        using var session = await OpenAsync(path);
        var row = Assert.Single(await session.ReadGroupRowsAsync(null));

        // A history list is capped, so a version can age out between being listed and being clicked.
        Assert.Null(await session.RestoreHistoryAsync(row.EntryUuid, 5));
        Assert.Null(await session.RestoreHistoryAsync(row.EntryUuid, -1));
        Assert.Null(await session.RestoreHistoryAsync("not-an-entry-uuid", 0));
        Assert.Empty(await session.ReadHistoryAsync("not-an-entry-uuid"));

        // Nothing was half-applied by the refusals: the entry still holds the live shape.
        var detail = await session.ReadDetailAsync(row.GroupUuid, row.EntryUuid);
        Assert.NotNull(detail);
        Assert.Equal(LiveTitle, detail!.Row.Title);
        Assert.Equal(LiveField, Assert.Single(detail.CustomFields).Name);
        Assert.Equal(LiveAttachment, Assert.Single(detail.Attachments).Row.Name);
        Assert.False(session.IsDirty);
    }

    /// <summary>
    /// A secret is compared by digest rather than as an expected/actual pair, because the runner prints
    /// the actual value of a failed string assertion - and a red run must not put a password in a log.
    /// </summary>
    private static string DigestOf(string value) =>
        Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(value)));

    private async Task<string> StageCopyAsync()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "history.kdbx");
        await File.WriteAllBytesAsync(path, BuildVault());
        return path;
    }

    private static async Task<KeePassVaultSession> OpenAsync(string path) =>
        await new KeePassVaultService().OpenAsync(
            await File.ReadAllBytesAsync(path),
            Path.GetFileName(path),
            FixturePassword,
            path);

    private static PwDatabase ReadDatabase(byte[] content)
    {
        var key = new CompositeKey();
        key.AddUserKey(new KcpPassword(FixturePassword));
        var database = new PwDatabase { MasterKey = key };
        new KdbxFile(database).Load(new MemoryStream(content, writable: false), KdbxFormat.Default, null);
        return database;
    }

    /// <summary>
    /// A vault holding one entry and one version of it that differs in every field the revert claims to
    /// carry - title, password, notes, TOTP, custom field and attachment alike - so a revert that
    /// quietly kept the live shape anywhere would show up as a wrong value rather than a passing run.
    /// </summary>
    private static byte[] BuildVault() =>
        KeePassTestVault.BuildGated(
            BuildOnce,
            content =>
            {
                var database = ReadDatabase(content);
                try
                {
                    var entry = database.RootGroup.Entries.Single();
                    var elder = entry.History.ToList().Single();
                    return entry.Strings.ReadSafe(PwDefs.TitleField) == LiveTitle
                        && elder.Strings.ReadSafe(PwDefs.TitleField) == ElderTitle
                        && elder.Binaries.Select(binary => binary.Key).SequenceEqual([ElderAttachment])
                        && entry.Binaries.Select(binary => binary.Key).SequenceEqual([LiveAttachment]);
                }
                finally
                {
                    database.Close();
                }
            },
            "KeePass history fixture");

    private static byte[] BuildOnce()
    {
        var key = new CompositeKey();
        key.AddUserKey(new KcpPassword(FixturePassword));
        var database = new PwDatabase();
        database.New(IOConnectionInfo.FromPath("history.kdbx"), key);
        database.Name = "History Fixture";
        database.RootGroup.Name = "History Root";
        database.HistoryMaxItems = 10;
        database.RecycleBinEnabled = false;

        var entry = new PwEntry(true, true);
        Shape(entry, ElderTitle, ElderPassword, ElderNotes, ElderField, ElderTotp, ElderAttachment);

        var snapshot = entry.CloneDeep();
        snapshot.History.Clear();
        // The dates are anchored to the run, not to the calendar: the database this fixture carries
        // remembers versions for 365 days, and the writer prunes anything older than that window. A
        // fixed year here would quietly age out of the window one day and the test would report a
        // lost version rather than a stale constant.
        snapshot.LastModificationTime = ElderTime;
        entry.History.Add(snapshot);

        Shape(entry, LiveTitle, LivePassword, "Live note", LiveField, "", LiveAttachment);
        entry.LastModificationTime = LiveTime;
        database.RootGroup.AddEntry(entry, true);

        using var stream = new MemoryStream();
        var writer = new KdbxFile(database);
        typeof(KdbxFile)
            .GetProperty("ForceVersion", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(writer, KeePassTestVault.Kdbx41);
        writer.Save(stream, database.RootGroup, KdbxFormat.Default, null);
        var content = stream.ToArray();
        database.Close();
        return content;
    }

    private static void Shape(
        PwEntry entry,
        string title,
        string password,
        string notes,
        string fieldName,
        string totp,
        string attachmentName)
    {
        entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
        entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, "history@example.com"));
        entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, password));
        entry.Strings.Set(PwDefs.UrlField, new ProtectedString(false, EntryUrl));
        entry.Strings.Set(PwDefs.NotesField, new ProtectedString(false, notes));
        entry.Strings.Remove("otp");
        entry.Strings.Remove(ElderField);
        entry.Strings.Remove(LiveField);
        if (totp.Length > 0)
        {
            entry.Strings.Set("otp", new ProtectedString(true, totp));
        }

        entry.Strings.Set(fieldName, new ProtectedString(true, $"{fieldName} value"));
        entry.Binaries.Clear();
        entry.Binaries.Set(attachmentName, new ProtectedBinary(true, [1, 2, 3, 4]));
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
