using System.Diagnostics;
using Monica.Core.Models;

namespace Monica.Platform.Services;

public static class PlatformFeatureKeys
{
    public const string FilePicker = "file-picker";
    public const string SecretProtection = "secret-protection";
    public const string Tray = "tray";
    public const string GlobalHotkey = "global-hotkey";
    public const string BrowserBridge = "browser-bridge";
    public const string NativePasskey = "native-passkey";
    public const string NativeNotification = "native-notification";
    public const string WindowSecurity = "window-security";
    public const string ExternalLinks = "external-links";
}

public sealed record PlatformIntegrationCapability(
    string Key,
    PlatformFeatureStatus Status,
    string Description,
    string? UnsupportedReason = null)
{
    public bool IsUsable => Status is PlatformFeatureStatus.Available or PlatformFeatureStatus.DesktopEquivalent;
}

public interface IPlatformIntegrationService
{
    string PlatformName { get; }
    IReadOnlyList<PlatformIntegrationCapability> GetCapabilities();
    PlatformIntegrationCapability GetCapability(string key);
}

public sealed record PlatformFilePickerFileType(string Name, IReadOnlyList<string> Patterns);

public sealed record PickedTextFile(string FileName, string Content);

public interface ISecretProtector
{
    PlatformIntegrationCapability Capability { get; }
    Task<string> ProtectAsync(string plainText, CancellationToken cancellationToken = default);
    Task<string> UnprotectAsync(string protectedText, CancellationToken cancellationToken = default);
}

public interface IFileSystemPickerService
{
    PlatformIntegrationCapability Capability { get; }
    Task<PickedTextFile?> OpenTextFileAsync(string title, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default);
    Task<string?> SaveTextFileAsync(string title, string suggestedFileName, string content, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default);
}

public interface IBrowserBridgeService
{
    PlatformIntegrationCapability Capability { get; }
}

public interface INativePasskeyService
{
    PlatformIntegrationCapability Capability { get; }
}

public interface ITrayService
{
    PlatformIntegrationCapability Capability { get; }
}

public interface IGlobalHotkeyService
{
    PlatformIntegrationCapability Capability { get; }
}

public interface IExternalLinkService
{
    PlatformIntegrationCapability Capability { get; }
    Task OpenAsync(Uri uri, CancellationToken cancellationToken = default);
}

public sealed class PlatformIntegrationService : IPlatformIntegrationService
{
    private readonly IReadOnlyDictionary<string, PlatformIntegrationCapability> _capabilities;

    public PlatformIntegrationService()
        : this(DetectPlatformName(), DetectCapabilities())
    {
    }

    public PlatformIntegrationService(string platformName, IEnumerable<PlatformIntegrationCapability> capabilities)
    {
        PlatformName = platformName;
        _capabilities = capabilities.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
    }

    public string PlatformName { get; }

