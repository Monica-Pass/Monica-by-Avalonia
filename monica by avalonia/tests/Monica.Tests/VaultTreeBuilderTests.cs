using System.Diagnostics;
using Avalonia;
using Monica.App.Controls;
using Monica.App.Features.Vault;
using Monica.Core.Models;
using Monica.Core.Services;
using Xunit;

namespace Monica.Tests;

public class VaultTreeBuilderTests
{
    private static readonly Category Bank = new() { Id = 11, Name = "Bank", SortOrder = 0 };

    private static readonly Category BankPrimary = new() { Id = 12, Name = "Bank/Primary", SortOrder = 1 };

    /// Every fixture timestamp is measured off this one instant, so a test that does not pass a
    /// `…SecondsAgo` cannot drift into a different order when the clock moves.
    private static readonly DateTimeOffset BaseTime = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private const string Term = "zulu-9f3-search-term";

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
    public void Folders_precede_entries_and_the_default_sort_is_recently_updated()
    {
        var rows = VaultTreeBuilder.Build(
            [Bank, BankPrimary],
            [
                Password(1, "Oldest", Bank.Id, updatedSecondsAgo: 300),
                Password(2, "Newest", Bank.Id, updatedSecondsAgo: 1),
                Password(3, "Middle", Bank.Id, updatedSecondsAgo: 100)
            ],
            [],
            [],
            new VaultTreeFilter());

        Assert.Equal(["f:Bank", "f:Bank/Primary", "p:2", "p:3", "p:1"], Keys(rows));
    }

