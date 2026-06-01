using Monica.Core.Models;
using Monica.Platform.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Monica.Tests;

public sealed class PlatformServiceTests
{
    [Fact]
    public void Webdav_paths_are_normalized_for_sync()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        using var provider = services.BuildServiceProvider();
        var service = new WebDavBackupService(provider.GetRequiredService<IHttpClientFactory>());

        var path = service.NormalizeRemotePath("/Monica/", "/backup/vault.json");

        Assert.Equal("/Monica/backup/vault.json", path);
    }

    [Fact]
    public async Task Mdbx_service_creates_metadata_and_stream()
    {
        var service = new MdbxVaultService();
        var path = Path.Combine(Path.GetTempPath(), "monica-tests", $"{Guid.NewGuid():N}.mdbx");

        var metadata = await service.CreateLocalMetadataAsync("Test", path, MdbxTigaMode.Sky);
        await using var stream = await service.OpenLocalStreamAsync(metadata);

        Assert.Equal("Test", metadata.Name);
        Assert.True(stream.CanWrite);
    }

    [Fact]
    public async Task KeePass_service_reports_missing_file()
    {
        var service = new KeePassVaultService();

        var summary = await service.InspectAsync(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.kdbx"), null);

        Assert.False(summary.Exists);
        Assert.Contains("not found", summary.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Platform_integration_reports_declared_capabilities()
    {
        var service = new PlatformIntegrationService(
            "TestOS",
            [
                PlatformIntegrationService.Available(PlatformFeatureKeys.FilePicker, "Picker works."),
                PlatformIntegrationService.Unsupported(PlatformFeatureKeys.NativePasskey, "Native passkeys are unavailable.")
            ]);

        Assert.Equal("TestOS", service.PlatformName);
        Assert.True(service.GetCapability(PlatformFeatureKeys.FilePicker).IsUsable);
        Assert.Equal(PlatformFeatureStatus.Unsupported, service.GetCapability(PlatformFeatureKeys.NativePasskey).Status);
        Assert.Equal(PlatformFeatureStatus.Unsupported, service.GetCapability("unknown").Status);
    }

    [Fact]
    public void Platform_capability_service_maps_native_passkey_status()
    {
        var integration = new PlatformIntegrationService(
            "TestOS",
            [
                PlatformIntegrationService.DesktopEquivalent(PlatformFeatureKeys.BrowserBridge, "Bridge works."),
                PlatformIntegrationService.DesktopEquivalent(PlatformFeatureKeys.GlobalHotkey, "Hotkey works."),
                PlatformIntegrationService.Unsupported(PlatformFeatureKeys.NativePasskey, "Credential provider unavailable.")
            ]);
        var service = new PlatformCapabilityService(integration);

        var autofill = service.GetCapability("autofill");
        var credentialProvider = service.GetCapability("credential-provider");

        Assert.Equal(PlatformFeatureStatus.DesktopEquivalent, autofill.Status);
        Assert.Equal(PlatformFeatureStatus.Unsupported, credentialProvider.Status);
        Assert.Equal("Credential provider unavailable.", credentialProvider.UnsupportedReason);
    }

    [Fact]
    public async Task Unsupported_secret_protector_throws_platform_reason()
    {
        var integration = new PlatformIntegrationService(
            "TestOS",
            [PlatformIntegrationService.Unsupported(PlatformFeatureKeys.SecretProtection, "No keyring.")]);
        var protector = new UnsupportedSecretProtector(integration);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => protector.ProtectAsync("secret"));

        Assert.Contains("No keyring", error.Message);
    }

    [Fact]
    public async Task System_external_link_service_throws_platform_reason_when_unsupported()
    {
        var integration = new PlatformIntegrationService(
            "TestOS",
            [PlatformIntegrationService.Unsupported(PlatformFeatureKeys.ExternalLinks, "No desktop shell.")]);
        var service = new SystemExternalLinkService(integration);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.OpenAsync(new Uri("https://example.com")));

        Assert.Contains("No desktop shell", error.Message);
    }

    [Fact]
    public void Secret_protector_factory_selects_platform_adapter()
    {
        var integration = new PlatformIntegrationService(
            "TestOS",
            [PlatformIntegrationService.Available(PlatformFeatureKeys.SecretProtection, "Secret protection works.")]);

        var protector = SecretProtectorFactory.Create(integration);

        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<WindowsSecretProtector>(protector);
        }
        else
        {
            Assert.IsType<UnsupportedSecretProtector>(protector);
        }
    }

    [Fact]
    public async Task Windows_secret_protector_roundtrips_current_user_secret()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var integration = new PlatformIntegrationService(
            "Windows",
            [PlatformIntegrationService.Available(PlatformFeatureKeys.SecretProtection, "Windows DPAPI is available.")]);
        var protector = new WindowsSecretProtector(integration);

        var protectedText = await protector.ProtectAsync("secret-value");
        var roundtripped = await protector.UnprotectAsync(protectedText);

        Assert.NotEqual("secret-value", protectedText);
        Assert.Equal("secret-value", roundtripped);
    }
}
