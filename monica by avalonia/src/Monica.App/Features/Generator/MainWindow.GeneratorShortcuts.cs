using Avalonia.Input;
using Monica.App.ViewModels;

namespace Monica.App;

public partial class MainWindow
{
    private void HandleGeneratorWorkspaceShortcut(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
        {
            if (viewModel.GeneratePasswordCommand.CanExecute(null))
            {
                viewModel.GeneratePasswordCommand.Execute(null);
                GeneratorWorkspaceView.FocusGeneratedPassword();
                e.Handled = true;
            }

            return;
        }

        if (e.Key != Key.C || e.KeyModifiers != KeyModifiers.Control)
        {
            return;
        }

        if (IsTextEditingSource(e.Source) && !GeneratorWorkspaceView.IsGeneratedPasswordSource(e.Source))
        {
            return;
        }

        if (viewModel.CopyGeneratedPasswordCommand.CanExecute(null))
        {
            viewModel.CopyGeneratedPasswordCommand.Execute(null);
            e.Handled = true;
        }
    }
}
