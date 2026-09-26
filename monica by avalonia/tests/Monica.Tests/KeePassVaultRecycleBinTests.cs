using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Serialization;
using Monica.Platform.Services;

namespace Monica.Tests;

/// <summary>
/// What happens when an entry or a folder leaves the tree: a recycle bin that is created on the first
/// recycle and reused after that, deletion records for the shapes that genuinely leave the database,
/// and a move that says "this changed place" without pretending the entry was edited.
/// </summary>
public sealed class KeePassVaultRecycleBinTests
{
    [Fact]
    public async Task Recycling_an_entry_creates_the_bin_under_the_root_and_keeps_the_entry()
    {
        var fixture = KeePassTestVault.Create("keepass-recycle-create");
        using var session = await OpenAsync(fixture);
        var cloud = fixture.Groups.Single(group => group.Path == "Personal/Cloud");
        var target = fixture.Entries.Single(entry => entry.Title == KeePassTestVault.CloudTitle);

        var status = await session.DeleteEntryAsync(target.Uuid, KeePassDeleteMode.RecycleBin);

        Assert.Equal(KeePassEntryDeleteStatus.Recycled, status);
        var bin = Assert.Single(session.Groups, group => group.IsRecycleBin);
        Assert.Equal("Recycle Bin", bin.Name);
        Assert.Equal("Recycle Bin", bin.Path);
        Assert.Equal(session.RootGroupUuid, bin.ParentUuid);
        Assert.Equal(bin.Uuid, session.RecycleBinUuid);
        Assert.True(session.IsInRecycleBin(bin.Uuid));
        Assert.False(session.IsInRecycleBin(cloud.Uuid));
        Assert.False(session.IsInRecycleBin(null));
        Assert.False(session.IsInRecycleBin(target.Uuid));

        // The entry is still in the database, only somewhere else, so the count the browser shows holds.
        Assert.Equal(2, session.EntryCount);
        var recycled = Assert.Single(await session.ReadGroupRowsAsync(bin.Uuid));
        Assert.Equal(KeePassTestVault.CloudTitle, recycled.Title);
        Assert.Equal([], await session.ReadGroupRowsAsync(cloud.Uuid));
        Assert.True(session.IsDirty);
    }

    [Fact]
    public async Task The_same_recycle_bin_serves_every_delete_and_a_dangling_pointer_gets_a_new_one()
    {
        var fixture = KeePassTestVault.Create("keepass-recycle-reuse");
        using var session = await OpenAsync(fixture);
        var cloud = fixture.Entries.Single(entry => entry.Title == KeePassTestVault.CloudTitle);
        var existing = fixture.Entries.Single(entry => entry.Title == KeePassTestVault.ExistingTitle);

        await session.DeleteEntryAsync(cloud.Uuid, KeePassDeleteMode.RecycleBin);
        var binUuid = session.RecycleBinUuid;
        await session.DeleteEntryAsync(existing.Uuid, KeePassDeleteMode.RecycleBin);

        Assert.Equal(binUuid, session.RecycleBinUuid);
        var bin = Assert.Single(session.Groups, group => group.IsRecycleBin);
        Assert.Equal(2, (await session.ReadGroupRowsAsync(bin.Uuid)).Count);

        // Deleting the bin itself leaves a pointer at a group that is no longer in the file. The next
        // recycle has to ignore it and open a fresh bin rather than drop entries into nothing.
        var removed = await session.DeleteGroupAsync(bin.Uuid, deleteContents: true);
        Assert.Equal(KeePassGroupDeleteStatus.Deleted, removed.Status);
        Assert.Null(session.RecycleBinUuid);
        Assert.False(session.IsInRecycleBin(bin.Uuid));
        Assert.Equal(0, session.EntryCount);
        Assert.Equal(
            KeePassEntryDeleteStatus.NotFound,
            await session.DeleteEntryAsync(existing.Uuid, KeePassDeleteMode.Permanent));

        await session.CreateEntryAsync(session.RootGroupUuid, new KeePassEntryEdit(
            "",
            "Replacement",
            "",
            "recycle-draft-secret",
            "",
            "",
            "",
            []));
        var replacement = Assert.Single(
            await session.ReadGroupRowsAsync(session.RootGroupUuid),
            row => row.Title == "Replacement");
        await session.DeleteEntryAsync(replacement.EntryUuid, KeePassDeleteMode.RecycleBin);

        var reborn = Assert.Single(session.Groups, group => group.IsRecycleBin);
        Assert.NotEqual(binUuid, reborn.Uuid);
        Assert.Equal(reborn.Uuid, session.RecycleBinUuid);
        Assert.Single(await session.ReadGroupRowsAsync(reborn.Uuid));
    }

