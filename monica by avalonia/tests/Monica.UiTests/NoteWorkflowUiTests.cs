using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Controls;
using Monica.App.Features.Notes;
using Monica.App.Features.Vault;
using Monica.App.ViewModels;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class NoteWorkflowUiTests
{
    public NoteWorkflowUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    // The note page is gone: the editor and its inspector are one surface the library hands over.
    [Fact]
    public void Note_surface_hosts_the_editor_and_an_inspector_that_collapses_without_taking_width()
    {
        var view = new NoteEditorView();

        Assert.NotNull(view.FindControl<Grid>("NoteEditorContent"));
        Assert.NotNull(view.FindControl<NoteInspectorView>("NoteInspectorRegion"));
        Assert.NotNull(view.FindControl<StackPanel>("NoteEditorEmptyState"));

        var xaml = File.ReadAllText(FindSourceFile("NoteEditorView.axaml"));
        Assert.Contains("ColumnDefinitions=\"*,Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"1\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Note_surface_keeps_a_way_in_when_no_note_is_open()
    {
        var editor = new NoteEditorView();

        Assert.NotNull(editor.FindControl<StackPanel>("NoteEditorEmptyState"));
        Assert.NotNull(editor.FindControl<Button>("EmptyNoteAddButton"));

        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        editor.DataContext = viewModel;
        Assert.False(viewModel.HasOpenNoteTabs);

        editor.FindControl<Button>("EmptyNoteAddButton")!.Command!.Execute(null);

        Assert.True(viewModel.HasOpenNoteTabs);
        Assert.IsType<NoteEditorTab>(viewModel.SelectedNoteTab);
    }

    [Fact]
    public void Note_surface_uses_the_width_contract_the_library_gives_it()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        viewModel.AddNoteCommand.Execute(null);

        viewModel.NoteWorkspaceViewportWidth = 1179;
        Assert.False(viewModel.IsNoteWorkspaceNarrow);
        Assert.False(viewModel.IsNoteInspectorPaneVisible);
        Assert.Equal(new Thickness(28, 24, 28, 20), viewModel.NoteEditorContentMargin);

        viewModel.NoteWorkspaceViewportWidth = 1180;
        Assert.True(viewModel.IsNoteInspectorPaneVisible);

        // Split view trades the inspector for a second column of its own, whatever the width is.
        viewModel.SetNoteViewModeCommand.Execute("split");
        Assert.True(viewModel.IsNoteEditorPaneVisible);
        Assert.True(viewModel.IsNotePreviewPaneVisible);
        Assert.False(viewModel.IsNoteInspectorPaneVisible);

        viewModel.SetNoteViewModeCommand.Execute("edit");
        viewModel.NoteWorkspaceViewportWidth = 759;
        Assert.True(viewModel.IsNoteWorkspaceNarrow);
        Assert.False(viewModel.IsNoteInspectorPaneVisible);
        Assert.Equal(new Thickness(16, 20, 16, 16), viewModel.NoteEditorContentMargin);
    }

    // A note's only way into a Bitwarden vault is this toolbar item - the library offers no row checkbox and
    // its select-all skips notes - so the promise checked here is that the door exists exactly when an
    // account exists to carry the note. What the door then does is a different promise, and it is checked in
    // BitwardenSyncWorkflowUiTests against a recording repository rather than a real MDBX vault.
    [Fact]
    public void Note_toolbar_offers_publish_only_where_an_account_can_carry_it()
    {
        using var library = LibraryUiHarness.Open();

        library.ViewModel.AddNoteCommand.Execute(null);
        library.Settle();

        Assert.False(library.ViewModel.BitwardenNotePublishOffered);

        library.ViewModel.BitwardenAccounts.Add(new Monica.App.ViewModels.BitwardenAccountDisplayItem(
            new Monica.Core.Bitwarden.BitwardenAccount
            {
                Id = 7,
                Email = "person@example.com",
                DisplayName = "Personal Bitwarden",
                AccountKey = "bw:v1:test-account",
                Endpoints = Monica.Core.Bitwarden.BitwardenEndpointSet.UnitedStates,
                Kdf = Monica.Core.Bitwarden.BitwardenKdfParameters.Pbkdf2(),
                IsConnected = true
            },
            "Personal Bitwarden",
            "https://vault.bitwarden.com",
            "Connected",
            "Last sync just now",
            "",
            "",
            "",
            0,
            0));
        library.Settle();

        Assert.True(library.ViewModel.BitwardenNotePublishOffered);
        // The item lives in the command bar's overflow, whose entries bind only once that menu opens, so
        // this checks the declaration the menu hands out rather than pretending to drive a live row.
        var toolbarXaml = File.ReadAllText(FindSourceFile("NoteEditorToolbarView.axaml"));
        Assert.Contains("Command=\"{Binding PublishCurrentNoteToBitwardenCommand}\"", toolbarXaml,
            StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding BitwardenNotePublishOffered}\"", toolbarXaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Note_toolbar_uses_a_native_command_bar_and_the_inspector_a_single_scroll_surface()
    {
        var toolbar = new NoteEditorToolbarView();
        var inspector = new NoteInspectorView();

        Assert.NotNull(toolbar.FindControl<FACommandBar>("NoteEditorCommandBar"));
        Assert.NotNull(toolbar.FindControl<Button>("SaveNoteButton"));
        Assert.NotNull(toolbar.FindControl<Button>("NoteHeadingMenuButton"));
        Assert.NotNull(toolbar.FindControl<Button>("NoteViewModeMenuButton"));
        Assert.NotNull(inspector.FindControl<ScrollViewer>("NoteInspectorScrollViewer"));

        var toolbarXaml = File.ReadAllText(FindSourceFile("NoteEditorToolbarView.axaml"));
        Assert.Contains("<fa:FACommandBar", toolbarXaml, StringComparison.Ordinal);
        Assert.Contains("<fa:FACommandBar.SecondaryCommands>", toolbarXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<WrapPanel", toolbarXaml, StringComparison.Ordinal);
        // Read mode used to be a page header control; the toolbar is the only place left that owns it.
        Assert.Contains("Command=\"{Binding SetNoteViewModeCommand}\" CommandParameter=\"preview\"",
            toolbarXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Note_view_mode_menu_offers_exactly_the_three_modes_as_one_radio_group()
    {
        var anchor = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(anchor);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var toolbar = new NoteEditorToolbarView { DataContext = viewModel };
        var host = new Window { Content = toolbar };
        host.Show();
        try
        {
            var viewMode = toolbar.FindControl<Button>("NoteViewModeMenuButton")!;
            var flyout = Assert.IsType<MenuFlyout>(viewMode.Flyout);
            // A closed flyout has realized nothing, so the commands and roles only read back once
            // the menu is on screen — which is also the only state a user can meet it in.
            flyout.ShowAt(viewMode);
            Dispatcher.UIThread.RunJobs();
            var modes = flyout.Items.OfType<MenuItem>().ToArray();

            Assert.Equal(3, modes.Length);
            Assert.All(modes, mode => Assert.Same(viewModel.SetNoteViewModeCommand, mode.Command));
            Assert.Equal(
                new[] { "edit", "preview", "split" },
                modes.Select(mode => mode.CommandParameter).ToArray());

            // One radio group, so the mode the user is reading is always the one that is marked.
            Assert.All(modes, mode => Assert.Equal(MenuItemToggleType.Radio, mode.ToggleType));
            flyout.Hide();
        }
        finally
        {
            host.Close();
        }
    }

    [Fact]
    public void Note_properties_remain_reachable_when_the_wide_inspector_collapses()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        viewModel.AddNoteCommand.Execute(null);
        viewModel.NoteWorkspaceViewportWidth = 900;
        var toolbar = new NoteEditorToolbarView { DataContext = viewModel };
        var host = new Window
        {
            Width = 900,
            Height = 180,
            Content = toolbar
        };
        host.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();

            var properties = toolbar.FindControl<Button>("CompactNotePropertiesButton");
            Assert.NotNull(properties);
            Assert.True(properties.IsVisible);
            Assert.True(properties.Bounds.Width > 0);
            Assert.True(properties.Bounds.Height > 0);
            var flyout = Assert.IsType<Flyout>(properties.Flyout);
            var flyoutSurface = Assert.IsType<Border>(flyout.Content);
            // The compact flyout carries the whole inspector, not a thinner copy of the fields.
            var inspector = Assert.IsType<NoteInspectorView>(flyoutSurface.Child);
            flyout.ShowAt(properties);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(viewModel, inspector.DataContext);
            flyout.Hide();

            viewModel.NoteWorkspaceViewportWidth = 1180;
            Dispatcher.UIThread.RunJobs();

            Assert.False(properties.IsVisible);
        }
        finally
        {
            host.Close();
        }
    }

    [Fact]
    public void Note_property_changes_mark_the_selected_draft_dirty()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        viewModel.AddNoteCommand.Execute(null);
        var tab = Assert.IsType<NoteEditorTab>(viewModel.SelectedNoteTab);
        tab.IsDirty = false;

        viewModel.NoteIsFavorite = true;

        Assert.True(tab.IsDirty);
        Assert.True(tab.DraftIsFavorite);
    }

    [Fact]
    public void Note_properties_expose_nested_category_picker()
    {
        var panel = new NotePropertiesPanelView();
        var picker = panel.FindControl<ComboBox>("NoteCategoryPicker");

        Assert.NotNull(picker);
        var xaml = File.ReadAllText(FindSourceFile("NotePropertiesPanelView.axaml"));
        Assert.Contains("ItemsSource=\"{Binding NoteCategoryOptions}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedNoteCategory}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"{Binding Indent}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ParentPath}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"{Binding FullPath}\"", xaml, StringComparison.Ordinal);
    }

    // The note tree owned its rows' commands; the library tree now owns every row's, and they still
    // belong to the workspace view model rather than to a view of their own.
    [Fact]
    public void Note_rows_are_driven_by_the_library_commands_owned_by_the_workspace()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            viewModel.NoteItems.Add(new Monica.Core.Models.SecureItem
            {
                Id = 43,
                ItemType = Monica.Core.Models.VaultItemType.Note,
                Title = "Command ownership"
            });
            viewModel.SelectSectionCommand.Execute(VaultPresets.LibrarySection);
            Dispatcher.UIThread.RunJobs();

            var workspace = Assert.Single(window.GetVisualDescendants().OfType<VaultWorkspaceView>());
            var tree = workspace.FindControl<VaultFolderTree>("VaultTree")!;

            Assert.Same(viewModel.EditSelectedVaultEntryCommand, tree.EditEntryCommand);
            Assert.Same(viewModel.MoveSelectedVaultEntryCommand, tree.MoveEntryCommand);
            Assert.Same(viewModel.DeleteSelectedVaultEntryCommand, tree.DeleteEntryCommand);

            var noteRow = Assert.Single(viewModel.VaultTreeRows.OfType<VaultTreeEntryRow>());
            viewModel.SelectedVaultRow = noteRow;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(VaultSurface.Note, viewModel.SelectedVaultSurface);

            // The command being wired is not enough: the library files an entry by opening a category
            // picker from the row's menu, so that item has to be on the menu for a selected entry.
            Assert.True(tree.IsEntrySelection);
            Assert.True(tree.ShowsEntryCommands);
            Assert.True(tree.ShowsEntryFolderPicker);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Note_xaml_avoids_magic_overlay_margins()
    {
        var toolbarXaml = File.ReadAllText(FindSourceFile("NoteEditorToolbarView.axaml"));
        var editorXaml = File.ReadAllText(FindSourceFile("NoteEditorView.axaml"));
        var stylesXaml = File.ReadAllText(FindSourceFile("NoteStyles.axaml"));

        Assert.DoesNotContain("Margin=\"0,0,158,0\"", toolbarXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Margin=\"-48", editorXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Margin=\"-48", toolbarXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Border.noteToolbar Button", stylesXaml, StringComparison.Ordinal);
    }

    private static string FindSourceFile(string fileName) =>
        XamlSource.PathOf(fileName);

    private static int CountOccurrences(string text, string value) =>
        XamlSource.CountOccurrences(text, value);
}
