using System.Windows.Input;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Controls;
using Monica.App.Features.Vault;
using Monica.App.ViewModels;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class VaultWorkspaceUiTests
{
    public VaultWorkspaceUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Library_header_creates_the_kind_the_active_filter_names()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            viewModel.SelectSectionCommand.Execute("Vault");
            Dispatcher.UIThread.RunJobs();

            var workspace = Assert.Single(window.GetVisualDescendants().OfType<VaultWorkspaceView>());
            var presetButton = workspace.FindControl<Button>("VaultCreatePresetButton")!;
            var menuButton = workspace.FindControl<Button>("VaultCreateMenuButton")!;

            var kinds = new (string Section, VaultEntryGroup Group, ICommand Command)[]
            {
                ("Passwords", VaultEntryGroup.Passwords, viewModel.AddPasswordCommand),
                ("Notes", VaultEntryGroup.Notes, viewModel.AddNoteCommand),
                ("Totp", VaultEntryGroup.Totp, viewModel.AddTotpCommand),
                ("Cards", VaultEntryGroup.Cards, viewModel.AddWalletItemCommand)
            };

            // With every kind in view none of them is the one meant, so the header has to ask — and a
            // closed flyout has realized nothing, so it has to be opened before its bindings read back.
            Assert.False(presetButton.IsVisible);
            Assert.Null(presetButton.Command);
            Assert.True(menuButton.IsVisible);
            var flyout = Assert.IsType<MenuFlyout>(menuButton.Flyout);
            flyout.ShowAt(menuButton);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(
                kinds.Select(kind => kind.Command),
                flyout.Items.OfType<MenuItem>().Select(item => item.Command).ToArray());
            flyout.Hide();

            foreach (var (section, group, command) in kinds)
            {
                viewModel.SelectSectionCommand.Execute(section);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(group, viewModel.VaultGroup);
                Assert.True(presetButton.IsVisible);
                Assert.Same(command, presetButton.Command);
                Assert.False(menuButton.IsVisible);
                var filter = new VaultTreeFilter(group);
                Assert.All(
                    viewModel.VaultTreeRows.OfType<VaultTreeEntryRow>(),
                    row => Assert.True(filter.Matches(row.Kind)));
            }
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Every_type_section_is_one_page()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;

            foreach (var section in new[] { "Vault", "Passwords", "Notes", "Totp", "Cards", "Vault" })
            {
                viewModel.SelectSectionCommand.Execute(section);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(section, viewModel.SelectedSection);
                Assert.Single(window.GetVisualDescendants().OfType<VaultWorkspaceView>());
                var host = Assert.Single(window.GetVisualDescendants().OfType<WorkspaceHostView>());
                Assert.Equal(new[] { "Vault" }, host.CreatedSections);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Library_escape_unwinds_search_then_selection_then_favorites()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            viewModel.Passwords.Add(new Monica.Core.Models.PasswordEntry { Title = "Alpha mail", IsFavorite = true });
            viewModel.Passwords.Add(new Monica.Core.Models.PasswordEntry { Title = "Beta bank" });
            viewModel.SelectSectionCommand.Execute("Passwords");
            viewModel.VaultFavoritesOnly = true;
            viewModel.VaultSearchText = "Alpha";
            Dispatcher.UIThread.RunJobs();

            var workspace = Assert.Single(window.GetVisualDescendants().OfType<VaultWorkspaceView>());
            var searchBox = workspace.FindControl<TextBox>("VaultSearchBox")!;
            searchBox.Focus();

            Assert.True(TryLibraryKey(workspace, viewModel, searchBox, Key.Escape));
            Assert.Empty(viewModel.VaultSearchText);
            Assert.True(viewModel.VaultFavoritesOnly);

            viewModel.SelectedVaultRow = viewModel.VaultTreeRows.OfType<VaultTreeEntryRow>().First();
            Dispatcher.UIThread.RunJobs();
            Assert.True(TryLibraryKey(workspace, viewModel, searchBox, Key.Escape));
            Assert.Null(viewModel.SelectedVaultRow);

            Assert.True(TryLibraryKey(workspace, viewModel, searchBox, Key.Escape));
            Assert.False(viewModel.VaultFavoritesOnly);
            Assert.False(TryLibraryKey(workspace, viewModel, searchBox, Key.Escape));
        }
        finally
        {
            window.Close();
        }
    }

    // Ctrl+F is one of the keys the four deleted pages answered, and this is the only search field
    // left in the app, so the shortcut and the name a screen reader reads have to stay wired.
    [Fact]
    public void Library_ctrl_f_hands_the_caret_to_the_search_field()
    {
        using var library = LibraryUiHarness.Open();
        var searchBox = library.Workspace.FindControl<TextBox>("VaultSearchBox")!;
        var headerButton = library.Workspace.FindControl<Button>("VaultCreateMenuButton")!;

        headerButton.Focus();
        library.Settle();
        Assert.False(searchBox.IsFocused);

        Assert.True(
            TryLibraryKey(library.Workspace, library.ViewModel, headerButton, Key.F, KeyModifiers.Control));
        library.Settle();

        Assert.True(searchBox.IsFocused);
        Assert.Equal(library.ViewModel.VaultSearchHelpText, AutomationProperties.GetHelpText(searchBox));
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(searchBox)));
    }

    [Fact]
    public void Control_a_checks_every_batchable_row_the_library_shows()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            viewModel.Passwords.Add(Entry(1, "Alpha", 100));
            viewModel.NoteItems.Add(new Monica.Core.Models.SecureItem
            {
                Id = 41,
                ItemType = Monica.Core.Models.VaultItemType.Note,
                Title = "Recipe"
            });
            viewModel.SelectSectionCommand.Execute("Vault");
            Dispatcher.UIThread.RunJobs();

            var workspace = Assert.Single(window.GetVisualDescendants().OfType<VaultWorkspaceView>());
            var searchBox = workspace.FindControl<TextBox>("VaultSearchBox")!;

            Assert.True(TryLibraryKey(workspace, viewModel, searchBox, Key.A, KeyModifiers.Control));
            // The note answers to no bulk command, so select-all cannot invent a check mark for it.
            Assert.Equal(1, viewModel.VaultBatchCount);
            Assert.True(viewModel.HasVaultBatchSelection);
            Assert.True(viewModel.Passwords[0].IsSelected);
            Assert.False(viewModel.NoteItems[0].IsSelected);

            viewModel.ClearVaultBatchSelectionCommand.Execute(null);
            Assert.False(viewModel.HasVaultBatchSelection);
            Assert.False(viewModel.Passwords[0].IsSelected);

            // Typing in the search box means selecting the text in it, not the whole library.
            searchBox.Focus();
            Assert.False(TryLibraryKey(workspace, viewModel, searchBox, Key.A, KeyModifiers.Control));
            Assert.False(viewModel.HasVaultBatchSelection);
        }
        finally
        {
            window.Close();
        }
    }

    // Select-all deliberately leaves the rows in place, so the ones already on screen have to be told
    // to read the check mark again. Reading the container rather than the view-model is the point: a
    // selection that only moves the header count looks exactly like this test failing.
    [Fact]
    public void Bulk_selection_repaints_the_check_marks_already_on_screen()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            viewModel.Passwords.Add(Entry(1, "Alpha", 200));
            viewModel.Passwords.Add(Entry(2, "Bravo", 100));
            viewModel.SelectSectionCommand.Execute("Vault");
            Dispatcher.UIThread.RunJobs();

            var workspace = Assert.Single(window.GetVisualDescendants().OfType<VaultWorkspaceView>());
            var tree = Assert.Single(workspace.GetVisualDescendants().OfType<VaultFolderTree>());
            var list = tree.FindControl<ListBox>("FolderTreeList")!;
            var boxes = viewModel.VaultTreeRows.OfType<VaultTreeEntryRow>()
                .Select(row => Assert.Single(
                    RowContainer(list, row).GetVisualDescendants().OfType<CheckBox>()))
                .ToArray();

            Assert.Equal(2, boxes.Length);
            Assert.All(boxes, box => Assert.False(box.IsChecked));

            viewModel.SelectAllVaultRowsCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.All(boxes, box => Assert.True(box.IsChecked == true));

            viewModel.ClearVaultBatchSelectionCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.All(boxes, box => Assert.False(box.IsChecked));
        }
        finally
        {
            window.Close();
        }
    }

    // The note editor used to measure the whole page it owned. Hosted in the library it only has the
    // detail pane, so the width it arranges its rail and inspector from has to be that pane's.
    [Fact]
    public void The_note_surface_reports_the_width_it_is_given()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.Width = 1280;
        window.Height = 800;
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            viewModel.NoteItems.Add(new Monica.Core.Models.SecureItem
            {
                Id = 41,
                ItemType = Monica.Core.Models.VaultItemType.Note,
                Title = "Recipe"
            });
            viewModel.SelectSectionCommand.Execute("Vault");
            Dispatcher.UIThread.RunJobs();

            var workspace = Assert.Single(window.GetVisualDescendants().OfType<VaultWorkspaceView>());
            var host = workspace.FindControl<ContentControl>("VaultSurfaceHost")!;
            var noteRow = Assert.Single(viewModel.VaultTreeRows.OfType<VaultTreeEntryRow>());

            viewModel.SelectedVaultRow = noteRow;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(VaultSurface.Note, viewModel.SelectedVaultSurface);
            Assert.IsType<Monica.App.Features.Notes.NoteEditorView>(host.Content);
            Assert.Equal(host.Bounds.Width, viewModel.NoteWorkspaceViewportWidth);
            Assert.InRange(viewModel.NoteWorkspaceViewportWidth, 1, window.Bounds.Width - 1);
        }
        finally
        {
            window.Close();
        }
    }

    private static ListBoxItem RowContainer(ListBox list, IFolderTreeRow row) =>
        (ListBoxItem)list.ContainerFromItem(row)!;

    [Fact]
    public void More_menu_sort_and_quick_filters_move_the_tree()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            viewModel.Categories.Add(new Monica.Core.Models.Category { Id = 90, Name = "Mail" });
            // Every one of the seven filters has to answer with a subset no other filter answers
            // with, so a checkbox wired to the wrong property cannot pass by coincidence.
            var entries = new[]
            {
                Entry(1, "Alpha", 400, twoFactor: true, remote: true),
                Entry(2, "Bravo", 300, withNotes: true),
                Entry(3, "Charlie", 200, passkey: true, attachments: true),
                Entry(4, "Delta", 100, boundNote: true, attachments: true),
                Entry(5, "Echo", 50, categorized: true, remote: true)
            };
            foreach (var entry in entries)
            {
                viewModel.Passwords.Add(entry);
            }
            viewModel.SelectSectionCommand.Execute("Passwords");
            Dispatcher.UIThread.RunJobs();

            var workspace = Assert.Single(window.GetVisualDescendants().OfType<VaultWorkspaceView>());
            var moreButton = workspace.FindControl<Button>("VaultMoreButton")!;
            var flyout = Assert.IsType<MenuFlyout>(moreButton.Flyout);
            flyout.ShowAt(moreButton);
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain("selected", moreButton.Classes);
            var sort = flyout.Items.OfType<MenuItem>().ElementAt(0);
            var filters = flyout.Items.OfType<MenuItem>().ElementAt(1);
            Assert.True(filters.IsVisible);

            // A closed submenu has realized nothing, so its bindings only read back once it is open.
            sort.IsSubMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
            var byTitle = Assert.Single(
                sort.Items.OfType<MenuItem>(),
                item => Equals(item.CommandParameter, "title-asc"));
            Assert.NotNull(byTitle.Command);
            byTitle.Command!.Execute(byTitle.CommandParameter);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("title-asc", viewModel.SelectedPasswordSort);
            Assert.Equal(["f:Mail", "p:5", "p:1", "p:2", "p:3", "p:4"], Keys(viewModel.VaultTreeRows));
            sort.IsSubMenuOpen = false;

            filters.IsSubMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
            var filterItems = filters.Items.OfType<MenuItem>().ToArray();
            var narrowed = new[]
            {
                new[] { "p:1" },
                new[] { "p:2" },
                new[] { "p:3" },
                new[] { "p:4" },
                new[] { "p:1", "p:2", "p:3", "p:4" },
                new[] { "p:2", "p:3", "p:4" },
                new[] { "p:3", "p:4" }
            };
            Assert.Equal(narrowed.Length, filterItems.Length);
            for (var index = 0; index < filterItems.Length; index++)
            {
                filterItems[index].IsChecked = true;
                Dispatcher.UIThread.RunJobs();

                Assert.True(viewModel.HasVaultQuickFilters, $"filter #{index} did not report itself on");
                Assert.True(moreButton.Classes.Contains("selected"), $"filter #{index} left the chip dark");
                Assert.Equal(narrowed[index], Keys(viewModel.VaultTreeRows));

                filterItems[index].IsChecked = false;
                Dispatcher.UIThread.RunJobs();

                Assert.False(viewModel.HasVaultQuickFilters);
                Assert.Equal(["f:Mail", "p:5", "p:1", "p:2", "p:3", "p:4"], Keys(viewModel.VaultTreeRows));
            }
            flyout.Hide();

            viewModel.SelectSectionCommand.Execute("Notes");
            Dispatcher.UIThread.RunJobs();
            flyout.ShowAt(moreButton);
            Dispatcher.UIThread.RunJobs();

            var otherPresetFilters = flyout.Items.OfType<MenuItem>().ElementAt(1);
            Assert.False(otherPresetFilters.IsVisible);
            var otherPresetSort = flyout.Items.OfType<MenuItem>().ElementAt(0);
            otherPresetSort.IsSubMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(
                new[] { "updated-desc", "title-asc", "created-desc", "favorites-first" },
                otherPresetSort.Items.OfType<MenuItem>()
                    .Where(item => item.IsVisible)
                    .Select(item => (string)item.CommandParameter!)
                    .ToArray());
            flyout.Hide();
        }
        finally
        {
            window.Close();
        }
    }

    // The library narrows eight ways and has no other way back to the whole thing, so the reset
    // entry has to appear exactly when something is applied and undo every dimension at once.
    [Fact]
    public void Clear_filters_entry_undoes_every_narrowing_at_once()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            var entries = new[]
            {
                Entry(11, "Alpha", 400, twoFactor: true),
                Entry(12, "Bravo", 300),
                Entry(13, "Charlie", 200, passkey: true)
            };
            entries[0].IsFavorite = true;
            foreach (var entry in entries)
            {
                viewModel.Passwords.Add(entry);
            }
            viewModel.SelectSectionCommand.Execute("Passwords");
            Dispatcher.UIThread.RunJobs();

            var workspace = Assert.Single(window.GetVisualDescendants().OfType<VaultWorkspaceView>());
            var moreButton = workspace.FindControl<Button>("VaultMoreButton")!;
            var flyout = Assert.IsType<MenuFlyout>(moreButton.Flyout);

            // A closed flyout has realized nothing, so open it once and keep it open: writing the
            // view model underneath a live popup is what the user actually does with the chip.
            flyout.ShowAt(moreButton);
            Dispatcher.UIThread.RunJobs();
            var clear = Assert.Single(
                flyout.Items.OfType<MenuItem>(),
                item => Equals(item.Command, viewModel.ClearVaultFiltersCommand));
            Assert.False(clear.IsVisible);

            // Search, the favorites chip and a quick filter are three separate surfaces, and only
            // Alpha survives all three — a reset that clears just the last one used shows up here.
            viewModel.VaultSearchText = "Alpha";
            viewModel.VaultFavoritesOnly = true;
            viewModel.QuickFilter2Fa = true;
            Dispatcher.UIThread.RunJobs();

            Assert.True(clear.IsVisible);
            Assert.Equal(["p:11"], Keys(viewModel.VaultTreeRows));

            clear.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.False(viewModel.HasVaultFilters);
            Assert.Empty(viewModel.VaultSearchText);
            Assert.False(viewModel.VaultFavoritesOnly);
            Assert.False(viewModel.QuickFilter2Fa);
            Assert.Equal(["p:13", "p:12", "p:11"], Keys(viewModel.VaultTreeRows));
            Assert.False(clear.IsVisible);
            flyout.Hide();
        }
        finally
        {
            window.Close();
        }
    }

    private static List<string> Keys(IEnumerable<Monica.App.Controls.IVaultTreeRow> rows) =>
        rows.Select(row => row.Key).ToList();

    private static Monica.Core.Models.PasswordEntry Entry(
        long id,
        string title,
        int updatedSecondsAgo,
        bool twoFactor = false,
        bool withNotes = false,
        bool passkey = false,
        bool boundNote = false,
        bool attachments = false,
        bool categorized = false,
        bool remote = false)
    {
        var timestamp = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero).AddSeconds(-updatedSecondsAgo);
        return new Monica.Core.Models.PasswordEntry
        {
            Id = id,
            Title = title,
            UpdatedAt = timestamp,
            CreatedAt = timestamp,
            AuthenticatorKey = twoFactor ? "JBSWY3DPEHPK3PXP" : "",
            Notes = withNotes ? "recovery kit in the drawer" : "",
            PasskeyBindings = passkey ? """[{"credentialId":"aXk"}]""" : "",
            BoundNoteId = boundNote ? 77 : null,
            HasAttachments = attachments,
            CategoryId = categorized ? 90 : null,
            BitwardenVaultId = remote ? 7 : null
        };
    }

    private static bool TryLibraryKey(
        VaultWorkspaceView workspace,
        MainWindowViewModel viewModel,
        Control source,
        Key key,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Source = source,
            Key = key,
            KeyModifiers = modifiers
        };
        workspace.TryHandleShortcut(viewModel, args);
        return args.Handled;
    }
}