    [Fact]
    public async Task A_pointer_at_the_root_is_not_allowed_to_swallow_deleted_entries()
    {
        var fixture = KeePassTestVault.Create("keepass-recycle-root-guard");
        var mispointed = RepointRecycleBinAtRoot(fixture.Content, fixture.Password);
        using var session = await new KeePassVaultService()
            .OpenAsync(mispointed, "ledger.kdbx", fixture.Password);

        await session.DeleteEntryAsync(
            fixture.Entries.Single(entry => entry.Title == KeePassTestVault.CloudTitle).Uuid,
            KeePassDeleteMode.RecycleBin);

        Assert.NotNull(session.RecycleBinUuid);
        Assert.NotEqual(session.RootGroupUuid, session.RecycleBinUuid);
        var bin = Assert.Single(session.Groups, group => group.IsRecycleBin);
        Assert.Equal(session.RootGroupUuid, bin.ParentUuid);
        Assert.Equal([], await session.ReadGroupRowsAsync(session.RootGroupUuid));
        var recycled = Assert.Single(await session.ReadGroupRowsAsync(session.RecycleBinUuid!));
        Assert.Equal(KeePassTestVault.CloudTitle, recycled.Title);
    }

    [Fact]
    public async Task Recycling_leaves_no_deletion_record_while_a_permanent_delete_writes_one()
    {
        var fixture = KeePassTestVault.Create("keepass-recycle-tombstone");
        using var session = await OpenAsync(fixture);
        var target = fixture.Entries.Single(entry => entry.Title == KeePassTestVault.CloudTitle);
        Assert.Empty(ReadDeletedObjects(fixture.Content, fixture.Password));

        await session.DeleteEntryAsync(target.Uuid, KeePassDeleteMode.RecycleBin);
        Assert.Empty(ReadDeletedObjects(await session.ExportAsync(), fixture.Password));

        var binUuid = session.RecycleBinUuid!;
        var recycledRow = Assert.Single(await session.ReadGroupRowsAsync(binUuid));
        await session.DeleteEntryAsync(recycledRow.EntryUuid, KeePassDeleteMode.Permanent);

        Assert.Equal(1, session.EntryCount);
        var tombstone = Assert.Single(
            ReadDeletedObjects(await session.ExportAsync(), fixture.Password),
            item => item.Uuid == target.Uuid);
        Assert.True(tombstone.DeletionTicks > 0);

        // Permanent deletion of an entry that is already gone reports the absence and adds no record.
        Assert.Equal(
            KeePassEntryDeleteStatus.NotFound,
            await session.DeleteEntryAsync(target.Uuid, KeePassDeleteMode.Permanent));
        Assert.Single(
            ReadDeletedObjects(await session.ExportAsync(), fixture.Password),
            item => item.Uuid == target.Uuid);
    }

