using Avalonia;
using Avalonia.Controls;

namespace Monica.App.Controls;

internal static class LifecycleWorkspaceLayout
{
    public const double NarrowBreakpoint = 760;
    public const double WideBreakpoint = 1100;
    public const double WideHeaderBreakpoint = 980;

    public static void ApplyContent(
        Grid grid,
        Border listRegion,
        Border detailRegion,
        Button backButton,
        bool narrow,
        bool showNarrowDetails,
        double listWidth)
    {
        grid.ColumnDefinitions[0].Width = narrow
            ? new GridLength(showNarrowDetails ? 0 : 1, showNarrowDetails ? GridUnitType.Pixel : GridUnitType.Star)
            : new GridLength(listWidth);
        grid.ColumnDefinitions[1].Width = narrow
            ? new GridLength(showNarrowDetails ? 1 : 0, showNarrowDetails ? GridUnitType.Star : GridUnitType.Pixel)
            : GridLength.Star;
        listRegion.IsVisible = !narrow || !showNarrowDetails;
        detailRegion.IsVisible = !narrow || showNarrowDetails;
        backButton.IsVisible = showNarrowDetails;
    }

    public static void ApplyHeader(Control searchRegion, Control commands, bool compact)
    {
        Grid.SetRow(searchRegion, compact ? 1 : 0);
        Grid.SetColumn(searchRegion, compact ? 0 : 1);
        Grid.SetColumnSpan(searchRegion, compact ? 3 : 1);
        searchRegion.Margin = compact ? new Thickness(0, 8, 0, 0) : new Thickness(0);
        Grid.SetRow(commands, 0);
        Grid.SetColumn(commands, 2);
    }
}
