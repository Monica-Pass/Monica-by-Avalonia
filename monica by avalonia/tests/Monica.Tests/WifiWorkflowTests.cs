using System.Text.Json.Nodes;
using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed class WifiWorkflowTests
{
    [Fact]
    public void Wifi_editor_preserves_a_single_exact_password_and_untouched_metadata()
    {
        const string metadata = " {\"ssid\":\"Office\",\"security\":\"WPA2_ENTERPRISE\",\"eap\":{\"method\":\"TLS\"}} ";
        using var editor = Editor(new() { Title = "Work", LoginType = PasswordLoginType.Wifi, WifiMetadata = metadata }, " p\nword ");
        Assert.Equal("Office", editor.WifiSsid);
        Assert.True(editor.Validate());
        Assert.Equal(" p\nword ", Assert.Single(editor.GetPasswordRows()));
        Assert.Equal(metadata, editor.BuildEntry("encrypted").WifiMetadata);

        editor.WifiSsid = "Office 2";
        var saved = JsonNode.Parse(editor.BuildEntry("encrypted").WifiMetadata)!;
        Assert.Equal("Office 2", saved["ssid"]!.GetValue<string>());
        Assert.Equal("TLS", saved["eap"]!["method"]!.GetValue<string>());
    }

    [Fact]
    public void Open_wifi_saves_without_password_but_personal_network_requires_it()
    {
        using var editor = Editor();
        Assert.True(editor.ImportWifiQrPayload("WIFI:T:nopass;S:Guest;;"));
        Assert.True(editor.Validate());
        Assert.Empty(editor.GetPasswordRows());
        Assert.Equal(PasswordLoginType.Wifi, editor.BuildEntry("").LoginType);
        editor.SelectedWifiSecurity = editor.WifiSecurityOptions.Single(choice => choice.Value == "WPA3");
        Assert.False(editor.Validate());
        Assert.Equal(PasswordEditorValidationTarget.Password, editor.ValidationTarget);
    }

    [Fact]
    public void Import_keeps_title_and_advanced_metadata_but_invalid_input_leaves_fields_unchanged()
    {
        using var editor = Editor(new() { Title = "Router", WifiMetadata = """{"proxy":{"kind":"future","secret":"protected-value"}}""" });
        editor.WifiQrText = "WIFI:T:WPA;S:Home;P: exact password ;H:true;;";
        editor.ImportWifiQrTextCommand.Execute(null);
        Assert.Equal("Router", editor.Title);
        Assert.Equal("Home", editor.WifiSsid);
        Assert.True(editor.WifiHiddenNetwork);
        Assert.Equal(" exact password ", editor.PasswordLines);
        Assert.Empty(editor.WifiQrText);
        var saved = editor.BuildEntry("encrypted");
        Assert.Equal("protected-value", JsonNode.Parse(saved.WifiMetadata)!["proxy"]!["secret"]!.GetValue<string>());
        Assert.False(editor.ImportWifiQrPayload("not a wifi QR"));
        Assert.Equal(saved.WifiMetadata, editor.BuildEntry("encrypted").WifiMetadata);
        Assert.Equal(" exact password ", editor.PasswordLines);
    }

    [Fact]
    public void Invalid_metadata_blocks_save_and_unknown_security_is_preserved()
    {
        using var editor = Editor(new() { Title = "Router", LoginType = PasswordLoginType.Wifi, WifiMetadata = "malformed" }, "password");
        Assert.False(editor.Validate());
        Assert.Equal(PasswordEditorValidationTarget.WifiMetadata, editor.ValidationTarget);
        Assert.False(editor.ImportWifiQrPayload("WIFI:T:WPA;S:Home;P:password;;"));
        Assert.Equal("malformed", editor.WifiMetadata);
        editor.WifiMetadata = """{"ssid":"Future","security":"FUTURE_SECURITY"}""";
        Assert.True(editor.Validate());
        Assert.Equal("FUTURE_SECURITY", editor.SelectedWifiSecurity!.Value);
    }

    [Fact]
    public async Task Wifi_detail_copies_through_sensitive_clipboard_and_revokes_commands_on_clear()
    {
        var crypto = Crypto();
        var clipboard = new Clipboard();
        using var detail = Detail(crypto, clipboard, new()
        {
            Title = "Home",
            LoginType = PasswordLoginType.Wifi,
            Password = crypto.EncryptString(" p;word "),
            WifiMetadata = """{"ssid":"Home","hiddenNetwork":true}"""
        });
        Assert.True(detail.CanShowWifiQr);
        Assert.False(detail.IsWifiQrVisible);
        Assert.Null(detail.WifiQrImage);
        await detail.CopyWifiQrCommand.ExecuteAsync(null);
        Assert.Equal(1, clipboard.SensitiveWrites);
        Assert.Equal(" p;word ", WifiQrCodec.Parse(clipboard.Text)!.Password);
        detail.ClearSensitiveState();
        Assert.False(detail.CanShowWifiQr);
        Assert.False(detail.CopyWifiQrCommand.CanExecute(null));
        Assert.False(detail.ToggleWifiQrCommand.CanExecute(null));
        detail.ToggleWifiQrCommand.Execute(null);
        Assert.Empty(detail.StatusText);
        Assert.False(detail.IsWifiQrVisible);
        Assert.Empty(detail.WifiSsid);
        await detail.CopyWifiQrCommand.ExecuteAsync(null);
        Assert.Equal(1, clipboard.SensitiveWrites);
    }

    [Theory]
    [InlineData("WPA2_ENTERPRISE", "password")]
    [InlineData("FUTURE", "password")]
    [InlineData("WPA2_WPA3", "vault:v1:unreadable")]
    public void Unsupported_or_unreadable_wifi_details_refuse_sharing(string security, string password)
    {
        var crypto = Crypto();
        using var detail = Detail(crypto, new Clipboard(), new()
        {
            Title = "Network",
            LoginType = PasswordLoginType.Wifi,
            Password = password,
            WifiMetadata = $"{{\"security\":\"{security}\"}}"
        });
        Assert.False(detail.CanShowWifiQr);
        Assert.NotEmpty(detail.WifiQrUnavailableReason);
    }

    [Fact]
    public void Editor_clear_releases_imported_network_and_qr_secrets()
    {
        using var editor = Editor();
        editor.ImportWifiQrPayload("WIFI:T:WPA;S:Private;P:private-secret;;");
        editor.WifiQrText = "WIFI:T:WPA;S:Other;P:other-secret;;";
        editor.ClearSensitiveState();
        Assert.Empty(editor.WifiSsid);
        Assert.Empty(editor.WifiQrText);
        Assert.Empty(editor.WifiImportStatus);
        Assert.Empty(editor.WifiSecurityOptions);
        Assert.False(editor.ImportWifiQrPayload("WIFI:S:Late;;"));
        Assert.Empty(editor.WifiMetadata);
    }

    private static PasswordEditorViewModel Editor(PasswordEntry? entry = null, string password = "") =>
        new(new LocalizationService(), new PasswordGeneratorService(), entry, [], password);

    private static CryptoService Crypto()
    {
        var crypto = new CryptoService();
        crypto.InitializeSession("wifi-test-master", Enumerable.Repeat((byte)7, 16).ToArray());
        return crypto;
    }

    private static PasswordDetailViewModel Detail(CryptoService crypto, Clipboard clipboard, PasswordEntry entry) =>
        new(new LocalizationService(), clipboard, crypto, new TotpService(), entry, [entry], null, null, [], []);

    private sealed class Clipboard : IClipboardService
    {
        public int SensitiveWrites { get; private set; }
        public string Text { get; private set; } = "";
        public Task SetTextAsync(string text, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task SetSensitiveTextAsync(string text, CancellationToken cancellationToken = default)
        {
            SensitiveWrites++;
            Text = text;
            return Task.CompletedTask;
        }
    }
}
