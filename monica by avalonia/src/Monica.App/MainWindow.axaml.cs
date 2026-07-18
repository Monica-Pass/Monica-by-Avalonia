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
            viewModel.ApplyWindowCapturePolicy();
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

        TryHandlePasswordWorkspaceShortcut(viewModel, e);
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
