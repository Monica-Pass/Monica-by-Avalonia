using Monica.Core.Models;
using Monica.Data.Mdbx;
using Monica.Platform.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;

namespace Monica.Tests;

public sealed partial class PlatformServiceTests
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
        Assert.Equal(
            path,
            service.NormalizeRemotePath("/Monica/", "/Monica/backup/vault.json"));
    }

    [Fact]
    public void Webdav_paths_preserve_case_sensitive_root_boundaries()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        using var provider = services.BuildServiceProvider();
        var service = new WebDavBackupService(provider.GetRequiredService<IHttpClientFactory>());

        var path = service.NormalizeRemotePath("/Monica", "/monica/team/vault.mdbx");

        Assert.Equal("/Monica/monica/team/vault.mdbx", path);
    }

    [Theory]
    [InlineData("../outside/vault.mdbx")]
    [InlineData("/Monica/%2e%2e/outside/vault.mdbx")]
    [InlineData("team\\outside.mdbx")]
    public void Webdav_paths_reject_escaping_segments(string relativePath)
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        using var provider = services.BuildServiceProvider();
        var service = new WebDavBackupService(provider.GetRequiredService<IHttpClientFactory>());

        Assert.Throws<ArgumentException>(() =>
            service.NormalizeRemotePath("/Monica", relativePath));
    }

    [Fact]
    public async Task Webdav_binary_transfer_streams_bytes_and_preserves_endpoint_base_path()
    {
        var handler = new RecordingWebDavHandler([5, 6, 7, 8]);
        var service = new WebDavBackupService(new RecordingHttpClientFactory(handler));
        var profile = new WebDavProfile
        {
            BaseUri = new Uri("https://dav.example.com/dav/"),
            RootPath = "/Monica",
            Username = "user",
            Password = "secret"
        };
        await using var upload = new MemoryStream([1, 2, 3, 4]);

        await service.UploadBinaryAsync(profile, "/Monica/team/vault.mdbx", upload);
        await using var download = new MemoryStream();
        var downloadedVersion = await service.DownloadBinaryVersionedAsync(
            profile,
            "/Monica/team/vault.mdbx",
            download);
        var probedVersion = await service.GetFileVersionAsync(profile, "/Monica/team/vault.mdbx");

        Assert.Equal([1, 2, 3, 4], handler.UploadedBytes);
        Assert.Equal("*", handler.UploadedIfNoneMatch);
        Assert.Equal([5, 6, 7, 8], download.ToArray());
        Assert.Equal("\"fixture-v1\"", downloadedVersion.ETag);
        Assert.Equal(downloadedVersion.ETag, probedVersion?.ETag);
        Assert.Contains(
            handler.Requests,
            request => request.Method == HttpMethod.Put &&
                request.Uri == new Uri("https://dav.example.com/dav/Monica/team/vault.mdbx"));
        Assert.Contains(
            handler.Requests,
            request => request.Method == HttpMethod.Get &&
                request.Uri == new Uri("https://dav.example.com/dav/Monica/team/vault.mdbx"));
        Assert.Contains(
            handler.Requests,
            request => request.Method.Method == "MKCOL" &&
                request.Uri == new Uri("https://dav.example.com/dav/Monica"));
        Assert.Contains(
            handler.Requests,
            request => request.Method.Method == "MKCOL" &&
                request.Uri == new Uri("https://dav.example.com/dav/Monica/team"));
    }

    [Fact]
    public async Task Webdav_text_download_rejects_declared_oversize_before_reading_content()
    {
        var content = new UnreadableDeclaredLengthContent(17);
        var handler = new RecordingWebDavHandler([], downloadContent: content);
        var service = new WebDavBackupService(
            new RecordingHttpClientFactory(handler),
            new WebDavBackupTransferLimits(MaximumTextBackupBytes: 16));
        var profile = new WebDavProfile
        {
            BaseUri = new Uri("https://dav.example.com/dav/"),
            RootPath = "/Monica"
        };

        var error = await Assert.ThrowsAsync<WebDavTextPayloadTooLargeException>(() =>
            service.DownloadTextAsync(profile, "backup.monica.enc.json"));

        Assert.Equal(16, error.MaximumBytes);
        Assert.False(content.WasRead);
    }

    [Fact]
    public async Task Webdav_text_transfer_stops_unknown_length_download_at_limit()
    {
        var content = new StreamContent(new NonSeekableReadStream(new byte[17]));
        var handler = new RecordingWebDavHandler([], downloadContent: content);
        var service = new WebDavBackupService(
            new RecordingHttpClientFactory(handler),
            new WebDavBackupTransferLimits(MaximumTextBackupBytes: 16));
        var profile = new WebDavProfile
        {
            BaseUri = new Uri("https://dav.example.com/dav/"),
            RootPath = "/Monica"
        };

        Assert.Null(content.Headers.ContentLength);
        var error = await Assert.ThrowsAsync<WebDavTextPayloadTooLargeException>(() =>
            service.DownloadTextAsync(profile, "backup.monica.enc.json"));

        Assert.Equal(16, error.MaximumBytes);
    }

    [Fact]
    public async Task Webdav_text_transfer_rejects_oversized_upload_before_request()
    {
        var handler = new RecordingWebDavHandler([]);
        var service = new WebDavBackupService(
            new RecordingHttpClientFactory(handler),
            new WebDavBackupTransferLimits(MaximumTextBackupBytes: 16));
        var profile = new WebDavProfile
        {
            BaseUri = new Uri("https://dav.example.com/dav/"),
            RootPath = "/Monica"
        };

        var error = await Assert.ThrowsAsync<WebDavTextPayloadTooLargeException>(() =>
            service.UploadTextAsync(profile, "backup.monica.enc.json", "12345678901234567"));

        Assert.Equal(16, error.MaximumBytes);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Webdav_text_transfer_roundtrips_normal_payload()
    {
        var handler = new RecordingWebDavHandler("secure backup"u8.ToArray());
        var service = new WebDavBackupService(
            new RecordingHttpClientFactory(handler),
            new WebDavBackupTransferLimits(MaximumTextBackupBytes: 32));
        var profile = new WebDavProfile
        {
            BaseUri = new Uri("https://dav.example.com/dav/"),
            RootPath = "/Monica"
        };

        await service.UploadTextAsync(profile, "backup.monica.enc.json", "secure backup");
        var downloaded = await service.DownloadTextAsync(profile, "backup.monica.enc.json");

        Assert.Equal("secure backup"u8.ToArray(), handler.UploadedBytes);
        Assert.Equal("secure backup", downloaded);
    }

    [Fact]
    public async Task Webdav_conditional_upload_uses_etag_and_maps_precondition_failure_to_conflict()
    {
        var handler = new RecordingWebDavHandler([], HttpStatusCode.PreconditionFailed);
        var service = new WebDavBackupService(new RecordingHttpClientFactory(handler));
        var profile = new WebDavProfile
        {
            BaseUri = new Uri("https://dav.example.com/dav/"),
            RootPath = "/Monica"
        };
        await using var upload = new MemoryStream([1, 2, 3, 4]);

        var error = await Assert.ThrowsAsync<RemoteFileConflictException>(() =>
            service.UploadBinaryConditionallyAsync(
                profile,
                "/Monica/team/vault.mdbx",
                upload,
                RemoteWriteCondition.Match(new RemoteFileVersion("\"fixture-v0\"", null, null))));

        Assert.Equal("\"fixture-v0\"", handler.UploadedIfMatch);
        Assert.Empty(handler.UploadedBytes);
        Assert.Contains("changed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Webdav_conditional_upload_falls_back_to_last_modified_validator()
    {
        var expectedLastModified = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var handler = new RecordingWebDavHandler([], HttpStatusCode.PreconditionFailed);
        var service = new WebDavBackupService(new RecordingHttpClientFactory(handler));
        var profile = new WebDavProfile
        {
            BaseUri = new Uri("https://dav.example.com/dav/"),
            RootPath = "/Monica"
        };
        await using var upload = new MemoryStream([1, 2, 3, 4]);

        await Assert.ThrowsAsync<RemoteFileConflictException>(() =>
            service.UploadBinaryConditionallyAsync(
                profile,
                "/Monica/team/vault.mdbx",
                upload,
                RemoteWriteCondition.Match(new RemoteFileVersion(null, expectedLastModified, null))));

        Assert.Equal(expectedLastModified, handler.UploadedIfUnmodifiedSince);
        Assert.Empty(handler.UploadedBytes);
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

    private sealed class RecordingHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingWebDavHandler(
        byte[] downloadBytes,
        HttpStatusCode putStatus = HttpStatusCode.Created,
        HttpContent? downloadContent = null) : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri? Uri)> Requests { get; } = [];
        public byte[] UploadedBytes { get; private set; } = [];
        public string? UploadedIfNoneMatch { get; private set; }
        public string? UploadedIfMatch { get; private set; }
        public DateTimeOffset? UploadedIfUnmodifiedSince { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri));
            if (request.Method.Method == "MKCOL")
            {
                return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
            }

            if (request.Method == HttpMethod.Put)
            {
                UploadedIfNoneMatch = request.Headers.IfNoneMatch.SingleOrDefault()?.ToString();
                UploadedIfMatch = request.Headers.IfMatch.SingleOrDefault()?.ToString();
                UploadedIfUnmodifiedSince = request.Headers.IfUnmodifiedSince;
                if (putStatus == HttpStatusCode.PreconditionFailed)
                {
                    return new HttpResponseMessage(putStatus);
                }

                UploadedBytes = request.Content is null
                    ? []
                    : await request.Content.ReadAsByteArrayAsync(cancellationToken);
                return new HttpResponseMessage(putStatus)
                {
                    Headers =
                    {
                        ETag = new EntityTagHeaderValue("\"fixture-v1\"")
                    }
                };
            }

            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = downloadContent ?? new ByteArrayContent(downloadBytes),
                    Headers =
                    {
                        ETag = new EntityTagHeaderValue("\"fixture-v1\"")
                    }
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class UnreadableDeclaredLengthContent(long declaredLength) : HttpContent
    {
        public bool WasRead { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            WasRead = true;
            return Task.FromException(new InvalidOperationException("Oversized response content must not be read."));
        }

        protected override bool TryComputeLength(out long length)
        {
            length = declaredLength;
            return true;
        }
    }

    private sealed class NonSeekableReadStream(byte[] content) : Stream
    {
        private readonly MemoryStream _inner = new(content, writable: false);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => _inner.Read(buffer);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
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
