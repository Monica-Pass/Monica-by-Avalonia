using System.Diagnostics;
using Monica.Platform.Services;

namespace Monica.Tests;

/// <summary>
/// What a search over an opened database answers: which fields it reads, what it refuses to read, how
/// it orders what it found, and how many it hands back when the answer is too big for one list.
/// </summary>
[Collection(KeePassVaultTestCollection.Name)]
public sealed class KeePassVaultSearchTests
{
    [Fact]
    public async Task An_empty_query_is_not_a_search()
    {
        var fixture = KeePassTestVault.Create("keepass-search-empty");
        using var session = await OpenAsync(fixture);

        foreach (var query in new[] { "", "   ", "\t\n" })
        {
            var results = await session.SearchEntriesAsync(query);
            Assert.Empty(results.Entries);
            Assert.Equal(0, results.TotalMatches);
            Assert.False(results.HasMore);
        }
    }

    [Fact]
    public async Task A_query_finds_an_entry_through_the_plain_text_fields_and_the_folder_it_sits_in()
    {
        var fixture = KeePassTestVault.Create("keepass-search-fields");
        using var session = await OpenAsync(fixture);

        // Each needle reaches the same two entries by a different door, so a search that only ever
        // looked at the title would show up as three of these four going empty.
        Assert.Equal(2, (await session.SearchEntriesAsync("note")).TotalMatches);
        Assert.Equal(2, (await session.SearchEntriesAsync("example.com")).TotalMatches);
        Assert.Equal(1, (await session.SearchEntriesAsync("cloud@")).TotalMatches);
        Assert.Equal(1, (await session.SearchEntriesAsync("Personal/Cloud")).TotalMatches);
    }

    [Fact]
    public async Task A_protected_value_stays_out_of_reach_of_the_query()
    {
        var fixture = KeePassTestVault.Create("keepass-search-protected");
        using var session = await OpenAsync(fixture);

        // The fixture's password and its one custom field are both protected. Typing either value has
        // to come back empty: a search that matched on secrets would turn the result list into a way to
        // ask the database yes-or-no questions about them.
        Assert.Equal(0, (await session.SearchEntriesAsync(KeePassTestVault.CloudPassword)).TotalMatches);
        Assert.Equal(0, (await session.SearchEntriesAsync("Production")).TotalMatches);
        Assert.Equal(0, (await session.SearchEntriesAsync("JBSWY3DPEHPK3PXP")).TotalMatches);

        // A protected field is skipped whole, label included: the same rule the Android browser applies,
        // where the choice is about the field rather than about which half of it gets read.
        Assert.Equal(0, (await session.SearchEntriesAsync("Tenant")).TotalMatches);
    }

    [Fact]
    public async Task A_custom_field_is_reached_by_its_value_or_by_its_label_when_it_is_plain_text()
    {
        var fixture = KeePassTestVault.Create("keepass-search-custom");
        using var session = await OpenAsync(fixture);
        var personal = fixture.Groups.Single(group => group.Path == "Personal").Uuid;
        await session.CreateEntryAsync(personal, new KeePassEntryEdit(
            "",
            "Warehouse bot",
            "",
            "custom-draft-secret",
            "",
            "",
            "",
            [new KeePassCustomField("Scope", "reports-bucket", IsProtected: false),
             new KeePassCustomField("Audit tag", "", IsProtected: false),
             new KeePassCustomField("Pin", "1234-5678", IsProtected: true)]));

        Assert.Equal(1, (await session.SearchEntriesAsync("reports-bucket")).TotalMatches);
        // An empty value still leaves the label, which is the only thing that names this entry.
        Assert.Equal(1, (await session.SearchEntriesAsync("Audit tag")).TotalMatches);
        Assert.Equal(0, (await session.SearchEntriesAsync("1234-5678")).TotalMatches);
        Assert.Equal(0, (await session.SearchEntriesAsync("custom-draft-secret")).TotalMatches);
    }

    [Fact]
    public async Task A_title_that_starts_with_the_query_outranks_the_rest_of_the_matches()
    {
        var fixture = KeePassTestVault.Create("keepass-search-ranking");
        using var session = await OpenAsync(fixture);
        var personal = fixture.Groups.Single(group => group.Path == "Personal").Uuid;
        await session.CreateEntryAsync(personal, Edit("Zeta cloud", "", "", ""));
        await session.CreateEntryAsync(personal, Edit(
            "Archive",
            "",
            "https://cloud-eu.internal",
            ""));

        var results = await session.SearchEntriesAsync("cloud");

        // Prefix on the title, then any title, then the other plain fields: the entry a person typing
        // "cloud" meant is the one called Cloud account, not whichever row the walk reached first.
        Assert.Equal(
            [KeePassTestVault.CloudTitle, "Zeta cloud", "Archive"],
            results.Entries.Select(entry => entry.Title).ToArray());
    }

    [Fact]
    public async Task A_folder_hit_reports_the_path_the_entry_was_found_under()
    {
        var fixture = KeePassTestVault.Create("keepass-search-path");
        using var session = await OpenAsync(fixture);

        var results = await session.SearchEntriesAsync("Personal/Cloud");

        var row = Assert.Single(results.Entries);
        Assert.Equal(KeePassTestVault.CloudTitle, row.Title);
        Assert.Equal("Personal/Cloud", row.GroupPath);
        Assert.False(results.HasMore);
    }

