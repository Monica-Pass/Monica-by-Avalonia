using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Serialization;
using Monica.Platform.Services;

namespace Monica.Tests;

/// <summary>
/// The recycle bin walked against a file the other client wrote. The desktop's own bin is covered by
/// <see cref="KeePassVaultRecycleBinTests"/>; what only this shape can answer is whether a bin authored by
/// kotpass 0.10.0 - the coordinate Monica for Android pins - is read as a bin at all, and whether the
/// folder pointer it leaves on a deleted entry is honoured by the way back out. The file ships with a
/// deletion record already inside it, so the two clients' deletion lists are compared in one database.
/// </summary>
public sealed class KeePassAndroidBinShapeTests
{
    private const string FixturePassword = "kdbx-parity-fixture-not-a-secret";
    private const string ZeroUuid = "00000000000000000000000000000000";
    private const uint Kdbx41 = 0x0004_0001;

    // The uuids staged by eng/kdbx/KotpassShapeFixture.kt, spelled the way that program spells them so a
    // reader can cross-check the two. Ids, not values: they are what a syncing client matches on, so a
    // test that cannot name them cannot say a deletion record survived.
    private const string OriginUuid = "11111111-2222-3333-4444-555555555555";
    private const string BinUuid = "66666666-7777-8888-9999-000000000001";
    private const string BinnedUuid = "66666666-7777-8888-9999-000000000002";
    private const string LiveUuid = "66666666-7777-8888-9999-000000000003";
    private const string DeletionOnlyUuid = "66666666-7777-8888-9999-000000000004";
    private const string BinnedTitle = "probe-binned";

    [Fact]
    public async Task An_android_authored_bin_unlocks_and_is_read_as_the_bin()
    {
        var fixture = ReadFixture();
        using var session = await OpenForPayload(fixture);

        Assert.Equal(Kdbx41, session.FormatVersion);
        Assert.Equal(2, session.EntryCount);
        Assert.Equal(1, session.RecycleBinEntryCount);
        Assert.Equal(Uuid(BinUuid), session.RecycleBinUuid);

        var origin = Assert.Single(session.Groups, group => group.Name == "Probe Origin");
        Assert.Equal(Uuid(OriginUuid), origin.Uuid);
        Assert.True(session.Groups.Single(group => group.Uuid == Uuid(BinUuid)).IsRecycleBin);

        var binned = Assert.Single(await session.ReadGroupRowsAsync(Uuid(BinUuid)));
        Assert.Equal(BinnedTitle, binned.Title);
        Assert.Equal(Uuid(BinUuid), binned.GroupUuid);

        // The deletion list Android wrote is read as it stands: one timed record for a uuid that is
        // nowhere in the tree.
        var record = Assert.Single(ReadDeletedObjects(fixture));
        Assert.Equal(Uuid(DeletionOnlyUuid), record.Uuid);
        Assert.True(record.DeletionTicks > 0);
    }

    /// <summary>
    /// The claim this file exists for: an entry Android moved into its own bin carries the folder it left,
    /// and the desktop puts it back there rather than at the root. The restore is then saved and the file
    /// reopened, because the pointer has to be cleared for the next client to read the entry as living
    /// where it now lives - which is what Monica Android's own restore does to the field.
    /// </summary>
    [Fact]
    public async Task Restoring_an_android_authored_entry_returns_it_to_the_folder_android_recorded()
    {
        byte[] payload;
        using (var session = await OpenForPayload(ReadFixture()))
        {
            var restored = await session.RestoreEntryAsync(Uuid(BinnedUuid));
            Assert.NotNull(restored);
            Assert.Equal(Uuid(OriginUuid), restored!.Row.GroupUuid);
            Assert.Equal("Probe Origin", restored.Row.GroupPath);
            Assert.Equal([], await session.ReadGroupRowsAsync(Uuid(BinUuid)));
            Assert.Equal(0, session.RecycleBinEntryCount);
            Assert.Equal(2, session.EntryCount);
            Assert.True(session.IsDirty);

            // A restore is not a deletion, so it must leave the one record the file came with.
            payload = await session.ExportAsync();
            Assert.Equal(Uuid(DeletionOnlyUuid), Assert.Single(ReadDeletedObjects(payload)).Uuid);
        }

        Assert.Equal(ZeroUuid, ReadOrigin(payload, BinnedUuid));

        using var reopened = await OpenForPayload(payload);
        var back = Assert.Single(await reopened.ReadGroupRowsAsync(Uuid(OriginUuid)));
        Assert.Equal(Uuid(BinnedUuid), back.EntryUuid);
        Assert.Equal(BinnedTitle, back.Title);
        Assert.Equal(2, reopened.EntryCount);
        Assert.Equal(0, reopened.RecycleBinEntryCount);
    }

