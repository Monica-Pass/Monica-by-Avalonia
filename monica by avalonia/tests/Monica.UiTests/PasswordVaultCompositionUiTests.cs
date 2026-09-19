using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Monica.App.Features.Passwords;
using Monica.App.Features.Vault;
using Monica.Core.Models;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class PasswordVaultCompositionUiTests
{
    public PasswordVaultCompositionUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Library_composes_one_tree_and_a_detail_surface_that_is_built_on_demand()
    {
        using var library = LibraryUiHarness.Open("Passwords");
        library.ViewModel.Passwords.Add(new PasswordEntry { Id = 61, Title = "Mail" });
        library.Settle();

        Assert.Single(library.Window.GetVisualDescendants().OfType<VaultWorkspaceView>());
        Assert.Null(library.SurfaceHost.Content);

        library.SelectFirstEntry();
        var first = Assert.IsType<PasswordDetailPaneView>(library.SurfaceHost.Content);

        // Re-selecting must reuse the cached surface, not build a second parser over the same entry.
        library.ViewModel.SelectedVaultRow = null;
        library.Settle();
        library.SelectFirstEntry();

        Assert.Same(first, Assert.IsType<PasswordDetailPaneView>(library.SurfaceHost.Content));
        Assert.Single(library.Window.GetVisualDescendants().OfType<PasswordDetailPaneView>());
    }

    // The list page used to collapse into one pane and drill in below ~800px. The library is a fixed
    // rail + detail, so no window width may hide either half.
    [Theory]
    [InlineData(680)]
    [InlineData(900)]
    [InlineData(1280)]
    public void Library_keeps_both_panes_at_every_window_width(double width)
    {
        using var library = LibraryUiHarness.Open("Passwords", width);
        library.ViewModel.Passwords.Add(new PasswordEntry { Id = 62, Title = "Bank" });
        library.Settle();
        library.SelectFirstEntry();

        var layout = library.Tree.GetLogicalAncestors().OfType<Grid>().First();
        Assert.Equal(new GridLength(340), layout.ColumnDefinitions[0].Width);
        Assert.Equal(new GridLength(1, GridUnitType.Star), layout.ColumnDefinitions[1].Width);

        var rail = library.Tree.GetLogicalAncestors().OfType<Border>().First();
        var detailRegion = library.SurfaceHost.GetLogicalAncestors().OfType<Border>().First();
        Assert.Equal(0, Grid.GetColumn(rail));
        Assert.Equal(1, Grid.GetColumn(detailRegion));
        Assert.True(rail.IsVisible);
        Assert.True(detailRegion.IsVisible);
        Assert.True(library.SurfaceHost.IsVisible);
    }

    // The deleted filter panel owned exactly eight toggles. The favourites chip carries one, so the
    // more menu has to carry the other seven — and each has to drive its own flag both ways.
    [Fact]
    public void Every_quick_filter_the_panel_owned_is_still_reachable()
    {
        using var library = LibraryUiHarness.Open("Passwords");
        var viewModel = library.ViewModel;
        var more = library.Workspace.FindControl<Button>("VaultMoreButton")!;
        var flyout = Assert.IsType<MenuFlyout>(more.Flyout);
        flyout.ShowAt(more);
        library.Settle();
        var submenu = flyout.Items.OfType<MenuItem>().ElementAt(1);
        submenu.IsSubMenuOpen = true;
        library.Settle();
        var items = submenu.Items.OfType<MenuItem>().ToArray();

        var filters = new (Action<bool> Write, Func<bool> Read)[]
        {
            (value => viewModel.QuickFilter2Fa = value, () => viewModel.QuickFilter2Fa),
            (value => viewModel.QuickFilterNotes = value, () => viewModel.QuickFilterNotes),
            (value => viewModel.QuickFilterPasskey = value, () => viewModel.QuickFilterPasskey),
            (value => viewModel.QuickFilterBoundNote = value, () => viewModel.QuickFilterBoundNote),
            (value => viewModel.QuickFilterUncategorized = value, () => viewModel.QuickFilterUncategorized),
            (value => viewModel.QuickFilterLocalOnly = value, () => viewModel.QuickFilterLocalOnly),
            (value => viewModel.QuickFilterAttachments = value, () => viewModel.QuickFilterAttachments)
        };
        Assert.Equal(filters.Length, items.Length);

        for (var index = 0; index < filters.Length; index++)
        {
            filters[index].Write(true);
            library.Settle();

            // Whichever item lit up is the one that flag drives; a mislabelled or dropped row shows
            // up here as either no item or the wrong item, not as a green test.
            var item = Assert.Single(items, candidate => candidate.IsChecked);
            Assert.True(viewModel.HasVaultQuickFilters);

            item.IsChecked = false;
            library.Settle();
            Assert.False(filters[index].Read());
            Assert.False(viewModel.HasVaultQuickFilters);
        }

        flyout.Hide();
    }

    [Fact]
    public void Password_data_commands_stay_off_the_library()
    {
        var libraryXaml = XamlSource.TextFor<VaultWorkspaceView>();

        Assert.DoesNotContain("PasswordCsv", libraryXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DeletedPasswords", libraryXaml, StringComparison.Ordinal);
        Assert.Contains(
            "Command=\"{Binding ImportPasswordCsvCommand}\"",
            XamlSource.Text("SyncImportView.axaml"),
            StringComparison.Ordinal);
        Assert.NotEmpty(XamlSource.ContainsAnywhere("CommandParameter=\"RecycleBin\""));
    }

    [Fact]
    public void Password_vault_styles_left_no_dead_selectors_behind()
    {
        // The list page's stylesheet was deleted with it, so every class it styled must be gone from
        // the app and the shared shell classes must be the ones the library actually applies.
        Assert.All(
            new[]
            {
                "passwordFilterOption",
                "passwordVaultList",
                "passwordChromeToolbar",
                "passwordListRegion",
                "passwordDetailRegion",
                "passwordFolderNavigationRegion"
            },
            deadClass => Assert.Empty(XamlSource.ContainsAnywhere(deadClass)));
        Assert.DoesNotContain("#101010", XamlSource.Text("VaultShellStyles.axaml"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "<Setter Property=\"Foreground\" Value=\"{DynamicResource TextOnAccentFillColorPrimaryBrush}\" />",
            XamlSource.Text("VaultShellStyles.axaml"),
            StringComparison.Ordinal);
        Assert.Contains("Classes=\"workspacePrimaryCommand\"", XamlSource.TextFor<VaultWorkspaceView>(), StringComparison.Ordinal);
    }

    [Fact]
    public void Password_detail_promotes_copy_actions_and_groups_secondary_commands()
    {
        var xaml = XamlSource.TextFor<PasswordDetailPaneView>();

        Assert.Contains("Text=\"{Binding L.CopyPassword}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding L.CopyUsername}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding L.CopyWebsite}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PasswordDetailMoreButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{Binding L.EditPassword}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{Binding L.ArchivePassword}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{Binding L.MoveToRecycleBin}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ToolTip.Tip=\"{Binding L.EditPassword}\"", xaml, StringComparison.Ordinal);
    }
}
