using System.Reflection;
using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using Monica.Platform.Services;
using Xunit;

namespace Monica.Tests;

/// <summary>
/// What a database says about how much of an entry's past to keep, and whether the writer honours it.
/// The numbers live in the file rather than in this client, so the claim under test is a round trip:
/// a policy read from a vault, a policy applied to one, and the versions an edit leaves behind once a
/// policy is in force. This is also where the two special values are pinned - a cap of -1 means "no
/// cap" to every KeePass client, while 0 means "keep nothing", and reading them as one behaviour was
/// how a vault that remembered versions without limit ended up with none.
/// </summary>
[Collection(KeePassVaultTestCollection.Name)]
public sealed class KeePassHistoryPolicyTests : IDisposable
{
    private const string FixturePassword = "policy-fixture-not-a-secret";
    private const string LiveTitle = "Live shape";
    private const string EntryUrl = "https://policy.example.com";
    private const string Attachment = "payload.bin";
    private const int AttachmentBytes = 2000;

    /// <summary>
    /// What the Android client's own constants say a new database starts at, pinned here rather than
    /// read from a file so the two sides cannot drift together and stay green.
    /// </summary>
    private const int AndroidMaxItems = 10;
    private const long AndroidMaxSizeBytes = 6L * 1024L * 1024L;
    private const uint AndroidMaintenanceDays = 365;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"monica-kdbx-policy-{Guid.NewGuid():N}");

    [Fact]
    public async Task A_new_database_starts_at_the_history_policy_the_android_client_writes()
    {
        var (database, payload) = KeePassVaultCreate.Build("policy.kdbx", FixturePassword, null);
        try
        {
            using var session = await new KeePassVaultService().OpenAsync(payload, "policy.kdbx", FixturePassword);
            var policy = await session.ReadHistoryPolicyAsync();
            Assert.Equal(AndroidMaxItems, policy.MaxItems);
            Assert.Equal(AndroidMaxSizeBytes, policy.MaxSizeBytes);
            Assert.Equal(AndroidMaintenanceDays, policy.MaintenanceDays);
        }
        finally
        {
            if (database.IsOpen)
            {
                database.Close();
            }
        }
    }

    [Fact]
    public async Task An_uncapped_policy_keeps_every_version_the_edits_produce()
    {
        using var session = await OpenAsync(BuildVault(3, daysAgo: 1));
        var row = Assert.Single(await session.ReadGroupRowsAsync(null));

        var applied = await session.ApplyHistoryPolicyAsync(new KeePassHistoryPolicy(-1, -1, 10_000));
        Assert.Equal(-1, applied.MaxItems);

        await EditThriceAsync(session, row.EntryUuid);
        var versions = await session.ReadHistoryAsync(row.EntryUuid);

        // Three snapshots from the edits on top of the three the file already remembered.
        Assert.Equal(6, versions.Count);
    }

    [Fact]
    public async Task A_count_cap_drops_the_oldest_versions_first()
    {
        using var session = await OpenAsync(BuildVault(3, daysAgo: 1));
        var row = Assert.Single(await session.ReadGroupRowsAsync(null));
        await session.ApplyHistoryPolicyAsync(new KeePassHistoryPolicy(2, -1, 10_000));

        await EditThriceAsync(session, row.EntryUuid);
        var versions = await session.ReadHistoryAsync(row.EntryUuid);

        Assert.Equal(2, versions.Count);
        Assert.Equal(["edited-1", "edited-2"], versions.Select(version => version.Title).ToArray());
    }

    [Fact]
    public async Task A_size_cap_drops_versions_that_no_count_would_have_cut()
    {
        using var session = await OpenAsync(BuildVault(3, daysAgo: 1));
        var row = Assert.Single(await session.ReadGroupRowsAsync(null));

        // A count high enough that only the byte cap can bite: each remembered version carries an
        // attachment, so the limit is reached by size long before it is reached by number.
        await session.ApplyHistoryPolicyAsync(new KeePassHistoryPolicy(100, 5000, 10_000));
        await EditThriceAsync(session, row.EntryUuid);
        var versions = await session.ReadHistoryAsync(row.EntryUuid);

        Assert.True(versions.Count is > 0 and < 6, $"the byte cap left {versions.Count} versions");
        Assert.Equal("edited-2", versions[^1].Title);
        Assert.All(versions, version => Assert.Equal(1, version.AttachmentCount));
    }

    [Fact]
    public async Task A_version_older_than_the_window_does_not_survive_the_next_edit()
    {
        using var session = await OpenAsync(BuildVault(1, daysAgo: 900));
        var row = Assert.Single(await session.ReadGroupRowsAsync(null));
        await session.ApplyHistoryPolicyAsync(new KeePassHistoryPolicy(-1, -1, 365));

        await EditThriceAsync(session, row.EntryUuid);
        var versions = await session.ReadHistoryAsync(row.EntryUuid);

        // Two survivors, not three: the entry's own shape before the first edit was as far out of the
        // window as the aged version, so the first snapshot the edits made was pruned on the spot.
        Assert.Equal(2, versions.Count);
        Assert.DoesNotContain("aged-1", versions.Select(version => version.Title));
    }

    [Fact]
    public async Task A_policy_of_no_history_leaves_the_entry_without_versions()
    {
        using var session = await OpenAsync(BuildVault(2, daysAgo: 1));
        var row = Assert.Single(await session.ReadGroupRowsAsync(null));
        await session.ApplyHistoryPolicyAsync(new KeePassHistoryPolicy(0, 0, 10_000));

        await EditThriceAsync(session, row.EntryUuid);

        Assert.Empty(await session.ReadHistoryAsync(row.EntryUuid));
    }

    [Fact]
    public async Task An_applied_policy_survives_the_file_it_is_saved_to()
    {
        var path = await StageCopyAsync(BuildVault(1, daysAgo: 1));
        using (var session = await OpenAsync(path))
        {
            var row = Assert.Single(await session.ReadGroupRowsAsync(null));
            await session.ApplyHistoryPolicyAsync(new KeePassHistoryPolicy(4, 123456, 30));
            await session.UpdateEntryAsync(EditOf(row.EntryUuid, "saved once"));
            var saved = await session.SaveAsync();
            Assert.True(saved.FileBytes > 0);
        }

        using (var reopened = await OpenAsync(path))
        {
            var policy = await reopened.ReadHistoryPolicyAsync();
            Assert.Equal(4, policy.MaxItems);
            Assert.Equal(123456, policy.MaxSizeBytes);
            Assert.Equal(30u, policy.MaintenanceDays);
        }
    }

    [Fact]
    public async Task A_cap_below_the_floor_the_file_cannot_hold_is_refused_without_touching_it()
    {
        using var session = await OpenAsync(BuildVault(1, daysAgo: 1));
        var before = await session.ReadHistoryPolicyAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => session.ApplyHistoryPolicyAsync(new KeePassHistoryPolicy(-2, -1, 365)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => session.ApplyHistoryPolicyAsync(new KeePassHistoryPolicy(10, -2, 365)));

        Assert.Equal(before, await session.ReadHistoryPolicyAsync());
        Assert.False(session.IsDirty);
    }

    private async Task<string> StageCopyAsync(byte[] content)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "policy.kdbx");
        await File.WriteAllBytesAsync(path, content);
        return path;
    }

    private async Task<KeePassVaultSession> OpenAsync(byte[] content) =>
        await OpenAsync(await StageCopyAsync(content));

    private static async Task<KeePassVaultSession> OpenAsync(string path) =>
        await new KeePassVaultService().OpenAsync(
            await File.ReadAllBytesAsync(path),
            Path.GetFileName(path),
            FixturePassword,
            path);

    private static async Task EditThriceAsync(KeePassVaultSession session, string entryUuid)
    {
        for (var round = 1; round <= 3; round++)
        {
            var updated = await session.UpdateEntryAsync(EditOf(entryUuid, $"edited-{round}"));
            Assert.NotNull(updated);
        }
    }

    private static KeePassEntryEdit EditOf(string entryUuid, string title) =>
        new(entryUuid, title, "policy@example.com", "policy-live-secret", EntryUrl, "note", "", []);

    /// <summary>
    /// One entry with the asked-for number of remembered versions, each carrying an attachment so a
    /// size cap has something to measure, each dated <paramref name="daysAgo" /> so the age window has
    /// something to judge, and none of them dated so far back that an unrelated edit would prune them.
    /// </summary>
    private static byte[] BuildVault(int versions, int daysAgo) =>
        KeePassTestVault.BuildGated(
            () => BuildOnce(versions, daysAgo),
            content =>
            {
                var database = ReadDatabase(content);
                try
                {
                    var entry = database.RootGroup.Entries.Single();
                    return entry.Strings.ReadSafe(PwDefs.TitleField) == LiveTitle
                        && entry.History.Count() == versions;
                }
                finally
                {
                    database.Close();
                }
            },
            "KeePass history policy fixture");

    private static byte[] BuildOnce(int versions, int daysAgo)
    {
        var key = new CompositeKey();
        key.AddUserKey(new KcpPassword(FixturePassword));
        var database = new PwDatabase();
        database.New(IOConnectionInfo.FromPath("policy.kdbx"), key);
        database.Name = "Policy Fixture";
        database.RootGroup.Name = "Policy Root";
        database.HistoryMaxItems = AndroidMaxItems;
        database.HistoryMaxSize = AndroidMaxSizeBytes;
        database.MaintenanceHistoryDays = AndroidMaintenanceDays;
        database.RecycleBinEnabled = false;

        var entry = new PwEntry(true, true);
        Shape(entry, LiveTitle);
        entry.LastModificationTime = Utc(daysAgo);
        for (var index = 1; index <= versions; index++)
        {
            var snapshot = entry.CloneDeep();
            snapshot.History.Clear();
            Shape(snapshot, $"aged-{index}");
            snapshot.LastModificationTime = Utc(daysAgo);
            entry.History.Add(snapshot);
        }

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

    private static DateTime Utc(int daysAgo) => DateTime.UtcNow.AddDays(-daysAgo);

    private static void Shape(PwEntry entry, string title)
    {
        entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
        entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, "policy@example.com"));
        entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "policy-elder-secret"));
        entry.Strings.Set(PwDefs.UrlField, new ProtectedString(false, EntryUrl));
        entry.Strings.Set(PwDefs.NotesField, new ProtectedString(false, "note"));
        entry.Binaries.Clear();
        entry.Binaries.Set(Attachment, new ProtectedBinary(true, new byte[AttachmentBytes]));
    }

    private static PwDatabase ReadDatabase(byte[] content)
    {
        var key = new CompositeKey();
        key.AddUserKey(new KcpPassword(FixturePassword));
        var database = new PwDatabase { MasterKey = key };
        new KdbxFile(database).Load(new MemoryStream(content, writable: false), KdbxFormat.Default, null);
        return database;
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
