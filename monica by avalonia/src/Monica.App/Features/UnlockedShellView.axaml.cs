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
    private static readonly DeferredNavigationItem[] DeferredMenuItems =
    [
        new("L.SecureNotes", "Notes", FASymbol.ProtectedDocument),
        new("L.Totp", "Totp", FASymbol.Clock),
        new("L.Cards", "Cards", FASymbol.ContactInfo),
        new("L.Generator", "Generator", FASymbol.Edit),
        new("L.Archive", "Archive", FASymbol.Library),
        new("L.RecycleBin", "RecycleBin", FASymbol.Delete),
        new("L.SecurityAnalysis", "SecurityAnalysis", FASymbol.Permissions),
        new("L.Timeline", "Timeline", FASymbol.Clock),
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
        foreach (var item in DeferredMenuItems)
        {
            VaultNavigationView.MenuItems.Add(CreateNavigationItem(item));
        }

        for (var index = 0; index < DeferredFooterItems.Length; index++)
        {
            VaultNavigationView.FooterMenuItems.Insert(
                index,
                CreateNavigationItem(DeferredFooterItems[index]));
        }
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
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var tag = (e.SelectedItem as Control)?.Tag?.ToString()
            ?? (e.SelectedItemContainer as Control)?.Tag?.ToString();
        viewModel.SelectSectionCommand.Execute(tag);
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
