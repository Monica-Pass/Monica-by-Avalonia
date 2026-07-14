using Avalonia.Controls;

namespace Monica.App.Features.Mdbx;

public partial class MdbxWorkspaceView : UserControl
{
    private const double NarrowBreakpoint = 760;
    private const double WideBreakpoint = 1040;

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
        MdbxWorkspaceLayoutGrid.ColumnDefinitions.Clear();
        MdbxWorkspaceLayoutGrid.RowDefinitions.Clear();
        if (IsNarrowLayout)
        {
            MdbxWorkspaceLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            MdbxWorkspaceLayoutGrid.RowDefinitions.Add(new RowDefinition(new GridLength(280)));
            MdbxWorkspaceLayoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            Grid.SetColumn(MdbxContentRegion, 0);
            Grid.SetRow(MdbxContentRegion, 1);
            return;
        }

        MdbxWorkspaceLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(IsMediumLayout ? 280 : 360)));
        MdbxWorkspaceLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        MdbxWorkspaceLayoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        Grid.SetColumn(MdbxContentRegion, 1);
        Grid.SetRow(MdbxContentRegion, 0);
    }
}
