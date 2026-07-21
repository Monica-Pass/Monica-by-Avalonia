using Avalonia.Controls;

namespace Monica.App.Features.Sync;

public partial class SyncImportView : UserControl
{
    private const double NarrowBreakpoint = 720;

    public SyncImportView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => UpdateResponsiveLayoutForWidth(e.NewSize.Width);
    }

    public bool IsNarrowLayout { get; private set; }

    public void UpdateResponsiveLayoutForWidth(double width)
    {
        IsNarrowLayout = width > 0 && width < NarrowBreakpoint;
        ImportSourceTabs.TabStripPlacement = IsNarrowLayout ? Dock.Top : Dock.Left;
        foreach (var tab in ImportSourceTabs.Items.OfType<TabItem>())
        {
            tab.MinWidth = IsNarrowLayout ? 0 : 190;
        }
    }
}