    public IReadOnlyList<PlatformIntegrationCapability> GetCapabilities() =>
        _capabilities.Values.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).ToArray();

    public PlatformIntegrationCapability GetCapability(string key) =>
        _capabilities.TryGetValue(key, out var capability)
            ? capability
            : Unsupported(key, "This platform adapter has not declared this feature.");

    private static string DetectPlatformName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "Windows";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "macOS";
        }

        if (OperatingSystem.IsLinux())
        {
            return "Linux";
        }

        return "Desktop";
    }

    private static IReadOnlyList<PlatformIntegrationCapability> DetectCapabilities()
    {
        if (OperatingSystem.IsWindows())
        {
            return
            [
                Available(PlatformFeatureKeys.FilePicker, "Windows file picker can be provided through Avalonia storage APIs."),
                Available(PlatformFeatureKeys.SecretProtection, "Windows secret protection will use a DPAPI-backed adapter."),
                Available(PlatformFeatureKeys.Tray, "Windows tray integration is available for desktop builds."),
                Available(PlatformFeatureKeys.GlobalHotkey, "Windows global hotkeys can be registered by a platform adapter."),
                DesktopEquivalent(PlatformFeatureKeys.BrowserBridge, "Browser integration is provided through a local desktop bridge."),
                Available(PlatformFeatureKeys.ExternalLinks, "External links can be opened through the Windows shell."),
                PlatformLimited(PlatformFeatureKeys.NativePasskey, "Full Windows credential-provider integration requires a dedicated native adapter."),
                DesktopEquivalent(PlatformFeatureKeys.NativeNotification, "Desktop notifications can replace Android notification features."),
                Available(PlatformFeatureKeys.WindowSecurity, "Window-level security features can be mapped to Windows shell behavior.")
            ];
        }

        if (OperatingSystem.IsMacOS())
        {
            return
            [
                DesktopEquivalent(PlatformFeatureKeys.FilePicker, "macOS file picking is available through Avalonia storage APIs."),
                PlatformLimited(PlatformFeatureKeys.SecretProtection, "Keychain-backed secret protection needs a macOS adapter."),
                PlatformLimited(PlatformFeatureKeys.Tray, "Menu bar integration needs a macOS adapter."),
                PlatformLimited(PlatformFeatureKeys.GlobalHotkey, "Global hotkeys require a macOS accessibility-aware adapter."),
                DesktopEquivalent(PlatformFeatureKeys.BrowserBridge, "Browser integration is provided through a local desktop bridge."),
                Available(PlatformFeatureKeys.ExternalLinks, "External links can be opened through the macOS desktop shell."),
                Unsupported(PlatformFeatureKeys.NativePasskey, "Android Credential Provider behavior is not available on macOS."),
                DesktopEquivalent(PlatformFeatureKeys.NativeNotification, "Desktop notifications can replace Android notification features."),
                PlatformLimited(PlatformFeatureKeys.WindowSecurity, "macOS window privacy behavior needs a native adapter.")
            ];
        }

        if (OperatingSystem.IsLinux())
        {
            return
            [
                DesktopEquivalent(PlatformFeatureKeys.FilePicker, "Linux file picking is available through Avalonia storage APIs."),
                PlatformLimited(PlatformFeatureKeys.SecretProtection, "Secret Service or keyring support needs a Linux adapter."),
                PlatformLimited(PlatformFeatureKeys.Tray, "Tray behavior depends on the active Linux desktop environment."),
                PlatformLimited(PlatformFeatureKeys.GlobalHotkey, "Global hotkeys depend on the compositor and desktop environment."),
                DesktopEquivalent(PlatformFeatureKeys.BrowserBridge, "Browser integration is provided through a local desktop bridge."),
                Available(PlatformFeatureKeys.ExternalLinks, "External links can be opened through the Linux desktop shell."),
                Unsupported(PlatformFeatureKeys.NativePasskey, "Android Credential Provider behavior is not available on Linux."),
                DesktopEquivalent(PlatformFeatureKeys.NativeNotification, "Desktop notifications can replace Android notification features."),
                PlatformLimited(PlatformFeatureKeys.WindowSecurity, "Linux screenshot/window privacy support depends on the compositor.")
            ];
        }

        return
        [
            DesktopEquivalent(PlatformFeatureKeys.FilePicker, "Avalonia storage APIs provide the cross-platform file picker path."),
            Unsupported(PlatformFeatureKeys.SecretProtection, "No secret protection adapter is available for this platform."),
            Unsupported(PlatformFeatureKeys.Tray, "No tray adapter is available for this platform."),
            Unsupported(PlatformFeatureKeys.GlobalHotkey, "No global hotkey adapter is available for this platform."),
            DesktopEquivalent(PlatformFeatureKeys.BrowserBridge, "Browser integration is provided through a local desktop bridge."),
            PlatformLimited(PlatformFeatureKeys.ExternalLinks, "External link launching depends on the current desktop shell."),
            Unsupported(PlatformFeatureKeys.NativePasskey, "Native passkey integration is not available for this platform."),
            Unsupported(PlatformFeatureKeys.NativeNotification, "No notification adapter is available for this platform."),
            Unsupported(PlatformFeatureKeys.WindowSecurity, "No window security adapter is available for this platform.")
        ];
    }

    public static PlatformIntegrationCapability Available(string key, string description) =>
        new(key, PlatformFeatureStatus.Available, description);

    public static PlatformIntegrationCapability DesktopEquivalent(string key, string description) =>
        new(key, PlatformFeatureStatus.DesktopEquivalent, description);

    public static PlatformIntegrationCapability PlatformLimited(string key, string description) =>
        new(key, PlatformFeatureStatus.PlatformLimited, description, description);

    public static PlatformIntegrationCapability Unsupported(string key, string reason) =>
        new(key, PlatformFeatureStatus.Unsupported, reason, reason);
}

