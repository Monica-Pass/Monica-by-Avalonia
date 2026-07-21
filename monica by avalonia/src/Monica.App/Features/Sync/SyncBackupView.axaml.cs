using Avalonia.Controls;

namespace Monica.App.Features.Sync;

public partial class SyncBackupView : UserControl
{
    private const double NarrowBreakpoint = 820;

    public SyncBackupView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => UpdateResponsiveLayoutForWidth(e.NewSize.Width);
    }

    public bool IsNarrowLayout { get; private set; }

    public void UpdateResponsiveLayoutForWidth(double width)
    {
        IsNarrowLayout = width > 0 && width < NarrowBreakpoint;
        BackupWorkspaceLayoutGrid.ColumnDefinitions.Clear();
        BackupWorkspaceLayoutGrid.RowDefinitions.Clear();
        if (IsNarrowLayout)
        {
            BackupWorkspaceLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            BackupWorkspaceLayoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            BackupWorkspaceLayoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetColumn(BackupHistoryRegion, 0);
            Grid.SetRow(BackupHistoryRegion, 1);
            BackupHistoryRegion.Margin = new Avalonia.Thickness(0, 16, 0, 0);
            return;
        }

        BackupWorkspaceLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(0.95, GridUnitType.Star)));
        BackupWorkspaceLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1.05, GridUnitType.Star)));
        BackupWorkspaceLayoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetColumn(BackupHistoryRegion, 1);
        Grid.SetRow(BackupHistoryRegion, 0);
        BackupHistoryRegion.Margin = new Avalonia.Thickness(0);
    }
}
