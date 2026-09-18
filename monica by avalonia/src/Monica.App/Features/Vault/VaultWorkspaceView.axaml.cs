using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Monica.App.Features.Authenticator;
using Monica.App.Features.Notes;
using Monica.App.Features.Passwords;
using Monica.App.Features.Wallet;
using Monica.App.ViewModels;

namespace Monica.App.Features.Vault;

public partial class VaultWorkspaceView : UserControl
{
    private readonly Dictionary<VaultSurface, Control> _surfaces = [];
    private MainWindowViewModel? _viewModel;

    public VaultWorkspaceView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnDataContextChanged(object? sender, EventArgs e) => AttachViewModel(DataContext as MainWindowViewModel);

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) =>
        AttachViewModel(DataContext as MainWindowViewModel);

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) => AttachViewModel(null);

    // The tree is a projection over entries that carry plaintext secrets, so it is only watched
    // while this page is on screen; the host caches the page across section switches.
    private void AttachViewModel(MainWindowViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        if (_viewModel is { } previous)
        {
            previous.PropertyChanged -= ViewModelOnPropertyChanged;
            previous.SetVaultTreeActive(false);
        }

        foreach (var surface in _surfaces.Values)
        {
            surface.DataContext = null;
        }

        _surfaces.Clear();
        VaultSurfaceHost.Content = null;
        _viewModel = viewModel;
        if (viewModel is null)
        {
            return;
        }

        viewModel.SetVaultTreeActive(true);
        viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        ShowSurface(viewModel.SelectedVaultSurface);
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedVaultSurface) && _viewModel is { } viewModel)
        {
            ShowSurface(viewModel.SelectedVaultSurface);
        }
    }

    private void ShowSurface(VaultSurface surface)
    {
        if (surface == VaultSurface.None)
        {
            VaultSurfaceHost.Content = null;
            return;
        }

        if (!_surfaces.TryGetValue(surface, out var view))
        {
            view = CreateSurface(surface);
            _surfaces[surface] = view;
        }

        view.DataContext = _viewModel;
        VaultSurfaceHost.Content = view;
    }

    private static Control CreateSurface(VaultSurface surface) => surface switch
    {
        VaultSurface.Note => new NoteEditorView(),
        VaultSurface.Totp => new AuthenticatorCodeConsoleView(),
        VaultSurface.Card => new WalletWorkbenchView(),
        _ => new PasswordDetailPaneView()
    };
}
