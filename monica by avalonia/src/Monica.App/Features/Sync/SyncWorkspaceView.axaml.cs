using Avalonia.Controls;

namespace Monica.App.Features.Sync;

public partial class SyncWorkspaceView : UserControl
{
    private const double NarrowBreakpoint = 760;
    private const double WideBreakpoint = 1040;
    private const double HeaderBreakpoint = 900;

    public SyncWorkspaceView()
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
        ApplyHeaderLayout(width);

        SyncWorkspaceLayoutGrid.ColumnDefinitions.Clear();
        SyncWorkspaceLayoutGrid.RowDefinitions.Clear();
        if (IsNarrowLayout)
        {
            SyncWorkspaceLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            SyncWorkspaceLayoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            SyncWorkspaceLayoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            Grid.SetColumn(SyncContentRegion, 0);
            Grid.SetRow(SyncContentRegion, 1);
            SyncSidebarRegion.MaxHeight = 264;
            SyncSidebarOverview.IsVisible = false;
            return;
        }

        SyncWorkspaceLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(IsMediumLayout ? 280 : 320)));
        SyncWorkspaceLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        SyncWorkspaceLayoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        Grid.SetColumn(SyncContentRegion, 1);
        Grid.SetRow(SyncContentRegion, 0);
        SyncSidebarRegion.MaxHeight = double.PositiveInfinity;
        SyncSidebarOverview.IsVisible = true;
    }

    private void ApplyHeaderLayout(double width)
    {
        var compact = width > 0 && width < HeaderBreakpoint;
        Grid.SetRow(SyncCommandBar, compact ? 2 : 0);
        Grid.SetColumn(SyncCommandBar, compact ? 0 : 1);
        Grid.SetColumnSpan(SyncCommandBar, compact ? 2 : 1);
        Grid.SetRowSpan(SyncCommandBar, compact ? 1 : 2);
        SyncCommandBar.Margin = compact
            ? new Avalonia.Thickness(0, 4, 0, 0)
            : new Avalonia.Thickness(0);
        Grid.SetRow(WebDavOperationProgressRegion, compact ? 3 : 2);
    }
}