    [Fact]
    public async Task Deleting_a_folder_records_every_uuid_it_took_with_it()
    {
        var fixture = KeePassTestVault.Create("keepass-recycle-group-tombstones");
        using var session = await OpenAsync(fixture);
        var personal = fixture.Groups.Single(group => group.Path == "Personal");
        var cloud = fixture.Groups.Single(group => group.Path == "Personal/Cloud");

        // A folder that still holds things is reported back before anything is removed, and a refused
        // delete records nothing - the file must not claim losses that never happened.
        var refused = await session.DeleteGroupAsync(personal.Uuid, deleteContents: false);
        Assert.Equal(KeePassGroupDeleteStatus.NotEmpty, refused.Status);
        Assert.Empty(ReadDeletedObjects(await session.ExportAsync(), fixture.Password));

        var deleted = await session.DeleteGroupAsync(personal.Uuid, deleteContents: true);

        Assert.Equal(KeePassGroupDeleteStatus.Deleted, deleted.Status);
        var tombstones = ReadDeletedObjects(await session.ExportAsync(), fixture.Password)
            .Select(item => item.Uuid)
            .ToArray();
        Assert.Contains(personal.Uuid, tombstones);
        Assert.Contains(cloud.Uuid, tombstones);
        foreach (var entry in fixture.Entries)
        {
            Assert.Contains(entry.Uuid, tombstones);
        }

        Assert.Equal(4, tombstones.Length);
        Assert.Equal(0, session.EntryCount);
    }

    [Fact]
    public async Task A_move_reports_a_new_place_without_looking_like_an_edited_entry()
    {
        var fixture = KeePassTestVault.Create("keepass-recycle-move-times");
        var personal = fixture.Groups.Single(group => group.Path == "Personal");
        var target = fixture.Entries.Single(entry => entry.Title == KeePassTestVault.CloudTitle);
        // The format stores these times to whole seconds, so a move that lands in the same second as
        // the file it came from would read back unchanged. The entry starts a decade behind.
        var start = Aged(fixture.Content, fixture.Password, target.Uuid, entry: true);
        using var session = await OpenForPayload(start, fixture.Password);
        var before = ReadEntryTimes(start, fixture.Password, target.Uuid);
        var rowBefore = await session.ReadDetailAsync(
            fixture.Groups.Single(group => group.Path == "Personal/Cloud").Uuid,
            target.Uuid);

        await session.MoveEntryAsync(target.Uuid, personal.Uuid);

        var after = ReadEntryTimes(await session.ExportAsync(), fixture.Password, target.Uuid);
        Assert.Equal(before.LastModified.Ticks, after.LastModified.Ticks);
        Assert.Equal(before.Created.Ticks, after.Created.Ticks);
        Assert.True(after.LocationChanged.Ticks > before.LocationChanged.Ticks, "location_not_advanced");
        Assert.Equal(
            rowBefore!.Row.UpdatedAt.Ticks,
            (await session.ReadDetailAsync(personal.Uuid, target.Uuid))!.Row.UpdatedAt.Ticks);
    }

    [Fact]
    public async Task A_moved_folder_reports_a_new_place_and_keeps_its_entries_intact()
    {
        var fixture = KeePassTestVault.Create("keepass-recycle-group-move");
        var cloud = fixture.Groups.Single(group => group.Path == "Personal/Cloud");
        var start = Aged(fixture.Content, fixture.Password, cloud.Uuid, entry: false);
        using var session = await OpenForPayload(start, fixture.Password);
        var before = ReadGroupTimes(start, fixture.Password, cloud.Uuid);
        var entry = fixture.Entries.Single(item => item.Title == KeePassTestVault.CloudTitle);
        var rowBefore = (await session.ReadGroupRowsAsync(cloud.Uuid)).Single();

        Assert.True(await session.MoveGroupAsync(cloud.Uuid, session.RootGroupUuid));

        Assert.Equal("Cloud", session.Groups.Single(group => group.Uuid == cloud.Uuid).Path);
        var after = ReadGroupTimes(await session.ExportAsync(), fixture.Password, cloud.Uuid);
        Assert.Equal(before.LastModified.Ticks, after.LastModified.Ticks);
        Assert.True(after.LocationChanged.Ticks > before.LocationChanged.Ticks, "location_not_advanced");
        var relocated = Assert.Single(
            await session.ReadGroupRowsAsync(cloud.Uuid),
            row => row.EntryUuid == entry.Uuid);
        Assert.Equal(rowBefore.UpdatedAt.Ticks, relocated.UpdatedAt.Ticks);
    }

