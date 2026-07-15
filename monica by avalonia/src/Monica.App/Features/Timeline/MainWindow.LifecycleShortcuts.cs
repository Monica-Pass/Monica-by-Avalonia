using Avalonia.Input;
using Monica.App.Features.Archive;
using Monica.App.Features.RecycleBin;
using Monica.App.Features.Timeline;
using Monica.App.ViewModels;

namespace Monica.App;

public partial class MainWindow
{
    private ArchiveWorkspaceView ArchiveWorkspaceView =>
        WorkspaceHost.GetOrCreate<ArchiveWorkspaceView>("Archive");
    private RecycleBinWorkspaceView RecycleBinWorkspaceView =>
        WorkspaceHost.GetOrCreate<RecycleBinWorkspaceView>("RecycleBin");
    private TimelineWorkspaceView TimelineWorkspaceView =>
        WorkspaceHost.GetOrCreate<TimelineWorkspaceView>("Timeline");

    internal bool TryHandleLifecycleWorkspaceShortcut(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        switch (viewModel.SelectedSection)
        {
            case "Archive":
                HandleArchiveShortcut(viewModel, e);
                break;
            case "RecycleBin":
                HandleRecycleBinShortcut(viewModel, e);
                break;
            case "Timeline":
                HandleTimelineShortcut(viewModel, e);
                break;
            default:
                return false;
        }

        return e.Handled;
    }

