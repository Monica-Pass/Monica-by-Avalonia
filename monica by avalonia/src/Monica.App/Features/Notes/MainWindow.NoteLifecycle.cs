using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Monica.App.Features.Notes;
using Monica.App.ViewModels;

namespace Monica.App;

public partial class MainWindow
{
    private bool _isClosingAfterShutdown;
    private bool _isHandlingUnsavedWindowClose;
    private MainWindowViewModel? _observedViewModel;

    internal Func<Task>? ShutdownRequestedAsync { get; set; }

    private void OnClosed(object? sender, EventArgs e)
    {
        ShutdownRequestedAsync = null;
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
            _observedViewModel = null;
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isClosingAfterShutdown)
        {
            return;
        }

        var viewModel = DataContext as MainWindowViewModel;
        if (!_isExplicitExitRequested && viewModel?.MinimizeToTray == true)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        var dirtyCount = viewModel?.OpenNoteTabs.Count(tab => tab.IsDirty) ?? 0;
        var shutdownRequested = ShutdownRequestedAsync;
        if (dirtyCount == 0 && shutdownRequested is null)
        {
            return;
        }

        e.Cancel = true;
        if (_isHandlingUnsavedWindowClose)
        {
            return;
        }

        _isHandlingUnsavedWindowClose = true;
        try
        {
            if (dirtyCount > 0 && viewModel is not null)
            {
                var result = await NoteClosePrompts.ShowUnsavedTabsAsync(this, viewModel, dirtyCount);
                if (result == FAContentDialogResult.Primary)
                {
                    await viewModel.SaveAllNoteTabsCommand.ExecuteAsync(null);
                    if (viewModel.OpenNoteTabs.Any(tab => tab.IsDirty))
                    {
                        return;
                    }
                }
                else if (result != FAContentDialogResult.Secondary)
                {
                    return;
                }
            }

            if (shutdownRequested is not null)
            {
                try
                {
                    await shutdownRequested();
                }
                catch (Exception exception)
                {
                    AppDiagnostics.Error("Application shutdown cleanup failed", exception);
                }
            }

            _isClosingAfterShutdown = true;
            Close();
        }
        finally
        {
            _isHandlingUnsavedWindowClose = false;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        }

        _observedViewModel = DataContext as MainWindowViewModel;
        if (_observedViewModel is not null)
        {
            SynchronizeBackgroundMemoryState(_observedViewModel);
            _observedViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        }

        UpdateWorkspaceActivation(_observedViewModel);
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsUnlocked))
        {
            UpdateWorkspaceActivation(sender as MainWindowViewModel);
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.SelectedNoteTab))
        {
            Dispatcher.UIThread.Post(() => CurrentVaultWorkspace?.HandleSelectedNoteTabChanged());
        }
    }

}
