using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Features.Notes;
using Monica.App.ViewModels;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class NoteWorkflowUiTests
{
    public NoteWorkflowUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Note_workspace_exposes_responsive_master_editor_regions()
    {
        var view = new NoteWorkspaceView();

        Assert.NotNull(view.FindControl<Grid>("NoteWorkspaceGrid"));
        Assert.NotNull(view.FindControl<NoteTreeView>("NoteTreeRegion"));
        Assert.NotNull(view.FindControl<NoteEditorView>("NoteEditorRegion"));
        Assert.NotNull(view.FindControl<NoteInspectorView>("NoteInspectorRegion"));
    }

    [Fact]
    public void Note_views_expose_empty_search_and_return_actions()
    {
        var tree = new NoteTreeView();
        var editor = new NoteEditorView();

        Assert.NotNull(tree.FindControl<TextBox>("NoteSearchBox"));
        Assert.NotNull(tree.FindControl<Button>("ClearNoteSearchButton"));
        Assert.NotNull(tree.FindControl<StackPanel>("NoteTreeEmptyState"));
        Assert.NotNull(tree.FindControl<Button>("EmptyNoteTreeAddButton"));
        Assert.NotNull(editor.FindControl<Grid>("NoteEditorContent"));
        Assert.NotNull(editor.FindControl<StackPanel>("NoteEditorEmptyState"));
        Assert.NotNull(editor.FindControl<Button>("BackToNoteListButton"));
        Assert.NotNull(tree.FindControl<Button>("NoteFolderNavigationButton"));
        Assert.NotNull(tree.FindControl<Button>("NoteTagNavigationButton"));
        Assert.NotNull(tree.FindControl<TextBox>("NewNoteFolderNameBox"));
        Assert.NotNull(tree.FindControl<Button>("CreateNoteFolderButton"));
        Assert.NotNull(tree.FindControl<Button>("RenameNoteFolderButton"));
        Assert.NotNull(tree.FindControl<Button>("DeleteNoteFolderButton"));
    }

    [Fact]
    public void Note_workspace_uses_wide_medium_narrow_and_split_layout_contracts()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();

        viewModel.NoteWorkspaceViewportWidth = 1179;
        Assert.True(viewModel.IsNoteTreePaneVisible);
        Assert.True(viewModel.IsNoteEditorWorkspaceVisible);
        Assert.False(viewModel.IsNoteInspectorPaneVisible);
        Assert.False(viewModel.ShowAddNoteInTreeHeader);
        Assert.Equal(new GridLength(0), viewModel.NoteInspectorColumnWidth);

        viewModel.NoteWorkspaceViewportWidth = 1180;
        Assert.True(viewModel.IsNoteInspectorPaneVisible);
        Assert.Equal(new GridLength(280), viewModel.NoteInspectorColumnWidth);

        viewModel.NoteSplitPreviewMode = true;
        Assert.False(viewModel.IsNoteTreePaneVisible);
        Assert.False(viewModel.IsNoteInspectorPaneVisible);
        Assert.True(viewModel.IsNoteEditorPaneVisible);
        Assert.True(viewModel.IsNotePreviewPaneVisible);

        viewModel.NoteSplitPreviewMode = false;
        viewModel.NoteWorkspaceViewportWidth = 759;
        viewModel.NoteNarrowShowsTree = true;
        Assert.True(viewModel.IsNoteTreePaneVisible);
        Assert.False(viewModel.IsNoteEditorWorkspaceVisible);
        Assert.True(viewModel.ShowAddNoteInTreeHeader);
        viewModel.NoteNarrowShowsTree = false;
        Assert.False(viewModel.IsNoteTreePaneVisible);
        Assert.True(viewModel.IsNoteEditorWorkspaceVisible);
        Assert.False(viewModel.ShowAddNoteInTreeHeader);
        Assert.Equal(new Thickness(16, 20, 16, 16), viewModel.NoteEditorContentMargin);
    }

    [Fact]
    public void Note_workspace_uses_native_command_and_single_scroll_surfaces()
    {
        var toolbar = new NoteEditorToolbarView();
        var inspector = new NoteInspectorView();
        var tree = new NoteTreeView();
        var tabs = new NoteTabStripView();

        Assert.NotNull(toolbar.FindControl<FACommandBar>("NoteEditorCommandBar"));
        Assert.NotNull(toolbar.FindControl<Button>("NoteHeadingMenuButton"));
        Assert.NotNull(inspector.FindControl<ScrollViewer>("NoteInspectorScrollViewer"));
        Assert.NotNull(tree.FindControl<ListBox>("NoteTreeList"));
        Assert.NotNull(tabs.FindControl<Grid>("NoteTabRail"));
        Assert.NotNull(tabs.FindControl<StackPanel>("NoteTabCommandRegion"));
        Assert.NotNull(tabs.FindControl<Border>("NoteInspectorHeader"));
        Assert.NotNull(tabs.FindControl<Border>("NoteDocumentCommandSurface"));

        var tabCommands = new[]
        {
            tabs.FindControl<Button>("PreviousNoteTabButton"),
            tabs.FindControl<Button>("NextNoteTabButton"),
            tabs.FindControl<Button>("AddNoteTabButton"),
            tabs.FindControl<Button>("SaveNoteTabButton"),
            tabs.FindControl<Button>("MoreNoteTabButton")
        };
        Assert.All(tabCommands, command =>
        {
            Assert.NotNull(command);
            Assert.True(command.Width >= 40);
            Assert.True(command.Height >= 40);
        });
    }

    [Fact]
    public void Note_properties_remain_reachable_when_the_wide_inspector_collapses()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
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
            var propertiesPanel = Assert.IsType<NotePropertiesPanelView>(flyoutSurface.Child);
            flyout.ShowAt(properties);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(viewModel, propertiesPanel.DataContext);
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

    [Fact]
    public void Note_tree_row_overflow_binds_commands_to_the_workspace_view_model()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var note = new Monica.Core.Models.SecureItem
        {
            Id = 42,
            ItemType = Monica.Core.Models.VaultItemType.Note,
            Title = "Command ownership"
        };
        var row = new NoteTreeItemView
        {
            DataContext = note,
            ViewModel = viewModel
        };
        var host = new Window
        {
            Width = 320,
            Height = 120,
            Content = row
        };
        host.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();
            var more = row.FindControl<Button>("NoteItemMoreButton");
            Assert.NotNull(more);
            var flyout = Assert.IsType<MenuFlyout>(more.Flyout);
            flyout.ShowAt(more);
            Dispatcher.UIThread.RunJobs();
            var commands = flyout.Items.OfType<MenuItem>().ToArray();

            Assert.Equal(3, commands.Length);
            Assert.Same(viewModel.OpenNoteCommand, commands[0].Command);
            Assert.Same(viewModel.ToggleNoteFavoriteCommand, commands[1].Command);
            Assert.Same(viewModel.DeleteNoteCommand, commands[2].Command);
            Assert.All(commands, command => Assert.Same(note, command.CommandParameter));
            flyout.Hide();
        }
        finally
        {
            host.Close();
        }
    }

    [Fact]
    public void Note_xaml_owns_secondary_commands_without_magic_overlay_margins()
    {
        var toolbarXaml = File.ReadAllText(FindSourceFile("NoteEditorToolbarView.axaml"));
        var tabsXaml = File.ReadAllText(FindSourceFile("NoteTabStripView.axaml"));
        var treeXaml = File.ReadAllText(FindSourceFile("NoteTreeView.axaml"));
        var treeItemXaml = File.ReadAllText(FindSourceFile("NoteTreeItemView.axaml"));
        var editorXaml = File.ReadAllText(FindSourceFile("NoteEditorView.axaml"));
        var stylesXaml = File.ReadAllText(FindSourceFile("NoteStyles.axaml"));

        Assert.Contains("<fa:FACommandBar", toolbarXaml, StringComparison.Ordinal);
        Assert.Contains("<fa:FACommandBar.SecondaryCommands>", toolbarXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NoteHeadingMenuButton\"", toolbarXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<WrapPanel", toolbarXaml, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinitions=\"Auto,*,Auto\"", tabsXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Margin=\"0,0,158,0\"", tabsXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("NoteTabStripWidth", tabsXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NoteTreeList\"", treeXaml, StringComparison.Ordinal);
        Assert.Contains("Classes.accent=\"{Binding IsNoteFolderNavigation}\"", treeXaml, StringComparison.Ordinal);
        Assert.Contains("ToggleNoteTreeGroupCommand", treeXaml, StringComparison.Ordinal);
        Assert.Contains("SelectNoteTreeGroupCommand", treeXaml, StringComparison.Ordinal);
        Assert.Contains("Classes.selected=\"{Binding IsSelected}\"", treeXaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsNoteFolderNavigation}\"", treeXaml, StringComparison.Ordinal);
        Assert.Contains("VirtualizingStackPanel", treeXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{Binding LoadCommand}\"", treeXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("L[NoteHome]", treeXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NoteItemMoreButton\"", treeItemXaml, StringComparison.Ordinal);
        Assert.Contains("<MenuFlyout>", treeItemXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Margin=\"-48", editorXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Border.noteToolbar Button", stylesXaml, StringComparison.Ordinal);
    }

    private static string FindSourceFile(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Monica.App", "Features", "Notes", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate {fileName} from the test output directory.");
    }
}
