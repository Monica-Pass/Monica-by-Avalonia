using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Monica.App.Features.Notes;
using Monica.App.ViewModels;

namespace Monica.App;

public partial class MainWindow
{
    private bool _isClosingAfterUnsavedNoteDecision;
    private bool _isHandlingUnsavedWindowClose;
    private MainWindowViewModel? _observedViewModel;

    private async void HandleNoteWorkspaceShortcut(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        if (NoteWorkspaceView.TryHandleShortcut(e))
        {
            return;
        }

        if (viewModel.IsNoteWorkspaceNarrow &&
            !viewModel.NoteNarrowShowsTree &&
            (e.Key == Key.Escape || (e.Key == Key.Left && e.KeyModifiers == KeyModifiers.Alt)))
        {
            viewModel.ShowNoteTreeCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (e.Key == Key.S)
        {
            e.Handled = true;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                await viewModel.SaveAllNoteTabsCommand.ExecuteAsync(null);
            }
            else
            {
                await viewModel.SaveNoteCommand.ExecuteAsync(null);
            }

            return;
        }

        if (e.Key == Key.N)
        {
            viewModel.AddNoteCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.W && viewModel.SelectedNoteTab is not null)
        {
            e.Handled = true;
            await NoteWorkspaceView.CloseTabWithPromptAsync(viewModel, viewModel.SelectedNoteTab);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
            _observedViewModel = null;
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isClosingAfterUnsavedNoteDecision ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var dirtyCount = viewModel.OpenNoteTabs.Count(tab => tab.IsDirty);
        if (dirtyCount == 0)
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
            var result = await NoteWorkspaceView.ShowUnsavedTabsDialogAsync(dirtyCount);
            if (result == FAContentDialogResult.Primary)
            {
                await viewModel.SaveAllNoteTabsCommand.ExecuteAsync(null);
                if (viewModel.OpenNoteTabs.Any(tab => tab.IsDirty))
                {
                    return;
                }

                _isClosingAfterUnsavedNoteDecision = true;
                Close();
            }
            else if (result == FAContentDialogResult.Secondary)
            {
                _isClosingAfterUnsavedNoteDecision = true;
                Close();
            }
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
            _observedViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        }

        UpdateWorkspaceActivation(_observedViewModel);

        Dispatcher.UIThread.Post(() =>
        {
            if (CurrentWorkspaceHost?.TryGet<NoteWorkspaceView>("Notes", out var noteWorkspace) == true)
            {
                noteWorkspace.UpdateTabScroll();
            }
        });
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsUnlocked))
        {
            UpdateWorkspaceActivation(sender as MainWindowViewModel);
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.SelectedNoteTab))
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (CurrentWorkspaceHost?.TryGet<NoteWorkspaceView>("Notes", out var noteWorkspace) == true)
                {
                    noteWorkspace.HandleSelectedTabChanged();
                }
            });
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.NoteTabWidth))
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (CurrentWorkspaceHost?.TryGet<NoteWorkspaceView>("Notes", out var noteWorkspace) == true)
                {
                    noteWorkspace.HandleTabWidthChanged();
                }
            });
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.NoteNarrowShowsTree) &&
                 _observedViewModel?.NoteNarrowShowsTree == true)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (CurrentWorkspaceHost?.TryGet<NoteWorkspaceView>("Notes", out var noteWorkspace) == true)
                {
                    noteWorkspace.FocusNoteTree();
                }
            });
        }
    }

}
