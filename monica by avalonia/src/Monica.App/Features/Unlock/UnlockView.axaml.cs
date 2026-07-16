using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Monica.App.ViewModels;

namespace Monica.App.Features.Unlock;

public partial class UnlockView : UserControl, IDisposable
{
    private MainWindowViewModel? _subscribedViewModel;
    private bool _disposed;

    public UnlockView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnDataContextChanged(object? sender, EventArgs e) =>
        AttachViewModel(DataContext as MainWindowViewModel);

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        AttachViewModel(DataContext as MainWindowViewModel);
        FocusMasterPasswordWhenReady();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) =>
        AttachViewModel(null);

    private void AttachViewModel(MainWindowViewModel? viewModel)
    {
        if (ReferenceEquals(_subscribedViewModel, viewModel))
        {
            return;
        }

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        }

        _subscribedViewModel = viewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged += ViewModelOnPropertyChanged;
        }

        FocusMasterPasswordWhenReady();
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(MainWindowViewModel.IsVaultAccessReady) or
            nameof(MainWindowViewModel.IsUnlocked))
        {
            FocusMasterPasswordWhenReady();
        }
    }

    private void FocusMasterPasswordWhenReady()
    {
        if (_disposed)
        {
            return;
        }

        if (_subscribedViewModel is { IsVaultAccessReady: false } or { IsUnlocked: true })
        {
            return;
        }

        var weakView = new WeakReference<UnlockView>(this);
        Dispatcher.UIThread.Post(() =>
        {
            if (weakView.TryGetTarget(out var view) &&
                !view._disposed &&
                view.MasterPasswordInput.IsEnabled)
            {
                view.MasterPasswordInput.Focus();
            }
        }, DispatcherPriority.Input);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DataContextChanged -= OnDataContextChanged;
        AttachedToVisualTree -= OnAttachedToVisualTree;
        DetachedFromVisualTree -= OnDetachedFromVisualTree;
        AttachViewModel(null);
        DataContext = null;
        Content = null;
    }
}