public sealed class UnsupportedSecretProtector(IPlatformIntegrationService platformIntegrationService) : ISecretProtector
{
    public PlatformIntegrationCapability Capability =>
        platformIntegrationService.GetCapability(PlatformFeatureKeys.SecretProtection);

    public Task<string> ProtectAsync(string plainText, CancellationToken cancellationToken = default) =>
        throw CreateUnsupportedException();

    public Task<string> UnprotectAsync(string protectedText, CancellationToken cancellationToken = default) =>
        throw CreateUnsupportedException();

    private InvalidOperationException CreateUnsupportedException() =>
        new(Capability.UnsupportedReason ?? "Secret protection is not supported on this platform.");
}

public sealed class CapabilityOnlyFileSystemPickerService(IPlatformIntegrationService platformIntegrationService) : IFileSystemPickerService
{
    public PlatformIntegrationCapability Capability => platformIntegrationService.GetCapability(PlatformFeatureKeys.FilePicker);

    public Task<PickedTextFile?> OpenTextFileAsync(string title, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
        throw CreateUnsupportedException();

    public Task<string?> SaveTextFileAsync(string title, string suggestedFileName, string content, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
        throw CreateUnsupportedException();

    private InvalidOperationException CreateUnsupportedException() =>
        new(Capability.UnsupportedReason ?? "File picking is not supported on this platform.");
}

public sealed class CapabilityOnlyBrowserBridgeService(IPlatformIntegrationService platformIntegrationService) : IBrowserBridgeService
{
    public PlatformIntegrationCapability Capability => platformIntegrationService.GetCapability(PlatformFeatureKeys.BrowserBridge);
}

public sealed class CapabilityOnlyNativePasskeyService(IPlatformIntegrationService platformIntegrationService) : INativePasskeyService
{
    public PlatformIntegrationCapability Capability => platformIntegrationService.GetCapability(PlatformFeatureKeys.NativePasskey);
}

public sealed class CapabilityOnlyTrayService(IPlatformIntegrationService platformIntegrationService) : ITrayService
{
    public PlatformIntegrationCapability Capability => platformIntegrationService.GetCapability(PlatformFeatureKeys.Tray);
}

public sealed class CapabilityOnlyGlobalHotkeyService(IPlatformIntegrationService platformIntegrationService) : IGlobalHotkeyService
{
    public PlatformIntegrationCapability Capability => platformIntegrationService.GetCapability(PlatformFeatureKeys.GlobalHotkey);
}

public sealed class SystemExternalLinkService(IPlatformIntegrationService platformIntegrationService) : IExternalLinkService
{
    public PlatformIntegrationCapability Capability => platformIntegrationService.GetCapability(PlatformFeatureKeys.ExternalLinks);

    public Task OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Capability.IsUsable)
        {
            throw new InvalidOperationException(Capability.UnsupportedReason ?? "External links are not supported on this platform.");
        }

        var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
        {
            UseShellExecute = true
        });

        return process is null
            ? Task.FromException(new InvalidOperationException("The desktop shell did not accept the external link."))
            : Task.CompletedTask;
    }
}
