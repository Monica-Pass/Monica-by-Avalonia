using Avalonia;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Monica.App.Features.Notes;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class NoteImagePreviewDecoderTests
{
    private const int PreviewCardWidthDips = 172;
    private const int ExtremeDesktopScale = 5;

    public NoteImagePreviewDecoderTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Oversized_landscape_preview_is_decoded_within_memory_bound()
    {
        var sourceBytes = CreatePngBytes(2400, 1600);

        var plan = NoteImagePreviewDecoder.CreatePlan(sourceBytes);

        Assert.Equal(new PixelSize(2400, 1600), plan.SourcePixelSize);
        Assert.Equal(NoteImagePreviewDecodeAxis.Width, plan.Axis);
        Assert.Equal(NoteImagePreviewDecoder.MaximumEdgePixels, plan.TargetPixels);
    }

    [Fact]
    public void Maximum_decode_edge_covers_extreme_desktop_scale_without_excess_density()
    {
        const int expectedMaximumEdgePixels = 1024;

        Assert.Equal(expectedMaximumEdgePixels, NoteImagePreviewDecoder.MaximumEdgePixels);
        Assert.True(
            NoteImagePreviewDecoder.MaximumEdgePixels >= PreviewCardWidthDips * ExtremeDesktopScale);
        Assert.Equal(
            4 * 1024 * 1024,
            NoteImagePreviewDecoder.MaximumEdgePixels * NoteImagePreviewDecoder.MaximumEdgePixels * 4);
    }

    [Fact]
    public void Oversized_portrait_preview_is_decoded_within_memory_bound()
    {
        var sourceBytes = CreatePngBytes(1600, 2400);

        var plan = NoteImagePreviewDecoder.CreatePlan(sourceBytes);

        Assert.Equal(new PixelSize(1600, 2400), plan.SourcePixelSize);
        Assert.Equal(NoteImagePreviewDecodeAxis.Height, plan.Axis);
        Assert.Equal(NoteImagePreviewDecoder.MaximumEdgePixels, plan.TargetPixels);
    }

    [Fact]
    public void Small_preview_is_not_upscaled_and_source_bytes_are_unchanged()
    {
        var sourceBytes = CreatePngBytes(640, 480);
        var originalBytes = sourceBytes.ToArray();

        var plan = NoteImagePreviewDecoder.CreatePlan(sourceBytes);
        using var preview = NoteImagePreviewDecoder.Decode(sourceBytes);

        Assert.Equal(new PixelSize(640, 480), plan.SourcePixelSize);
        Assert.Equal(NoteImagePreviewDecodeAxis.Original, plan.Axis);
        Assert.Equal(0, plan.TargetPixels);
        Assert.Equal(originalBytes, sourceBytes);
    }

    [Fact]
    public void Empty_image_is_rejected_before_decode()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            NoteImagePreviewDecoder.CreatePlan([]));

        Assert.Equal("encodedImage", exception.ParamName);
    }

    [Fact]
    public void Unsupported_image_is_rejected_before_decode()
    {
        Assert.Throws<InvalidDataException>(() =>
            NoteImagePreviewDecoder.CreatePlan([1, 2, 3, 4]));
    }

    private static byte[] CreatePngBytes(int width, int height)
    {
        using var imageData = new MemoryStream();
        using (var compressor = new ZLibStream(imageData, CompressionLevel.Fastest, leaveOpen: true))
        {
            var transparentScanline = new byte[checked((width * 4) + 1)];
            for (var row = 0; row < height; row++)
            {
                compressor.Write(transparentScanline);
            }
        }

        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;
        header[9] = 6;
        WritePngChunk(png, "IHDR", header);
        WritePngChunk(png, "IDAT", imageData.ToArray());
        WritePngChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WritePngChunk(Stream target, string chunkType, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        target.Write(length);

        var typeBytes = Encoding.ASCII.GetBytes(chunkType);
        target.Write(typeBytes);
        target.Write(data);

        var crc = 0xffffffffu;
        UpdateCrc(typeBytes, ref crc);
        UpdateCrc(data, ref crc);
        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, ~crc);
        target.Write(checksum);
    }

    private static void UpdateCrc(ReadOnlySpan<byte> bytes, ref uint crc)
    {
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1));
            }
        }
    }
}