    [Fact]
    public async Task A_recycled_entry_survives_the_file_and_reopens_inside_the_bin()
    {
        var fixture = KeePassTestVault.Create("keepass-recycle-round-trip");
        var permanent = fixture.Entries.Single(entry => entry.Title == KeePassTestVault.ExistingTitle);
        var path = TestTempPaths.CreateFilePath(".kdbx");
        using (var session = await OpenAsync(fixture))
        {
            await session.DeleteEntryAsync(
                fixture.Entries.Single(entry => entry.Title == KeePassTestVault.CloudTitle).Uuid,
                KeePassDeleteMode.RecycleBin);
            await session.DeleteEntryAsync(permanent.Uuid, KeePassDeleteMode.Permanent);
            await File.WriteAllBytesAsync(path, fixture.Content);
            await session.SaveToAsync(path);
        }

        var written = await File.ReadAllBytesAsync(path);
        using var reopened = await new KeePassVaultService()
            .OpenAsync(written, Path.GetFileName(path), fixture.Password);

        var bin = Assert.Single(reopened.Groups, group => group.IsRecycleBin);
        Assert.Equal(bin.Uuid, reopened.RecycleBinUuid);
        Assert.Equal(1, reopened.EntryCount);
        var survivor = Assert.Single(await CollectRowsAsync(reopened));
        Assert.Equal(KeePassTestVault.CloudTitle, survivor.Title);
        Assert.Equal("Recycle Bin", survivor.GroupPath);
        var recorded = Assert.Single(
            ReadDeletedObjects(written, fixture.Password),
            item => item.Uuid == permanent.Uuid);
        Assert.True(recorded.DeletionTicks > 0);
    }

    /// <summary>
    /// A bin the user filed away from the root is still the bin: the pointer has to find the folder it
    /// names, not the folder that happens to hold it.
    /// </summary>
    [Fact]
    public async Task A_recycle_bin_filed_below_the_root_is_still_the_bin()
    {
        var fixture = KeePassTestVault.Create("keepass-recycle-nested-bin");
        var cloud = fixture.Entries.Single(entry => entry.Title == KeePassTestVault.CloudTitle);
        var personal = fixture.Groups.Single(group => group.Path == "Personal");
        string binUuid;
        byte[] written;

        using (var session = await OpenAsync(fixture))
        {
            await session.DeleteEntryAsync(cloud.Uuid, KeePassDeleteMode.RecycleBin);
            binUuid = session.RecycleBinUuid ?? throw new InvalidOperationException("bin_pointer_missing");
            Assert.True(await session.MoveGroupAsync(binUuid, personal.Uuid));
            Assert.True(session.IsInRecycleBin(binUuid));
            written = await session.ExportAsync();
        }

        using var reopened = await OpenForPayload(written, fixture.Password);

        Assert.Equal(binUuid, reopened.RecycleBinUuid);
        Assert.True(reopened.IsInRecycleBin(binUuid));
        var bin = Assert.Single(reopened.Groups, group => group.IsRecycleBin);
        Assert.Equal("Personal/Recycle Bin", bin.Path);
        var survivor = Assert.Single(await reopened.ReadGroupRowsAsync(bin.Uuid));
        Assert.Equal(KeePassTestVault.CloudTitle, survivor.Title);
    }

    private static async Task<KeePassVaultSession> OpenAsync(KeePassTestVault.Fixture fixture) =>
        await new KeePassVaultService().OpenAsync(fixture.Content, "ledger.kdbx", fixture.Password);

    private static async Task<KeePassVaultSession> OpenForPayload(byte[] payload, string password) =>
        await new KeePassVaultService().OpenAsync(payload, "ledger.kdbx", password);

