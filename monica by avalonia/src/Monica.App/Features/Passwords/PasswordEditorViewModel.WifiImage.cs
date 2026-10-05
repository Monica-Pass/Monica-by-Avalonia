using System.Security.Cryptography;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class PasswordEditorViewModel
{
    private readonly IFileSystemPickerService? _wifiFilePicker;
    public bool CanImportWifiQrImage => !IsSensitiveStateCleared && _wifiFilePicker?.Capability.IsUsable == true;

    [RelayCommand(CanExecute = nameof(CanImportWifiQrImage))]
    private async Task ImportWifiQrImageAsync()
    {
        try
        {
            var file = await _wifiFilePicker!.OpenBinaryFileAsync(L.Get("WifiImportQrImage"),
                [new("QR images", ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"])]);
            if (file is null)
            {
                return;
            }

            string? payload;
            try
            {
                payload = await Task.Run(() => TotpQrCodeDecoder.TryDecode(file.Content));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(file.Content);
            }

            ImportWifiQrPayload(payload);
        }
        catch (Exception)
        {
            if (!IsSensitiveStateCleared)
            {
                WifiImportStatus = L.Get("WifiQrImageFailed");
            }
        }
    }
}
