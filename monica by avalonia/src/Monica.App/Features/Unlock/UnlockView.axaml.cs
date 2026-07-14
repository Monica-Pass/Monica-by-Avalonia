using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Monica.App.ViewModels;

namespace Monica.App.Features.Unlock;

public partial class UnlockView : UserControl
{
    private MainWindowViewModel? _subscribedViewModel;

    public UnlockView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachViewModel(DataContext as MainWindowViewModel);
        AttachedToVisualTree += (_, _) =>
        {
            AttachViewModel(DataContext as MainWindowViewModel);
            FocusMasterPasswordWhenReady();
        };
        DetachedFromVisualTree += (_, _) => AttachViewModel(null);
    }

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
        if (_subscribedViewModel is { IsVaultAccessReady: false } or { IsUnlocked: true })
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (MasterPasswordInput.IsEnabled)
            {
                MasterPasswordInput.Focus();
            }
        }, DispatcherPriority.Input);
    }
}
