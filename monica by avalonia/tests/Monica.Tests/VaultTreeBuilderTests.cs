using Avalonia;
using Monica.App.Controls;
using Monica.App.Features.Vault;
using Monica.Core.Models;
using Xunit;

namespace Monica.Tests;

public class VaultTreeBuilderTests
{
    private static readonly Category Bank = new() { Id = 11, Name = "Bank", SortOrder = 0 };

    private static readonly Category BankPrimary = new() { Id = 12, Name = "Bank/Primary", SortOrder = 1 };

    [Fact]
    public void Entries_filed_in_a_folder_hang_under_it_one_indent_deeper()
    {
        var rows = VaultTreeBuilder.Build(
            [Bank],
            [Password(1, "Checking", categoryId: Bank.Id)],
            [],
            [],
            new VaultTreeFilter());

        Assert.Equal(["f:Bank", "p:1"], Keys(rows));
        Assert.Equal(0, Indent(rows[0]));
        Assert.Equal(FolderTreeLayout.IndentStep, Indent(rows[1]));
        Assert.True(rows[0].HasChildren);
        Assert.True(rows[0].IsExpanded);
    }

    [Fact]
    public void Unfiled_entries_land_at_the_root_after_every_folder()
    {
        var rows = VaultTreeBuilder.Build(
            [Bank],
            [Password(1, "Loose"), Password(2, "In bank", Bank.Id)],
            [],
            [],
            new VaultTreeFilter());

        Assert.Equal(["f:Bank", "p:2", "p:1"], Keys(rows));
        Assert.Equal(0, Indent(rows[2]));
    }

    [Fact]
    public void Collapsed_folder_hides_its_entries_but_still_offers_the_chevron()
    {
        var rows = VaultTreeBuilder.Build(
            [Bank],
            [Password(1, "Checking", Bank.Id)],
            [],
            [VaultTreeKey.Folder("Bank")],
            new VaultTreeFilter());

        Assert.Equal(["f:Bank"], Keys(rows));
        Assert.True(rows[0].HasChildren);
        Assert.False(rows[0].IsExpanded);
    }

    [Fact]
    public void Folder_with_nothing_inside_has_no_chevron()
    {
        var rows = VaultTreeBuilder.Build([Bank], [], [], [], new VaultTreeFilter());

        Assert.Equal(["f:Bank"], Keys(rows));
        Assert.False(rows[0].HasChildren);
    }

    [Fact]
    public void Folders_precede_entries_and_entries_follow_sort_order_then_title()
    {
        var rows = VaultTreeBuilder.Build(
            [Bank, BankPrimary],
            [
                Password(1, "Aa", Bank.Id, sortOrder: 5),
                Password(2, "Bb", Bank.Id, sortOrder: 1),
                Password(3, "Cc", Bank.Id, sortOrder: 5)
            ],
            [],
            [],
            new VaultTreeFilter());

        Assert.Equal(["f:Bank", "f:Bank/Primary", "p:2", "p:1", "p:3"], Keys(rows));
    }

    [Fact]
    public void Intermediate_folder_created_by_a_path_carries_no_category()
    {
        var rows = VaultTreeBuilder.Build([BankPrimary], [], [], [], new VaultTreeFilter());

        var bank = Assert.IsType<VaultTreeFolderRow>(rows[0]);
        Assert.Null(bank.CategoryId);
        Assert.Equal("Bank", bank.Label);

        var primary = Assert.IsType<VaultTreeFolderRow>(rows[1]);
        Assert.Equal("Bank/Primary", primary.Path);
        Assert.Equal(BankPrimary.Id, primary.CategoryId);
        Assert.Equal(FolderTreeLayout.IndentStep, primary.Indent.Left);
    }

    [Fact]
    public void Search_keeps_matching_entries_and_their_ancestor_folders_and_forces_them_open()
    {
        var rows = VaultTreeBuilder.Build(
            [Bank, BankPrimary],
            [Password(1, "Checking", BankPrimary.Id)],
            [],
            [VaultTreeKey.Folder("Bank"), VaultTreeKey.Folder("Bank/Primary")],
            new VaultTreeFilter(Search: "check"));

        Assert.Equal(["f:Bank", "f:Bank/Primary", "p:1"], Keys(rows));
        Assert.All(rows.Where(row => row is VaultTreeFolderRow), row => Assert.True(row.IsExpanded));
    }

    [Fact]
    public void Search_prunes_folders_that_hold_no_match()
    {
        var rows = VaultTreeBuilder.Build(
            [Bank, new Category { Id = 90, Name = "Mail" }],
            [Password(1, "Checking", Bank.Id), Password(2, "Posteo", 90)],
            [],
            [],
            new VaultTreeFilter(Search: "post"));

        Assert.Equal(["f:Mail", "p:2"], Keys(rows));
    }

    [Fact]
    public void Search_reads_the_password_username_and_website_as_well_as_its_title()
    {
        var rows = VaultTreeBuilder.Build(
            [],
            [Password(1, "Work", username: "joyins"), Password(2, "Home", website: "example.org")],
            [],
            [],
            new VaultTreeFilter(Search: "joy"));

        Assert.Equal(["p:1"], Keys(rows));

        rows = VaultTreeBuilder.Build(
            [],
            [Password(1, "Work", username: "joyins"), Password(2, "Home", website: "example.org")],
            [],
            [],
            new VaultTreeFilter(Search: "example.org"));

        Assert.Equal(["p:2"], Keys(rows));
    }

