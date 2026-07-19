using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Monica.App.ViewModels;

namespace Monica.App.Features.SecurityAnalysis;

public partial class SecurityAnalysisWorkspaceView : UserControl
{
    private const double NarrowBreakpoint = 760;
    private const double WideBreakpoint = 1040;
    private const double HeaderBreakpoint = 900;
    private MainWindowViewModel? _viewModel;

    public SecurityAnalysisWorkspaceView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => UpdateResponsiveLayoutForWidth(e.NewSize.Width);
        DataContextChanged += OnDataContextChanged;
        AddHandler(KeyDownEvent, OnWorkspaceKeyDown, RoutingStrategies.Tunnel);
    }

    public bool IsNarrowLayout { get; private set; }
    public bool IsMediumLayout { get; private set; }

    public void UpdateResponsiveLayoutForWidth(double width)
    {
        IsNarrowLayout = width > 0 && width < NarrowBreakpoint;
        IsMediumLayout = width >= NarrowBreakpoint && width < WideBreakpoint;
        ApplyHeaderLayout(width);
        SecurityAnalysisLayoutGrid.ColumnDefinitions.Clear();
        SecurityAnalysisLayoutGrid.RowDefinitions.Clear();
        if (IsNarrowLayout)
        {
            SecurityAnalysisLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            SecurityAnalysisLayoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            Grid.SetColumn(SecurityIssueDetailRegion, 0);
            ApplyPaneVisibility();
            return;
        }

        SecurityAnalysisLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(IsMediumLayout ? 300 : 360)));
        SecurityAnalysisLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        SecurityAnalysisLayoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        Grid.SetColumn(SecurityIssueDetailRegion, 1);
        ApplyPaneVisibility();
    }

    private void ApplyHeaderLayout(double width)
    {
        var compact = width > 0 && width < HeaderBreakpoint;
        Grid.SetRow(SecurityAnalysisCommandBar, compact ? 1 : 0);
        Grid.SetColumn(SecurityAnalysisCommandBar, compact ? 0 : 1);
        Grid.SetColumnSpan(SecurityAnalysisCommandBar, compact ? 2 : 1);
        SecurityAnalysisCommandBar.Margin = compact
            ? new Avalonia.Thickness(0, 4, 0, 0)
            : new Avalonia.Thickness(0);
        Grid.SetRow(SecuritySummaryRegion, compact ? 2 : 1);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        }

        ApplyPaneVisibility();
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.SecurityAnalysisNarrowShowsList) or nameof(MainWindowViewModel.SelectedSecurityIssue))
        {
            Dispatcher.UIThread.Post(ApplyPaneVisibility);
        }
    }

    private void ApplyPaneVisibility()
    {
        var showList = !IsNarrowLayout || _viewModel?.SecurityAnalysisNarrowShowsList != false || _viewModel.SelectedSecurityIssue is null;
        SecurityIssueListRegion.IsVisible = showList;
        SecurityIssueDetailRegion.IsVisible = !IsNarrowLayout || !showList;
        SecurityAnalysisInspectorView.BackButton.IsVisible = IsNarrowLayout && !showList;
    }

    private void OnWorkspaceKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        var control = e.Source as Control;
        var isEditingSearch = control is TextBox;
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var searchBox = SecurityAnalysisFilterPane.SearchBox;
            searchBox.Focus();
            searchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.R && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _viewModel.RefreshSecurityAnalysisCommand.Execute(null);
            e.Handled = true;
        }
        else if (IsNarrowLayout && !_viewModel.SecurityAnalysisNarrowShowsList &&
                 (e.Key == Key.Escape || e.Key == Key.Left && e.KeyModifiers.HasFlag(KeyModifiers.Alt)))
        {
            _viewModel.ShowSecurityIssueListCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _viewModel.HasSecurityIssueFilters)
        {
            _viewModel.ClearSecurityIssueFiltersCommand.Execute(null);
            e.Handled = true;
        }
        else if (!isEditingSearch && e.Key is Key.Up or Key.Down)
        {
            SelectAdjacentIssue(e.Key == Key.Down ? 1 : -1);
            e.Handled = true;
        }
        else if (!isEditingSearch && e.Key == Key.Enter && _viewModel.SelectedSecurityIssue is not null)
        {
            _viewModel.ShowSecurityIssueDetailsCommand.Execute(_viewModel.SelectedSecurityIssue);
            e.Handled = true;
        }
    }

    private void SelectAdjacentIssue(int delta)
    {
        if (_viewModel is null || _viewModel.FilteredSecurityIssueItems.Count == 0)
        {
            return;
        }

        var items = _viewModel.FilteredSecurityIssueItems;
        var index = _viewModel.SelectedSecurityIssue is null ? -1 : items.IndexOf(_viewModel.SelectedSecurityIssue);
        var next = index < 0 ? (delta > 0 ? 0 : items.Count - 1) : Math.Clamp(index + delta, 0, items.Count - 1);
        _viewModel.SelectedSecurityIssue = items[next];
        SecurityAnalysisIssueListView.IssueList.ScrollIntoView(items[next]);
    }
}
