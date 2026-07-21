using Avalonia.Controls;

namespace Monica.App.Features.Sync;

public partial class SyncExportView : UserControl
{
    private const double NarrowBreakpoint = 720;

    public SyncExportView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => UpdateResponsiveLayoutForWidth(e.NewSize.Width);
    }

    public bool IsNarrowLayout { get; private set; }

    public void UpdateResponsiveLayoutForWidth(double width)
    {
        IsNarrowLayout = width > 0 && width < NarrowBreakpoint;
        ExportFormatTabs.TabStripPlacement = IsNarrowLayout ? Dock.Top : Dock.Left;
        foreach (var tab in ExportFormatTabs.Items.OfType<TabItem>())
        {
            tab.MinWidth = IsNarrowLayout ? 0 : 190;
        }
    }
}
