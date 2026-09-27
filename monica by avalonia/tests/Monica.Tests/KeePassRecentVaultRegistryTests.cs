using Monica.App.Services;

namespace Monica.Tests;

/// <summary>
/// The rules about which .kdbx files this machine remembers, asked of the registry itself. These are
/// the shape of the list - one row per file, newest first, never more than a screenful - and they hold
/// whether the file behind a row still exists or ever did.
/// </summary>
public sealed class KeePassRecentVaultRegistryTests
{
    private const string FirstPath = @"D:\vaults\ledger.kdbx";
    private const string SecondPath = @"D:\vaults\work.kdbx";

    [Fact]
    public void Remembering_a_path_twice_keeps_one_row_and_moves_it_to_the_front()
    {
        var vaults = new List<KeePassRecentVaultSetting>();
        var opened = DateTimeOffset.Parse("2026-09-01T08:00:00+00:00");
        KeePassRecentVaultRegistry.Remember(vaults, SecondPath, null, opened);
        var secondArrived = vaults[0].AddedAtUtc;
        KeePassRecentVaultRegistry.Remember(vaults, FirstPath, null, opened.AddMinutes(1));
        var firstArrived = vaults[0].AddedAtUtc;
        KeePassRecentVaultRegistry.Remember(vaults, SecondPath, null, opened.AddMinutes(2));

        var ordered = KeePassRecentVaultRegistry.Ordered(vaults);

        Assert.Equal(2, ordered.Count);
        Assert.Equal(SecondPath, ordered[0].Path);
        Assert.Equal(FirstPath, ordered[1].Path);
        // Re-opening is not re-adding: every row keeps the date it first appeared, so a list the user
        // has been building for months does not restart each time they open their main vault. Only
        // the opening date moves, and it moves on the one row that was opened.
        Assert.Equal(secondArrived, ordered[0].AddedAtUtc);
        Assert.Equal(firstArrived, ordered[1].AddedAtUtc);
        Assert.NotEqual(ordered[0].AddedAtUtc, ordered[0].LastOpenedAtUtc);
        Assert.Equal(ordered[1].AddedAtUtc, ordered[1].LastOpenedAtUtc);
    }

    [Fact]
    public void The_list_stops_at_twelve_and_drops_the_least_recent()
    {
        var vaults = new List<KeePassRecentVaultSetting>();
        var opened = DateTimeOffset.Parse("2026-09-01T08:00:00+00:00");
        for (var index = 0; index < 15; index++)
        {
            KeePassRecentVaultRegistry.Remember(
                vaults,
                $@"D:\vaults\file{index:D2}.kdbx",
                null,
                opened.AddMinutes(index));
        }

        var ordered = KeePassRecentVaultRegistry.Ordered(vaults);

        Assert.Equal(KeePassRecentVaultRegistry.Limit, ordered.Count);
        // Remember puts the newest at the front, so what falls off the back is the oldest opening.
        Assert.Equal(@"D:\vaults\file14.kdbx", ordered[0].Path);
        Assert.Equal(@"D:\vaults\file03.kdbx", ordered[^1].Path);
        Assert.All(ordered, item => Assert.False(string.IsNullOrEmpty(item.Path)));
    }

    [Fact]
    public void A_row_without_a_name_is_shown_by_its_file_name()
    {
        var vaults = new List<KeePassRecentVaultSetting>();
        KeePassRecentVaultRegistry.Remember(vaults, FirstPath, "   ", DateTimeOffset.Parse("2026-09-01T08:00:00+00:00"));

        var ordered = KeePassRecentVaultRegistry.Ordered(vaults);

        Assert.Equal("ledger.kdbx", Assert.Single(ordered).DisplayName);
    }

    [Fact]
    public void Windows_spellings_of_one_file_are_one_row()
    {
        var vaults = new List<KeePassRecentVaultSetting>();
        var opened = DateTimeOffset.Parse("2026-09-01T08:00:00+00:00");
        KeePassRecentVaultRegistry.Remember(vaults, @"d:\VAULTS\ledger.kdbx", null, opened);
        KeePassRecentVaultRegistry.Remember(vaults, @"D:\vaults\LEDGER.KDBX", null, opened.AddHours(1));

        var ordered = KeePassRecentVaultRegistry.Ordered(vaults);

        var only = Assert.Single(ordered);
        Assert.Equal(@"D:\vaults\LEDGER.KDBX", only.Path);
    }

    [Fact]
    public void A_duplicate_keeps_the_earliest_arrival_and_the_latest_opening()
    {
        List<KeePassRecentVaultSetting> vaults =
        [
            Row(FirstPath, "ledger.kdbx", "2026-05-01T00:00:00.0000000Z", "2026-09-10T00:00:00.0000000Z"),
            Row(FirstPath, "ledger.kdbx", "2026-01-01T00:00:00.0000000Z", "2026-02-02T00:00:00.0000000Z"),
            Row("", "no path at all", "2026-01-01T00:00:00.0000000Z", "2026-01-02T00:00:00.0000000Z"),
            Row(SecondPath, "", "", "2026-03-03T00:00:00.0000000Z"),
            Row(SecondPath, "", "2026-04-04T00:00:00.0000000Z", "")
        ];

        var ordered = KeePassRecentVaultRegistry.Ordered(vaults);

        Assert.Equal(2, ordered.Count);
        Assert.Equal(FirstPath, ordered[0].Path);
        Assert.Equal("2026-01-01T00:00:00.0000000Z", ordered[0].AddedAtUtc);
        Assert.Equal("2026-09-10T00:00:00.0000000Z", ordered[0].LastOpenedAtUtc);
        // A row that cannot say when it was opened still gets shown, under the name of its file.
        Assert.Equal("work.kdbx", ordered[1].DisplayName);
        Assert.Equal("2026-04-04T00:00:00.0000000Z", ordered[1].AddedAtUtc);
        Assert.Equal("2026-03-03T00:00:00.0000000Z", ordered[1].LastOpenedAtUtc);
    }

    [Fact]
    public void Forgetting_a_path_leaves_the_other_rows_where_they_were()
    {
        var vaults = new List<KeePassRecentVaultSetting>();
        var opened = DateTimeOffset.Parse("2026-09-01T08:00:00+00:00");
        KeePassRecentVaultRegistry.Remember(vaults, FirstPath, null, opened);
        KeePassRecentVaultRegistry.Remember(vaults, SecondPath, null, opened.AddMinutes(1));

        KeePassRecentVaultRegistry.Forget(vaults, @"D:\VAULTS\ledger.kdbx");

        var remaining = Assert.Single(KeePassRecentVaultRegistry.Ordered(vaults));
        Assert.Equal(SecondPath, remaining.Path);
    }

    private static KeePassRecentVaultSetting Row(
        string path,
        string displayName,
        string addedAtUtc,
        string lastOpenedAtUtc) => new()
        {
            Path = path,
            DisplayName = displayName,
            AddedAtUtc = addedAtUtc,
            LastOpenedAtUtc = lastOpenedAtUtc
        };
}
