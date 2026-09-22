using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Monica.App;

// Why this is a window at all: Avalonia 12.0.4 gives a tray icon nothing to notify with (measured on
// the shipped binary - TrayIcon and NotifyIcon are there, ShowBalloonTip is not), and the icon can be
// parked in the notification-area overflow, where a hover tooltip never reaches someone who is not
// already looking for it. So the once-per-install "your window is still running, here is where it
// went" explanation is drawn by Monica itself, in the corner the tray icon lives.
internal sealed partial class TrayHintWindow : Window
{
    private const double EdgeMarginDip = 16;

    internal TrayHintWindow(string title, string body, string showAction)
    {
        InitializeComponent();
        HintTitle.Text = title;
        HintBody.Text = body;
        HintShowButton.Content = showAction;
        Opened += (_, _) => AnchorBesideTray();
        Resized += (_, _) => AnchorBesideTray();
    }

    internal event Action? SurfaceRequested;

    internal Button ShowActionControl => HintShowButton;

    private void OnShowButtonClicked(object? sender, RoutedEventArgs e) => SurfaceRequested?.Invoke();

    // Screen areas and Position are device pixels while every size on the window is device-independent,
    // so the sizes have to be converted before they are subtracted from the corner: at 150 % scaling an
    // unconverted 340 dip width would leave the bubble 170 px past the right edge of the screen.
    private void AnchorBesideTray()
    {
        var screen = Screens?.Primary ?? Screens?.ScreenFromVisual(this);
        if (screen is null)
        {
            return;
        }

        var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
        // ClientSize is the size SizeToContent settled on; Bounds keeps reporting the pre-content height
        // for this window's whole life (measured: bounds 340x707.33 while the client was 340x121.33), so
        // anchoring off Bounds would leave the bubble most of a screen above the tray.
        var widthDip = ClientSize.Width > 0 ? ClientSize.Width : Width;
        var heightDip = ClientSize.Height > 0 ? ClientSize.Height : DesiredSize.Height;
        var area = screen.WorkingArea;
        var margin = EdgeMarginDip * scaling;
        var position = new PixelPoint(
            (int)Math.Round(area.Right - widthDip * scaling - margin),
            (int)Math.Round(area.Bottom - heightDip * scaling - margin));
        Position = position;
    }
}
