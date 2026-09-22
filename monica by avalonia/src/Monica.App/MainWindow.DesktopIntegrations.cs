using Avalonia.Controls;
using Avalonia.Threading;
using Monica.App.Features.Vault;
using Monica.App.ViewModels;

namespace Monica.App;

public partial class MainWindow
{
    private bool _isExplicitExitRequested;

    internal void ShowFromDesktopIntegration()
    {
        AppDiagnostics.Info("Desktop integration surfaced the main window.");
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    internal void FocusDesktopQuickSearch()
    {
        if (DataContext is not MainWindowViewModel { IsUnlocked: true } viewModel)
        {
            return;
        }

        viewModel.SelectedSection = VaultPresets.LibrarySection;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (DataContext is MainWindowViewModel { IsUnlocked: true })
                {
                    VaultWorkspaceView.FocusSearch();
                }
            },
            DispatcherPriority.ContextIdle);
    }

    internal void RequestExplicitExit()
    {
        _isExplicitExitRequested = true;
        ShowFromDesktopIntegration();
        Close();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (!_isExplicitExitRequested &&
            WindowState == WindowState.Minimized &&
            DataContext is MainWindowViewModel { MinimizeToTray: true })
        {
            Hide();
        }
    }
}