    [Theory]
    [InlineData("title-asc", new[] { "f:Bank", "p:3", "p:2", "p:1" })]
    [InlineData("website-asc", new[] { "f:Bank", "p:1", "p:2", "p:3" })]
    [InlineData("username-asc", new[] { "f:Bank", "p:2", "p:1", "p:3" })]
    [InlineData("created-desc", new[] { "f:Bank", "p:3", "p:1", "p:2" })]
    [InlineData("favorites-first", new[] { "f:Bank", "p:1", "p:2", "p:3" })]
    [InlineData("updated-desc", new[] { "f:Bank", "p:2", "p:1", "p:3" })]
    public void Each_sort_key_orders_the_rows_in_its_folder(string sort, string[] expectedKeys)
    {
        // Title / website / username are set so each text key gives a different answer, and the
        // timestamps are set so the two time keys disagree with them and with each other.
        var rows = VaultTreeBuilder.Build(
            [Bank],
            [
                Password(1, "Zebra", Bank.Id, website: "a.example", username: "carol",
                    updatedSecondsAgo: 50, createdSecondsAgo: 20, favorite: true),
                Password(2, "Yankee", Bank.Id, website: "b.example", username: "alice",
                    updatedSecondsAgo: 10, createdSecondsAgo: 90),
                Password(3, "Alpha", Bank.Id, website: "", username: "",
                    updatedSecondsAgo: 900, createdSecondsAgo: 5)
            ],
            [],
            [],
            new VaultTreeFilter(Sort: sort));

        Assert.Equal(expectedKeys, Keys(rows));
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

    [Fact]
    public void Search_reads_every_field_a_credential_carries()
    {
        // One field per pass, and the term only ever lands in that field: a field the tree stops
        // reading drops its own row, and the field name is what the failure has to say.
        var fields = new (string Name, Action<PasswordEntry> Write)[]
        {
            ("title", entry => entry.Title = Term),
            ("username", entry => entry.Username = Term),
            ("website", entry => entry.Website = Term),
            ("notes", entry => entry.Notes = Term),
            ("authenticatorKey", entry => entry.AuthenticatorKey = Term),
            ("appName", entry => entry.AppName = Term),
            ("appPackageName", entry => entry.AppPackageName = Term),
            ("email", entry => entry.Email = Term),
            ("phone", entry => entry.Phone = Term),
            ("addressLine", entry => entry.AddressLine = Term),
            ("city", entry => entry.City = Term),
            ("state", entry => entry.State = Term),
            ("zipCode", entry => entry.ZipCode = Term),
            ("country", entry => entry.Country = Term),
            ("creditCardHolder", entry => entry.CreditCardHolder = Term),
            ("creditCardExpiry", entry => entry.CreditCardExpiry = Term),
            ("ssoProvider", entry => entry.SsoProvider = Term),
            ("passkeyBindings", entry => entry.PasskeyBindings = Term),
            ("wifiMetadata", entry => entry.WifiMetadata = Term),
            ("sshKeyData", entry => entry.SshKeyData = Term),
            ("keepassGroupPath", entry => entry.KeepassGroupPath = Term),
            ("mdbxFolderId", entry => entry.MdbxFolderId = Term),
            ("bitwardenFolderId", entry => entry.BitwardenFolderId = Term)
        };

        foreach (var (name, write) in fields)
        {
            var tagged = new PasswordEntry { Id = 1, Title = "Untagged" };
            write(tagged);
            var rows = VaultTreeBuilder.Build(
                [],
                [tagged, Password(2, "Untagged")],
                [],
                [],
                new VaultTreeFilter(Search: Term));

            Assert.True(Keys(rows).SequenceEqual(["p:1"]), $"Search no longer reads {name}.");
        }
    }

    [Fact]
    public void Search_adds_the_metadata_ids_that_arrive_after_the_in_memory_narrowing()
    {
        var entries = new[] { Password(1, "Checking"), Password(2, "Posteo") };

        var rows = VaultTreeBuilder.Build(
            [], entries, [], [],
            new VaultTreeFilter(Search: "post", CustomFieldMatchIds: new HashSet<long> { 1 }));
        Assert.Equal(["p:1", "p:2"], Keys(rows));

        rows = VaultTreeBuilder.Build(
            [], entries, [], [],
            new VaultTreeFilter(Search: "post", AttachmentMatchIds: new HashSet<long> { 1 }));
        Assert.Equal(["p:1", "p:2"], Keys(rows));
    }

    [Fact]
    public void Search_reads_what_a_secure_item_payload_hides()
    {
        // A library row shows these entries' titles and nothing else from their payloads, so a field the
        // tree stops decoding drops its row in silence. One case per payload field, and the failure
        // names the field; the companion has the same type and no term anywhere in it.
        var cases = new (string Name, string Term, SecureItem Tagged, SecureItem Untagged)[]
        {
            ("totp issuer", Term, Totp(1, issuer: Term), Totp(2)),
            ("totp account", Term, Totp(3, accountName: Term), Totp(4)),
            ("totp type", "hotp", Totp(5, otpType: "HOTP"), Totp(6)),
            ("card number", Term, Card(7, data => data.CardNumber = Term), Card(8)),
            ("cardholder", Term, Card(9, data => data.CardholderName = Term), Card(10)),
            ("bank name", Term, Card(11, data => data.BankName = Term), Card(12)),
            ("card brand", Term, Card(13, data => data.Brand = Term), Card(14)),
            ("card billing address", Term, Card(15, data => data.BillingAddress = Term), Card(16)),
            ("document number", Term, Document(17, data => data.DocumentNumber = Term), Document(18)),
            ("document name", Term, Document(19, data => data.FullName = Term), Document(20)),
            ("document issuer", Term, Document(21, data => data.IssuedBy = Term), Document(22)),
            ("document nationality", Term, Document(23, data => data.Nationality = Term), Document(24)),
            ("document extra info", Term, Document(25, data => data.AdditionalInfo = Term), Document(26)),
            ("address name", Term, Address(27, data => data.FullName = Term), Address(28)),
            ("address company", Term, Address(29, data => data.Company = Term), Address(30)),
            ("address street", Term, Address(31, data => data.StreetAddress = Term), Address(32)),
            ("address city", Term, Address(33, data => data.City = Term), Address(34)),
            ("address province", Term, Address(35, data => data.StateProvince = Term), Address(36)),
            ("address postal code", Term, Address(37, data => data.PostalCode = Term), Address(38)),
            ("address country", Term, Address(39, data => data.Country = Term), Address(40)),
            ("address phone", Term, Address(41, data => data.Phone = Term), Address(42)),
            ("address email", Term, Address(43, data => data.Email = Term), Address(44)),
            ("account provider", Term, Account(45, data => data.Provider = Term), Account(46)),
            ("account name", Term, Account(47, data => data.AccountName = Term), Account(48)),
            ("account holder", Term, Account(49, data => data.AccountHolderName = Term), Account(50)),
            ("account email", Term, Account(51, data => data.Email = Term), Account(52)),
            ("account phone", Term, Account(53, data => data.Phone = Term), Account(54)),
            ("account username", Term, Account(55, data => data.Username = Term), Account(56)),
            ("account id", Term, Account(57, data => data.AccountId = Term), Account(58)),
            ("account masked number", Term, Account(59, data => data.MaskedAccountNumber = Term), Account(60)),
            ("account iban", Term, Account(61, data => data.Iban = Term), Account(62)),
            ("account swift bic", Term, Account(63, data => data.SwiftBic = Term), Account(64)),
            ("account website", Term, Account(65, data => data.Website = Term), Account(66)),
            ("account currency", Term, Account(67, data => data.Currency = Term), Account(68)),
            ("note body", Term, Note(69, content: $"Rotation {Term} at 3am"), Note(70)),
            ("note tag", Term, Note(71, tags: Term), Note(72))
        };

        foreach (var (name, term, tagged, untagged) in cases)
        {
            var rows = VaultTreeBuilder.Build(
                [], [], [tagged, untagged], [], new VaultTreeFilter(Search: term));
            Assert.True(
                Keys(rows).SequenceEqual([$"s:{tagged.Id}"]),
                $"Search no longer reads {name}.");
        }
    }

    [Fact]
    public void A_query_asks_every_word_to_land_somewhere()
    {
        // The note page already read a space as "and". The tree applies that to every type, so words in
        // different fields still find one entry — and one missing word still prunes it.
        var entries = new[] { Password(1, "Courier", phone: "+55 938 1122"), Password(2, "Untagged") };

        var rows = VaultTreeBuilder.Build(
            [], entries, [], [], new VaultTreeFilter(Search: "  courier, 938;; 1122 "));
        Assert.Equal(["p:1"], Keys(rows));

        rows = VaultTreeBuilder.Build(
            [], entries, [], [], new VaultTreeFilter(Search: "courier 9999"));
        Assert.Empty(Keys(rows));
    }

    [Fact]
    [Trait("Category", "perf-budget")]
    public void Searching_a_library_of_payloads_rebuilds_the_tree_within_its_budget()
    {
        // Calling the builder directly skips the view model's payload memo, so every pass pays the full
        // decode: this is the first search after a load, which is the pass that cannot be cached.
        // Measured here at 90-200 ms per rebuild, so the ceiling leaves room for a noisy CI box without
        // hiding a real regression.
        var items = Enumerable.Range(1, 5_000).Select(index => index switch
        {
            <= 1_000 => Note(index, content: $"{new string('x', 512)} body{index}"),
            <= 2_000 => Card(index, data => data.CardholderName = $"Holder {index}"),
            <= 3_000 => Totp(index, issuer: $"Issuer {index}"),
            <= 4_000 => Account(index, data => data.AccountName = $"account{index}@example.org"),
            _ => Document(index, data => data.DocumentNumber = $"D{index:D5}")
        }).ToArray();
        var filter = new VaultTreeFilter(Search: "no-such-term-anywhere");

        VaultTreeBuilder.Build([], [], items, [], filter);
        var stopwatch = Stopwatch.StartNew();
        for (var pass = 0; pass < 5; pass++)
        {
            VaultTreeBuilder.Build([], [], items, [], filter);
        }

        var perRebuild = stopwatch.ElapsedMilliseconds / 5.0;
        Assert.True(
            perRebuild < 400,
            $"A 5,000-item payload search rebuild took {perRebuild:0.##} ms.");
    }

    [Fact]
    [Trait("Category", "perf-budget")]
    public void Searching_a_large_library_rebuilds_the_tree_within_its_budget()
    {
        // A term nothing matches is the worst case: every entry walks all 23 fields, and every folder
        // survives on its subtree count. The ceiling is a cliff detector, not a benchmark.
        var entries = Enumerable.Range(1, 5_000)
            .Select(id => Password(id, $"Password {id:D5}", username: $"user{id:D5}"))
            .ToArray();
        var filter = new VaultTreeFilter(Search: "no-such-term-anywhere");

        VaultTreeBuilder.Build([], entries, [], [], filter);
        var stopwatch = Stopwatch.StartNew();
        for (var pass = 0; pass < 5; pass++)
        {
            VaultTreeBuilder.Build([], entries, [], [], filter);
        }

        var perRebuild = stopwatch.ElapsedMilliseconds / 5.0;
        Assert.True(
            perRebuild < 200,
            $"A 5,000-entry library search rebuild took {perRebuild:0.##} ms.");
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
    public void Each_quick_filter_keeps_only_the_passwords_it_describes()
    {
        var credential = Password(1, "Alpha", Bank.Id);
        credential.AuthenticatorKey = "JBSWY3DPEHPK3PXP";
        credential.HasAttachments = true;
        credential.BitwardenVaultId = 7;

        var passkey = Password(2, "Beta", Bank.Id);
        passkey.Notes = "recovery kit in the drawer";
        passkey.PasskeyBindings = """[{"credentialId":"aXk"}]""";
        passkey.KeepassDatabaseId = 3;

        var bound = Password(3, "Gamma");
        bound.BoundNoteId = 21;
        bound.Notes = "shelf";

        var cases = new (VaultQuickFilters Filter, string[] Expected)[]
        {
            (new VaultQuickFilters(TwoFactor: true), ["f:Bank", "p:1"]),
            (new VaultQuickFilters(WithNotes: true), ["f:Bank", "p:2", "p:3"]),
            (new VaultQuickFilters(Passkey: true), ["f:Bank", "p:2"]),
            (new VaultQuickFilters(BoundNote: true), ["p:3"]),
            (new VaultQuickFilters(Uncategorized: true), ["p:3"]),
            (new VaultQuickFilters(LocalOnly: true), ["p:3"]),
            (new VaultQuickFilters(WithAttachments: true), ["f:Bank", "p:1"]),
            (new VaultQuickFilters(Uncategorized: true, LocalOnly: true), ["p:3"]),
            (new VaultQuickFilters(TwoFactor: true, WithAttachments: true), ["f:Bank", "p:1"]),
            (new VaultQuickFilters(Uncategorized: true, WithAttachments: true), [])
        };

        foreach (var (filter, expected) in cases)
        {
            var rows = VaultTreeBuilder.Build(
                [Bank],
                [credential, passkey, bound],
                [],
                [],
                new VaultTreeFilter(QuickFilters: filter));

            Assert.Equal(expected, Keys(rows));
        }
    }

    [Fact]
    public void Quick_filters_speak_only_about_passwords_so_secure_items_survive_them()
    {
        // The more menu offers the filters under the passwords preset alone, so the tree never has
        // to guess what "has a passkey" means for a note — but it must not drop one either.
        var remote = Password(1, "Alpha");
        remote.BitwardenVaultId = 7;
        var rows = VaultTreeBuilder.Build(
            [],
            [remote],
            [Secure(21, VaultItemType.Note, "Shops")],
            [],
            new VaultTreeFilter(QuickFilters: new VaultQuickFilters(LocalOnly: true)));

        Assert.Equal(["s:21"], Keys(rows));
    }

    [Fact]
    public void An_unnarrowed_library_reports_itself_as_unfiltered()
    {
        Assert.False(new VaultTreeFilter().IsNarrowing);
        Assert.False(new VaultTreeFilter(Search: "   ").IsNarrowing);
        Assert.False(new VaultTreeFilter(QuickFilters: VaultQuickFilters.None).IsNarrowing);
        Assert.False(new VaultTreeFilter(Sort: "title-asc").IsNarrowing);
        Assert.True(new VaultTreeFilter(Search: "x").IsNarrowing);
        Assert.True(new VaultTreeFilter(Group: VaultEntryGroup.Notes).IsNarrowing);
        Assert.True(new VaultTreeFilter(QuickFilters: new VaultQuickFilters(TwoFactor: true)).IsNarrowing);
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

    [Fact]
    public void A_row_advertises_only_the_actions_its_entry_can_answer_to()
    {
        var rows = VaultTreeBuilder.Build(
            [Bank],
            [
                Password(1, "Work", username: "joyins", authenticatorKey: "JBSWY3DPEHPK3PXP"),
                Password(2, "Static", categoryId: Bank.Id),
            ],
            [Secure(21, VaultItemType.Totp, "Authenticator"), Secure(22, VaultItemType.Note, "Recipes")],
            [],
            new VaultTreeFilter());
        var byKey = rows.ToDictionary(row => row.Key);

        // A folder holds nothing a row action can read, so it advertises no action at all.
        var folder = byKey["f:Bank"];
        Assert.False(folder.IsBatchable);
        Assert.False(folder.CanCopyUsername);
        Assert.False(folder.CanCopySecret);
        Assert.False(folder.CanCopyCode);

        var work = byKey["p:1"];
        Assert.True(work.IsBatchable);
        Assert.True(work.CanCopyUsername);
        // The seed lives on the password row, so the code is copied from here rather than from a
        // separate authenticator leaf.
        Assert.True(work.CanCopyCode);

        var bare = byKey["p:2"];
        Assert.False(bare.CanCopyUsername);
        Assert.False(bare.CanCopyCode);

        // An authenticator item carries a code and neither half of a credential.
        var totp = byKey["s:21"];
        Assert.True(totp.IsBatchable);
        Assert.True(totp.CanCopyCode);
        Assert.False(totp.CanCopyUsername);
        Assert.False(totp.CanCopySecret);

        // No bulk command reaches a note, so its row shows no check mark.
        Assert.False(byKey["s:22"].IsBatchable);
    }

    [Fact]
    public void The_check_mark_belongs_to_the_entry_so_a_rebuild_reads_the_same_selection()
    {
        var checking = Password(1, "Checking");
        var card = Secure(21, VaultItemType.BankCard, "Debit");
        var byKey = VaultTreeBuilder
            .Build([Bank], [checking], [card], [], new VaultTreeFilter())
            .ToDictionary(row => row.Key);

        Assert.False(byKey["p:1"].IsSelected);
        byKey["p:1"].IsSelected = true;
        byKey["s:21"].IsSelected = true;
        Assert.True(checking.IsSelected);
        Assert.True(card.IsSelected);

        var rebuilt = VaultTreeBuilder
            .Build([Bank], [checking], [card], [], new VaultTreeFilter())
            .ToDictionary(row => row.Key);
        Assert.True(rebuilt["p:1"].IsSelected);
        Assert.True(rebuilt["s:21"].IsSelected);

        // A folder row has no box to check, and a stray write must not reach anything.
        byKey["f:Bank"].IsSelected = true;
        Assert.False(byKey["f:Bank"].IsSelected);
    }

    private static List<string> Keys(IReadOnlyList<IVaultTreeRow> rows) => rows.Select(row => row.Key).ToList();

    private static double Indent(IVaultTreeRow row) => row.Indent.Left;

    private static PasswordEntry Password(
        long id,
        string title,
        long? categoryId = null,
        string username = "",
        string website = "",
        string phone = "",
        bool favorite = false,
        string authenticatorKey = "",
        int updatedSecondsAgo = 0,
        int createdSecondsAgo = 0) =>
        new()
        {
            Id = id,
            Title = title,
            CategoryId = categoryId,
            Username = username,
            Website = website,
            Phone = phone,
            IsFavorite = favorite,
            AuthenticatorKey = authenticatorKey,
            UpdatedAt = BaseTime.AddSeconds(-updatedSecondsAgo),
            CreatedAt = BaseTime.AddSeconds(-createdSecondsAgo)
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
            IsFavorite = favorite,
            UpdatedAt = BaseTime,
            CreatedAt = BaseTime
        };

    // The title is a fixed neutral word on purpose: a row that survives a search is there because the
    // tree read the payload behind it, not because the fixture happened to repeat the term on screen.
    private static SecureItem PayloadItem(long id, VaultItemType itemType, string itemData) => new()
    {
        Id = id,
        ItemType = itemType,
        Title = "Untagged",
        ItemData = itemData,
        UpdatedAt = BaseTime,
        CreatedAt = BaseTime
    };

    private static T Payload<T>(Action<T>? write) where T : new()
    {
        var data = new T();
        write?.Invoke(data);
        return data;
    }

    private static SecureItem Totp(
        long id,
        string issuer = "",
        string accountName = "",
        string otpType = "TOTP") =>
        PayloadItem(
            id,
            VaultItemType.Totp,
            TotpDataResolver.ToItemData(new TotpData(
                "JBSWY3DPEHPK3PXP",
                issuer,
                accountName,
                OtpType: otpType)));

    private static SecureItem Card(long id, Action<BankCardWalletData>? write = null) =>
        PayloadItem(id, VaultItemType.BankCard, WalletItemDataCodec.EncodeBankCard(Payload(write)));

    private static SecureItem Document(long id, Action<DocumentWalletData>? write = null) =>
        PayloadItem(id, VaultItemType.Document, WalletItemDataCodec.EncodeDocument(Payload(write)));

    private static SecureItem Address(long id, Action<BillingAddressWalletData>? write = null) =>
        PayloadItem(id, VaultItemType.BillingAddress, WalletItemDataCodec.EncodeBillingAddress(Payload(write)));

    private static SecureItem Account(long id, Action<PaymentAccountWalletData>? write = null) =>
        PayloadItem(
            id,
            VaultItemType.PaymentAccount,
            WalletItemDataCodec.EncodePaymentAccount(Payload(write)));

    private static SecureItem Note(long id, string content = "", string tags = "") =>
        PayloadItem(
            id,
            VaultItemType.Note,
            NoteContentCodec.BuildSavePayload("Untagged", content, tags, isMarkdown: false).ItemData);
}
