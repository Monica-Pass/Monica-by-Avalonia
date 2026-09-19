using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using FluentIcons.Common;
using Monica.App.Controls;
using Monica.App.ViewModels;

namespace Monica.App.Features;

public partial class UnlockedShellView : UserControl
{
    private static readonly DeferredNavigationItem[] DeferredVaultItems =
    [
        new("L.Library", "Vault", Symbol.Library),
        new("L.Passwords", "Passwords", Symbol.Key),
        new("L.SecureNotes", "Notes", Symbol.Note),
        new("L.Totp", "Totp", Symbol.Fingerprint),
        new("L.Cards", "Cards", Symbol.WalletCreditCard)
    ];

    private static readonly DeferredNavigationItem[] DeferredToolItems =
    [
        new("L.Generator", "Generator", Symbol.Sparkle),
        new("L.SecurityAnalysis", "SecurityAnalysis", Symbol.Shield),
        new("L.Timeline", "Timeline", Symbol.Clock)
    ];

    private static readonly DeferredNavigationItem[] DeferredStorageItems =
    [
        new("L.Archive", "Archive", Symbol.Archive),
        new("L.RecycleBin", "RecycleBin", Symbol.Delete),
        new("L.MdbxVaults", "Mdbx", Symbol.Database)
    ];

    private static readonly DeferredNavigationItem[] DeferredFooterItems =
    [
        new("L.DatabaseManagement", "DatabaseManagement", Symbol.Storage),
        new("L.SyncAndBackup", "Sync", Symbol.ArrowSync),
        new("L.Settings", "Settings", Symbol.Settings)
    ];

    private readonly WorkspaceHostView _workspaceHost;
    private readonly Dictionary<string, FANavigationViewItem> _navigationItemsByTag = new(StringComparer.OrdinalIgnoreCase);
    private NavigationSelectionMirror? _navigationMirror;
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
        _workspaceScaffold = new Grid { Margin = new Thickness(16) };
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
        VaultNavigationView.MenuItems.Add(CreateNavigationHeader("L.VaultNavigationGroup", "Vault"));
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

        var lockItem = CreateNavigationItem(new DeferredNavigationItem("LockVaultText", "Lock", Symbol.LockClosed));
        lockItem.Name = "LockVaultNavigationItem";
        lockItem.SelectsOnInvoked = false;
        VaultNavigationView.FooterMenuItems.Add(lockItem);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                ObserveViewModel(viewModel);
            }
        };
        if (DataContext is MainWindowViewModel dataContext)
        {
            ObserveViewModel(dataContext);
        }
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

    private FANavigationViewItem CreateNavigationItem(DeferredNavigationItem source)
    {
        var item = new FANavigationViewItem
        {
            Tag = source.Tag,
            IconSource = new FluentSymbolIconSource { Symbol = source.Symbol }
        };
        item.Bind(ContentControl.ContentProperty, new Binding(source.LabelPath));
        _navigationItemsByTag[source.Tag] = item;
        return item;
    }

    private void ObserveViewModel(MainWindowViewModel viewModel)
    {
        if (!ReferenceEquals(_navigationMirror?.ViewModel, viewModel))
        {
            ReleaseViewModelObservation();
            _navigationMirror = NavigationSelectionMirror.Attach(viewModel, this);
        }

        SyncNavigationSelection(viewModel.SelectedSection);
    }

    private void ReleaseViewModelObservation()
    {
        _navigationMirror?.Dispose();
        _navigationMirror = null;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is MainWindowViewModel viewModel)
        {
            ObserveViewModel(viewModel);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ReleaseViewModelObservation();
    }

    // Sections can also change without a rail click (unlock, keyboard shortcuts, in-page
    // navigation), and FANavigationView only highlights what it selected itself.
    private void SyncNavigationSelection(string section)
    {
        if (!_deferredNavigationInitialized ||
            !_navigationItemsByTag.TryGetValue(section, out var item) ||
            ReferenceEquals(VaultNavigationView.SelectedItem, item))
        {
            return;
        }

        VaultNavigationView.SelectedItem = item;
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

    private void WorkspaceHost_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.OtherWorkspaceViewportWidth = e.NewSize.Width;
            viewModel.OtherWorkspaceViewportHeight = e.NewSize.Height;
        }
    }

    // The view model must not be able to reach a shell it no longer displays. Subscribing the
    // shell itself would do exactly that: the only place to unsubscribe is the visual-tree detach,
    // and a minimized window never runs another layout pass, so the detach — and with it the whole
    // workspace graph — is deferred until the window comes back. This mirror is what the view model
    // holds instead; it keeps the shell weak and unsubscribes itself once the shell is gone.
    private sealed class NavigationSelectionMirror : IDisposable
    {
        private readonly WeakReference<UnlockedShellView> _shell;

        private NavigationSelectionMirror(MainWindowViewModel viewModel, UnlockedShellView shell)
        {
            ViewModel = viewModel;
            _shell = new WeakReference<UnlockedShellView>(shell);
            viewModel.PropertyChanged += OnPropertyChanged;
        }

        public MainWindowViewModel ViewModel { get; }

        public static NavigationSelectionMirror Attach(MainWindowViewModel viewModel, UnlockedShellView shell) =>
            new(viewModel, shell);

        public void Dispose() => ViewModel.PropertyChanged -= OnPropertyChanged;

        private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!_shell.TryGetTarget(out var shell))
            {
                Dispose();
                return;
            }

            if (e.PropertyName == nameof(MainWindowViewModel.SelectedSection) &&
                sender is MainWindowViewModel viewModel)
            {
                shell.SyncNavigationSelection(viewModel.SelectedSection);
            }
        }
    }

    private sealed record DeferredNavigationItem(
        string LabelPath,
        string Tag,
        Symbol Symbol);
}
