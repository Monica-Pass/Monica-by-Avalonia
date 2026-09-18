using System.Collections;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Monica.App.Services;

namespace Monica.App.Controls;

public partial class VaultFolderTree : UserControl
{
    private enum FolderNamingMode
    {
        None,
        Create,
        Rename,
    }

    private FolderNamingMode _namingMode;

    private const double DragThresholdPixels = 5;

    private IFolderTreeRow? _dragSourceRow;
    private Visual? _dragSourceItem;
    private ListBoxItem? _dragTargetItem;
    private Point _dragPressedAt;
    private bool _isDraggingFolder;

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<VaultFolderTree, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<ILocalizationService?> TextProperty =
        AvaloniaProperty.Register<VaultFolderTree, ILocalizationService?>(nameof(Text));

    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<VaultFolderTree, object?>(nameof(SelectedItem));

    public static readonly StyledProperty<ICommand?> AllFoldersCommandProperty =
        AvaloniaProperty.Register<VaultFolderTree, ICommand?>(nameof(AllFoldersCommand));

    public static readonly StyledProperty<bool> IsAllFoldersSelectedProperty =
        AvaloniaProperty.Register<VaultFolderTree, bool>(nameof(IsAllFoldersSelected));

    public static readonly StyledProperty<ICommand?> ToggleExpansionCommandProperty =
        AvaloniaProperty.Register<VaultFolderTree, ICommand?>(nameof(ToggleExpansionCommand));

    public static readonly StyledProperty<ICommand?> CreateFolderCommandProperty =
        AvaloniaProperty.Register<VaultFolderTree, ICommand?>(nameof(CreateFolderCommand));

    public static readonly StyledProperty<ICommand?> RenameFolderCommandProperty =
        AvaloniaProperty.Register<VaultFolderTree, ICommand?>(nameof(RenameFolderCommand));

    public static readonly StyledProperty<ICommand?> DeleteFolderCommandProperty =
        AvaloniaProperty.Register<VaultFolderTree, ICommand?>(nameof(DeleteFolderCommand));

    public static readonly StyledProperty<ICommand?> MoveFolderCommandProperty =
        AvaloniaProperty.Register<VaultFolderTree, ICommand?>(nameof(MoveFolderCommand));

    public static readonly StyledProperty<bool> CanManageSelectedProperty =
        AvaloniaProperty.Register<VaultFolderTree, bool>(nameof(CanManageSelected));

    public static readonly StyledProperty<string?> FolderNameProperty =
        AvaloniaProperty.Register<VaultFolderTree, string?>(nameof(FolderName));

    public static readonly StyledProperty<bool> IsNamingProperty =
        AvaloniaProperty.Register<VaultFolderTree, bool>(nameof(IsNaming));

    public static readonly StyledProperty<string> NamingPlaceholderProperty =
        AvaloniaProperty.Register<VaultFolderTree, string>(nameof(NamingPlaceholder), string.Empty);

    public VaultFolderTree()
    {
        InitializeComponent();
        FolderTreeList.AddHandler(
            InputElement.PointerPressedEvent,
            OnTreePointerPressed,
            RoutingStrategies.Tunnel);
        FolderTreeList.AddHandler(
            InputElement.PointerMovedEvent,
            OnTreePointerMoved,
            RoutingStrategies.Tunnel);
        FolderTreeList.AddHandler(
            InputElement.PointerReleasedEvent,
            OnTreePointerReleased,
            RoutingStrategies.Tunnel);
        FolderTreeList.AddHandler(
            InputElement.PointerCaptureLostEvent,
            OnTreePointerCaptureLost,
            RoutingStrategies.Tunnel);
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public ILocalizationService? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public ICommand? AllFoldersCommand
    {
        get => GetValue(AllFoldersCommandProperty);
        set => SetValue(AllFoldersCommandProperty, value);
    }

    public bool IsAllFoldersSelected
    {
        get => GetValue(IsAllFoldersSelectedProperty);
        set => SetValue(IsAllFoldersSelectedProperty, value);
    }

    public ICommand? ToggleExpansionCommand
    {
        get => GetValue(ToggleExpansionCommandProperty);
        set => SetValue(ToggleExpansionCommandProperty, value);
    }

    public ICommand? CreateFolderCommand
    {
        get => GetValue(CreateFolderCommandProperty);
        set => SetValue(CreateFolderCommandProperty, value);
    }

    public ICommand? RenameFolderCommand
    {
        get => GetValue(RenameFolderCommandProperty);
        set => SetValue(RenameFolderCommandProperty, value);
    }

    public ICommand? DeleteFolderCommand
    {
        get => GetValue(DeleteFolderCommandProperty);
        set => SetValue(DeleteFolderCommandProperty, value);
    }

    public ICommand? MoveFolderCommand
    {
        get => GetValue(MoveFolderCommandProperty);
        set => SetValue(MoveFolderCommandProperty, value);
    }

    public bool CanManageSelected
    {
        get => GetValue(CanManageSelectedProperty);
        set => SetValue(CanManageSelectedProperty, value);
    }

    public string? FolderName
    {
        get => GetValue(FolderNameProperty);
        set => SetValue(FolderNameProperty, value);
    }

    public bool IsNaming
    {
        get => GetValue(IsNamingProperty);
        set => SetValue(IsNamingProperty, value);
    }

    public string NamingPlaceholder
    {
        get => GetValue(NamingPlaceholderProperty);
        set => SetValue(NamingPlaceholderProperty, value);
    }

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(FolderTreeList);

        // The menu acts on the selection, and a right-click on an unselected row would otherwise
        // rename or delete whatever was selected before.
        if (point.Properties.IsRightButtonPressed)
        {
            if (e.Source is Visual source && FindRowItem(source)?.DataContext is IFolderTreeRow rightClicked &&
                !ReferenceEquals(FolderTreeList.SelectedItem, rightClicked))
            {
                FolderTreeList.SelectedItem = rightClicked;
            }

            return;
        }

        if (!point.Properties.IsLeftButtonPressed ||
            e.Source is not Visual pressed ||
            FindRowItem(pressed) is not ListBoxItem item ||
            item.DataContext is not IFolderTreeRow row ||
            // Dragging reparents folders; an entry moves through the context menu instead.
            row.IsEntryLeaf())
        {
            return;
        }

        _dragSourceRow = row;
        _dragSourceItem = item;
        _dragPressedAt = point.Position;
    }

