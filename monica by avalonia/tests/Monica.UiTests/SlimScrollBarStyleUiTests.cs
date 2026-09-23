using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Monica.UiTests;

/// Pins what the shell's scrollbar actually renders. FluentAvalonia sizes its bars from the Windows
/// UISettings COM interop rather than from application resources, so a resource with a matching name
/// changes nothing measurable - these read geometry and painted fills off the visual tree, which is
/// the only place the claim can be checked.
[Collection(AvaloniaUiTestCollection.Name)]
public sealed class SlimScrollBarStyleUiTests
{
    public SlimScrollBarStyleUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Vertical_bar_is_a_slim_thumb_floating_in_a_wider_hit_strip()
    {
        var window = OpenScrollableList(out var bar);

        try
        {
            Assert.Equal(12, bar.Bounds.Width);

            var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
            Assert.Equal(4, thumb.Bounds.Width);
            Assert.Equal(4, thumb.Bounds.X);
            Assert.True(thumb.Background is ISolidColorBrush { Color.A: > 0 });
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Bar_has_no_arrow_buttons_and_paints_no_track()
    {
        var window = OpenScrollableList(out var bar);

        try
        {
            Assert.DoesNotContain(bar.GetVisualDescendants().OfType<RepeatButton>(), button =>
                button.Name is "PART_LineUpButton" or "PART_LineDownButton");

            // The two page buttons stay live so clicking the channel still pages; they just draw
            // nothing, which is what removes the grey trough that filled the whole 14px before.
            var pageButtons = bar.GetVisualDescendants().OfType<RepeatButton>().ToArray();
            Assert.Equal(2, pageButtons.Length);
            foreach (var pageButton in pageButtons)
            {
                var painted = pageButton.GetVisualDescendants().OfType<Border>().Single();
                Assert.True(painted.Background is null || painted.Background is ISolidColorBrush { Color.A: 0 });
            }
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Moving_the_slim_thumb_still_scrolls()
    {
        var window = OpenScrollableList(out var bar, out var scrollViewer);

        try
        {
            Assert.True(bar.Maximum > 120);

            bar.Value = 120;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(120, scrollViewer.Offset.Y);
        }
        finally
        {
            window.Close();
        }
    }

    private static Window OpenScrollableList(out ScrollBar bar) =>
        OpenScrollableList(out bar, out _);

    private static Window OpenScrollableList(out ScrollBar bar, out ScrollViewer scrollViewer)
    {
        var list = new ListBox
        {
            ItemsSource = Enumerable.Range(0, 200).Select(index => $"row {index}").ToArray(),
            Height = 200,
        };
        var window = new Window { Width = 400, Height = 300, Content = list };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        bar = window.GetVisualDescendants().OfType<ScrollBar>().Single(candidate =>
            candidate.Orientation == Orientation.Vertical);
        scrollViewer = list.GetVisualDescendants().OfType<ScrollViewer>().First();
        return window;
    }
}
