using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Monica.App.ViewModels;

namespace Monica.App.Features.DatabaseManagement;

public partial class DatabaseManagementWorkspaceView : UserControl
{
    private MainWindowViewModel? _viewModel;

    public DatabaseManagementWorkspaceView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        }

        base.OnDataContextChanged(e);
        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
            ResetScrollIfSelected();
        }
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedSection))
        {
            ResetScrollIfSelected();
        }
    }

    private void ResetScrollIfSelected()
    {
        if (!string.Equals(_viewModel?.SelectedSection, "DatabaseManagement", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DatabaseManagementScrollViewer.Offset = new Vector(0, 0);
        Dispatcher.UIThread.Post(() => DatabaseManagementScrollViewer.Offset = new Vector(0, 0));
    }
}