    /// <summary>
    /// Pushes one object's times a decade into the past. A move stamps the place with the current
    /// second, and a file written a moment ago would otherwise compare equal to itself.
    /// </summary>
    private static byte[] Aged(byte[] payload, string password, string uuid, bool entry)
    {
        var past = new DateTime(2016, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        var database = Load(payload, password);
        try
        {
            if (entry)
            {
                var found = FindEntryOrNull(database.RootGroup, uuid)
                    ?? throw new InvalidOperationException("entry_not_found");
                found.CreationTime = past;
                found.LastModificationTime = past;
                found.LocationChanged = past;
            }
            else
            {
                var found = FindGroupOrNull(database.RootGroup, uuid)
                    ?? throw new InvalidOperationException("group_not_found");
                found.CreationTime = past;
                found.LastModificationTime = past;
                found.LocationChanged = past;
            }

            return Save(database);
        }
        finally
        {
            database.Close();
        }
    }

    /// <summary>
    /// Writes a database that claims the root folder as its recycle bin, the shape a file gets when a
    /// client points the pointer at something that is not a bin.
    /// </summary>
    private static byte[] RepointRecycleBinAtRoot(byte[] payload, string password)
    {
        var database = Load(payload, password);
        database.RecycleBinEnabled = true;
        database.RecycleBinUuid = database.RootGroup.Uuid;
        var written = Save(database);
        database.Close();
        return written;
    }

    private static IReadOnlyList<(string Uuid, long DeletionTicks)> ReadDeletedObjects(
        byte[] payload,
        string password)
    {
        var database = Load(payload, password);
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

    private static (DateTime Created, DateTime LastModified, DateTime LocationChanged) ReadEntryTimes(
        byte[] payload,
        string password,
        string entryUuid)
    {
        var database = Load(payload, password);
        try
        {
            var entry = FindEntry(database.RootGroup, entryUuid);
            return (entry.CreationTime, entry.LastModificationTime, entry.LocationChanged);
        }
        finally
        {
            database.Close();
        }
    }

    private static (DateTime LastModified, DateTime LocationChanged) ReadGroupTimes(
        byte[] payload,
        string password,
        string groupUuid)
    {
        var database = Load(payload, password);
        try
        {
            var group = FindGroup(database.RootGroup, groupUuid);
            return (group.LastModificationTime, group.LocationChanged);
        }
        finally
        {
            database.Close();
        }
    }

    private static PwEntry FindEntry(PwGroup group, string uuid) =>
        FindEntryOrNull(group, uuid) ?? throw new InvalidOperationException("entry_not_found");

    private static PwEntry? FindEntryOrNull(PwGroup group, string uuid)
    {
        var direct = group.Entries.FirstOrDefault(
            item => string.Equals(item.Uuid.ToHexString(), uuid, StringComparison.OrdinalIgnoreCase));
        if (direct is not null)
        {
            return direct;
        }

        foreach (var child in group.Groups)
        {
            var nested = FindEntryOrNull(child, uuid);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static PwGroup FindGroup(PwGroup group, string uuid) =>
        FindGroupOrNull(group, uuid) ?? throw new InvalidOperationException("group_not_found");

    private static PwGroup? FindGroupOrNull(PwGroup group, string uuid)
    {
        foreach (var child in group.Groups)
        {
            if (string.Equals(child.Uuid.ToHexString(), uuid, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }

            if (FindGroupOrNull(child, uuid) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private static PwDatabase Load(byte[] payload, string password)
    {
        var key = new CompositeKey();
        key.AddUserKey(new KcpPassword(password));
        var database = new PwDatabase { MasterKey = key };
        new KdbxFile(database).Load(new MemoryStream(payload, writable: false), KdbxFormat.Default, null);
        return database;
    }

    private static byte[] Save(PwDatabase database)
    {
        using var stream = new MemoryStream();
        new KdbxFile(database).Save(stream, database.RootGroup, KdbxFormat.Default, null);
        return stream.ToArray();
    }

    private static async Task<IReadOnlyList<KeePassEntryRow>> CollectRowsAsync(KeePassVaultSession session)
    {
        var rows = new List<KeePassEntryRow>();
        foreach (var group in session.Groups.Prepend(session.RootGroupRow))
        {
            rows.AddRange(await session.ReadGroupRowsAsync(group.Uuid));
        }

        return rows;
    }
}
