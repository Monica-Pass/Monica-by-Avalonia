using Avalonia.Threading;
using Monica.App.ViewModels;

namespace Monica.App;

// Where the window went is the one thing the tray cannot say for itself, so the first time the user
// hides it the app draws the explanation beside the corner the tray icon occupies. Which installs have
// been told is the ViewModel's call; the window and its clock are owned here.
public partial class MainWindow
{
    private readonly DispatcherTimer _trayHintTimer = new();
    private TrayHintWindow? _trayHintWindow;

    internal bool IsTrayHintVisible => _trayHintWindow?.IsVisible == true;

    internal TrayHintWindow? ActiveTrayHint => _trayHintWindow;

    internal bool IsTrayHintRetirementScheduled => _trayHintTimer.IsEnabled;

    internal TimeSpan TrayHintRetirementInterval => _trayHintTimer.Interval;

    private void InitializeTrayHintLifecycle()
    {
        _trayHintTimer.Tick += (_, _) => CloseTrayHint();
        Closed += (_, _) => CloseTrayHint();
    }

    // Called from the two paths that put the window out of sight - minimize and close-to-tray - because
    // both are the user acting on the window right now, which is the only moment this lands.
    private void SurfaceTrayHintAfterHide()
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.ShouldSurfaceTrayHint())
        {
            return;
        }

        viewModel.MarkTrayHintSurfaced();
        CloseTrayHint();
        var hint = new TrayHintWindow(
            viewModel.TrayHintTitleText,
            viewModel.TrayHintBodyText,
            viewModel.TrayHintShowActionText);
        hint.SurfaceRequested += CloseTrayHintAndSurfaceMain;
        _trayHintWindow = hint;
        AppDiagnostics.Info("Tray hint surfaced after the window was hidden.");
        hint.Show();
        _trayHintTimer.Interval = viewModel.TrayHintDwell;
        _trayHintTimer.Start();
    }

    private void CloseTrayHintAndSurfaceMain()
    {
        CloseTrayHint();
        ShowFromDesktopIntegration();
    }

    private void CloseTrayHint()
    {
        _trayHintTimer.Stop();
        if (_trayHintWindow is not { } hint)
        {
            return;
        }

        _trayHintWindow = null;
        hint.SurfaceRequested -= CloseTrayHintAndSurfaceMain;
        hint.Close();
    }
}
