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
        var deleteFolder = new DelegateCommand(() => { });
        var deleteEntry = new DelegateCommand(() => { });
        var copySecret = new DelegateCommand<IVaultTreeRow>(_ => { });
        var selected = new FakeFolderRow("alpha", "Alpha");
        var row = new FakeFolderRow("beta", "Beta");
        var tree = new VaultFolderTree
        {
            ItemsSource = new[] { selected, row },
            SelectedItem = selected,
            DeleteFolderCommand = deleteFolder,
            DeleteEntryCommand = deleteEntry,
            CopyRowSecretCommand = copySecret,
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

            // One menu holds every group; a folder row shows only the folder half of it, and a
            // folder it cannot delete stays in the menu disabled rather than vanishing.
            Assert.True(ItemFor(items, deleteFolder).IsVisible);
            Assert.False(ItemFor(items, deleteFolder).IsEnabled);
            Assert.False(ItemFor(items, deleteEntry).IsVisible);
            Assert.False(ItemFor(items, copySecret).IsVisible);
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
        var deleteFolder = new DelegateCommand(() => { });
        var deleteEntry = new DelegateCommand(() => deletes++);
        var tree = new VaultFolderTree
        {
            ItemsSource = new IFolderTreeRow[] { folder, entry },
            CanManageSelected = true,
            DeleteFolderCommand = deleteFolder,
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
            Assert.False(ItemFor(items, deleteFolder).IsVisible);
            Assert.True(ItemFor(items, deleteEntry).IsVisible);

            ItemFor(items, deleteEntry).Command!.Execute(null);
            Assert.Equal(1, deletes);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Entry_rows_carry_only_the_copy_actions_and_no_per_row_check_mark()
    {
        var copied = new List<IVaultTreeRow>();
        var credential = new FakeEntryRow("p:1", "Checking")
        {
            CanCopyUsername = true,
            CanCopySecret = true,
            IsBatchable = true
        };
        var folder = new FakeFolderRow("alpha", "Alpha");
        var copyUsername = new DelegateCommand<IVaultTreeRow>(copied.Add);
        var copySecret = new DelegateCommand<IVaultTreeRow>(copied.Add);
        var copyCode = new DelegateCommand<IVaultTreeRow>(copied.Add);
        var tree = new VaultFolderTree
        {
            ItemsSource = new IFolderTreeRow[] { folder, credential },
            CopyRowUsernameCommand = copyUsername,
            CopyRowSecretCommand = copySecret,
            CopyRowCodeCommand = copyCode,
        };
        var window = new Window { Content = tree };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();

            var list = tree.FindControl<ListBox>("FolderTreeList")!;
            var credentialItem = RowContainer(list, credential);
            var folderItem = RowContainer(list, folder);

            // Rows no longer carry a per-row check mark; bulk selection is driven from the batch
            // flyout (select-all/clear), so neither a leaf nor a folder renders a CheckBox.
            Assert.Empty(credentialItem.GetVisualDescendants().OfType<CheckBox>());
            Assert.Empty(folderItem.GetVisualDescendants().OfType<CheckBox>());

            window.MouseDown(CenterOf(window, credentialItem), MouseButton.Right);
            window.MouseUp(CenterOf(window, credentialItem), MouseButton.Right);
            Dispatcher.UIThread.RunJobs();

            var items = MenuItems(RowContextMenuHost(credentialItem)!);
            Assert.True(ItemFor(items, copyUsername).IsVisible);
            Assert.True(ItemFor(items, copySecret).IsVisible);
            // Nothing on this row holds a code, so the item is not offered at all.
            Assert.False(ItemFor(items, copyCode).IsVisible);

            // The menu acts on the row it was opened from, whatever the list selection says.
            ItemFor(items, copySecret).Command!.Execute(ItemFor(items, copySecret).CommandParameter);
            Assert.Same(credential, Assert.Single(copied));
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
    public void Dragging_an_entry_row_onto_a_folder_requests_the_move()
    {
        var folder = new FakeFolderRow("alpha", "Alpha");
        var entry = new FakeEntryRow("p:1", "Checking");
        var moves = new List<FolderMoveRequest>();
        var tree = new VaultFolderTree
        {
            ItemsSource = new IFolderTreeRow[] { folder, entry },
            MoveEntryToFolderCommand = new RecordingMoveCommand(moves),
        };
        var window = new Window { Content = tree };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();

            var list = tree.FindControl<ListBox>("FolderTreeList")!;
            var folderItem = RowContainer(list, folder);
            var entryItem = RowContainer(list, entry);

            // The host declared a command that takes a destination folder, so the row lifts.
            window.MouseDown(CenterOf(window, entryItem), MouseButton.Left);
            window.MouseMove(CenterOf(window, folderItem));
            Assert.Contains("draggingSource", entryItem.Classes);
            Assert.Contains("dropTarget", folderItem.Classes);

            window.MouseUp(CenterOf(window, folderItem), MouseButton.Left);

            var move = Assert.Single(moves);
            Assert.Same(entry, move.Source);
            Assert.Same(folder, move.Target);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void The_entry_move_item_shows_only_where_a_picker_answers_it()
    {
        var folder = new FakeFolderRow("alpha", "Alpha");
        var entry = new FakeEntryRow("p:1", "Checking");
        var picker = new RecordingMoveCommand(new List<FolderMoveRequest>());
        var tree = new VaultFolderTree
        {
            ItemsSource = new IFolderTreeRow[] { folder, entry },
            MoveEntryCommand = picker,
        };
        var window = new Window { Content = tree };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var list = tree.FindControl<ListBox>("FolderTreeList")!;
            var entryItem = RowContainer(list, entry);
            window.MouseDown(CenterOf(window, entryItem), MouseButton.Right);
            window.MouseUp(CenterOf(window, entryItem), MouseButton.Right);
            Dispatcher.UIThread.RunJobs();

            var items = MenuItems(RowContextMenuHost(entryItem)!);
            var item = ItemFor(items, picker);
            Assert.True(tree.IsEntrySelection);
            Assert.True(item.IsVisible);

            // The same row where entries move by drag instead: the item would run no command, so it
            // leaves the menu rather than sitting on it as a dead button.
            tree.MoveEntryCommand = null;
            tree.MoveEntryToFolderCommand = new RecordingMoveCommand(new List<FolderMoveRequest>());
            Dispatcher.UIThread.RunJobs();

            Assert.True(tree.ShowsEntryCommands);
            Assert.False(tree.ShowsEntryFolderPicker);
            Assert.Null(item.Command);
            Assert.False(item.IsVisible);
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
    public void A_row_with_a_host_keeps_the_glyph_and_carries_the_picture_slot()
    {
        var entry = new FakeEntryRow("p:1", "Checking")
        {
            WebsiteIconHost = "example.com",
        };
        var tree = new VaultFolderTree
        {
            ItemsSource = new IFolderTreeRow[] { entry },
        };
        var window = new Window { Content = tree };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();

            var list = tree.FindControl<ListBox>("FolderTreeList")!;
            var picture = RowPicture(list, entry);

            // The row hands the host over verbatim, which is the only part the control owns: the cache
            // decides whether that host is worth asking about.
            Assert.Equal("example.com", picture.Host);

            // An automated run is kept offline, so nothing can arrive to be shown. The picture still
            // has to stay hidden instead of flashing an empty box, and the type glyph it sits on top of
            // has to keep painting - a row that loses its shape when a website has no icon is worse
            // than a row with no icon.
            Assert.False(picture.IsVisible);
            Assert.False(picture.IsHitTestVisible);
            // The slot is measured before a picture exists, so an answer that lands later fills a box
            // that is already there instead of pushing the rest of the row sideways.
            Assert.Equal(16d, picture.Width);
            Assert.Equal(16d, picture.Height);
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
            // throws here rather than declining quietly. The check mark is a Button too, so the
            // command it answers to is what makes a button the chevron.
            var entryItem = RowContainer(list, entry);
            var chevron = entryItem.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, tree.ToggleExpansionCommand));
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

    // The glyph is read out of the named slot rather than guessed from the tree shape.
    private static Symbol RowGlyph(ListBox list, IFolderTreeRow row)
    {
        var slot = RowIconSlot(list, row);
        var glyph = Assert.Single(slot.GetVisualChildren().OfType<FluentIcon>());
        return (Symbol)glyph.Icon;
    }

    private static WebsiteIconImage RowPicture(ListBox list, IFolderTreeRow row) =>
        Assert.Single(RowIconSlot(list, row).GetVisualChildren().OfType<WebsiteIconImage>());

    private static Panel RowIconSlot(ListBox list, IFolderTreeRow row)
    {
        // The slot is named, not guessed by shape: the row keeps its chevron in a panel of its own, and a
        // picture sits in the glyph's slot on top of it, so "the first icon in a Grid" stopped being the
        // row's type glyph the moment the fallback layer appeared.
        return Assert.Single(
            RowContainer(list, row).GetVisualDescendants().OfType<Panel>(),
            panel => panel.Name == "RowIconSlot");
    }

    // The whole row is the right-click target, so the menu hangs off its background panel.
    private static Panel? RowContextMenuHost(ListBoxItem rowItem) =>
        rowItem.GetVisualDescendants()
            .OfType<Panel>()
            .SingleOrDefault(host => host.ContextMenu is not null);

    private static MenuItem[] MenuItems(Panel host) =>
        host.ContextMenu!.Items.OfType<MenuItem>().ToArray();

    // Menus carry items the row hides rather than dropping them, so tests name an item by the
    // command behind it — a position would move every time the menu gains an action.
    private static MenuItem ItemFor(MenuItem[] items, ICommand command) =>
        items.Single(item => ReferenceEquals(item.Command, command));

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

        public bool IsBatchable => false;

        public bool CanCopyUsername => false;

        public bool CanCopySecret => false;

        public bool CanCopyCode => false;

        public string? WebsiteIconHost => null;

        public bool IsSelected
        {
            get => false;
            set { }
        }
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

        public bool IsBatchable { get; init; }

        public bool CanCopyUsername { get; init; }

        public bool CanCopySecret { get; init; }

        public bool CanCopyCode { get; init; }

        public string? WebsiteIconHost { get; init; }

        public bool IsSelected { get; set; }
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
