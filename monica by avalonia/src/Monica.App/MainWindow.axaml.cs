using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Monica.App.Features.Vault;
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
        PropertyChanged += (_, args) =>
        {
            if (args.Property == WindowStateProperty)
            {
                OnWindowStateChanged(this, EventArgs.Empty);
            }
        };
        DataContextChanged += OnDataContextChanged;
        InitializeSecurityLifecycle();
        InitializeStatusNoticeLifecycle();
        InitializeBackgroundMemoryLifecycle();
        InitializeTrayHintLifecycle();
        Closed += (_, _) => CloseAutoTypePicker("MainWindowClosed");
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

        if (VaultPresets.IsLibrarySection(viewModel.SelectedSection) &&
            VaultWorkspaceView.TryHandleShortcut(viewModel, e))
        {
            return;
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
            }
        }
    }

    // Ctrl+N means "add one of what I am looking at", and after the type pages became presets the
    // library already knows which type that is; only the sections outside it still name a command.
    private static bool TryExecuteCurrentSectionNewCommand(MainWindowViewModel viewModel)
    {
        if (VaultPresets.IsLibrarySection(viewModel.SelectedSection))
        {
            return TryExecuteNewCommand(viewModel.VaultCreateCommand);
        }

        return viewModel.SelectedSection switch
        {
            "Generator" => TryExecuteNewCommand(viewModel.Generator.GeneratePasswordCommand),
            "Mdbx" => TryExecuteNewCommand(viewModel.CreateMdbxVaultCommand),
            _ => false
        };
    }

    private static bool TryExecuteNewCommand(System.Windows.Input.ICommand? command)
    {
        if (command is null || !command.CanExecute(null))
        {
            return false;
        }

        command.Execute(null);
        return true;
    }

    private static bool IsTextEditingSource(object? source) => source is TextBox;

}
