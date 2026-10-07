using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Monica.App.Features.Authenticator;
using Monica.App.Features.Notes;
using Monica.App.Features.Passwords;
using Monica.App.Features.Wallet;
using Monica.App.ViewModels;

namespace Monica.App.Features.Vault;

public partial class VaultWorkspaceView : UserControl
{
    private readonly Dictionary<VaultSurface, Control> _surfaces = [];
    // TOTP values are presentation state and are intentionally not persisted.  Keep the refresh
    // loop on the visible vault workspace so the code/progress bar follows the clock while the
    // authenticator surface is open, without waking a locked or background workspace.
    private readonly DispatcherTimer _totpRefreshTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(250)
    };
    private MainWindowViewModel? _viewModel;

    public VaultWorkspaceView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        _totpRefreshTimer.Tick += OnTotpRefreshTimerTick;
        VaultSurfaceHost.SizeChanged += (_, e) => ReportNoteViewportWidth(e.NewSize.Width);
    }

    // The note editor arranges its rail, tabs and inspector from the width it is given, and in the
    // library that is the detail pane rather than the whole page.
    private void ReportNoteViewportWidth(double width)
    {
        if (_viewModel is { } viewModel && viewModel.SelectedVaultSurface == VaultSurface.Note)
        {
            viewModel.NoteWorkspaceViewportWidth = width;
        }
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

        _totpRefreshTimer.Stop();

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
        _totpRefreshTimer.Start();
        ShowSurface(viewModel.SelectedVaultSurface);
        VaultEditorDialogWarmup.EnsureWarmedFor(viewModel.VaultGroup);
    }

    private void OnTotpRefreshTimerTick(object? sender, EventArgs e)
    {
        // The timer runs on Avalonia's UI dispatcher.  Refreshing the observable item properties
        // here updates both the selected authenticator console and any visible list rows without
        // rebuilding the vault tree on every tick.
        if (_viewModel is { IsUnlocked: true } viewModel)
        {
            viewModel.RefreshTotpPresentations();
        }
    }

    // A rail tap can change the preset while this page stays cached on screen, so the editors to
    // prepare are re-chosen on every section change, not only on attach.
    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is not { } viewModel)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.SelectedVaultSurface):
                ShowSurface(viewModel.SelectedVaultSurface);
                break;
            case nameof(MainWindowViewModel.SelectedSection):
                VaultEditorDialogWarmup.EnsureWarmedFor(viewModel.VaultGroup);
                break;
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
        if (surface == VaultSurface.Note)
        {
            ReportNoteViewportWidth(VaultSurfaceHost.Bounds.Width);
        }
    }

    private Control CreateSurface(VaultSurface surface) => surface switch
    {
        VaultSurface.Note => CreateNoteSurface(),
        VaultSurface.Totp => new AuthenticatorCodeConsoleView(),
        VaultSurface.Card => new WalletWorkbenchView(),
        _ => new PasswordDetailPaneView()
    };

    // The editor closes a tab on its own request; only this page knows that closing has to be
    // confirmed and that the tree has to drop the row afterwards.
    private NoteEditorView CreateNoteSurface()
    {
        var editor = new NoteEditorView();
        editor.CloseRequested += (_, args) =>
        {
            if (_viewModel is { } viewModel)
            {
                CloseNoteTabWithPrompt(viewModel, args.Tab);
            }
        };
        return editor;
    }
}
