using Avalonia.Controls;

namespace Monica.App.Features.Settings;

public partial class SettingsWorkspaceView : UserControl
{
    private const double NarrowBreakpoint = 760;
    private const double WideBreakpoint = 1040;

    public SettingsWorkspaceView()
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
        SettingsWorkspaceLayoutGrid.ColumnDefinitions.Clear();
        SettingsWorkspaceLayoutGrid.RowDefinitions.Clear();
        if (IsNarrowLayout)
        {
            SettingsWorkspaceLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            SettingsWorkspaceLayoutGrid.RowDefinitions.Add(new RowDefinition(new GridLength(292)));
            SettingsWorkspaceLayoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            Grid.SetColumn(SettingsContentRegion, 0);
            Grid.SetRow(SettingsContentRegion, 1);
            return;
        }

        SettingsWorkspaceLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(IsMediumLayout ? 216 : 248)));
        SettingsWorkspaceLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        SettingsWorkspaceLayoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        Grid.SetColumn(SettingsContentRegion, 1);
        Grid.SetRow(SettingsContentRegion, 0);
    }
}
