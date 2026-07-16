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
