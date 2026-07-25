using Avalonia.Controls;
using Monica.App.ViewModels;

namespace Monica.App.Features.Sync.Bitwarden;

public partial class BitwardenSyncSourceView : UserControl
{
    public BitwardenSyncSourceView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            viewModel.LoadBitwardenAccountsCommand.CanExecute(null))
        {
            viewModel.LoadBitwardenAccountsCommand.Execute(null);
        }
    }
}
