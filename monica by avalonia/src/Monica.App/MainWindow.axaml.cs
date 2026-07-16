using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Monica.App.ViewModels;

namespace Monica.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosing;
        Closed += OnClosed;
        DataContextChanged += OnDataContextChanged;
        InitializeSecurityLifecycle();
        InitializeBackgroundMemoryLifecycle();
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.EnableWindowCaptureProtection();
            await viewModel.InitializeCommand.ExecuteAsync(null);
        }
    }

    private void MainWindow_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainWindowViewModel activityViewModel)
        {
            activityViewModel.RecordUserActivity();
        }

        if (DataContext is not MainWindowViewModel viewModel ||
            !viewModel.IsUnlocked)
        {
            return;
        }

        if (string.Equals(viewModel.SelectedSection, "Notes", StringComparison.OrdinalIgnoreCase))
        {
            HandleNoteWorkspaceShortcut(viewModel, e);
            if (e.Handled)
            {
                return;
            }
        }

        if (string.Equals(viewModel.SelectedSection, "Totp", StringComparison.OrdinalIgnoreCase))
        {
            HandleAuthenticatorWorkspaceShortcut(viewModel, e);
            if (e.Handled)
            {
                return;
            }
        }

        if (string.Equals(viewModel.SelectedSection, "Cards", StringComparison.OrdinalIgnoreCase))
        {
            HandleWalletWorkspaceShortcut(viewModel, e);
            if (e.Handled)
            {
                return;
            }
        }

        if (string.Equals(viewModel.SelectedSection, "Generator", StringComparison.OrdinalIgnoreCase))
        {
            HandleGeneratorWorkspaceShortcut(viewModel, e);
            if (e.Handled)
            {
                return;
            }
        }

        if (TryHandleLifecycleWorkspaceShortcut(viewModel, e))
        {
            return;
        }

        if (e.Key == Key.F5 && !IsTextEditingSource(e.Source))
        {
            if (viewModel.LoadCommand.CanExecute(null))
            {
                viewModel.LoadCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.N)
        {
            if (TryExecuteCurrentSectionNewCommand(viewModel))
            {
                e.Handled = true;
                return;
            }
        }

        if (!string.Equals(viewModel.SelectedSection, "Passwords", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

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

        if (e.Key == Key.Enter && viewModel.HasCurrentSelectedPasswordDetails && !PasswordVaultView.IsNonSearchTextEditingSource(e.Source))
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

    private static bool TryExecuteCurrentSectionNewCommand(MainWindowViewModel viewModel)
    {
        switch (viewModel.SelectedSection)
        {
            case "Passwords":
                if (viewModel.AddPasswordCommand.CanExecute(null))
                {
                    viewModel.AddPasswordCommand.Execute(null);
                    return true;
                }

                return false;

            case "Totp":
                if (viewModel.AddTotpCommand.CanExecute(null))
                {
                    viewModel.AddTotpCommand.Execute(null);
                    return true;
                }

                return false;

            case "Cards":
                if (viewModel.AddWalletItemCommand.CanExecute(null))
                {
                    viewModel.AddWalletItemCommand.Execute(null);
                    return true;
                }

                return false;

            case "Generator":
                if (viewModel.GeneratePasswordCommand.CanExecute(null))
                {
                    viewModel.GeneratePasswordCommand.Execute(null);
                    return true;
                }

                return false;

            case "Mdbx":
                if (viewModel.CreateMdbxVaultCommand.CanExecute(null))
                {
                    viewModel.CreateMdbxVaultCommand.Execute(null);
                    return true;
                }

                return false;

            default:
                return false;
        }
    }

    private static bool IsTextEditingSource(object? source) => source is TextBox;

}
