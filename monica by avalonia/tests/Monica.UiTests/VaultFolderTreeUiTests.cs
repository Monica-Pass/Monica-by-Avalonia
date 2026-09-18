using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
            var menuHost = targetItem.GetVisualDescendants()
                .OfType<Panel>()
                .SingleOrDefault(host => host.ContextMenu is not null);

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
            Assert.Equal(3, menu.Items.Count);
            Assert.All(
                menu.Items.OfType<MenuItem>().Skip(1),
                item => Assert.False(item.IsEnabled));
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

    private static Point CenterOf(Window window, ListBoxItem item) =>
        item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;

    private sealed record FakeFolderRow(string Key, string Label) : IFolderTreeRow
    {
        public Thickness Indent => default;

        public bool HasChildren { get; init; }

        public bool IsExpanded { get; init; }
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
}