    /// <summary>
    /// Emptying the same Android-authored bin. Two uuids leave the tree - the bin folder and the entry
    /// inside it - and both have to reach the deletion list beside the record Android already wrote, each
    /// exactly once. A client syncing against this file reads that list to tell a deletion from a folder
    /// that merely went missing.
    /// </summary>
    [Fact]
    public async Task Emptying_an_android_authored_bin_adds_every_uuid_it_took_to_the_deletion_list()
    {
        byte[] payload;
        using (var session = await OpenForPayload(ReadFixture()))
        {
            Assert.Equal(1, await session.EmptyRecycleBinAsync());

            Assert.Null(session.RecycleBinUuid);
            Assert.Equal(1, session.EntryCount);
            Assert.Equal(0, session.RecycleBinEntryCount);
            Assert.Equal(
                new[] { Uuid(LiveUuid) },
                (await session.ReadGroupRowsAsync(session.RootGroupUuid))
                    .Select(row => row.EntryUuid).ToArray());
            payload = await session.ExportAsync();
            Assert.Equal(DeletedAfterEmpty, Keys(ReadDeletedObjects(payload)));
        }

        using var reopened = await OpenForPayload(payload);
        Assert.Null(reopened.RecycleBinUuid);
        Assert.Equal(1, reopened.EntryCount);
        Assert.Equal(DeletedAfterEmpty, Keys(ReadDeletedObjects(payload)));
        Assert.Equal(0, reopened.Groups.Count(group => group.IsRecycleBin));
    }

    private static string[] DeletedAfterEmpty =>
        new[] { Uuid(BinUuid), Uuid(BinnedUuid), Uuid(DeletionOnlyUuid) }.Order(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// Both clients spell a uuid their own way - kotpass with dashes, this client without - and neither
    /// spelling is the file's. Normalised here so a failure says which uuid moved rather than which
    /// library wrote the separators.
    /// </summary>
    private static string Uuid(string uuid) =>
        uuid.Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    private static string[] Keys(IReadOnlyList<(string Uuid, long DeletionTicks)> records) =>
        records.Select(record => record.Uuid).Order(StringComparer.Ordinal).ToArray();

    private static byte[] ReadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Kdbx", "android-kotpass-bin-v1.kdbx");
        return File.ReadAllBytes(path);
    }

    private static async Task<KeePassVaultSession> OpenForPayload(byte[] payload) =>
        await new KeePassVaultService().OpenAsync(payload, "android-kotpass-bin-v1.kdbx", FixturePassword);

    private static IReadOnlyList<(string Uuid, long DeletionTicks)> ReadDeletedObjects(byte[] payload)
    {
        var database = Load(payload);
        try
        {
            return database.DeletedObjects
                .Select(item => (item.Uuid.ToHexString(), item.DeletionTime.Ticks))
                .ToArray();
        }
        finally
        {
            database.Close();
        }
    }

    /// <summary>
    /// Where the file says the entry used to live, or the reason there is no answer: a missing entry would
    /// otherwise read as an empty pointer and look like a cleared field.
    /// </summary>
    private static string ReadOrigin(byte[] payload, string entryUuid)
    {
        var database = Load(payload);
        try
        {
            return FindOrigin(database.RootGroup, Uuid(entryUuid)) ?? "origin_entry_missing";
        }
        finally
        {
            database.Close();
        }
    }

    private static string? FindOrigin(PwGroup group, string entryUuid)
    {
        foreach (var entry in group.Entries)
        {
            if (string.Equals(entry.Uuid.ToHexString(), entryUuid, StringComparison.OrdinalIgnoreCase))
            {
                return entry.PreviousParentGroup?.ToHexString() ?? ZeroUuid;
            }
        }

        return group.Groups
            .Select(child => FindOrigin(child, entryUuid))
            .FirstOrDefault(origin => origin is not null);
    }

    private static PwDatabase Load(byte[] payload)
    {
        var key = new CompositeKey();
        key.AddUserKey(new KcpPassword(FixturePassword));
        var database = new PwDatabase { MasterKey = key };
        new KdbxFile(database).Load(new MemoryStream(payload), KdbxFormat.Default, null);
        return database;
    }
}
