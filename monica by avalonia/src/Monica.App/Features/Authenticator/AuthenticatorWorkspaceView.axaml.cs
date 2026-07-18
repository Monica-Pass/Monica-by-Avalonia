using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Monica.App.Features;
using Monica.App.ViewModels;

namespace Monica.App.Features.Authenticator;

public partial class AuthenticatorWorkspaceView : UserControl
{
    public const double NarrowLayoutBreakpoint = 760;
    public const double WideLayoutBreakpoint = 1100;
    public const double WideHeaderBreakpoint = 980;

    private MainWindowViewModel? _observedViewModel;
    private double _layoutWidth;

    public AuthenticatorWorkspaceView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => UpdateResponsiveLayoutForWidth(e.NewSize.Width);
        DataContextChanged += OnDataContextChanged;
        Dispatcher.UIThread.Post(VaultEditorDialogWarmup.EnsureTotpWarmed, DispatcherPriority.Background);
    }

    public bool IsNarrowLayout { get; private set; }

    public bool IsMediumLayout { get; private set; }

    public bool IsSearchBox(object? source) => ReferenceEquals(source, AuthenticatorSearchBox);

    public bool IsNonSearchTextEditingSource(object? source) => source is TextBox textBox && !IsSearchBox(textBox);

    public void FocusSearch()
    {
        AuthenticatorSearchBox.Focus();
        AuthenticatorSearchBox.SelectAll();
    }

    public bool IsSearchFocused => AuthenticatorSearchBox.IsFocused;

    public void FocusAccountList() => Dispatcher.UIThread.Post(() => AuthenticatorAccountListView.AccountList.Focus());

    public void FocusCodeRegion() => AuthenticatorCodeRegion.Focus();

    public void SelectAdjacentAuthenticator(MainWindowViewModel viewModel, int delta)
    {
        var visibleItems = viewModel.FilteredTotpItems;
        if (visibleItems.Count == 0)
        {
            return;
        }

        var currentIndex = viewModel.SelectedTotpItem is null
            ? -1
            : visibleItems.ToList().FindIndex(item => item.Id == viewModel.SelectedTotpItem.Id);
        var nextIndex = currentIndex < 0
            ? (delta > 0 ? 0 : visibleItems.Count - 1)
            : Math.Clamp(currentIndex + delta, 0, visibleItems.Count - 1);
        viewModel.SelectedTotpItem = visibleItems[nextIndex];
        AuthenticatorAccountListView.AccountList.ScrollIntoView(visibleItems[nextIndex]);
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
        if (e.PropertyName is not (nameof(MainWindowViewModel.TotpNarrowShowsList) or nameof(MainWindowViewModel.SelectedTotpItem)))
        {
            return;
        }

        Dispatcher.UIThread.Post(ApplyResponsiveLayout);
    }

    private void ApplyResponsiveLayout()
    {
        ApplyContentLayout();
        ApplyHeaderLayout();
    }

    private void ApplyContentLayout()
    {
        var showNarrowDetails = IsNarrowLayout &&
            _observedViewModel?.TotpNarrowShowsList == false &&
            _observedViewModel.SelectedTotpItem is not null;

        SetContentColumns(showNarrowDetails);
        AuthenticatorListRegion.IsVisible = !IsNarrowLayout || !showNarrowDetails;
        AuthenticatorCodeRegion.IsVisible = !IsNarrowLayout || showNarrowDetails;
        AuthenticatorInspectorRegion.IsVisible = !IsNarrowLayout && !IsMediumLayout;
        AuthenticatorCodeConsole.SetBackButtonVisible(showNarrowDetails);
    }

    private void SetContentColumns(bool showNarrowDetails)
    {
        var columns = AuthenticatorMasterDetailGrid.ColumnDefinitions;
        columns[0].Width = IsNarrowLayout
            ? new GridLength(showNarrowDetails ? 0 : 1, showNarrowDetails ? GridUnitType.Pixel : GridUnitType.Star)
            : new GridLength(IsMediumLayout ? 280 : 300);
        columns[1].Width = IsNarrowLayout
            ? new GridLength(showNarrowDetails ? 1 : 0, showNarrowDetails ? GridUnitType.Star : GridUnitType.Pixel)
            : new GridLength(1, GridUnitType.Star);
        columns[2].Width = IsNarrowLayout || IsMediumLayout ? new GridLength(0) : new GridLength(300);
    }

    private void ApplyHeaderLayout()
    {
        var useCompactHeader = _layoutWidth > 0 && _layoutWidth < WideHeaderBreakpoint;
        var columns = AuthenticatorHeaderGrid.ColumnDefinitions;
        columns[0].Width = new GridLength(1, GridUnitType.Star);
        columns[1].Width = useCompactHeader ? GridLength.Auto : new GridLength(320);
        columns[2].Width = useCompactHeader ? new GridLength(0) : GridLength.Auto;

        PositionHeaderControls(useCompactHeader);
        ScanAuthenticatorButton.IsVisible = !useCompactHeader;
        RefreshAuthenticatorButton.IsVisible = !useCompactHeader;
    }

    private void PositionHeaderControls(bool compact)
    {
        Grid.SetRow(AuthenticatorSearchRegion, compact ? 1 : 0);
        Grid.SetColumn(AuthenticatorSearchRegion, compact ? 0 : 1);
        Grid.SetColumnSpan(AuthenticatorSearchRegion, compact ? 2 : 1);
        AuthenticatorSearchRegion.Margin = compact ? new Thickness(0, 8, 0, 0) : new Thickness(0);
        Grid.SetRow(AuthenticatorHeaderCommands, 0);
        Grid.SetColumn(AuthenticatorHeaderCommands, compact ? 1 : 2);
    }
}
