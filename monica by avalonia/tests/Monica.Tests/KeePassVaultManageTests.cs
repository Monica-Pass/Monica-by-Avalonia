using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Serialization;
using Monica.Platform.Services;

namespace Monica.Tests;

/// <summary>
/// Structural edits of an unlocked database: folders and entries created, renamed, moved and
/// deleted, and the counts and paths the browser shows afterwards describing the database as it now
/// is. The last test proves a mutation survives the trip to disk, because an in-memory tree nobody
/// can re-open is not a KeePass client.
/// </summary>
public sealed class KeePassVaultManageTests
{
    [Fact]
    public async Task Create_group_lands_under_the_parent_and_the_index_sees_it()
    {
        var fixture = KeePassTestVault.Create("keepass-manage-group-create");
        using var session = await OpenAsync(fixture);
        var personal = fixture.Groups.Single(group => group.Path == "Personal");

        var created = await session.CreateGroupAsync(personal.Uuid, "  Bank accounts  ");

        Assert.NotNull(created);
        Assert.Equal("Bank accounts", created!.Name);
        Assert.Equal("Personal/Bank accounts", created.Path);
        Assert.Equal(personal.Uuid, created.ParentUuid);
        Assert.Equal(3, session.GroupCount);
        Assert.Contains(created.Uuid, session.Groups.Select(group => group.Uuid));
        Assert.True(session.IsDirty);
    }

    [Fact]
    public async Task Rename_folder_renames_it_and_the_paths_below_follow()
    {
        var fixture = KeePassTestVault.Create("keepass-manage-group-rename");
        using var session = await OpenAsync(fixture);
        var personal = fixture.Groups.Single(group => group.Path == "Personal");

        var bank = await session.CreateGroupAsync(personal.Uuid, "Bank");
        var child = await session.CreateGroupAsync(bank!.Uuid, "Current");
        var renamed = await session.RenameGroupAsync(bank.Uuid, "Finance");

        Assert.NotNull(renamed);
        Assert.Equal("Personal/Finance", renamed!.Path);
        Assert.Equal(
            "Personal/Finance/Current",
            session.Groups.Single(group => group.Uuid == child!.Uuid).Path);
        Assert.Null(await session.RenameGroupAsync(personal.Uuid, "   "));
        Assert.Equal("Personal", session.Groups.Single(group => group.Uuid == personal.Uuid).Path);
    }

    [Fact]
    public async Task Moving_a_folder_reparents_it_and_refuses_the_moves_that_would_lose_it()
    {
        var fixture = KeePassTestVault.Create("keepass-manage-group-move");
        using var session = await OpenAsync(fixture);
        var personal = fixture.Groups.Single(group => group.Path == "Personal");
        var nested = await session.CreateGroupAsync(personal.Uuid, "Nested");

        // A folder dropped into its own child would leave the tree through the branch being moved.
        Assert.False(await session.MoveGroupAsync(personal.Uuid, nested!.Uuid));
        Assert.False(await session.MoveGroupAsync(nested.Uuid, nested.Uuid));

        Assert.True(await session.MoveGroupAsync(nested.Uuid, session.RootGroupUuid));
        Assert.Equal("Nested", session.Groups.Single(group => group.Uuid == nested.Uuid).Path);

        var dirty = session.IsDirty;
        Assert.False(await session.MoveGroupAsync(nested.Uuid, session.RootGroupUuid));
        Assert.Equal(dirty, session.IsDirty);
        Assert.Equal("Personal", session.Groups.Single(group => group.Uuid == personal.Uuid).Path);
    }

    [Fact]
    public async Task A_folder_holding_anything_is_reported_before_it_is_emptied()
    {
        var fixture = KeePassTestVault.Create("keepass-manage-group-refuse");
        using var session = await OpenAsync(fixture);
        var personal = fixture.Groups.Single(group => group.Path == "Personal");
        var groupsBefore = session.Groups.Select(group => group.Uuid).ToArray();
        var entriesBefore = session.EntryCount;

        var refused = await session.DeleteGroupAsync(personal.Uuid, deleteContents: false);

        Assert.Equal(KeePassGroupDeleteStatus.NotEmpty, refused.Status);
        Assert.Equal(2, refused.EntryCount);
        Assert.Equal(1, refused.GroupCount);
        Assert.False(session.IsDirty);
        Assert.Equal(entriesBefore, session.EntryCount);
        Assert.Equal(groupsBefore, session.Groups.Select(group => group.Uuid));

        var deleted = await session.DeleteGroupAsync(personal.Uuid, deleteContents: true);

        Assert.Equal(KeePassGroupDeleteStatus.Deleted, deleted.Status);
        Assert.Equal(2, deleted.EntryCount);
        Assert.Equal([], session.Groups);
        Assert.Equal(0, session.EntryCount);
        Assert.True(session.IsDirty);
    }