    [Theory]
    [InlineData(VaultEntryGroup.Passwords, new[] { "p:1" })]
    [InlineData(VaultEntryGroup.Notes, new[] { "s:21" })]
    [InlineData(VaultEntryGroup.Totp, new[] { "s:22" })]
    [InlineData(VaultEntryGroup.Cards, new[] { "s:23" })]
    public void Each_preset_keeps_only_its_own_slice_of_the_library(
        VaultEntryGroup group,
        string[] expectedKeys)
    {
        var rows = VaultTreeBuilder.Build(
            [],
            [Password(1, "Checking")],
            [
                Secure(21, VaultItemType.Note, "Shops"),
                Secure(22, VaultItemType.Totp, "GitHub"),
                Secure(23, VaultItemType.BankCard, "Debit")
            ],
            [],
            new VaultTreeFilter(Group: group));

        Assert.Equal(expectedKeys, Keys(rows));
    }

    [Fact]
    public void Favorites_flag_drops_everything_unstarred()
    {
        var rows = VaultTreeBuilder.Build(
            [],
            [Password(1, "Checking"), Password(2, "Vault", favorite: true)],
            [Secure(21, VaultItemType.Note, "Shops", favorite: true)],
            [],
            new VaultTreeFilter(FavoritesOnly: true));

        Assert.Equal(["s:21", "p:2"], Keys(rows));
    }

    [Fact]
    public void An_unnarrowed_library_reports_itself_as_unfiltered()
    {
        Assert.False(new VaultTreeFilter().IsNarrowing);
        Assert.False(new VaultTreeFilter(Search: "   ").IsNarrowing);
        Assert.True(new VaultTreeFilter(Search: "x").IsNarrowing);
        Assert.True(new VaultTreeFilter(Group: VaultEntryGroup.Notes).IsNarrowing);
    }

    [Fact]
    public void A_password_and_a_secure_item_sharing_an_id_are_two_different_rows()
    {
        var rows = VaultTreeBuilder.Build(
            [],
            [Password(42, "Checking")],
            [Secure(42, VaultItemType.Note, "Shops")],
            [],
            new VaultTreeFilter());

        Assert.Equal(["p:42", "s:42"], Keys(rows));
    }

    [Fact]
    public void Password_typed_secure_items_never_reach_the_tree()
    {
        var rows = VaultTreeBuilder.Build(
            [],
            [],
            [Secure(31, VaultItemType.Password, "Ghost")],
            [],
            new VaultTreeFilter());

        Assert.Empty(rows);
    }

    [Fact]
    public void Entry_rows_read_as_leaves_and_carry_their_counterpart_model()
    {
        var password = Password(1, "Checking", username: "joyins");
        var note = Secure(21, VaultItemType.Note, "Shops");

        var rows = VaultTreeBuilder.Build([], [password], [note], [], new VaultTreeFilter());

        var passwordRow = Assert.IsType<VaultTreeEntryRow>(rows[0]);
        Assert.True(passwordRow.IsEntryRow);
        Assert.Equal(VaultTreeRowKind.Entry, passwordRow.RowKind);
        Assert.False(passwordRow.HasChildren);
        Assert.False(passwordRow.IsExpanded);
        Assert.Same(password, passwordRow.Password);
        Assert.Null(passwordRow.Item);

        var noteRow = Assert.IsType<VaultTreeEntryRow>(rows[1]);
        Assert.Same(note, noteRow.Item);
        Assert.Null(noteRow.Password);
    }

    [Fact]
    public void Folder_rows_declare_themselves_folders_and_render_no_entry_glyph()
    {
        var rows = VaultTreeBuilder.Build([Bank], [], [], [], new VaultTreeFilter());

        var folder = Assert.IsType<VaultTreeFolderRow>(rows[0]);
        Assert.False(folder.IsEntryRow);
        Assert.Equal(VaultTreeRowKind.Folder, folder.RowKind);
        Assert.Equal(Bank.Id, folder.CategoryId);
    }

    [Fact]
    public void Secondary_text_is_the_account_for_a_credential_and_the_kind_for_a_secure_item()
    {
        var rows = VaultTreeBuilder.Build(
            [],
            [Password(1, "Checking", username: "joyins"), Password(2, "Home", website: "example.org")],
            [Secure(21, VaultItemType.BillingAddress, "Flat")],
            [],
            new VaultTreeFilter());

        var byKey = rows.ToDictionary(row => row.Key);

        Assert.Equal("joyins", byKey["p:1"].EntryDetail);
        Assert.Equal("example.org", byKey["p:2"].EntryDetail);
        Assert.Equal("Address", byKey["s:21"].EntryDetail);
    }

    [Fact]
    public void A_nameless_entry_falls_back_to_its_kind_instead_of_rendering_a_blank_row()
    {
        var rows = VaultTreeBuilder.Build([], [Password(1, "  ")], [], [], new VaultTreeFilter());

        Assert.Equal(VaultEntryKinds.LabelFor(VaultEntryKind.Password), rows[0].Label);
    }

    private static List<string> Keys(IReadOnlyList<IVaultTreeRow> rows) => rows.Select(row => row.Key).ToList();

    private static double Indent(IVaultTreeRow row) => row.Indent.Left;

    private static PasswordEntry Password(
        long id,
        string title,
        long? categoryId = null,
        string username = "",
        string website = "",
        int sortOrder = 0,
        bool favorite = false) =>
        new()
        {
            Id = id,
            Title = title,
            CategoryId = categoryId,
            Username = username,
            Website = website,
            SortOrder = sortOrder,
            IsFavorite = favorite
        };

    private static SecureItem Secure(
        long id,
        VaultItemType itemType,
        string title,
        bool favorite = false) =>
        new()
        {
            Id = id,
            ItemType = itemType,
            Title = title,
            IsFavorite = favorite
        };
}
