using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Monica.App.ViewModels;

namespace Monica.App;

public partial class MainWindow
{
    private readonly DispatcherTimer _autoLockTimer = new();
    private MainWindowViewModel? _securityObservedViewModel;
    private bool _isSecurityLifecycleClosed;

    internal bool IsAutoLockCheckScheduled => _autoLockTimer.IsEnabled;

    internal TimeSpan AutoLockCheckInterval => _autoLockTimer.Interval;

    internal int AutoLockScheduleCount { get; private set; }

    private void InitializeSecurityLifecycle()
    {
        Activated += SecurityOnActivated;
        Deactivated += SecurityOnDeactivated;
        Closed += SecurityOnClosed;
        DataContextChanged += SecurityOnDataContextChanged;
        AddHandler(PointerPressedEvent, SecurityOnPointerPressed, RoutingStrategies.Tunnel);
        _autoLockTimer.Tick += SecurityOnAutoLockTimerTick;
        ObserveSecurityViewModel(DataContext as MainWindowViewModel);
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
        await RunScheduledAutoLockCheckAsync();
    }

    internal async Task RunScheduledAutoLockCheckAsync()
    {
        _autoLockTimer.Stop();
        var viewModel = DataContext as MainWindowViewModel;
        if (viewModel is not null)
        {
            await viewModel.CheckAutoLockAsync();
        }

        if (ReferenceEquals(viewModel, DataContext))
        {
            ScheduleAutoLockCheck(viewModel);
        }
    }

    private void SecurityOnDataContextChanged(object? sender, EventArgs e) =>
        ObserveSecurityViewModel(DataContext as MainWindowViewModel);

    private void ObserveSecurityViewModel(MainWindowViewModel? viewModel)
    {
        if (_securityObservedViewModel is not null)
        {
            _securityObservedViewModel.AutoLockScheduleChanged -= SecurityOnAutoLockScheduleChanged;
        }

        _securityObservedViewModel = viewModel;
        if (_securityObservedViewModel is not null)
        {
            _securityObservedViewModel.AutoLockScheduleChanged += SecurityOnAutoLockScheduleChanged;
        }

        ScheduleAutoLockCheck(viewModel);
    }

    private void SecurityOnAutoLockScheduleChanged(object? sender, EventArgs e)
    {
        if (sender is MainWindowViewModel viewModel && ReferenceEquals(viewModel, DataContext))
        {
            ScheduleAutoLockCheck(viewModel);
        }
    }

    private void ScheduleAutoLockCheck(MainWindowViewModel? viewModel)
    {
        if (_isSecurityLifecycleClosed)
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (ReferenceEquals(viewModel, DataContext))
                    {
                        ScheduleAutoLockCheck(viewModel);
                    }
                },
                DispatcherPriority.Background);
            return;
        }

        _autoLockTimer.Stop();
        if (viewModel is null || !viewModel.TryGetAutoLockDelay(out var delay))
        {
            return;
        }

        _autoLockTimer.Interval = delay;
        _autoLockTimer.Start();
        AutoLockScheduleCount++;
    }

    private void SecurityOnClosed(object? sender, EventArgs e)
    {
        _isSecurityLifecycleClosed = true;
        _autoLockTimer.Stop();
        _autoLockTimer.Tick -= SecurityOnAutoLockTimerTick;
        DataContextChanged -= SecurityOnDataContextChanged;
        if (_securityObservedViewModel is not null)
        {
            _securityObservedViewModel.AutoLockScheduleChanged -= SecurityOnAutoLockScheduleChanged;
            _securityObservedViewModel = null;
        }
    }
}