    [Fact]
    public async Task A_recycled_entry_leaves_the_results_with_the_tree()
    {
        var fixture = KeePassTestVault.Create("keepass-search-recycle");
        using var session = await OpenAsync(fixture);
        var target = fixture.Entries.Single(entry => entry.Title == KeePassTestVault.CloudTitle);
        Assert.Equal(1, (await session.SearchEntriesAsync(KeePassTestVault.CloudTitle)).TotalMatches);

        await session.DeleteEntryAsync(target.Uuid, KeePassDeleteMode.RecycleBin);

        Assert.Equal(0, (await session.SearchEntriesAsync(KeePassTestVault.CloudTitle)).TotalMatches);
        // Recyling keeps the entry in the database, so the entry is still counted - it is only out of
        // what a search will surface.
        Assert.Equal(2, session.EntryCount);
        Assert.Single(await session.ReadGroupRowsAsync(session.RecycleBinUuid!));
    }

    [Fact]
    public async Task A_hit_inside_a_recycled_folder_is_out_too()
    {
        var fixture = KeePassTestVault.Create("keepass-search-recycled-folder");
        using var session = await OpenAsync(fixture);
        var personal = fixture.Groups.Single(group => group.Path == "Personal");

        await session.DeleteEntryAsync(
            fixture.Entries.Single(entry => entry.Title == KeePassTestVault.CloudTitle).Uuid,
            KeePassDeleteMode.RecycleBin);
        // Filing the whole subtree away puts the hit one level below the bin, which is the case a walk
        // that only skipped the bin group itself would let back in.
        Assert.True(await session.MoveGroupAsync(personal.Uuid, session.RecycleBinUuid!));

        Assert.Equal(0, (await session.SearchEntriesAsync("cloud@")).TotalMatches);
        Assert.Equal(0, (await session.SearchEntriesAsync(KeePassTestVault.ExistingTitle)).TotalMatches);
    }

    [Fact]
    public async Task An_already_cancelled_walk_does_not_start()
    {
        var fixture = KeePassTestVault.Create("keepass-search-cancel");
        using var session = await OpenAsync(fixture);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => session.SearchEntriesAsync(KeePassTestVault.CloudTitle, cancellation.Token));
    }

    [Fact]
    public async Task A_long_list_reports_what_it_did_not_show()
    {
        var path = Path.Combine(Path.GetTempPath(), $"monica-search-cap-{Guid.NewGuid():N}.kdbx");
        try
        {
            const int Matched = KeePassVaultSession.SearchResultCap + 50;
            KeePassSmokeVaultWriter.Write(
                path,
                "keepass-search-cap-not-a-secret",
                Matched,
                5);
            using var session = await OpenFileAsync(path, "keepass-search-cap-not-a-secret");

            var results = await session.SearchEntriesAsync("Entry 000");

            Assert.Equal(KeePassVaultSession.SearchResultCap, results.Entries.Count);
            Assert.Equal(Matched, results.TotalMatches);
            Assert.True(results.HasMore);
            // Counting goes on past the cap, so the rows that did come back are still the ranked front
            // of the list rather than whichever entries the walk happened to reach first.
            Assert.Equal("Entry 000001", results.Entries[0].Title);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    [Trait("Category", "perf-budget")]
    public async Task Scanning_a_large_database_stays_inside_its_budget()
    {
        var path = Path.Combine(Path.GetTempPath(), $"monica-search-perf-{Guid.NewGuid():N}.kdbx");
        try
        {
            const int Entries = 20_000;
            KeePassSmokeVaultWriter.Write(path, "keepass-search-perf-not-a-secret", Entries, 20);
            using var session = await OpenFileAsync(path, "keepass-search-perf-not-a-secret");

            // Warm the path first: the first walk pays for tiered compilation, which is not a property
            // of the scan. Measured steady state on this machine is 18 ms for 20,000 entries, and a
            // query that matches nothing is the worst case because every field gets read.
            await session.SearchEntriesAsync("Entry 0001");
            var start = Stopwatch.GetTimestamp();
            var results = await session.SearchEntriesAsync("zzz-nothing-matches-this");
            var elapsed = Stopwatch.GetElapsedTime(start);

            Assert.Equal(0, results.TotalMatches);
            Assert.True(
                elapsed.TotalMilliseconds < 1_500,
                $"search_budget_exceeded elapsed={elapsed.TotalMilliseconds:F1}ms entries={Entries}");
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static KeePassEntryEdit Edit(string title, string userName, string url, string notes) =>
        new("", title, userName, "", url, notes, "", []);

    private static async Task<KeePassVaultSession> OpenAsync(KeePassTestVault.Fixture fixture) =>
        await new KeePassVaultService().OpenAsync(fixture.Content, "ledger.kdbx", fixture.Password);

    private static async Task<KeePassVaultSession> OpenFileAsync(string path, string password) =>
        await new KeePassVaultService().OpenAsync(await File.ReadAllBytesAsync(path), path, password);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
