using Monica.Core.Models;

namespace Monica.Platform.Services;

public interface IClipboardService
{
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);
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

public interface IWebDavBackupService
{
    string NormalizeRemotePath(string rootPath, string relativePath);
    Task<IReadOnlyList<RemoteFileEntry>> ListAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default);
    Task UploadTextAsync(WebDavProfile profile, string relativePath, string content, CancellationToken cancellationToken = default);
    Task<string> DownloadTextAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default);
    Task DeleteAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default);
}

public interface IOneDriveBackupService
{
    PlatformCapability GetCapability();
}

public interface IKeePassVaultService
{
    Task<KeePassVaultSummary> InspectAsync(string path, string? password, CancellationToken cancellationToken = default);
}

public sealed record KeePassVaultSummary(string Path, bool Exists, string Status, int GroupCount, int EntryCount);

public interface IMdbxVaultService
{
    Task<LocalMdbxDatabase> CreateLocalMetadataAsync(string name, string filePath, MdbxTigaMode mode = MdbxTigaMode.Multi, CancellationToken cancellationToken = default);
    Task<Stream> OpenLocalStreamAsync(LocalMdbxDatabase database, CancellationToken cancellationToken = default);
}
