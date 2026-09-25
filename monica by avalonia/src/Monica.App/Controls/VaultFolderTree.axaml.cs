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

    public static readonly StyledProperty<ICommand?> EditEntryCommandProperty =
        AvaloniaProperty.Register<VaultFolderTree, ICommand?>(nameof(EditEntryCommand));

    public static readonly StyledProperty<ICommand?> MoveEntryCommandProperty =
        AvaloniaProperty.Register<VaultFolderTree, ICommand?>(nameof(MoveEntryCommand));

    // Two hosts move an entry in two different ways: the library page asks which category to file it
    // under, an opened .kdbx has folders to drop onto. Only the second one takes a drop, so the drag
    // gate and the menu item read separate properties instead of guessing from one.
    public static readonly StyledProperty<ICommand?> MoveEntryToFolderCommandProperty =
        AvaloniaProperty.Register<VaultFolderTree, ICommand?>(nameof(MoveEntryToFolderCommand));

    public static readonly StyledProperty<ICommand?> DeleteEntryCommandProperty =
        AvaloniaProperty.Register<VaultFolderTree, ICommand?>(nameof(DeleteEntryCommand));

    // Copy is asked for row by row because a tree mixes the kinds that hold different values, and
    // the parameter is the row itself so a copy never waits for the selection to catch up.
    public static readonly StyledProperty<ICommand?> CopyRowUsernameCommandProperty =
        AvaloniaProperty.Register<VaultFolderTree, ICommand?>(nameof(CopyRowUsernameCommand));

    public static readonly StyledProperty<ICommand?> CopyRowSecretCommandProperty =
        AvaloniaProperty.Register<VaultFolderTree, ICommand?>(nameof(CopyRowSecretCommand));

    public static readonly StyledProperty<ICommand?> CopyRowCodeCommandProperty =
        AvaloniaProperty.Register<VaultFolderTree, ICommand?>(nameof(CopyRowCodeCommand));

    public static readonly StyledProperty<bool> CanManageSelectedProperty =
        AvaloniaProperty.Register<VaultFolderTree, bool>(nameof(CanManageSelected));

    // False for a tree the user can read but not write: an opened file the app cannot save back yet.
    // Hiding the edit items entirely beats showing a menu full of disabled commands.
    public static readonly StyledProperty<bool> CanManageRowsProperty =
        AvaloniaProperty.Register<VaultFolderTree, bool>(nameof(CanManageRows), true);

    public static readonly StyledProperty<bool> ShowsFolderCommandsProperty =
        AvaloniaProperty.Register<VaultFolderTree, bool>(nameof(ShowsFolderCommands));

    public static readonly StyledProperty<bool> ShowsEntryCommandsProperty =
        AvaloniaProperty.Register<VaultFolderTree, bool>(nameof(ShowsEntryCommands));

    // The "move to folder" menu item appears only where a picker-style command is wired to it; an
    // entry that moves by drag has no use for a menu item that does nothing.
    public static readonly StyledProperty<bool> ShowsEntryFolderPickerProperty =
        AvaloniaProperty.Register<VaultFolderTree, bool>(nameof(ShowsEntryFolderPicker));

    // The context menu is built per row, but its items live in one menu, so the menu group is chosen
    // from the row the pointer last pressed rather than from the item's own data context.
    public static readonly StyledProperty<bool> IsEntrySelectionProperty =
        AvaloniaProperty.Register<VaultFolderTree, bool>(nameof(IsEntrySelection));

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
        RefreshRowCommandVisibility();
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

    public ICommand? EditEntryCommand
    {
        get => GetValue(EditEntryCommandProperty);
        set => SetValue(EditEntryCommandProperty, value);
    }

    public ICommand? MoveEntryCommand
    {
        get => GetValue(MoveEntryCommandProperty);
        set => SetValue(MoveEntryCommandProperty, value);
    }

    public ICommand? MoveEntryToFolderCommand
    {
        get => GetValue(MoveEntryToFolderCommandProperty);
        set => SetValue(MoveEntryToFolderCommandProperty, value);
    }

    public ICommand? DeleteEntryCommand
    {
        get => GetValue(DeleteEntryCommandProperty);
        set => SetValue(DeleteEntryCommandProperty, value);
    }

    public ICommand? CopyRowUsernameCommand
    {
        get => GetValue(CopyRowUsernameCommandProperty);
        set => SetValue(CopyRowUsernameCommandProperty, value);
    }

    public ICommand? CopyRowSecretCommand
    {
        get => GetValue(CopyRowSecretCommandProperty);
        set => SetValue(CopyRowSecretCommandProperty, value);
    }

    public ICommand? CopyRowCodeCommand
    {
        get => GetValue(CopyRowCodeCommandProperty);
        set => SetValue(CopyRowCodeCommandProperty, value);
    }

    public bool IsEntrySelection
    {
        get => GetValue(IsEntrySelectionProperty);
        set => SetValue(IsEntrySelectionProperty, value);
    }

    public bool CanManageSelected
    {
        get => GetValue(CanManageSelectedProperty);
        set => SetValue(CanManageSelectedProperty, value);
    }

    public bool CanManageRows
    {
        get => GetValue(CanManageRowsProperty);
        set => SetValue(CanManageRowsProperty, value);
    }

    public bool ShowsFolderCommands
    {
        get => GetValue(ShowsFolderCommandsProperty);
        set => SetValue(ShowsFolderCommandsProperty, value);
    }

    public bool ShowsEntryCommands
    {
        get => GetValue(ShowsEntryCommandsProperty);
        set => SetValue(ShowsEntryCommandsProperty, value);
    }

    public bool ShowsEntryFolderPicker
    {
        get => GetValue(ShowsEntryFolderPickerProperty);
        set => SetValue(ShowsEntryFolderPickerProperty, value);
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SelectedItemProperty)
        {
            var selected = change.GetNewValue<object?>();
            IsEntrySelection = selected is IFolderTreeRow row && row.IsEntryLeaf();
        }
        else if (change.Property == IsEntrySelectionProperty ||
                 change.Property == CanManageRowsProperty ||
                 change.Property == MoveEntryCommandProperty)
        {
            RefreshRowCommandVisibility();
        }
    }

    private void RefreshRowCommandVisibility()
    {
        ShowsFolderCommands = CanManageRows && !IsEntrySelection;
        ShowsEntryCommands = CanManageRows && IsEntrySelection;
        ShowsEntryFolderPicker = ShowsEntryCommands && MoveEntryCommand is not null;
    }

    public bool IsTreeFocused => FolderTreeList.IsFocused;

    public void FocusTree() => FolderTreeList.Focus();

    // Arrow keys move the selection through the host, so the row they land on has to stay on screen.
    public void ScrollIntoView(object item) => FolderTreeList.ScrollIntoView(item);

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
            // An entry can be dragged only where the host can take a drop.
            (row.IsEntryLeaf() && MoveEntryToFolderCommand is null))
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
        // Cleared before the read, an entry drag would look itself up as no longer a drag and fall
        // through to the folder command.
        var command = ActiveMoveCommand;
        ClearDragState();

        if (source is null || target is not IFolderTreeRow targetRow ||
            targetRow.IsEntryLeaf() ||
            command is not { } move)
        {
            return;
        }

        var request = new FolderMoveRequest(source, targetRow);
        if (move.CanExecute(request))
        {
            move.Execute(request);
        }
    }

    /// <summary>
    /// A dragged folder is reparented and a dragged entry is moved into the folder it lands on; both
    /// drops onto a folder row, so one drop path serves both and the host decides which it accepts.
    /// </summary>
    private ICommand? ActiveMoveCommand =>
        _dragSourceRow is { } source && source.IsEntryLeaf() ? MoveEntryToFolderCommand : MoveFolderCommand;

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
            accepts = ActiveMoveCommand?.CanExecute(new FolderMoveRequest(source, target)) == true;
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
        if (e.Key == Key.F2 && CanManageSelected && CanManageRows && !IsEntrySelection)
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
