using Monica.Core.Models;
using Monica.Data.Mdbx;
using Monica.Platform.Services;
using Microsoft.Data.Sqlite;
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
    public void Security_baseline_webdav_rejects_insecure_transport()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            WebDavEndpointPolicy.EnsureSecure(new Uri("http://dav.example.com/")));

        Assert.Contains("HTTPS", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mdbx_service_creates_metadata_and_stream()
    {
        var service = new MdbxVaultService(new MdbxTestVaultEngine());
        var path = Path.Combine(Path.GetTempPath(), "monica-tests", $"{Guid.NewGuid():N}.mdbx");

        var metadata = await service.CreateLocalMetadataAsync("Test", path, MdbxTigaMode.Sky);
        await using var stream = await service.OpenLocalStreamAsync(metadata);

        Assert.Equal("Test", metadata.Name);
        Assert.False(string.IsNullOrWhiteSpace(metadata.EncryptedPassword));
        Assert.Equal("mdbx-1/argon2id", metadata.KdfProfile);
        Assert.True(stream.CanWrite);
        Assert.Equal("MDBX-1", await ReadMdbxFormatVersionAsync(path));
    }

    [Fact]
    public async Task Mdbx_service_uses_native_bridge_without_fallback_engine_when_available()
    {
        var bridge = new RecordingNativeBridge();
        var service = new MdbxVaultService(new ThrowingMdbxVaultEngine(), bridge);
        var path = Path.Combine(Path.GetTempPath(), "monica-tests", $"{Guid.NewGuid():N}.mdbx");

        var metadata = await service.CreateLocalMetadataAsync("Native", path, MdbxTigaMode.Power);
        await using var stream = await service.OpenLocalStreamAsync(metadata);

        Assert.Equal(1, bridge.CreateCalls);
        Assert.Equal(1, bridge.OpenCalls);
        Assert.Equal("MDBX-1 vault native-vault", metadata.Description);
        Assert.True(stream.CanWrite);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Mdbx_service_inspects_existing_file_with_native_bridge_without_fallback_engine_when_available()
    {
        var bridge = new RecordingNativeBridge();
        var service = new MdbxVaultService(new ThrowingMdbxVaultEngine(), bridge);
        var path = Path.Combine(Path.GetTempPath(), "monica-tests", $"{Guid.NewGuid():N}.mdbx");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "existing native vault");

        var metadata = await service.CreateLocalMetadataAsync("Existing native", path, MdbxTigaMode.Multi);

        Assert.Equal(0, bridge.CreateCalls);
        Assert.Equal(1, bridge.OpenCalls);
        Assert.Equal("MDBX-1 vault native-vault", metadata.Description);
        Assert.Equal(path, metadata.WorkingCopyPath);
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
    public async Task Capability_only_file_picker_throws_platform_reason()
    {
        var integration = new PlatformIntegrationService(
            "TestOS",
            [PlatformIntegrationService.Unsupported(PlatformFeatureKeys.FilePicker, "No picker.")]);
        var service = new CapabilityOnlyFileSystemPickerService(integration);

        var openError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.OpenTextFileAsync("Open", [new PlatformFilePickerFileType("JSON", ["*.json"])]));
        var saveError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveTextFileAsync("Save", "monica.json", "{}", [new PlatformFilePickerFileType("JSON", ["*.json"])]));

        Assert.Contains("No picker", openError.Message);
        Assert.Contains("No picker", saveError.Message);
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

    private static async Task<string> ReadMdbxFormatVersionAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT format_version FROM vault_meta LIMIT 1";
        return (string)(await command.ExecuteScalarAsync() ?? "");
    }

    private sealed class ThrowingMdbxVaultEngine : IMdbxVaultEngine
    {
        public Task CreateVaultAsync(string path, string password, MdbxTigaMode mode, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Fallback engine should not be used when native MDBX is available.");

        public Task OpenVaultAsync(string path, string password, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Fallback engine should not be used when native MDBX is available.");

        public Task<MdbxVaultInspection> InspectAsync(string path, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Fallback engine should not be used when native MDBX is available.");
    }

    private sealed class RecordingNativeBridge : IMdbxNativeBridge
    {
        public bool IsAvailable => true;
        public int CreateCalls { get; private set; }
        public int OpenCalls { get; private set; }

        public Task<IMdbxNativeVault> CreateVaultAsync(string path, string password, string deviceId, MdbxTigaMode mode, CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Environment.CurrentDirectory);
            File.WriteAllText(path, "native vault");
            return Task.FromResult<IMdbxNativeVault>(new RecordingNativeVault());
        }

        public Task<IMdbxNativeVault> OpenVaultAsync(string path, string password, string deviceId, CancellationToken cancellationToken = default)
        {
            OpenCalls++;
            return Task.FromResult<IMdbxNativeVault>(new RecordingNativeVault());
        }
    }

    private sealed class RecordingNativeVault : IMdbxNativeVault
    {
        public Task<MdbxNativeVaultInfo> GetInfoAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new MdbxNativeVaultInfo("native-vault", "native-device"));

        public Task<MdbxNativeProjectRecord> CreateProjectAsync(string title, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MdbxNativeProjectRecord>> ListProjectsAsync(bool includeDeleted, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MdbxNativeEntryRecord> CreateEntryAsync(string projectId, string entryType, string title, string payloadJson, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MdbxNativeEntryRecord>> ListEntriesAsync(string projectId, string? entryType = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MdbxNativeEntryRecord>> ListDeletedEntriesAsync(string projectId, string? entryType = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MdbxNativeEntryRecord> UpdateEntryAsync(string projectId, string entryId, string entryType, string title, string payloadJson, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MdbxNativeEntryRecord> MoveEntryAsync(string projectId, string entryId, string targetProjectId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteEntryAsync(string projectId, string entryId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MdbxNativeEntryRecord> RestoreEntryAsync(string projectId, string entryId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MdbxNativeAttachmentRecord> CreateAttachmentMetadataAsync(string projectId, string? entryId, string fileName, string? mediaType, string contentHash, ulong originalSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MdbxNativeAttachmentRecord>> ListAttachmentsByProjectAsync(string projectId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MdbxNativeAttachmentRecord>> ListAttachmentsByEntryAsync(string entryId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MdbxNativeAttachmentRecord> WriteAttachmentInlineContentAsync(string attachmentId, byte[] content, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<byte[]> ReadAttachmentContentAsync(string attachmentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MdbxNativeAttachmentRecord> RenameAttachmentAsync(string attachmentId, string fileName, string? mediaType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAttachmentAsync(string attachmentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void Dispose()
        {
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
