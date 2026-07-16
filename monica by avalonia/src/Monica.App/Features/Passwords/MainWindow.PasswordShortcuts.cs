using Avalonia.Input;
using Monica.App.ViewModels;

namespace Monica.App;

public partial class MainWindow
{
    internal void TryHandlePasswordWorkspaceShortcut(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        if (e.Key == Key.Left && e.KeyModifiers == KeyModifiers.Alt && viewModel.SelectedPassword is not null)
        {
            ClosePasswordDetails(viewModel);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.F)
        {
            PasswordVaultView.FocusSearch();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.N)
        {
            if (viewModel.AddPasswordCommand.CanExecute(null))
            {
                viewModel.AddPasswordCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Escape &&
            PasswordVaultView.IsSearchFocused &&
            viewModel.HasPasswordSearchText)
        {
            if (viewModel.ClearPasswordSearchCommand.CanExecute(null))
            {
                viewModel.ClearPasswordSearchCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Escape &&
            viewModel.SelectedPassword is not null &&
            (PasswordVaultView.IsNarrowLayout || PasswordVaultView.IsDetailFocused))
        {
            ClosePasswordDetails(viewModel);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && viewModel.HasPasswordFilters)
        {
            if (viewModel.ClearPasswordFiltersCommand.CanExecute(null))
            {
                viewModel.ClearPasswordFiltersCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.C &&
            !IsTextEditingSource(e.Source) &&
            viewModel.SelectedPassword is not null &&
            e.KeyModifiers is KeyModifiers.Control or (KeyModifiers.Control | KeyModifiers.Shift))
        {
            var command = e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                ? viewModel.CopyPasswordCommand
                : viewModel.CopyUsernameCommand;
            if (command.CanExecute(viewModel.SelectedPassword))
            {
                command.Execute(viewModel.SelectedPassword);
                e.Handled = true;
            }

            return;
        }

        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (e.Key == Key.F2 && !IsTextEditingSource(e.Source))
        {
            if (viewModel.SelectedPassword is not null &&
                viewModel.EditPasswordCommand.CanExecute(viewModel.SelectedPassword))
            {
                viewModel.EditPasswordCommand.Execute(viewModel.SelectedPassword);
                e.Handled = true;
            }

            return;
        }

        if (e.Key is Key.Up or Key.Down && !PasswordVaultView.IsNonSearchTextEditingSource(e.Source))
        {
            PasswordVaultView.SelectAdjacentPassword(viewModel, e.Key == Key.Down ? 1 : -1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter &&
            viewModel.HasCurrentSelectedPasswordDetails &&
            !PasswordVaultView.IsNonSearchTextEditingSource(e.Source))
        {
            PasswordVaultView.FocusDetails();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && !IsTextEditingSource(e.Source))
        {
            if (viewModel.SelectedPassword is not null &&
                viewModel.DeletePasswordCommand.CanExecute(viewModel.SelectedPassword))
            {
                viewModel.DeletePasswordCommand.Execute(viewModel.SelectedPassword);
                e.Handled = true;
            }
        }
    }

    private void ClosePasswordDetails(MainWindowViewModel viewModel)
    {
        if (viewModel.CloseSelectedPasswordDetailsCommand.CanExecute(null))
        {
            viewModel.CloseSelectedPasswordDetailsCommand.Execute(null);
            PasswordVaultView.FocusPasswordList();
        }
    }
}
