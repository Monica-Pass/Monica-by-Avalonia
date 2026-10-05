using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.App.ViewModels;

public sealed partial class PasswordDetailViewModel
{
    private string? _wifiQrPayload;
    public bool IsWifi => Entry.LoginType == PasswordLoginType.Wifi;
    public string WifiSsid { get; private set; } = "";
    public string WifiSecurityLabel { get; private set; } = "";
    public bool WifiHiddenNetwork { get; private set; }
    public bool CanShowWifiQr => !IsSensitiveStateCleared && _wifiQrPayload is not null;
    public bool IsWifiQrVisible { get; private set; }
    public Bitmap? WifiQrImage { get; private set; }
    public string WifiQrActionLabel => L.Get(IsWifiQrVisible ? "WifiHideQr" : "WifiShowQr");
    public string WifiQrUnavailableReason { get; private set; } = "";

    private void InitializeWifiDetails(ICryptoService cryptoService, bool assumePlaintext)
    {
        if (!IsWifi)
        {
            return;
        }

        var readable = WifiNetworkData.TryRead(Entry.WifiMetadata, Entry.Title, out var network);
        WifiSsid = network.Ssid;
        WifiHiddenNetwork = network.HiddenNetwork;
        WifiSecurityLabel = readable ? L.Get($"WifiSecurity.{network.Security}") : L.Get("WifiMetadataInvalid");
        if (readable && !Enum.GetNames<WifiSecurity>().Contains(network.Security, StringComparer.Ordinal))
        {
            WifiSecurityLabel = network.Security;
        }

        var secret = PasswordSecretResolver.Read(Entry.Password, cryptoService, assumePlaintext);
        if (readable && (network.IsOpen || secret.IsReadable))
        {
            _wifiQrPayload = WifiQrCodec.Build(network, secret.Value);
        }

        WifiQrUnavailableReason = _wifiQrPayload is null ? L.Get("WifiQrUnavailable") : "";
    }

    [RelayCommand(CanExecute = nameof(CanShowWifiQr))]
    private void ToggleWifiQr()
    {
        if (!CanShowWifiQr)
        {
            return;
        }

        WifiQrImage?.Dispose();
        WifiQrImage = null;
        IsWifiQrVisible = !IsWifiQrVisible;
        if (IsWifiQrVisible)
        {
            WifiQrImage = BarcodePreviewRenderer.Render(_wifiQrPayload!, BarcodePreviewMode.QrCode);
            if (WifiQrImage is null)
            {
                IsWifiQrVisible = false;
                StatusText = L.Get("BarcodeRenderFailed");
            }
        }

        RaiseWifiDetailsState();
    }

    [RelayCommand(CanExecute = nameof(CanShowWifiQr))]
    private async Task CopyWifiQrAsync()
    {
        if (!CanShowWifiQr)
        {
            return;
        }

        await _clipboardService.SetSensitiveTextAsync(_wifiQrPayload!);
        if (!IsSensitiveStateCleared)
        {
            StatusText = L.Get("WifiQrCopied");
        }
    }

    private void ClearWifiDetails()
    {
        _wifiQrPayload = null;
        WifiQrImage?.Dispose();
        WifiQrImage = null;
        IsWifiQrVisible = false;
        WifiSsid = "";
        WifiSecurityLabel = "";
        WifiQrUnavailableReason = "";
        WifiHiddenNetwork = false;
        RaiseWifiDetailsState();
        ToggleWifiQrCommand.NotifyCanExecuteChanged();
        CopyWifiQrCommand.NotifyCanExecuteChanged();
    }

    private void RaiseWifiDetailsState()
    {
        OnPropertyChanged(nameof(IsWifi));
        OnPropertyChanged(nameof(WifiSsid));
        OnPropertyChanged(nameof(WifiSecurityLabel));
        OnPropertyChanged(nameof(WifiHiddenNetwork));
        OnPropertyChanged(nameof(WifiQrImage));
        OnPropertyChanged(nameof(IsWifiQrVisible));
        OnPropertyChanged(nameof(WifiQrActionLabel));
        OnPropertyChanged(nameof(CanShowWifiQr));
        OnPropertyChanged(nameof(WifiQrUnavailableReason));
    }
}
