using Avalonia;
using Avalonia.Controls;
using Monica.App.Features;
using Monica.App.ViewModels;

namespace Monica.App;

public partial class MainWindow
{
    private bool _isUnlockedShellHibernated;

    private void InitializeBackgroundMemoryLifecycle()
    {
        PropertyChanged += BackgroundMemoryOnPropertyChanged;
        Closed += BackgroundMemoryOnClosed;
    }

    private void BackgroundMemoryOnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property != WindowStateProperty)
        {
            return;
        }

        SetUnlockedShellHibernated(WindowState == WindowState.Minimized);
    }

    private void SetUnlockedShellHibernated(bool isHibernated)
    {
        if (_isUnlockedShellHibernated == isHibernated)
        {
            return;
        }

        _isUnlockedShellHibernated = isHibernated;
        if (isHibernated)
        {
            _workspaceActivationVersion++;
            if (CurrentWorkspaceHost is { } workspaceHost)
            {
                workspaceHost.IsActive = false;
            }

            VaultEditorDialogWarmup.SuspendPreparedViews();
            SynchronizeBackgroundMemoryState(DataContext as MainWindowViewModel);
            return;
        }

        VaultEditorDialogWarmup.ResumePreparedViews();
        var viewModel = DataContext as MainWindowViewModel;
        SynchronizeBackgroundMemoryState(viewModel);
        UpdateWorkspaceActivation(viewModel);
    }

    private void SynchronizeBackgroundMemoryState(MainWindowViewModel? viewModel) =>
        viewModel?.SetUnlockedShellHibernated(_isUnlockedShellHibernated);

    private void BackgroundMemoryOnClosed(object? sender, EventArgs e)
    {
        PropertyChanged -= BackgroundMemoryOnPropertyChanged;
        Closed -= BackgroundMemoryOnClosed;
        _isUnlockedShellHibernated = false;
        VaultEditorDialogWarmup.ResumePreparedViews();
    }
}