    [Fact]
    public async Task The_root_folder_is_not_a_deletable_renamable_or_movable_row()
    {
        var fixture = KeePassTestVault.Create("keepass-manage-root-guard");
        using var session = await OpenAsync(fixture);

        Assert.Equal(
            KeePassGroupDeleteStatus.NotFound,
            (await session.DeleteGroupAsync(session.RootGroupUuid, deleteContents: true)).Status);
        Assert.False(await session.MoveGroupAsync(session.RootGroupUuid, fixture.Groups[0].Uuid));
        Assert.Null(await session.RenameGroupAsync(session.RootGroupUuid, "Renamed root"));
        Assert.Equal(2, session.EntryCount);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public async Task Creating_an_entry_fills_it_from_the_draft_and_counts_it()
    {
        var fixture = KeePassTestVault.Create("keepass-manage-entry-create");
        using var session = await OpenAsync(fixture);
        var personal = fixture.Groups.Single(group => group.Path == "Personal");

        var created = await session.CreateEntryAsync(personal.Uuid, new KeePassEntryEdit(
            "",
            "Card PIN",
            "me@example.com",
            "drafted-secret",
            "https://card.example.com",
            "Issued 2026",
            "",
            [new KeePassCustomField("Issuer", "Local Bank", IsProtected: true)]));

        Assert.NotNull(created);
        Assert.Equal("Card PIN", created!.Row.Title);
        Assert.Equal("Personal", created.Row.GroupPath);
        Assert.Equal("drafted-secret", created.Password);
        Assert.Equal(3, session.EntryCount);
        Assert.True(session.IsDirty);

        var reloaded = await session.ReadDetailAsync(personal.Uuid, created.Row.EntryUuid);
        Assert.NotNull(reloaded);
        Assert.Equal("Local Bank", Assert.Single(reloaded!.CustomFields).Value);

        Assert.Null(await session.CreateEntryAsync(
            "not-a-folder-uuid",
            new KeePassEntryEdit("", "Dropped", "", "", "", "", "", [])));
        Assert.Equal(3, session.EntryCount);
    }

    [Fact]
    public async Task A_created_entry_carries_no_history_snapshot_of_itself_but_an_edited_one_does()
    {
        var fixture = KeePassTestVault.Create("keepass-manage-entry-history");
        using var session = await OpenAsync(fixture);
        var personal = fixture.Groups.Single(group => group.Path == "Personal");

        var created = await session.CreateEntryAsync(personal.Uuid, new KeePassEntryEdit(
            "", "First card", "", "secret", "", "", "", []));
        var edited = await session.UpdateEntryAsync(new KeePassEntryEdit(
            fixture.Entries.Single(entry => entry.Title == KeePassTestVault.ExistingTitle).Uuid,
            "Renamed",
            "existing@example.com",
            "source-secret",
            "",
            "",
            "",
            []));

        var payload = await session.ExportAsync();
        var historyCounts = ReadHistoryCounts(payload, fixture.Password);

        // KPCLib's default history limit is non-zero, so a create that ran the whole edit path would
        // file the entry's own blank shape as its first backup.
        Assert.Equal(0, historyCounts[created!.Row.EntryUuid]);
        Assert.Equal(1, historyCounts[edited!.Row.EntryUuid]);
    }

    [Fact]
    public async Task Deleting_an_entry_permanently_drops_it_from_the_group_and_the_counts()
    {
        var fixture = KeePassTestVault.Create("keepass-manage-entry-delete");
        using var session = await OpenAsync(fixture);
        var cloud = fixture.Groups.Single(group => group.Path == "Personal/Cloud");
        var target = fixture.Entries.Single(entry => entry.Title == KeePassTestVault.CloudTitle);

        Assert.Equal(
            KeePassEntryDeleteStatus.PermanentlyDeleted,
            await session.DeleteEntryAsync(target.Uuid, KeePassDeleteMode.Permanent));
        Assert.Equal(1, session.EntryCount);
        Assert.Equal([], await session.ReadGroupRowsAsync(cloud.Uuid));
        Assert.Null(await session.ReadDetailAsync(cloud.Uuid, target.Uuid));
        Assert.Equal(
            KeePassEntryDeleteStatus.NotFound,
            await session.DeleteEntryAsync(target.Uuid, KeePassDeleteMode.Permanent));
        Assert.True(session.IsDirty);
    }

    [Fact]
    public async Task Moving_an_entry_changes_its_folder_and_refuses_the_folder_it_is_in()
    {
        var fixture = KeePassTestVault.Create("keepass-manage-entry-move");
        using var session = await OpenAsync(fixture);
        var cloud = fixture.Groups.Single(group => group.Path == "Personal/Cloud");
        var personal = fixture.Groups.Single(group => group.Path == "Personal");
        var target = fixture.Entries.Single(entry => entry.Title == KeePassTestVault.CloudTitle);

        var moved = await session.MoveEntryAsync(target.Uuid, personal.Uuid);

        Assert.NotNull(moved);
        Assert.Equal("Personal", moved!.Row.GroupPath);
        Assert.Equal(personal.Uuid, moved.Row.GroupUuid);
        Assert.Equal(2, session.EntryCount);
        Assert.Equal([], await session.ReadGroupRowsAsync(cloud.Uuid));
        Assert.True(session.IsDirty);

        // The folder the entry already sits in, a folder that is gone, and an entry that is gone are
        // all refused without touching the counts.
        Assert.Null(await session.MoveEntryAsync(target.Uuid, personal.Uuid));
        Assert.Null(await session.MoveEntryAsync(target.Uuid, "not-a-folder-uuid"));
        Assert.Null(await session.MoveEntryAsync("not-an-entry-uuid", cloud.Uuid));
        Assert.Equal(2, session.EntryCount);

        var payload = await session.ExportAsync();
        using var reopened = await new KeePassVaultService()
            .OpenAsync(payload, "ledger.kdbx", fixture.Password);
        var onDisk = Assert.Single(
            await CollectRowsAsync(reopened),
            row => row.Title == KeePassTestVault.CloudTitle);
        Assert.Equal("Personal", onDisk.GroupPath);
    }

    [Fact]
    public async Task Structural_edits_survive_the_file_and_reopen_in_the_same_shape()
    {
        var fixture = KeePassTestVault.Create("keepass-manage-round-trip");
        var path = TestTempPaths.CreateFilePath(".kdbx");
        var personal = fixture.Groups.Single(group => group.Path == "Personal");
        using (var session = await OpenAsync(fixture))
        {
            var bank = await session.CreateGroupAsync(personal.Uuid, "Bank");
            await session.CreateGroupAsync(bank!.Uuid, "Current");
            await session.CreateEntryAsync(bank.Uuid, new KeePassEntryEdit(
                "", "First card", "me@example.com", "round-trip-secret", "", "", "", []));
            await session.MoveGroupAsync(
                fixture.Groups.Single(group => group.Path == "Personal/Cloud").Uuid,
                bank.Uuid);
            await session.DeleteEntryAsync(
                fixture.Entries.Single(entry => entry.Title == KeePassTestVault.ExistingTitle).Uuid,
                KeePassDeleteMode.Permanent);
            await File.WriteAllBytesAsync(path, fixture.Content);
            await session.SaveToAsync(path);
        }

        var written = await File.ReadAllBytesAsync(path);
        using var reopened = await new KeePassVaultService()
            .OpenAsync(written, Path.GetFileName(path), fixture.Password);

        Assert.Equal(4, reopened.GroupCount);
        Assert.Equal(2, reopened.EntryCount);
        Assert.Equal(
            ["Personal", "Personal/Bank", "Personal/Bank/Cloud", "Personal/Bank/Current"],
            reopened.Groups.Select(group => group.Path).OrderBy(path => path, StringComparer.Ordinal));
        var card = Assert.Single(
            await CollectRowsAsync(reopened),
            row => row.Title == "First card");
        Assert.Equal("Personal/Bank", card.GroupPath);
        Assert.DoesNotContain(
            KeePassTestVault.ExistingTitle,
            (await CollectRowsAsync(reopened)).Select(row => row.Title));
    }

    private static async Task<KeePassVaultSession> OpenAsync(KeePassTestVault.Fixture fixture) =>
        await new KeePassVaultService().OpenAsync(fixture.Content, "ledger.kdbx", fixture.Password);

    /// <summary>
    /// Reads a written payload with the raw library rather than through the session, because history
    /// is exactly what the session's read model leaves out.
    /// </summary>
    private static Dictionary<string, int> ReadHistoryCounts(byte[] payload, string password)
    {
        var key = new CompositeKey();
        key.AddUserKey(new KcpPassword(password));
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var database = new PwDatabase { MasterKey = key };
        new KdbxFile(database).Load(new MemoryStream(payload, writable: false), KdbxFormat.Default, null);
        CountHistory(database.RootGroup, counts);
        database.Close();
        return counts;
    }

    private static void CountHistory(PwGroup group, Dictionary<string, int> counts)
    {
        foreach (var entry in group.Entries)
        {
            counts[entry.Uuid.ToHexString()] = (int)entry.History.UCount;
        }

        foreach (var child in group.Groups)
        {
            CountHistory(child, counts);
        }
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
