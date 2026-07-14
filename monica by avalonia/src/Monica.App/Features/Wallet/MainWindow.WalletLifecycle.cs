using Avalonia.Input;
using Monica.App.Features.Wallet;
using Monica.App.ViewModels;

namespace Monica.App;

public partial class MainWindow
{
    private WalletWorkspaceView WalletWorkspaceView =>
        WorkspaceHost.GetOrCreate<WalletWorkspaceView>("Cards");

    private void HandleWalletWorkspaceShortcut(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        if (TryReturnToWalletList(viewModel, e) || TryHandleWalletControlShortcut(viewModel, e))
        {
            return;
        }

        if (e.KeyModifiers != KeyModifiers.None || WalletWorkspaceView.IsNonSearchTextEditingSource(e.Source))
        {
            return;
        }

        HandleWalletNavigationShortcut(viewModel, e);
    }

    private bool TryReturnToWalletList(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        var isReturnKey = e.Key == Key.Escape || (e.Key == Key.Left && e.KeyModifiers == KeyModifiers.Alt);
        if (!isReturnKey || !WalletWorkspaceView.IsNarrowLayout || viewModel.WalletNarrowShowsList)
        {
            return false;
        }

        viewModel.ShowWalletListCommand.Execute(null);
        WalletWorkspaceView.FocusItemList();
        e.Handled = true;
        return true;
    }

    private bool TryHandleWalletControlShortcut(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.F)
        {
            WalletWorkspaceView.FocusSearch();
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Escape && viewModel.HasWalletSearchText)
        {
            viewModel.ClearWalletSearchCommand.Execute(null);
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.C &&
            e.KeyModifiers == KeyModifiers.Control &&
            !IsTextEditingSource(e.Source) &&
            viewModel.SelectedWalletItem is not null)
        {
            if (viewModel.CopySelectedWalletPrimaryFieldCommand.CanExecute(null))
            {
                viewModel.CopySelectedWalletPrimaryFieldCommand.Execute(null);
                e.Handled = true;
            }

            return e.Handled;
        }

        return false;
    }

    private void HandleWalletNavigationShortcut(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down)
        {
            WalletWorkspaceView.SelectAdjacentWalletItem(viewModel, e.Key == Key.Down ? 1 : -1);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && viewModel.SelectedWalletItem is not null)
        {
            viewModel.ShowWalletDetailsCommand.Execute(viewModel.SelectedWalletItem);
            WalletWorkspaceView.FocusWorkbench();
            e.Handled = true;
        }
        else if (e.Key == Key.F2 && !IsTextEditingSource(e.Source))
        {
            TryExecuteWalletItemCommand(viewModel.EditWalletItemCommand, viewModel, e);
        }
        else if (e.Key == Key.Delete && !IsTextEditingSource(e.Source))
        {
            TryExecuteWalletItemCommand(viewModel.DeleteWalletItemCommand, viewModel, e);
        }
    }

    private static bool TryExecuteWalletItemCommand(
        System.Windows.Input.ICommand command,
        MainWindowViewModel viewModel,
        KeyEventArgs e)
    {
        var item = viewModel.SelectedWalletItem;
        if (item is null || !command.CanExecute(item))
        {
            return false;
        }

        command.Execute(item);
        e.Handled = true;
        return true;
    }
}
