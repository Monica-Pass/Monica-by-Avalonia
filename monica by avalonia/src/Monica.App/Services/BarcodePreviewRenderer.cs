using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ZXing;
using ZXing.Common;
using ZXing.Rendering;

namespace Monica.App.Services;

public enum BarcodePreviewMode
{
    QrCode,
    Code128
}

public static class BarcodePreviewRenderer
{
    public const int MaximumPayloadCharacters = 4096;

    public static PixelData? Encode(string payload, BarcodePreviewMode mode)
    {
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > MaximumPayloadCharacters)
        {
            return null;
        }

        try
        {
            var (format, width, height) = mode switch
            {
                BarcodePreviewMode.Code128 => (BarcodeFormat.CODE_128, 1100, 360),
                _ => (BarcodeFormat.QR_CODE, 900, 900)
            };

            var writer = new BarcodeWriterPixelData
            {
                Format = format,
                Options = new EncodingOptions
                {
                    Width = width,
                    Height = height,
                    Margin = 2,
                    PureBarcode = true
                }
            };

            var pixels = writer.Write(payload);
            return pixels.Width > 0 && pixels.Height > 0 && pixels.Pixels.Length > 0 ? pixels : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static Bitmap? Render(string payload, BarcodePreviewMode mode)
    {
        try
        {
            var pixels = Encode(payload, mode);
            if (pixels is null)
            {
                return null;
            }

            var bitmap = new WriteableBitmap(
                new PixelSize(pixels.Width, pixels.Height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Opaque);
            using var framebuffer = bitmap.Lock();
            var rowBytes = checked(framebuffer.RowBytes * pixels.Height);
            Marshal.Copy(pixels.Pixels, 0, framebuffer.Address, Math.Min(rowBytes, pixels.Pixels.Length));
            return bitmap;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
