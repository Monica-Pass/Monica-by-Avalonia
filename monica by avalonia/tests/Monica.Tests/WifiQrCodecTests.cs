using System.Text.Json.Nodes;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.Tests;

public sealed class WifiQrCodecTests
{
    [Fact]
    public void Android_payload_vector_escapes_reserved_characters_and_preserves_secret_whitespace()
    {
        var network = new WifiNetworkData("家用;wifi\\\"", nameof(WifiSecurity.WPA2_WPA3), true);
        var payload = WifiQrCodec.Build(network, " p:a,ss;\\\" ");

        Assert.Equal("WIFI:T:WPA;S:家用\\;wifi\\\\\\\";P: p\\:a\\,ss\\;\\\\\\\" ;H:true;;", payload);
        var imported = Assert.IsType<WifiQrImport>(WifiQrCodec.Parse(payload));
        Assert.Equal(network, imported.Network);
        Assert.Equal(" p:a,ss;\\\" ", imported.Password);
    }

    [Theory]
    [InlineData("WIFI:T:nopass;S:Guest;;", "NONE", "")]
    [InlineData("wifi:T:WEP;S:Legacy;P:12345;;", "WEP", "12345")]
    [InlineData("WiFi:T:SAE;S:Modern;P:password;H:TRUE;;", "WPA3", "password")]
    [InlineData("WIFI:S:Default;P:password;;", "WPA2_WPA3", "password")]
    public void Imports_supported_android_connection_types(string payload, string security, string password)
    {
        var imported = Assert.IsType<WifiQrImport>(WifiQrCodec.Parse(payload));
        Assert.Equal(security, imported.Network.Security);
        Assert.Equal(password, imported.Password);
    }

    [Theory]
    [InlineData("otpauth://totp/Example?secret=ABC")]
    [InlineData("WIFI:T:WPA;P:secret;;")]
    [InlineData("WIFI:T:WPA-EAP;S:Enterprise;P:secret;;")]
    [InlineData("WIFI:S:First;S:Second;;")]
    [InlineData("WIFI:S:Trailing\\")]
    public void Invalid_or_unsupported_qr_does_not_silently_change_the_network(string payload) =>
        Assert.Null(WifiQrCodec.Parse(payload));

    [Theory]
    [InlineData("WPA2_ENTERPRISE")]
    [InlineData("WPA3_ENTERPRISE")]
    [InlineData("FUTURE_SECURITY")]
    public void Enterprise_and_unknown_security_never_generate_personal_connection_qr(string security) =>
        Assert.Null(WifiQrCodec.Build(new("Office", security, false), "secret"));

    [Fact]
    public void Open_network_omits_any_stored_password_and_oversized_payload_is_refused()
    {
        Assert.Equal("WIFI:T:nopass;S:Guest;;", WifiQrCodec.Build(new("Guest", "NONE", false), "private"));
        Assert.Null(WifiQrCodec.Build(new("Home", "WPA2_WPA3", false), ""));
        Assert.Null(WifiQrCodec.Build(new(new string('x', 4096), "NONE", false), ""));
        Assert.Null(WifiQrCodec.Parse("WIFI:S:" + new string('x', 4096) + ";;"));
    }

    [Fact]
    public void Editing_common_fields_keeps_android_advanced_settings_and_future_extensions()
    {
        const string original = """{"ssid":"Office","security":"WPA2_ENTERPRISE","eap":{"method":"TLS","future":7},"proxy":{"kind":"custom-proxy","host":"proxy"},"ip":{"kind":"static","dns1":"1.1.1.1"},"macRandomization":"DEVICE_MAC","future":[1,{"a":true}]}""";
        Assert.True(WifiNetworkData.TryRead(original, "Fallback", out var network));
        var updated = (network with { Ssid = "New Office", HiddenNetwork = true }).ApplyTo(original);
        var before = JsonNode.Parse(original)!;
        var after = JsonNode.Parse(updated)!;

        foreach (var key in new[] { "eap", "proxy", "ip", "macRandomization", "future" })
        {
            Assert.True(JsonNode.DeepEquals(before[key], after[key]));
        }

        Assert.True(WifiNetworkData.TryRead(updated, "Fallback", out var reread));
        Assert.Equal("New Office", reread.Ssid);
        Assert.True(reread.HiddenNetwork);
        Assert.Equal("WPA2_ENTERPRISE", reread.Security);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("invalid")]
    [InlineData("{\"ssid\":1}")]
    [InlineData("{\"security\":null}")]
    [InlineData("{\"ssid\":\"one\",\"ssid\":\"two\"}")]
    [InlineData("{\"hiddenNetwork\":\"false\"}")]
    public void Malformed_metadata_is_reported_without_discarding_it(string metadata) =>
        Assert.False(WifiNetworkData.TryRead(metadata, "Fallback", out _));
}
