using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Monica.App.Features;
using Monica.App.ViewModels;

namespace Monica.App.Features.Wallet;

public partial class WalletWorkspaceView : UserControl
{
    public const double NarrowLayoutBreakpoint = 760;
    public const double WideLayoutBreakpoint = 1100;
    public const double WideHeaderBreakpoint = 980;

    private MainWindowViewModel? _observedViewModel;
    private double _layoutWidth;

    public WalletWorkspaceView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => UpdateResponsiveLayoutForWidth(e.NewSize.Width);
        DataContextChanged += OnDataContextChanged;
        Dispatcher.UIThread.Post(VaultEditorDialogWarmup.EnsureWalletWarmed, DispatcherPriority.Background);
    }

    public bool IsNarrowLayout { get; private set; }

    public bool IsMediumLayout { get; private set; }

    public bool IsSearchBox(object? source) => ReferenceEquals(source, WalletSearchBox);

    public bool IsNonSearchTextEditingSource(object? source) => source is TextBox textBox && !IsSearchBox(textBox);

    public void FocusSearch()
    {
        WalletSearchBox.Focus();
        WalletSearchBox.SelectAll();
    }

    public void FocusItemList() => Dispatcher.UIThread.Post(() => WalletItemList.Focus());

    public void FocusWorkbench() => WalletWorkbenchRegion.Focus();

    public void SelectAdjacentWalletItem(MainWindowViewModel viewModel, int delta)
    {
        var visibleItems = viewModel.FilteredWalletItems;
        if (visibleItems.Count == 0)
        {
            return;
        }

        var currentIndex = viewModel.SelectedWalletItem is null
            ? -1
            : visibleItems.ToList().FindIndex(item => item.Id == viewModel.SelectedWalletItem.Id);
        var nextIndex = currentIndex < 0
            ? (delta > 0 ? 0 : visibleItems.Count - 1)
            : Math.Clamp(currentIndex + delta, 0, visibleItems.Count - 1);
        viewModel.SelectedWalletItem = visibleItems[nextIndex];
        WalletItemList.ScrollIntoView(visibleItems[nextIndex]);
    }

    public void UpdateResponsiveLayoutForWidth(double width)
    {
        _layoutWidth = Math.Max(0, width);
        IsNarrowLayout = width > 0 && width < NarrowLayoutBreakpoint;
        IsMediumLayout = width >= NarrowLayoutBreakpoint && width < WideLayoutBreakpoint;
        ApplyResponsiveLayout();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        }

        _observedViewModel = DataContext as MainWindowViewModel;
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged += ViewModelOnPropertyChanged;
        }

        ApplyResponsiveLayout();
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MainWindowViewModel.WalletNarrowShowsList) or nameof(MainWindowViewModel.SelectedWalletItem)))
        {
            return;
        }

        Dispatcher.UIThread.Post(ApplyResponsiveLayout);
    }

    private void ApplyResponsiveLayout()
    {
        var showNarrowDetails = IsNarrowLayout &&
            _observedViewModel?.WalletNarrowShowsList == false &&
            _observedViewModel.SelectedWalletItem is not null;
        SetContentColumns(showNarrowDetails);
        WalletListRegion.IsVisible = !IsNarrowLayout || !showNarrowDetails;
        WalletWorkbenchRegion.IsVisible = !IsNarrowLayout || showNarrowDetails;
        WalletInspectorRegion.IsVisible = !IsNarrowLayout && !IsMediumLayout;
        WalletWorkbench.SetBackButtonVisible(showNarrowDetails);
        WalletWorkbench.SetCompactSupplementVisible(IsNarrowLayout || IsMediumLayout);
        ApplyHeaderLayout();
    }

    private void SetContentColumns(bool showNarrowDetails)
    {
        var columns = WalletMasterDetailGrid.ColumnDefinitions;
        if (IsNarrowLayout)
        {
            columns[0].Width = showNarrowDetails ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            columns[1].Width = showNarrowDetails ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            columns[2].Width = new GridLength(0);
            return;
        }

        columns[0].Width = new GridLength(IsMediumLayout ? 280 : 300);
        columns[1].Width = new GridLength(1, GridUnitType.Star);
        columns[2].Width = IsMediumLayout ? new GridLength(0) : new GridLength(300);
    }

    private void ApplyHeaderLayout()
    {
        var compact = _layoutWidth > 0 && _layoutWidth < WideHeaderBreakpoint;
        var columns = WalletHeaderGrid.ColumnDefinitions;
        columns[0].Width = new GridLength(1, GridUnitType.Star);
        columns[1].Width = compact ? GridLength.Auto : new GridLength(320);
        columns[2].Width = compact ? new GridLength(0) : GridLength.Auto;
        Grid.SetRow(WalletSearchRegion, compact ? 1 : 0);
        Grid.SetColumn(WalletSearchRegion, compact ? 0 : 1);
        Grid.SetColumnSpan(WalletSearchRegion, compact ? 2 : 1);
        WalletSearchRegion.Margin = compact ? new Thickness(0, 8, 0, 0) : new Thickness(0);
        Grid.SetColumn(WalletHeaderCommands, compact ? 1 : 2);
    }
}
