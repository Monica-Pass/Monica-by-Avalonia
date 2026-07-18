using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace Monica.App.Services;

public static class TotpQrCodeDecoder
{
    private const int MaxImageBytes = 16 * 1024 * 1024;
    private const int MaxImageDimension = 4096;
    private const long MaxPixelCount = 16_000_000;

    public static string? TryDecode(byte[] imageBytes)
    {
        if (imageBytes is null || imageBytes.Length == 0 || imageBytes.Length > MaxImageBytes)
        {
            return null;
        }

        using var bitmap = SKBitmap.Decode(imageBytes);
        if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0 || bitmap.Width > MaxImageDimension || bitmap.Height > MaxImageDimension || (long)bitmap.Width * bitmap.Height > MaxPixelCount)
        {
            return null;
        }

        var pixels = bitmap.Pixels;
        var rgb = new byte[pixels.Length * 3];
        try
        {
            for (var index = 0; index < pixels.Length; index++)
            {
                var offset = index * 3;
                rgb[offset] = pixels[index].Red;
                rgb[offset + 1] = pixels[index].Green;
                rgb[offset + 2] = pixels[index].Blue;
            }

            var reader = new BarcodeReaderGeneric
            {
                Options = new DecodingOptions
                {
                    TryHarder = true,
                    TryInverted = true,
                    PossibleFormats = [BarcodeFormat.QR_CODE]
                }
            };
            return reader.Decode(rgb, bitmap.Width, bitmap.Height, RGBLuminanceSource.BitmapFormat.RGB24)?.Text?.Trim();
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(rgb);
            Array.Clear(pixels, 0, pixels.Length);
        }
    }
}
