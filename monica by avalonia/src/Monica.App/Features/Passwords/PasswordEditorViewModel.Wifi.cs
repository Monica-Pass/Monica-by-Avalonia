using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.App.ViewModels;

public sealed record WifiSecurityChoice(string Value, string Label);

public sealed partial class PasswordEditorViewModel
{
    private bool _loadingWifiFields;
    private bool _wifiFieldsChanged;

    [ObservableProperty]
    private string _wifiSsid = "";

    [ObservableProperty]
    private WifiSecurityChoice? _selectedWifiSecurity;

    [ObservableProperty]
    private bool _wifiHiddenNetwork;

    [ObservableProperty]
    private string _wifiQrText = "";

    [ObservableProperty]
    private string _wifiImportStatus = "";

    public bool IsWifi => SelectedLoginType?.Value == PasswordLoginType.Wifi;
    public bool WifiIsEnterprise => IsWifi && SelectedWifiSecurity?.Value is nameof(WifiSecurity.WPA2_ENTERPRISE) or nameof(WifiSecurity.WPA3_ENTERPRISE);
    public bool WifiRequiresPassword => !IsWifi || SelectedWifiSecurity?.Value != nameof(WifiSecurity.NONE);
    public bool HasWifiValidationError => ValidationTarget is PasswordEditorValidationTarget.WifiSsid or PasswordEditorValidationTarget.WifiMetadata;
    public IReadOnlyList<WifiSecurityChoice> WifiSecurityOptions { get; private set; } = [];

    private void InitializeWifiFields()
    {
        WifiSecurityOptions = Enum.GetNames<WifiSecurity>()
            .Select(value => new WifiSecurityChoice(value, L.Get($"WifiSecurity.{value}"))).ToArray();
        RefreshWifiFields();
    }

    private void RefreshWifiFields()
    {
        if (WifiSecurityOptions.Count == 0)
        {
            return;
        }

        _loadingWifiFields = true;
        try
        {
            WifiNetworkData.TryRead(WifiMetadata, Title, out var network);
            WifiSsid = network.Ssid;
            if (!WifiSecurityOptions.Any(choice => choice.Value == network.Security))
            {
                WifiSecurityOptions = [.. WifiSecurityOptions, new(network.Security, network.Security)];
                OnPropertyChanged(nameof(WifiSecurityOptions));
            }

            SelectedWifiSecurity = WifiSecurityOptions.First(choice => choice.Value == network.Security);
            WifiHiddenNetwork = network.HiddenNetwork;
            _wifiFieldsChanged = false;
        }
        finally
        {
            _loadingWifiFields = false;
        }
    }

    partial void OnWifiMetadataChanged(string value) => RefreshWifiFields();
    partial void OnWifiSsidChanged(string value) => WifiFieldsChanged();
    partial void OnWifiHiddenNetworkChanged(bool value) => WifiFieldsChanged();
    partial void OnSelectedWifiSecurityChanged(WifiSecurityChoice? value)
    {
        WifiFieldsChanged();
        OnPropertyChanged(nameof(WifiRequiresPassword));
        OnPropertyChanged(nameof(WifiIsEnterprise));
        ClearCorrectedPasswordValidation();
    }

    private void WifiFieldsChanged()
    {
        if (!_loadingWifiFields)
        {
            _wifiFieldsChanged = true;
            if (HasWifiValidationError)
            {
                ClearValidation();
            }
        }
    }

    private string BuildWifiMetadata() => IsWifi && _wifiFieldsChanged
        ? new WifiNetworkData(WifiSsid, SelectedWifiSecurity?.Value ?? nameof(WifiSecurity.WPA2_WPA3), WifiHiddenNetwork).ApplyTo(WifiMetadata)
        : WifiMetadata;

    public bool ImportWifiQrPayload(string? payload)
    {
        if (IsSensitiveStateCleared)
        {
            return false;
        }

        var imported = WifiQrCodec.Parse(payload);
        if (imported is null || !WifiNetworkData.TryRead(WifiMetadata, Title, out _))
        {
            WifiImportStatus = L.Get("WifiQrInvalid");
            return false;
        }

        WifiMetadata = imported.Network.ApplyTo(WifiMetadata);
        SelectedLoginType = LoginTypeOptions.Single(choice => choice.Value == PasswordLoginType.Wifi);
        if (string.IsNullOrWhiteSpace(Title))
        {
            Title = imported.Network.Ssid;
        }

        PasswordLines = imported.Password;
        WifiQrText = "";
        WifiImportStatus = L.Get("WifiQrImported");
        return true;
    }

    [RelayCommand]
    private void ImportWifiQrText() => ImportWifiQrPayload(WifiQrText);

    private void ClearWifiState()
    {
        _loadingWifiFields = true;
        WifiSsid = "";
        SelectedWifiSecurity = null;
        WifiHiddenNetwork = false;
        WifiQrText = "";
        WifiImportStatus = "";
        WifiSecurityOptions = [];
        _wifiFieldsChanged = false;
        _loadingWifiFields = false;
        OnPropertyChanged(nameof(WifiSecurityOptions));
        ImportWifiQrImageCommand.NotifyCanExecuteChanged();
    }
}
