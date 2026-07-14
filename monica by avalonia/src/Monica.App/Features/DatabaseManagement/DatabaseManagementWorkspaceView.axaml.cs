using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Monica.App.ViewModels;

namespace Monica.App.Features.DatabaseManagement;

public partial class DatabaseManagementWorkspaceView : UserControl
{
    private const double NarrowBreakpoint = 760;
    private const double WideBreakpoint = 1040;
    private MainWindowViewModel? _viewModel;

    public DatabaseManagementWorkspaceView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => UpdateResponsiveLayoutForWidth(e.NewSize.Width);
    }

    public bool IsNarrowLayout { get; private set; }
    public bool IsMediumLayout { get; private set; }

    public void UpdateResponsiveLayoutForWidth(double width)
    {
        IsNarrowLayout = width > 0 && width < NarrowBreakpoint;
        IsMediumLayout = width >= NarrowBreakpoint && width < WideBreakpoint;
        DatabaseWorkspaceLayoutGrid.ColumnDefinitions.Clear();
        DatabaseWorkspaceLayoutGrid.RowDefinitions.Clear();
        if (IsNarrowLayout)
        {
            DatabaseWorkspaceLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            DatabaseWorkspaceLayoutGrid.RowDefinitions.Add(new RowDefinition(new GridLength(280)));
            DatabaseWorkspaceLayoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            Grid.SetColumn(DatabaseContentRegion, 0);
            Grid.SetRow(DatabaseContentRegion, 1);
            return;
        }

        DatabaseWorkspaceLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(IsMediumLayout ? 280 : 360)));
        DatabaseWorkspaceLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        DatabaseWorkspaceLayoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        Grid.SetColumn(DatabaseContentRegion, 1);
        Grid.SetRow(DatabaseContentRegion, 0);
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

        DatabaseContentRegion.Offset = new Vector(0, 0);
        Dispatcher.UIThread.Post(() => DatabaseContentRegion.Offset = new Vector(0, 0));
    }
}