    private void HandleArchiveShortcut(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        if (TryHandleArchiveSelectionShortcut(viewModel, e)) return;
        if (HandleLifecycleNavigation(e, ArchiveWorkspaceView, viewModel.ArchiveSearchText, viewModel.ClearArchiveSearchCommand,
            () => viewModel.ArchiveNarrowShowsList, () => viewModel.CloseArchivedPasswordDetailsCommand.Execute(null))) return;
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.F) ArchiveWorkspaceView.FocusSearch();
        else if (e.Key is Key.Up or Key.Down && !IsTextEditingSource(e.Source)) ArchiveWorkspaceView.SelectAdjacent(viewModel, e.Key == Key.Down ? 1 : -1);
        else if (e.Key == Key.Enter && !IsTextEditingSource(e.Source) && viewModel.SelectedArchivedPassword is not null)
        {
            viewModel.ShowArchivedPasswordDetailsCommand.Execute(viewModel.SelectedArchivedPassword);
            ArchiveWorkspaceView.FocusDetails();
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.R && viewModel.SelectedArchivedPassword is not null)
            viewModel.UnarchivePasswordCommand.Execute(viewModel.SelectedArchivedPassword);
        else if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None && !IsTextEditingSource(e.Source) && viewModel.SelectedArchivedPassword is not null)
            viewModel.DeletePasswordCommand.Execute(viewModel.SelectedArchivedPassword);
        else return;
        e.Handled = true;
    }

    private void HandleRecycleBinShortcut(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        if (TryHandleRecycleBinSelectionShortcut(viewModel, e)) return;
        if (HandleLifecycleNavigation(e, RecycleBinWorkspaceView, viewModel.RecycleBinSearchText, viewModel.ClearRecycleBinSearchCommand,
            () => viewModel.RecycleBinNarrowShowsList, () => viewModel.CloseDeletedPasswordDetailsCommand.Execute(null))) return;
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.F) RecycleBinWorkspaceView.FocusSearch();
        else if (e.Key is Key.Up or Key.Down && !IsTextEditingSource(e.Source)) RecycleBinWorkspaceView.SelectAdjacent(viewModel, e.Key == Key.Down ? 1 : -1);
        else if (e.Key == Key.Enter && !IsTextEditingSource(e.Source) && viewModel.SelectedDeletedPassword is not null)
        {
            viewModel.ShowDeletedPasswordDetailsCommand.Execute(viewModel.SelectedDeletedPassword);
            RecycleBinWorkspaceView.FocusDetails();
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.R && viewModel.SelectedDeletedPassword is not null)
            viewModel.RestorePasswordCommand.Execute(viewModel.SelectedDeletedPassword);
        else if (e.KeyModifiers == KeyModifiers.Shift && e.Key == Key.Delete && !IsTextEditingSource(e.Source) && viewModel.SelectedDeletedPassword is not null)
            viewModel.DeletePasswordPermanentlyCommand.Execute(viewModel.SelectedDeletedPassword);
        else return;
        e.Handled = true;
    }

    private static bool TryHandleArchiveSelectionShortcut(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && viewModel.HasSelectedArchivedPasswords)
            viewModel.ClearArchivedPasswordSelectionCommand.Execute(null);
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.A &&
                 !IsTextEditingSource(e.Source) && viewModel.HasFilteredArchivedPasswords)
            viewModel.AreAllFilteredArchivedPasswordsSelected = true;
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.R && viewModel.HasSelectedArchivedPasswords)
            viewModel.UnarchiveSelectedArchivedPasswordsCommand.Execute(null);
        else
            return false;

        e.Handled = true;
        return true;
    }

    private static bool TryHandleRecycleBinSelectionShortcut(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && viewModel.HasSelectedDeletedPasswords)
            viewModel.ClearDeletedPasswordSelectionCommand.Execute(null);
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.A &&
                 !IsTextEditingSource(e.Source) && viewModel.HasFilteredDeletedPasswords)
            viewModel.AreAllFilteredDeletedPasswordsSelected = true;
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.R && viewModel.HasSelectedDeletedPasswords)
            viewModel.RestoreSelectedDeletedPasswordsCommand.Execute(null);
        else if (e.KeyModifiers == KeyModifiers.Shift && e.Key == Key.Delete &&
                 !IsTextEditingSource(e.Source) && viewModel.HasSelectedDeletedPasswords)
            viewModel.DeleteSelectedDeletedPasswordsPermanentlyCommand.Execute(null);
        else
            return false;

        e.Handled = true;
        return true;
    }

    private void HandleTimelineShortcut(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        if (HandleLifecycleNavigation(e, TimelineWorkspaceView, viewModel.TimelineSearchText, viewModel.ClearTimelineSearchCommand,
            () => viewModel.TimelineNarrowShowsList, () => viewModel.CloseTimelineEntryDetailsCommand.Execute(null))) return;
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.F) TimelineWorkspaceView.FocusSearch();
        else if (e.Key is Key.Up or Key.Down && !IsTextEditingSource(e.Source)) TimelineWorkspaceView.SelectAdjacent(viewModel, e.Key == Key.Down ? 1 : -1);
        else if (e.Key == Key.Enter && !IsTextEditingSource(e.Source) && viewModel.SelectedTimelineEntry is not null)
        {
            viewModel.ShowTimelineEntryDetailsCommand.Execute(viewModel.SelectedTimelineEntry);
            TimelineWorkspaceView.FocusDetails();
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.S && viewModel.SaveTimelineExportCommand.CanExecute(null))
            viewModel.SaveTimelineExportCommand.Execute(null);
        else return;
        e.Handled = true;
    }

    private static bool HandleLifecycleNavigation(
        KeyEventArgs e,
        object view,
        string searchText,
        System.Windows.Input.ICommand clearSearchCommand,
        Func<bool> showsList,
        Action closeDetails)
    {
        var isNarrow = view switch
        {
            ArchiveWorkspaceView archive => archive.IsNarrowLayout,
            RecycleBinWorkspaceView recycle => recycle.IsNarrowLayout,
            TimelineWorkspaceView timeline => timeline.IsNarrowLayout,
            _ => false
        };
        if ((e.Key == Key.Escape || e.Key == Key.Left && e.KeyModifiers == KeyModifiers.Alt) && isNarrow && !showsList())
        {
            closeDetails();
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Escape && !string.IsNullOrWhiteSpace(searchText))
        {
            clearSearchCommand.Execute(null);
            e.Handled = true;
            return true;
        }

        return false;
    }
}
