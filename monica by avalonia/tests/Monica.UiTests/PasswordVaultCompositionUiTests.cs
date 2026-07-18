using Avalonia.Controls;
using Monica.App.Features.Passwords;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class PasswordVaultCompositionUiTests
{
    public PasswordVaultCompositionUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Password_vault_is_composed_from_focused_workspace_views()
    {
        var view = new PasswordVaultView();

        Assert.NotNull(view.FindControl<PasswordVaultToolbarView>("PasswordVaultToolbar"));
        Assert.NotNull(view.FindControl<PasswordFolderFilterView>("PasswordFolderFilters"));
        Assert.NotNull(view.FindControl<PasswordListPaneView>("PasswordListPane"));
        var detailHost = view.FindControl<ContentControl>("PasswordDetailPaneHost");
        Assert.NotNull(detailHost);
        Assert.IsNotType<PasswordDetailPaneView>(detailHost.Content);

        view.FocusDetails();

        Assert.IsType<PasswordDetailPaneView>(detailHost.Content);
    }

    [Fact]
    public void Focused_password_views_expose_required_keyboard_and_state_controls()
    {
        var toolbar = new PasswordVaultToolbarView();
        var list = new PasswordListPaneView();
        var details = new PasswordDetailPaneView();

        Assert.NotNull(toolbar.FindControl<TextBox>("PasswordSearchBox"));
        Assert.NotNull(toolbar.FindControl<Button>("PasswordSearchClearButton"));
        Assert.NotNull(list.FindControl<ListBox>("PasswordListBox"));
        Assert.NotNull(list.FindControl<CheckBox>("SelectAllVisiblePasswordsCheckBox"));
        Assert.NotNull(details.FindControl<Button>("BackToPasswordListButton"));
        Assert.NotNull(details.FindControl<Button>("RetryPasswordDetailsButton"));
    }

    [Fact]
    public void Password_filter_controls_are_named_compact_and_theme_aware()
    {
        var toolbar = new PasswordVaultToolbarView();
        _ = new PasswordQuickFilterPanelView();
        var toolbarXaml = File.ReadAllText(FindPasswordFeatureFile("PasswordVaultToolbarView.axaml"));
        var filterPanelXaml = File.ReadAllText(FindPasswordFeatureFile("PasswordQuickFilterPanelView.axaml"));
        var listXaml = File.ReadAllText(FindPasswordFeatureFile("PasswordListPaneView.axaml"));
        var stylesXaml = File.ReadAllText(FindPasswordFeatureFile("PasswordVaultStyles.axaml"));

        Assert.NotNull(toolbar.FindControl<Button>("PasswordQuickFiltersButton"));
        Assert.Contains("x:Name=\"PasswordQuickFiltersButton\"", toolbarXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding L.PasswordFilters}\"", toolbarXaml, StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.Name=\"{Binding L.PasswordFilters}\"",
            toolbarXaml,
            StringComparison.Ordinal);
        Assert.Contains("<local:PasswordQuickFilterPanelView />", toolbarXaml, StringComparison.Ordinal);
        Assert.Equal(8, CountOccurrences(filterPanelXaml, "Classes=\"passwordFilterOption\""));
        Assert.DoesNotContain("Classes=\"passwordFilterChip\"", toolbarXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding PasswordListStatusText}\"", listXaml, StringComparison.Ordinal);
        Assert.Contains(
            "<Setter Property=\"Foreground\" Value=\"{DynamicResource LayerFillColorAltBrush}\" />",
            stylesXaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain("#101010", stylesXaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Password_vault_redesign_wide_layout_exposes_folder_list_and_details()
    {
        var view = new PasswordVaultView();

        view.UpdateResponsiveLayoutForWidth(1280);

        var folderNavigation = view.FindControl<Border>("PasswordFolderNavigationRegion");
        Assert.NotNull(folderNavigation);
        Assert.True(folderNavigation.IsVisible);
        Assert.True(view.FindControl<Border>("PasswordListRegion")!.IsVisible);
        Assert.True(view.FindControl<Border>("PasswordDetailRegion")!.IsVisible);
    }

    [Fact]
    public void Password_vault_redesign_medium_hides_folder_rail_and_narrow_drills_into_details()
    {
        var view = new PasswordVaultView();

        view.UpdateResponsiveLayoutForWidth(900);
        var layout = view.FindControl<Grid>("PasswordMasterDetailGrid");
        var listRegion = view.FindControl<Border>("PasswordListRegion")!;
        var detailRegion = view.FindControl<Border>("PasswordDetailRegion")!;
        Assert.False(view.IsWideLayout);
        Assert.False(view.FindControl<Border>("PasswordFolderNavigationRegion")!.IsVisible);
        Assert.Equal(new GridLength(320), layout!.ColumnDefinitions[0].Width);
        Assert.Equal(new GridLength(1, GridUnitType.Star), layout.ColumnDefinitions[1].Width);
        Assert.Equal(0, Grid.GetColumn(listRegion));
        Assert.Equal(1, Grid.GetColumn(detailRegion));
        Assert.True(listRegion.IsVisible);
        Assert.True(detailRegion.IsVisible);

        view.UpdateResponsiveLayoutForWidth(680);
        Assert.True(view.IsNarrowLayout);
        Assert.False(view.FindControl<Border>("PasswordFolderNavigationRegion")!.IsVisible);
        Assert.Equal(new GridLength(1, GridUnitType.Star), layout.ColumnDefinitions[0].Width);
        Assert.True(listRegion.IsVisible);
        Assert.False(detailRegion.IsVisible);
    }

    [Fact]
    public void Password_vault_redesign_detail_promotes_copy_actions_and_groups_secondary_commands()
    {
        var details = new PasswordDetailPaneView();
        var xaml = File.ReadAllText(FindPasswordFeatureFile("PasswordDetailPaneView.axaml"));

        Assert.NotNull(details.FindControl<Button>("PasswordDetailMoreButton"));
        Assert.Contains("Text=\"{Binding L.CopyPassword}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding L.CopyUsername}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding L.CopyWebsite}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PasswordDetailMoreButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{Binding L.EditPassword}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{Binding L.ArchivePassword}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{Binding L.MoveToRecycleBin}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ToolTip.Tip=\"{Binding L.EditPassword}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Password_vault_redesign_toolbar_keeps_primary_add_and_secondary_data_commands()
    {
        var toolbar = new PasswordVaultToolbarView();
        var xaml = File.ReadAllText(FindPasswordFeatureFile("PasswordVaultToolbarView.axaml"));

        Assert.NotNull(toolbar.FindControl<Button>("PasswordQuickFiltersButton"));
        Assert.Contains("Text=\"{Binding L.AddPassword}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{Binding L.ImportPasswordCsv}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{Binding L.ExportPasswordCsv}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{Binding L.DeletedPasswords}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<local:PasswordFolderNavigationView", File.ReadAllText(FindPasswordFeatureFile("PasswordVaultView.axaml")), StringComparison.Ordinal);
        Assert.DoesNotContain("passwordFolderBar", File.ReadAllText(FindPasswordFeatureFile("PasswordFolderFilterView.axaml")), StringComparison.Ordinal);
    }

    [Fact]
    public void Password_vault_redesign_keeps_folder_navigation_available_at_every_width()
    {
        var view = new PasswordVaultView();
        var folderNavigation = new PasswordFolderNavigationView();
        var compactFilters = view.FindControl<PasswordFolderFilterView>("PasswordFolderFilters");

        Assert.NotNull(folderNavigation.FindControl<ListBox>("PasswordFolderNavigationList"));
        Assert.NotNull(compactFilters);
        Assert.NotNull(compactFilters.FindControl<ComboBox>("CompactPasswordFolderPicker"));

        view.UpdateResponsiveLayoutForWidth(1280);
        Assert.False(compactFilters.ShowCompactFolderPicker);

        view.UpdateResponsiveLayoutForWidth(900);
        Assert.True(compactFilters.ShowCompactFolderPicker);
    }

    private static int CountOccurrences(string value, string fragment) =>
        value.Split(fragment, StringSplitOptions.None).Length - 1;

    private static string FindPasswordFeatureFile(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "Monica.App",
                "Features",
                "Passwords",
                fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate {fileName} from the test output directory.");
    }
}
