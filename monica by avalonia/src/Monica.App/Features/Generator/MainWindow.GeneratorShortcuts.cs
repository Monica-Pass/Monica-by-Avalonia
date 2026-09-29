using Avalonia.Input;
using Monica.App.ViewModels;

namespace Monica.App;

public partial class MainWindow
{
    private void HandleGeneratorWorkspaceShortcut(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
        {
            if (viewModel.Generator.GeneratePasswordCommand.CanExecute(null))
            {
                viewModel.Generator.GeneratePasswordCommand.Execute(null);
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

        if (viewModel.Generator.CopyGeneratedPasswordCommand.CanExecute(null))
        {
            viewModel.Generator.CopyGeneratedPasswordCommand.Execute(null);
            e.Handled = true;
        }
    }
}
