using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Monica.App.ViewModels;

namespace Monica.App;

public partial class MainWindow
{
    private readonly DispatcherTimer _autoLockTimer = new()
    {
        Interval = TimeSpan.FromSeconds(5)
    };

    private void InitializeSecurityLifecycle()
    {
        Activated += SecurityOnActivated;
        Deactivated += SecurityOnDeactivated;
        Closed += SecurityOnClosed;
        AddHandler(PointerPressedEvent, SecurityOnPointerPressed, RoutingStrategies.Tunnel);
        _autoLockTimer.Tick += SecurityOnAutoLockTimerTick;
        _autoLockTimer.Start();
    }

    private async void SecurityOnActivated(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.HandleWindowActivatedAsync();
        }
    }

    private void SecurityOnDeactivated(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.HandleWindowDeactivated();
        }
    }

    private void SecurityOnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.RecordUserActivity();
        }
    }

    private async void SecurityOnAutoLockTimerTick(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.CheckAutoLockAsync();
        }
    }

    private void SecurityOnClosed(object? sender, EventArgs e)
    {
        _autoLockTimer.Stop();
        _autoLockTimer.Tick -= SecurityOnAutoLockTimerTick;
    }
}
