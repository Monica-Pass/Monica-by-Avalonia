using Avalonia.Controls;

namespace Monica.App.Features.Mdbx;

public partial class MdbxWorkspaceView : UserControl
{
    private const double NarrowBreakpoint = 760;
    private const double WideBreakpoint = 1040;
    private const double HeaderBreakpoint = 900;

    public MdbxWorkspaceView()
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
        MdbxWorkspaceLayoutGrid.ColumnDefinitions.Clear();
        MdbxWorkspaceLayoutGrid.RowDefinitions.Clear();
        if (IsNarrowLayout)
        {
            MdbxWorkspaceLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            MdbxWorkspaceLayoutGrid.RowDefinitions.Add(new RowDefinition(new GridLength(280)));
            MdbxWorkspaceLayoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            Grid.SetColumn(MdbxContentRegion, 0);
            Grid.SetRow(MdbxContentRegion, 1);
            MdbxWorkbenchLayoutGrid.ColumnDefinitions.Clear();
            MdbxWorkbenchLayoutGrid.RowDefinitions.Clear();
            MdbxWorkbenchLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            MdbxWorkbenchLayoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            MdbxWorkbenchLayoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetColumn(MdbxSectionNavigator, 0);
            Grid.SetRow(MdbxSectionNavigator, 0);
            var workbench = MdbxWorkbenchLayoutGrid.Children.OfType<MdbxWorkbenchView>().Single();
            Grid.SetColumn(workbench, 0);
            Grid.SetRow(workbench, 1);
            return;
        }

        MdbxWorkspaceLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(IsMediumLayout ? 280 : 340)));
        MdbxWorkspaceLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        MdbxWorkspaceLayoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        Grid.SetColumn(MdbxContentRegion, 1);
        Grid.SetRow(MdbxContentRegion, 0);
        MdbxWorkbenchLayoutGrid.ColumnDefinitions.Clear();
        MdbxWorkbenchLayoutGrid.RowDefinitions.Clear();
        MdbxWorkbenchLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(176)));
        MdbxWorkbenchLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        MdbxWorkbenchLayoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetColumn(MdbxSectionNavigator, 0);
        Grid.SetRow(MdbxSectionNavigator, 0);
        var desktopWorkbench = MdbxWorkbenchLayoutGrid.Children.OfType<MdbxWorkbenchView>().Single();
        Grid.SetColumn(desktopWorkbench, 1);
        Grid.SetRow(desktopWorkbench, 0);
    }

    private void ApplyHeaderLayout(double width)
    {
        var compact = width > 0 && width < HeaderBreakpoint;
        Grid.SetRow(MdbxCommandBar, compact ? 2 : 0);
        Grid.SetColumn(MdbxCommandBar, compact ? 0 : 1);
        Grid.SetColumnSpan(MdbxCommandBar, compact ? 2 : 1);
        Grid.SetRowSpan(MdbxCommandBar, compact ? 1 : 2);
        MdbxCommandBar.Margin = compact
            ? new Avalonia.Thickness(0, 4, 0, 0)
            : new Avalonia.Thickness(0);
    }
}
