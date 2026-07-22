using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.App.ViewModels;

public sealed partial class PasswordDetailViewModel
{
    private void InitializeBarcodePreview(ICryptoService cryptoService, PasswordEntry entry)
    {
        if (entry.LoginType != PasswordLoginType.Barcode)
        {
            return;
        }

        var secret = PasswordSecretResolver.Read(entry.Password, cryptoService);
        if (!secret.IsReadable || string.IsNullOrWhiteSpace(secret.Value))
        {
            return;
        }

        BarcodePayload = secret.Value;
        RefreshBarcodeImage();
    }

    [RelayCommand]
    private void SelectBarcodeMode(BarcodePreviewMode mode)
    {
        if (IsSensitiveStateCleared || !IsBarcode || BarcodeMode == mode)
        {
            return;
        }

        BarcodeMode = mode;
        RefreshBarcodeImage();
        OnPropertyChanged(nameof(IsQrBarcodeMode));
        OnPropertyChanged(nameof(IsCode128BarcodeMode));
    }

    [RelayCommand]
    private async Task CopyBarcodePayloadAsync()
    {
        if (IsSensitiveStateCleared || !HasBarcodePayload)
        {
            return;
        }

        await _clipboardService.SetSensitiveTextAsync(BarcodePayload);
        StatusText = L.Get("CopiedBarcodePayload");
    }

    private void RefreshBarcodeImage()
    {
        BarcodeImage?.Dispose();
        BarcodeImage = BarcodePreviewRenderer.Render(BarcodePayload, BarcodeMode);
        OnPropertyChanged(nameof(BarcodeImage));
        OnPropertyChanged(nameof(HasBarcodePreview));
    }
}
