using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Monica.App.Controls;
using Monica.App.Services;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class WebsiteIconImageUiTests
{
    public WebsiteIconImageUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    // The suite keeps this process offline, so a picture that paints here can only have come off disk -
    // which is the case the settings switch used to miss. Turning it off stopped the next round trip and
    // left every already-cached icon painted, so the switch read as broken to the person using it.
    [Fact]
    public void Turning_the_switch_off_hides_a_picture_that_is_already_on_disk()
    {
        const string host = "toggle-fixture.invalid";
        var path = WebsiteIconCache.DiskPath(host);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WritePicture(path);
        WebsiteIconCache.SetUserWantsIcons(true);
        var image = new WebsiteIconImage { Host = host };
        var window = new Window { Content = image };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(image.Source);
            Assert.True(image.IsVisible);

            // The flip is what repaints the rows already on screen; nothing rebuilds the list.
            WebsiteIconCache.SetUserWantsIcons(false);
            Dispatcher.UIThread.RunJobs();

            Assert.Null(image.Source);
            Assert.False(image.IsVisible);
        }
        finally
        {
            window.Close();
            WebsiteIconCache.SetUserWantsIcons(true);
            File.Delete(path);
        }
    }

    private static void WritePicture(string path)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(16, 16),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);
        using var stream = File.Create(path);
        bitmap.Save(stream, new PngBitmapEncoderOptions());
    }
}