    private void OnTreePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragSourceRow is null || _dragSourceItem is null)
        {
            return;
        }

        var position = e.GetPosition(FolderTreeList);
        if (!_isDraggingFolder)
        {
            // Clicking a row must stay a click: the drag only takes over once the pointer travels.
            if (Math.Abs(position.X - _dragPressedAt.X) < DragThresholdPixels &&
                Math.Abs(position.Y - _dragPressedAt.Y) < DragThresholdPixels)
            {
                return;
            }

            _isDraggingFolder = true;
            _dragSourceItem.Classes.Add("draggingSource");
            e.Pointer.Capture(FolderTreeList);
        }

        HighlightDropTarget(FindRowAt(position));
    }

    private void OnTreePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDraggingFolder)
        {
            ClearDragState();
            return;
        }

        var source = _dragSourceRow;
        var target = _dragTargetItem?.DataContext;
        ClearDragState();

        if (source is null || target is not IFolderTreeRow targetRow ||
            targetRow.IsEntryLeaf() ||
            MoveFolderCommand is not { } command)
        {
            return;
        }

        var request = new FolderMoveRequest(source, targetRow);
        if (command.CanExecute(request))
        {
            command.Execute(request);
        }
    }

    private void OnTreePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => ClearDragState();

    // Only the folder the pointer is over can accept the drop, so the ring follows its bounds:
    // a captured pointer reports the list as its source, not the visual underneath.
    private void HighlightDropTarget(ListBoxItem? candidate)
    {
        var accepts = false;
        if (_dragSourceRow is { } source &&
            candidate?.DataContext is IFolderTreeRow target &&
            !target.IsEntryLeaf() &&
            !ReferenceEquals(target, source))
        {
            accepts = MoveFolderCommand?.CanExecute(new FolderMoveRequest(source, target)) == true;
        }

        var item = accepts ? candidate : null;
        if (ReferenceEquals(_dragTargetItem, item))
        {
            return;
        }

        _dragTargetItem?.Classes.Remove("dropTarget");
        _dragTargetItem = item;
        item?.Classes.Add("dropTarget");
    }

    private ListBoxItem? FindRowAt(Point position) => FolderTreeList
        .GetVisualDescendants()
        .OfType<ListBoxItem>()
        .Where(item => item.DataContext is IFolderTreeRow &&
                       item.TranslatePoint(new Point(0, 0), FolderTreeList) is { } top &&
                       position.Y >= top.Y &&
                       position.Y < top.Y + item.Bounds.Height)
        .FirstOrDefault();

    private void ClearDragState()
    {
        if (_dragSourceItem is not null)
        {
            _dragSourceItem.Classes.Remove("draggingSource");
        }

        if (_dragTargetItem is not null)
        {
            _dragTargetItem.Classes.Remove("dropTarget");
        }

        _dragSourceRow = null;
        _dragSourceItem = null;
        _dragTargetItem = null;
        _isDraggingFolder = false;
    }

    private static Visual? FindRowItem(Visual source)
    {
        Visual? node = source;
        while (node is not null and not ListBoxItem)
        {
            node = node.GetVisualParent();
        }

        return node;
    }

    private void OnCreateFolderClick(object? sender, RoutedEventArgs e) => BeginNaming(FolderNamingMode.Create, null);

    private void OnRenameFolderClick(object? sender, RoutedEventArgs e) => BeginRename();

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && CanManageSelected)
        {
            BeginRename();
            e.Handled = true;
        }
    }

    private void BeginRename()
    {
        if (SelectedItem is IFolderTreeRow row)
        {
            BeginNaming(FolderNamingMode.Rename, row.Label);
        }
    }

    private void BeginNaming(FolderNamingMode mode, string? initialName)
    {
        _namingMode = mode;
        FolderName = initialName ?? string.Empty;
        NamingPlaceholder = Text?.Get(
            mode == FolderNamingMode.Rename ? "RenameFolder" : "NewFolder") ?? string.Empty;
        IsNaming = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                FolderNameBox.Focus();
                FolderNameBox.SelectAll();
            },
            DispatcherPriority.Input);
    }

    private void OnFolderNameKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                CommitNaming();
                e.Handled = true;
                break;
            case Key.Escape:
                CancelNaming();
                e.Handled = true;
                break;
        }
    }

    private void OnFolderNameLostFocus(object? sender, RoutedEventArgs e) => CommitNaming();

    private void CommitNaming()
    {
        if (_namingMode == FolderNamingMode.None)
        {
            return;
        }

        var command = _namingMode == FolderNamingMode.Rename ? RenameFolderCommand : CreateFolderCommand;
        _namingMode = FolderNamingMode.None;
        IsNaming = false;

        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
        }
    }

    private void CancelNaming()
    {
        _namingMode = FolderNamingMode.None;
        IsNaming = false;
        FolderName = string.Empty;
    }
}
