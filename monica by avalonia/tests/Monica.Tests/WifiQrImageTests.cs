using System.Runtime.InteropServices;
using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Platform.Services;
using SkiaSharp;

namespace Monica.Tests;

public sealed class WifiQrImageTests
{
    [Fact]
    public async Task Image_import_decodes_a_real_qr_then_clears_the_file_buffer()
    {
        var pixels = BarcodePreviewRenderer.Encode("WIFI:T:WPA;S:Home;P: exact-secret ;H:true;;", BarcodePreviewMode.QrCode)!;
        using var bitmap = new SKBitmap(pixels.Width, pixels.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        Marshal.Copy(pixels.Pixels, 0, bitmap.GetPixels(), pixels.Pixels.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = encoded.ToArray();
        var picker = new Picker(Task.FromResult<PickedBinaryFile?>(new("network.png", bytes)));
        using var editor = Editor(picker);

        await editor.ImportWifiQrImageCommand.ExecuteAsync(null);

        Assert.Equal("Home", editor.WifiSsid);
        Assert.Equal(" exact-secret ", editor.PasswordLines);
        Assert.True(editor.WifiHiddenNetwork);
        Assert.True(editor.Validate());
        Assert.All(bytes, value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public async Task Image_result_after_editor_close_is_discarded_and_its_buffer_is_cleared()
    {
        var source = new TaskCompletionSource<PickedBinaryFile?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var editor = Editor(new Picker(source.Task));
        var pending = editor.ImportWifiQrImageCommand.ExecuteAsync(null);
        editor.ClearSensitiveState();
        var bytes = new byte[] { 1, 2, 3, 4 };
        source.SetResult(new("late.png", bytes));
        await pending;

        Assert.All(bytes, value => Assert.Equal((byte)0, value));
        Assert.Empty(editor.WifiSsid);
        Assert.Empty(editor.WifiMetadata);
        Assert.Empty(editor.PasswordLines);
        Assert.Empty(editor.WifiImportStatus);
        Assert.False(editor.ImportWifiQrImageCommand.CanExecute(null));
    }

    [Fact]
    public async Task Image_picker_failure_does_not_echo_private_paths_or_credentials()
    {
        using var editor = Editor(new Picker(Task.FromException<PickedBinaryFile?>(
            new IOException("private-path private-network-secret"))));
        await editor.ImportWifiQrImageCommand.ExecuteAsync(null);
        Assert.Equal(editor.L.Get("WifiQrImageFailed"), editor.WifiImportStatus);
        Assert.DoesNotContain("private-", editor.WifiImportStatus, StringComparison.Ordinal);
    }

    private static PasswordEditorViewModel Editor(IFileSystemPickerService picker) =>
        new(new LocalizationService(), new PasswordGeneratorService(), null, [], "", fileSystemPickerService: picker);

    private sealed class Picker(Task<PickedBinaryFile?> result) : IFileSystemPickerService
    {
        public PlatformIntegrationCapability Capability => new("file-picker", PlatformFeatureStatus.Available, "Test picker");
        public Task<PickedBinaryFile?> OpenBinaryFileAsync(string title, IReadOnlyList<PlatformFilePickerFileType> fileTypes,
            CancellationToken cancellationToken = default) => result;
        public Task<PickedTextFile?> OpenTextFileAsync(string title, IReadOnlyList<PlatformFilePickerFileType> fileTypes,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> SaveTextFileAsync(string title, string suggestedFileName, string content,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> SaveBinaryFileAsync(string title, string suggestedFileName, ReadOnlyMemory<byte> content,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PickedSaveTarget?> PickSaveFileTargetAsync(string title, string suggestedFileName,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
