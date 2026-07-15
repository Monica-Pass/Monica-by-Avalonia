using Monica.Core.Models;

namespace Monica.Platform.Services;

public interface IClipboardService
{
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);

    Task SetSensitiveTextAsync(string text, CancellationToken cancellationToken = default) =>
        SetTextAsync(text, cancellationToken);

    void ConfigureSensitiveClear(TimeSpan? lifetime)
    {
    }

    Task ClearOwnedContentAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

public interface IClipboardAdapter
{
    Task<string?> GetTextAsync(CancellationToken cancellationToken = default);
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public interface IClipboardExpiryScheduler
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public interface IPlatformCapabilityService
{
    IReadOnlyList<PlatformCapability> GetCapabilities();
    PlatformCapability GetCapability(string key);
}

public sealed class PlatformCapabilityService(IPlatformIntegrationService? platformIntegrationService = null) : IPlatformCapabilityService
{
    private readonly IPlatformIntegrationService _platformIntegrationService =
        platformIntegrationService ?? new PlatformIntegrationService();

    public IReadOnlyList<PlatformCapability> GetCapabilities() =>
        FeatureCatalog.AndroidParityFeatures.Select(ApplyPlatformStatus).ToArray();

    public PlatformCapability GetCapability(string key) =>
        GetCapabilities().FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
        ?? new PlatformCapability(
            key,
            key,
            "This feature is not registered in the desktop feature catalog.",
            PlatformFeatureStatus.Unsupported,
            "This feature is not registered in the desktop feature catalog.");

    private PlatformCapability ApplyPlatformStatus(PlatformCapability capability)
    {
        return capability.Key switch
        {
            "autofill" => ApplyAutofillStatus(capability),
            "credential-provider" => ApplyNativePasskeyStatus(capability),
            _ => capability
        };
    }

    private PlatformCapability ApplyAutofillStatus(PlatformCapability capability)
    {
        var browserBridge = _platformIntegrationService.GetCapability(PlatformFeatureKeys.BrowserBridge);
        var globalHotkey = _platformIntegrationService.GetCapability(PlatformFeatureKeys.GlobalHotkey);

        if (browserBridge.IsUsable && globalHotkey.IsUsable)
        {
            return capability with
            {
                Status = PlatformFeatureStatus.DesktopEquivalent,
                UnsupportedReason = null
            };
        }

        return capability with
        {
            Status = PlatformFeatureStatus.PlatformLimited,
            UnsupportedReason = browserBridge.UnsupportedReason ?? globalHotkey.UnsupportedReason ?? capability.UnsupportedReason
        };
    }

    private PlatformCapability ApplyNativePasskeyStatus(PlatformCapability capability)
    {
        var nativePasskey = _platformIntegrationService.GetCapability(PlatformFeatureKeys.NativePasskey);
        return capability with
        {
            Status = nativePasskey.Status,
            UnsupportedReason = nativePasskey.UnsupportedReason
        };
    }
}

public sealed record RemoteFileEntry(string Path, bool IsDirectory, long? Length, DateTimeOffset? LastModified);

public sealed record RemoteFileVersion(string? ETag, DateTimeOffset? LastModified, long? Length)
{
    public bool HasValidator => !string.IsNullOrWhiteSpace(ETag) || LastModified is not null;
}

public sealed record RemoteWriteCondition(bool RequireMissing, RemoteFileVersion? ExpectedVersion)
{
    public static RemoteWriteCondition CreateOnly { get; } = new(true, null);

    public static RemoteWriteCondition Match(RemoteFileVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (!version.HasValidator)
        {
            throw new ArgumentException("A remote ETag or Last-Modified validator is required.", nameof(version));
        }

        return new RemoteWriteCondition(false, version);
    }
}

public sealed class RemoteFileConflictException(string message) : InvalidOperationException(message);

public interface IWebDavBackupService
{
    string NormalizeRemotePath(string rootPath, string relativePath);
    Task<IReadOnlyList<RemoteFileEntry>> ListAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default);
    Task UploadTextAsync(WebDavProfile profile, string relativePath, string content, CancellationToken cancellationToken = default);
    Task<string> DownloadTextAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default);
    Task UploadBinaryAsync(WebDavProfile profile, string relativePath, Stream content, CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException("Binary WebDAV upload is not available."));
    Task DownloadBinaryAsync(WebDavProfile profile, string relativePath, Stream destination, CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException("Binary WebDAV download is not available."));
    Task<RemoteFileVersion?> GetFileVersionAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default) =>
        Task.FromResult<RemoteFileVersion?>(null);
    Task<RemoteFileVersion> UploadBinaryConditionallyAsync(WebDavProfile profile, string relativePath, Stream content, RemoteWriteCondition condition, CancellationToken cancellationToken = default) =>
        Task.FromException<RemoteFileVersion>(new NotSupportedException("Conditional binary WebDAV upload is not available."));
    Task<RemoteFileVersion> DownloadBinaryVersionedAsync(WebDavProfile profile, string relativePath, Stream destination, CancellationToken cancellationToken = default) =>
        Task.FromException<RemoteFileVersion>(new NotSupportedException("Versioned binary WebDAV download is not available."));
    Task DeleteAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default);
}

public interface IOneDriveBackupService
{
    PlatformCapability GetCapability();
    Task<OneDriveAccountInfo?> GetCachedAccountAsync(string? accountId = null, CancellationToken cancellationToken = default);
    Task<OneDriveSignInChallenge> BeginSignInAsync(CancellationToken cancellationToken = default);
    Task SignOutAsync(string? accountId = null, CancellationToken cancellationToken = default);
    Task<RemoteFileVersion?> GetFileVersionAsync(string accountId, string remotePath, CancellationToken cancellationToken = default);
    Task<RemoteFileVersion> UploadBinaryConditionallyAsync(
        string accountId,
        string remotePath,
        Stream content,
        RemoteWriteCondition condition,
        CancellationToken cancellationToken = default);
    Task<RemoteFileVersion> DownloadBinaryVersionedAsync(
        string accountId,
        string remotePath,
        Stream destination,
        CancellationToken cancellationToken = default);
    Task ClearSensitiveCacheAsync(CancellationToken cancellationToken = default);
}

public sealed record OneDriveAccountInfo(string AccountId, string DisplayName, string Username);

public sealed record OneDriveDeviceCodePrompt(
    string UserCode,
    Uri VerificationUri,
    DateTimeOffset ExpiresAt,
    string Message);

public sealed record OneDriveSignInChallenge(
    OneDriveDeviceCodePrompt Prompt,
    Task<OneDriveAccountInfo> Completion);

public interface IKeePassVaultService
{
    Task<KeePassVaultSummary> InspectAsync(string path, string? password, CancellationToken cancellationToken = default);
    Task<KeePassVaultSnapshot> ReadAsync(
        ReadOnlyMemory<byte> content,
        string fileName,
        string? password,
        CancellationToken cancellationToken = default);
}

public sealed record KeePassVaultSummary(string Path, bool Exists, string Status, int GroupCount, int EntryCount);

public interface IMdbxVaultService
{
    Task<LocalMdbxDatabase> CreateLocalMetadataAsync(string name, string filePath, MdbxTigaMode mode = MdbxTigaMode.Multi, CancellationToken cancellationToken = default);
    Task<Stream> OpenLocalStreamAsync(LocalMdbxDatabase database, CancellationToken cancellationToken = default);
}
