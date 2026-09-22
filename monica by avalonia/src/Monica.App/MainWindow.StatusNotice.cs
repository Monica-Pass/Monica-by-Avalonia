using Avalonia.Threading;
using Monica.App.ViewModels;

namespace Monica.App;

// The clock behind the status bar's self-clearing acknowledgements. Which sentences are temporary
// is decided in the ViewModel, where the action is; arming a timer is decided here, because a timer
// is exactly the thing a ViewModel should not own.
public partial class MainWindow
{
    // Repeating in Avalonia, so the tick handler re-arms only while a notice is still pending —
    // which makes it stop by itself the moment the line it was watching is gone.
    private readonly DispatcherTimer _statusNoticeTimer = new();
    private MainWindowViewModel? _statusNoticeObservedViewModel;

    internal bool IsStatusNoticeRetirementScheduled => _statusNoticeTimer.IsEnabled;

    internal TimeSpan StatusNoticeRetirementInterval => _statusNoticeTimer.Interval;

    private void InitializeStatusNoticeLifecycle()
    {
        DataContextChanged += (_, _) => ObserveStatusNoticeViewModel(DataContext as MainWindowViewModel);
        _statusNoticeTimer.Tick += (_, _) => RunScheduledStatusNoticeRetirement();
        ObserveStatusNoticeViewModel(DataContext as MainWindowViewModel);
    }

    private void ObserveStatusNoticeViewModel(MainWindowViewModel? viewModel)
    {
        if (_statusNoticeObservedViewModel is not null)
        {
            _statusNoticeObservedViewModel.StatusNoticeScheduleChanged -= OnStatusNoticeScheduleChanged;
        }

        _statusNoticeObservedViewModel = viewModel;
        if (_statusNoticeObservedViewModel is not null)
        {
            _statusNoticeObservedViewModel.StatusNoticeScheduleChanged += OnStatusNoticeScheduleChanged;
        }

        ArmStatusNoticeRetirement(viewModel);
    }

    private void OnStatusNoticeScheduleChanged(object? sender, EventArgs e)
    {
        if (sender is MainWindowViewModel viewModel && ReferenceEquals(viewModel, DataContext))
        {
            ArmStatusNoticeRetirement(viewModel);
        }
    }

    private void ArmStatusNoticeRetirement(MainWindowViewModel? viewModel)
    {
        _statusNoticeTimer.Stop();
        if (viewModel is null || !viewModel.IsStatusNoticePending)
        {
            return;
        }

        _statusNoticeTimer.Interval = viewModel.StatusNoticeDwell;
        _statusNoticeTimer.Start();
    }

    internal void RunScheduledStatusNoticeRetirement()
    {
        var viewModel = _statusNoticeObservedViewModel;
        if (viewModel is null)
        {
            return;
        }

        viewModel.RetireExpiredStatusNotice();
        // A tick that arrived before the dwell really elapsed leaves the sentence on screen; this
        // puts the clock back under it rather than letting it sit there with nothing watching.
        ArmStatusNoticeRetirement(viewModel);
    }
}
