using Avalonia;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Monica.App.Features.Notes;

internal enum NoteImagePreviewDecodeAxis
{
    Original,
    Width,
    Height
}

internal readonly record struct NoteImagePreviewDecodePlan(
    PixelSize SourcePixelSize,
    NoteImagePreviewDecodeAxis Axis,
    int TargetPixels);

internal static class NoteImagePreviewDecoder
{
    internal const int MaximumEdgePixels = 1600;

    internal static Bitmap Decode(byte[] encodedImage)
    {
        var plan = CreatePlan(encodedImage);
        var stream = new MemoryStream(encodedImage, writable: false);
        try
        {
            return plan.Axis switch
            {
                NoteImagePreviewDecodeAxis.Width => Bitmap.DecodeToWidth(
                    stream,
                    plan.TargetPixels,
                    BitmapInterpolationMode.MediumQuality),
                NoteImagePreviewDecodeAxis.Height => Bitmap.DecodeToHeight(
                    stream,
                    plan.TargetPixels,
                    BitmapInterpolationMode.MediumQuality),
                _ => new Bitmap(stream)
            };
        }
        finally
        {
            stream.Dispose();
        }
    }

    internal static NoteImagePreviewDecodePlan CreatePlan(byte[] encodedImage)
    {
        ArgumentNullException.ThrowIfNull(encodedImage);
        if (encodedImage.Length == 0)
        {
            throw new ArgumentException("Encoded image must not be empty.", nameof(encodedImage));
        }

        using var stream = new MemoryStream(encodedImage, writable: false);
        using var codec = SKCodec.Create(stream)
            ?? throw new InvalidDataException("The attachment is not a supported image.");
        var sourcePixelSize = new PixelSize(codec.Info.Width, codec.Info.Height);
        if (sourcePixelSize.Width <= 0 || sourcePixelSize.Height <= 0)
        {
            throw new InvalidDataException("The attachment has invalid image dimensions.");
        }

        if (sourcePixelSize.Width <= MaximumEdgePixels &&
            sourcePixelSize.Height <= MaximumEdgePixels)
        {
            return new NoteImagePreviewDecodePlan(
                sourcePixelSize,
                NoteImagePreviewDecodeAxis.Original,
                0);
        }

        return new NoteImagePreviewDecodePlan(
            sourcePixelSize,
            sourcePixelSize.Width >= sourcePixelSize.Height
                ? NoteImagePreviewDecodeAxis.Width
                : NoteImagePreviewDecodeAxis.Height,
            MaximumEdgePixels);
    }
}
