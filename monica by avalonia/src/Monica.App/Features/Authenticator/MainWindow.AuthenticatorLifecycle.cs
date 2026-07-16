using Avalonia.Input;
using Monica.App.Features.Authenticator;
using Monica.App.ViewModels;

namespace Monica.App;

public partial class MainWindow
{
    private AuthenticatorWorkspaceView AuthenticatorWorkspaceView =>
        WorkspaceHost.GetOrCreate<AuthenticatorWorkspaceView>("Totp");

    internal void HandleAuthenticatorWorkspaceShortcut(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        if (TryReturnToAuthenticatorList(viewModel, e) || TryHandleAuthenticatorControlShortcut(viewModel, e))
        {
            return;
        }

        if (e.KeyModifiers != KeyModifiers.None || AuthenticatorWorkspaceView.IsNonSearchTextEditingSource(e.Source))
        {
            return;
        }

        HandleAuthenticatorNavigationShortcut(viewModel, e);
    }

    private bool TryReturnToAuthenticatorList(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        var isReturnKey = e.Key == Key.Escape || (e.Key == Key.Left && e.KeyModifiers == KeyModifiers.Alt);
        if (!isReturnKey || !AuthenticatorWorkspaceView.IsNarrowLayout || viewModel.TotpNarrowShowsList)
        {
            return false;
        }

        viewModel.ShowTotpListCommand.Execute(null);
        AuthenticatorWorkspaceView.FocusAccountList();
        e.Handled = true;
        return true;
    }

    private bool TryHandleAuthenticatorControlShortcut(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.F)
        {
            AuthenticatorWorkspaceView.FocusSearch();
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Escape &&
            AuthenticatorWorkspaceView.IsSearchFocused &&
            viewModel.HasTotpSearchText)
        {
            viewModel.ClearTotpSearchCommand.Execute(null);
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Escape && viewModel.HasTotpFilterOrSearch)
        {
            viewModel.ClearTotpFiltersCommand.Execute(null);
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control && !IsTextEditingSource(e.Source))
        {
            return TryExecuteAuthenticatorItemCommand(viewModel.CopyTotpCommand, viewModel, e);
        }

        return false;
    }

    private void HandleAuthenticatorNavigationShortcut(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down)
        {
            AuthenticatorWorkspaceView.SelectAdjacentAuthenticator(viewModel, e.Key == Key.Down ? 1 : -1);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && viewModel.SelectedTotpItem is not null)
        {
            viewModel.ShowTotpDetailsCommand.Execute(viewModel.SelectedTotpItem);
            AuthenticatorWorkspaceView.FocusCodeRegion();
            e.Handled = true;
        }
        else if (e.Key == Key.F2 && !IsTextEditingSource(e.Source))
        {
            TryExecuteAuthenticatorItemCommand(viewModel.EditTotpCommand, viewModel, e);
        }
        else if (e.Key == Key.Delete && !IsTextEditingSource(e.Source))
        {
            TryExecuteAuthenticatorItemCommand(viewModel.DeleteTotpCommand, viewModel, e);
        }
    }

    private static bool TryExecuteAuthenticatorItemCommand(
        System.Windows.Input.ICommand command,
        MainWindowViewModel viewModel,
        KeyEventArgs e)
    {
        var item = viewModel.SelectedTotpItem;
        if (item is null || !command.CanExecute(item))
        {
            return false;
        }

        command.Execute(item);
        e.Handled = true;
        return true;
    }
}
