using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Monica.App.Controls;
using Monica.App.ViewModels;

namespace Monica.App.Features;

public partial class UnlockedShellView : UserControl
{
    private const double CompactContentBreakpoint = 920;

    private static readonly DeferredNavigationItem[] DeferredVaultItems =
    [
        new("L.SecureNotes", "Notes", FASymbol.ProtectedDocument),
        new("L.Totp", "Totp", FASymbol.Clock),
        new("L.Cards", "Cards", FASymbol.ContactInfo)
    ];

    private static readonly DeferredNavigationItem[] DeferredToolItems =
    [
        new("L.Generator", "Generator", FASymbol.Edit),
        new("L.SecurityAnalysis", "SecurityAnalysis", FASymbol.Permissions),
        new("L.Timeline", "Timeline", FASymbol.Clock)
    ];

    private static readonly DeferredNavigationItem[] DeferredStorageItems =
    [
        new("L.Archive", "Archive", FASymbol.Library),
        new("L.RecycleBin", "RecycleBin", FASymbol.Delete),
        new("L.MdbxVaults", "Mdbx", FASymbol.Folder)
    ];

    private static readonly DeferredNavigationItem[] DeferredFooterItems =
    [
        new("L.DatabaseManagement", "DatabaseManagement", FASymbol.Library),
        new("L.SyncAndBackup", "Sync", FASymbol.Sync),
        new("L.Settings", "Settings", FASymbol.Setting)
    ];

    private readonly WorkspaceHostView _workspaceHost;
    private Grid? _workspaceScaffold;
    private bool _workspaceScaffoldInitialized;
    private bool _shellChromeInitialized;
    private bool _deferredNavigationInitialized;

    public UnlockedShellView()
    {
        _workspaceHost = new WorkspaceHostView();
        _workspaceHost.Bind(
            WorkspaceHostView.SectionProperty,
            new Binding(nameof(MainWindowViewModel.SelectedSection)));
        _workspaceHost.SizeChanged += WorkspaceHost_OnSizeChanged;
        SizeChanged += Shell_OnSizeChanged;
        Content = CreateLoadingPlaceholder();
        Dispatcher.UIThread.Post(InitializeWorkspaceScaffold, DispatcherPriority.Background);
    }

    private void InitializeWorkspaceScaffold()
    {
        if (_workspaceScaffoldInitialized)
        {
            return;
        }

        _workspaceScaffoldInitialized = true;
        _workspaceScaffold = new Grid { Margin = new Thickness(18) };
        _workspaceScaffold.Children.Add(_workspaceHost);
        Content = _workspaceScaffold;
        Dispatcher.UIThread.Post(InitializeDeferredShellChrome, DispatcherPriority.SystemIdle);
    }

    private void InitializeDeferredShellChrome()
    {
        if (_shellChromeInitialized || TopLevel.GetTopLevel(this) is null)
        {
            return;
        }

        _shellChromeInitialized = true;
        _workspaceScaffold?.Children.Remove(_workspaceHost);
        _workspaceScaffold = null;
        InitializeComponent();
        UpdateShellLayout(Bounds.Width);
        WorkspaceHostSlot.Content = _workspaceHost;
        Dispatcher.UIThread.Post(InitializeDeferredNavigation, DispatcherPriority.SystemIdle);
    }

    private void InitializeDeferredNavigation()
    {
        if (_deferredNavigationInitialized || TopLevel.GetTopLevel(this) is null)
        {
            return;
        }

        _deferredNavigationInitialized = true;
        AddNavigationItems(DeferredVaultItems);
        VaultNavigationView.MenuItems.Add(CreateNavigationHeader("L.ToolsNavigationGroup", "Tools"));
        AddNavigationItems(DeferredToolItems);
        VaultNavigationView.MenuItems.Add(CreateNavigationHeader("L.StorageNavigationGroup", "Storage"));
        AddNavigationItems(DeferredStorageItems);

        for (var index = 0; index < DeferredFooterItems.Length; index++)
        {
            VaultNavigationView.FooterMenuItems.Insert(
                index,
                CreateNavigationItem(DeferredFooterItems[index]));
        }

        VaultNavigationView.FooterMenuItems.Add(new FANavigationViewItemSeparator());
        var lockItem = CreateNavigationItem(new DeferredNavigationItem("LockVaultText", "Lock", FASymbol.Admin));
        lockItem.Name = "LockVaultNavigationItem";
        lockItem.SelectsOnInvoked = false;
        VaultNavigationView.FooterMenuItems.Add(lockItem);
    }

    private void AddNavigationItems(IEnumerable<DeferredNavigationItem> items)
    {
        foreach (var item in items)
        {
            VaultNavigationView.MenuItems.Add(CreateNavigationItem(item));
        }
    }

    private static FANavigationViewItemHeader CreateNavigationHeader(string labelPath, string tag)
    {
        var header = new FANavigationViewItemHeader { Tag = tag };
        header.Bind(ContentControl.ContentProperty, new Binding(labelPath));
        return header;
    }

    private static FANavigationViewItem CreateNavigationItem(DeferredNavigationItem source)
    {
        var item = new FANavigationViewItem
        {
            Tag = source.Tag,
            IconSource = new FASymbolIconSource { Symbol = source.Symbol }
        };
        item.Bind(ContentControl.ContentProperty, new Binding(source.LabelPath));
        return item;
    }

    private static Control CreateLoadingPlaceholder() =>
        new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Children =
            {
                new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Monica",
                            HorizontalAlignment = HorizontalAlignment.Center,
                            FontSize = 20
                        },
                        new ProgressBar
                        {
                            Width = 220,
                            Height = 4,
                            IsIndeterminate = true
                        }
                    }
                }
            }
        };

    private void NavigationView_OnSelectionChanged(object? sender, FANavigationViewSelectionChangedEventArgs e)
    {
        var tag = (e.SelectedItem as Control)?.Tag?.ToString()
            ?? (e.SelectedItemContainer as Control)?.Tag?.ToString();
        ActivateNavigationTag(tag);
    }

    private void NavigationView_OnItemInvoked(object? sender, FANavigationViewItemInvokedEventArgs e)
    {
        var tag = (e.InvokedItemContainer as Control)?.Tag?.ToString();
        if (!string.Equals(tag, "Lock", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ActivateNavigationTag(tag);
    }

    internal void ActivateNavigationTag(string? tag)
    {
        if (DataContext is not MainWindowViewModel viewModel || string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        if (string.Equals(tag, "Lock", StringComparison.OrdinalIgnoreCase))
        {
            if (viewModel.LockCommand.CanExecute(null))
            {
                viewModel.LockCommand.Execute(null);
            }

            return;
        }

        viewModel.SelectSectionCommand.Execute(tag);
    }

    private void Shell_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_shellChromeInitialized)
        {
            UpdateShellLayout(e.NewSize.Width);
        }
    }

    private void UpdateShellLayout(double width)
    {
        WorkspaceContentGrid.Margin = width > 0 && width < CompactContentBreakpoint
            ? new Thickness(12)
            : new Thickness(20);
    }

    private void WorkspaceHost_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.OtherWorkspaceViewportWidth = e.NewSize.Width;
            viewModel.OtherWorkspaceViewportHeight = e.NewSize.Height;
        }
    }

    private sealed record DeferredNavigationItem(
        string LabelPath,
        string Tag,
        FASymbol Symbol);
}
