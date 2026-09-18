using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentIcons.Avalonia;
using FluentIcons.Common;
using Monica.App.Controls;
using Monica.App.Services;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class VaultFolderTreeUiTests
{
    public VaultFolderTreeUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Folder_tree_creates_a_named_folder_inline()
    {
        var createCalls = 0;
        var tree = new VaultFolderTree
        {
            ItemsSource = new[] { new FakeFolderRow("alpha", "Alpha") },
            CreateFolderCommand = new DelegateCommand(() => createCalls++),
        };

        tree.FindControl<Button>("CreateFolderButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(tree.IsNaming);
        Assert.Equal(string.Empty, tree.FolderName);

        var nameBox = tree.FindControl<TextBox>("FolderNameBox")!;
        nameBox.Text = "Beta";
        nameBox.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));

        Assert.Equal(1, createCalls);
        Assert.False(tree.IsNaming);
        Assert.Equal("Beta", tree.FolderName);
    }

    [Fact]
    public void Folder_tree_escape_abandons_the_pending_name()
    {
        var createCalls = 0;
        var tree = new VaultFolderTree
        {
            ItemsSource = new[] { new FakeFolderRow("alpha", "Alpha") },
            CreateFolderCommand = new DelegateCommand(() => createCalls++),
        };
        tree.FindControl<Button>("CreateFolderButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var nameBox = tree.FindControl<TextBox>("FolderNameBox")!;
        nameBox.Text = "Beta";

        nameBox.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Escape,
        });
        nameBox.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));

        Assert.Equal(0, createCalls);
        Assert.False(tree.IsNaming);
        Assert.Equal(string.Empty, tree.FolderName);
    }

    [Fact]
    public void Folder_tree_renames_the_selected_row_with_f2()
    {
        var renameCalls = 0;
        var row = new FakeFolderRow("alpha", "Alpha");
        var tree = new VaultFolderTree
        {
            ItemsSource = new[] { row },
            SelectedItem = row,
            CanManageSelected = true,
            RenameFolderCommand = new DelegateCommand(() => renameCalls++),
        };

        tree.FindControl<ListBox>("FolderTreeList")!
            .RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.F2,
            });

        Assert.True(tree.IsNaming);
        Assert.Equal("Alpha", tree.FolderName);

        var nameBox = tree.FindControl<TextBox>("FolderNameBox")!;
        nameBox.Text = "Renamed";
        nameBox.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));

        Assert.Equal(1, renameCalls);
    }

    [Fact]
    public void Folder_rows_are_right_clickable_across_their_full_width()
    {
        var selected = new FakeFolderRow("alpha", "Alpha");
        var row = new FakeFolderRow("beta", "Beta");
        var tree = new VaultFolderTree
        {
            ItemsSource = new[] { selected, row },
            SelectedItem = selected,
        };
        var window = new Window { Content = tree };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();

            var list = tree.FindControl<ListBox>("FolderTreeList")!;
            var targetItem = RowContainer(list, row);
            var menuHost = RowContextMenuHost(targetItem);

            Assert.NotNull(menuHost);
            Assert.Same(row, menuHost!.DataContext);
            Assert.Contains(
                menuHost.GetVisualDescendants().OfType<TextBlock>(),
                label => label.Text == "Beta");

            // Right-clicking the blank part of a row has to act on that row, not on whatever
            // happened to be selected before.
            window.MouseDown(CenterOf(window, targetItem), MouseButton.Right);
            window.MouseUp(CenterOf(window, targetItem), MouseButton.Right);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(row, list.SelectedItem);
            var menu = Assert.Single(
                window.GetVisualDescendants().OfType<ContextMenu>(),
                contextMenu => ReferenceEquals(contextMenu, menuHost.ContextMenu));
            var items = menu.Items.OfType<MenuItem>().ToArray();
            Assert.Equal(6, items.Length);

            // One menu holds both groups; a folder row shows only the folder half of it.
            Assert.All(items.Take(3), item => Assert.True(item.IsVisible));
            Assert.All(items.Skip(3), item => Assert.False(item.IsVisible));
            Assert.All(items.Skip(1).Take(2), item => Assert.False(item.IsEnabled));
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Entry_rows_offer_the_entry_actions_instead_of_the_folder_ones()
    {
        var deletes = 0;
        var folder = new FakeFolderRow("alpha", "Alpha");
        var entry = new FakeEntryRow("p:1", "Checking");
        var deleteEntry = new DelegateCommand(() => deletes++);
        var tree = new VaultFolderTree
        {
            ItemsSource = new IFolderTreeRow[] { folder, entry },
            CanManageSelected = true,
            DeleteEntryCommand = deleteEntry,
        };
        var window = new Window { Content = tree };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();

            var list = tree.FindControl<ListBox>("FolderTreeList")!;
            var entryItem = RowContainer(list, entry);
            window.MouseDown(CenterOf(window, entryItem), MouseButton.Right);
            window.MouseUp(CenterOf(window, entryItem), MouseButton.Right);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(entry, list.SelectedItem);
            Assert.True(tree.IsEntrySelection);

            var entryMenuHost = RowContextMenuHost(entryItem);
            Assert.NotNull(entryMenuHost);

            var items = MenuItems(entryMenuHost!);
            Assert.All(items.Take(3), item => Assert.False(item.IsVisible));
            Assert.All(items.Skip(3), item => Assert.True(item.IsVisible));
            Assert.Same(deleteEntry, items[5].Command);

            items[5].Command!.Execute(null);
            Assert.Equal(1, deletes);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Entry_leaves_take_no_part_in_drag_and_drop()
    {
        var folder = new FakeFolderRow("alpha", "Alpha");
        var entry = new FakeEntryRow("p:1", "Checking");
        var moves = new List<FolderMoveRequest>();
        var tree = new VaultFolderTree
        {
            ItemsSource = new IFolderTreeRow[] { folder, entry },
            MoveFolderCommand = new RecordingMoveCommand(moves),
        };
        var window = new Window { Content = tree };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();

            var list = tree.FindControl<ListBox>("FolderTreeList")!;
            var folderItem = RowContainer(list, folder);
            var entryItem = RowContainer(list, entry);

            // An entry has no folder name to drag by and no children to receive one; it moves
            // through the context menu, where the host can validate the destination.
            window.MouseDown(CenterOf(window, entryItem), MouseButton.Left);
            window.MouseMove(CenterOf(window, folderItem));
            window.MouseUp(CenterOf(window, folderItem), MouseButton.Left);
            Assert.Empty(moves);
            Assert.DoesNotContain("draggingSource", entryItem.Classes);

            window.MouseDown(CenterOf(window, folderItem), MouseButton.Left);
            window.MouseMove(CenterOf(window, entryItem));
            Assert.DoesNotContain("dropTarget", entryItem.Classes);
            window.MouseUp(CenterOf(window, entryItem), MouseButton.Left);
            Assert.Empty(moves);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Dragging_a_folder_row_onto_another_requests_the_reparent()
    {
        var source = new FakeFolderRow("alpha", "Alpha");
        var target = new FakeFolderRow("beta", "Beta");
        var moves = new List<FolderMoveRequest>();
        var tree = new VaultFolderTree
        {
            ItemsSource = new[] { source, target },
            MoveFolderCommand = new RecordingMoveCommand(moves),
        };
        var window = new Window { Content = tree };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();

            var list = tree.FindControl<ListBox>("FolderTreeList")!;
            var sourceItem = RowContainer(list, source);
            var targetItem = RowContainer(list, target);

            window.MouseDown(CenterOf(window, sourceItem), MouseButton.Left);
            window.MouseMove(CenterOf(window, targetItem));

            Assert.Contains("draggingSource", sourceItem.Classes);
            Assert.Contains("dropTarget", targetItem.Classes);

            window.MouseUp(CenterOf(window, targetItem), MouseButton.Left);

            var move = Assert.Single(moves);
            Assert.Same(source, move.Source);
            Assert.Same(target, move.Target);
            Assert.DoesNotContain("draggingSource", sourceItem.Classes);
            Assert.DoesNotContain("dropTarget", targetItem.Classes);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void A_press_that_barely_moves_stays_a_click()
    {
        var source = new FakeFolderRow("alpha", "Alpha");
        var target = new FakeFolderRow("beta", "Beta");
        var moves = new List<FolderMoveRequest>();
        var tree = new VaultFolderTree
        {
            ItemsSource = new[] { source, target },
            MoveFolderCommand = new RecordingMoveCommand(moves),
        };
        var window = new Window { Content = tree };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();

            var list = tree.FindControl<ListBox>("FolderTreeList")!;
            var sourceItem = RowContainer(list, source);
            var targetItem = RowContainer(list, target);
            var pressedAt = CenterOf(window, sourceItem);

            window.MouseDown(pressedAt, MouseButton.Left);
            window.MouseMove(new Point(pressedAt.X + 2, pressedAt.Y + 2));
            window.MouseUp(CenterOf(window, targetItem), MouseButton.Left);

            Assert.Empty(moves);
            Assert.DoesNotContain("draggingSource", sourceItem.Classes);
            Assert.DoesNotContain("dropTarget", targetItem.Classes);
            Assert.Same(source, list.SelectedItem);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void The_drop_ring_follows_what_the_host_is_willing_to_accept()
    {
        var source = new FakeFolderRow("alpha", "Alpha");
        var target = new FakeFolderRow("beta", "Beta");
        var moves = new List<FolderMoveRequest>();
        var tree = new VaultFolderTree
        {
            ItemsSource = new[] { source, target },
            MoveFolderCommand = new RecordingMoveCommand(moves, accepts: false),
        };
        var window = new Window { Content = tree };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();

            var list = tree.FindControl<ListBox>("FolderTreeList")!;
            var sourceItem = RowContainer(list, source);
            var targetItem = RowContainer(list, target);

            window.MouseDown(CenterOf(window, sourceItem), MouseButton.Left);
            window.MouseMove(CenterOf(window, targetItem));

            Assert.Contains("draggingSource", sourceItem.Classes);
            Assert.DoesNotContain("dropTarget", targetItem.Classes);

            window.MouseUp(CenterOf(window, targetItem), MouseButton.Left);
            Assert.Empty(moves);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Rows_paint_the_glyph_the_row_asked_for()
    {
        var folder = new FakeFolderRow("alpha", "Alpha");
        var entry = new FakeEntryRow("p:1", "Checking");
        var tree = new VaultFolderTree
        {
            ItemsSource = new IFolderTreeRow[] { folder, entry },
        };
        var window = new Window { Content = tree };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();

            var list = tree.FindControl<ListBox>("FolderTreeList")!;

            // An unset Icon leaves the control default in place and nothing is logged, so the only
            // way to tell a working row glyph from a failed binding is to read the realized value.
            Assert.Equal(Symbol.Folder, RowGlyph(list, folder));
            Assert.Equal(Symbol.Key, RowGlyph(list, entry));
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Expanding_accepts_any_row_kind_the_template_hands_it()
    {
        var folder = new FakeFolderRow("alpha", "Alpha");
        var entry = new FakeEntryRow("p:1", "Checking");
        var tree = new VaultFolderTree
        {
            ItemsSource = new IFolderTreeRow[] { folder, entry },
        };
        var window = new Window { Content = tree };
        var toggled = new List<IFolderTreeRow>();
        tree.ToggleExpansionCommand = new DelegateCommand<IFolderTreeRow>(toggled.Add);
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();

            var list = tree.FindControl<ListBox>("FolderTreeList")!;

            // Every row realizes the chevron button, and the button asks the host whether it can
            // run even on a leaf it hides the chevron for; a host command narrowed to one row type
            // throws here rather than declining quietly.
            var entryItem = RowContainer(list, entry);
            var chevron = entryItem.GetVisualDescendants().OfType<Button>().Single();
            chevron.Command!.Execute(chevron.CommandParameter);

            var toggledRow = Assert.Single(toggled);
            Assert.Same(entry, toggledRow);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Folder_tree_header_resets_the_filter_through_the_host_command()
    {
        var resetCalls = 0;
        var resetCommand = new DelegateCommand(() => resetCalls++);
        var tree = new VaultFolderTree
        {
            ItemsSource = Array.Empty<FakeFolderRow>(),
            AllFoldersCommand = resetCommand,
        };
        var resetButton = tree.FindControl<Button>("AllFoldersButton")!;

        Assert.Same(resetCommand, resetButton.Command);
        resetButton.Command!.Execute(null);
        Assert.Equal(1, resetCalls);
    }

    [Fact]
    public void Folder_tree_captions_come_from_the_supplied_text_source()
    {
        var text = new LocalizationService();
        var tree = new VaultFolderTree
        {
            Text = text,
            ItemsSource = Array.Empty<FakeFolderRow>(),
            CreateFolderCommand = new DelegateCommand(() => { }),
        };

        Assert.Equal(text.AllFolders, tree.FindControl<Button>("AllFoldersButton")!.Content);
        Assert.Equal(
            text.NewFolder,
            ToolTip.GetTip(tree.FindControl<Button>("CreateFolderButton")!));

        tree.FindControl<Button>("CreateFolderButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(text.NewFolder, tree.NamingPlaceholder);
    }

    private static ListBoxItem RowContainer(ListBox list, IFolderTreeRow row) =>
        (ListBoxItem)list.ContainerFromItem(row)!;

    // The row glyph is the one icon placed directly in the row grid; the chevrons hang off a button.
    private static Symbol RowGlyph(ListBox list, IFolderTreeRow row)
    {
        var glyph = Assert.Single(
            RowContainer(list, row).GetVisualDescendants().OfType<FluentIcon>(),
            icon => Grid.GetColumn(icon) == 1);
        return (Symbol)glyph.Icon;
    }

    // The whole row is the right-click target, so the menu hangs off its background panel.
    private static Panel? RowContextMenuHost(ListBoxItem rowItem) =>
        rowItem.GetVisualDescendants()
            .OfType<Panel>()
            .SingleOrDefault(host => host.ContextMenu is not null);

    private static MenuItem[] MenuItems(Panel host) =>
        host.ContextMenu!.Items.OfType<MenuItem>().ToArray();

    private static Point CenterOf(Window window, ListBoxItem item) =>
        item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;

    private sealed record FakeFolderRow(string Key, string Label) : IVaultTreeRow
    {
        public Thickness Indent => default;

        public bool HasChildren { get; init; }

        public bool IsExpanded { get; init; }

        public VaultTreeRowKind RowKind => VaultTreeRowKind.Folder;

        public bool IsEntryRow => false;

        public Symbol EntrySymbol => Symbol.Folder;

        public string EntryDetail => "";
    }

    private sealed record FakeEntryRow(string Key, string Label) : IVaultTreeRow
    {
        public Thickness Indent => default;

        public bool HasChildren => false;

        public bool IsExpanded => false;

        public VaultTreeRowKind RowKind => VaultTreeRowKind.Entry;

        public bool IsEntryRow => true;

        public Symbol EntrySymbol => Symbol.Key;

        public string EntryDetail => "joyins";
    }

    private sealed class RecordingMoveCommand(List<FolderMoveRequest> moves, bool accepts = true) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => accepts && parameter is FolderMoveRequest;

        public void Execute(object? parameter)
        {
            if (parameter is FolderMoveRequest move)
            {
                moves.Add(move);
            }
        }
    }

    private sealed class DelegateCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute();
    }

    // A host command typed to the shared row contract — the widest type the template may hand it.
    private sealed class DelegateCommand<T>(Action<T> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => parameter is T;

        public void Execute(object? parameter) => execute((T)parameter!);
    }
}
