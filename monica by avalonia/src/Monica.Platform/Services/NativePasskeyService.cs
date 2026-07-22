using System.Runtime.InteropServices;

namespace Monica.Platform.Services;

public sealed record NativePasskeySupport(
    bool IsWebAuthnClientApiAvailable,
    uint WebAuthnApiVersion,
    bool CanActAsWindowsCredentialProvider,
    string StatusReason)
{
    public static NativePasskeySupport Unavailable(string reason) =>
        new(false, 0, false, reason);
}

internal static class NativePasskeyProbe
{
    public static uint TryGetWebAuthnApiVersion()
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }

        try
        {
            return WebAuthNGetApiVersionNumber();
        }
        catch (DllNotFoundException)
        {
            return 0;
        }
        catch (EntryPointNotFoundException)
        {
            return 0;
        }
        catch (BadImageFormatException)
        {
            return 0;
        }
    }

    [DllImport("webauthn.dll", EntryPoint = "WebAuthNGetApiVersionNumber")]
    private static extern uint WebAuthNGetApiVersionNumber();
}

public sealed class WindowsNativePasskeyService : INativePasskeyService
{
    private readonly PlatformIntegrationCapability _capability;

    public WindowsNativePasskeyService(IPlatformIntegrationService platformIntegrationService)
    {
        var version = NativePasskeyProbe.TryGetWebAuthnApiVersion();
        _capability = platformIntegrationService.GetCapability(PlatformFeatureKeys.NativePasskey);
        Support = new NativePasskeySupport(
            version > 0,
            version,
            CanActAsWindowsCredentialProvider: false,
            version > 0
                ? $"Windows WebAuthn client API v{version} is present; a packaged credential-provider extension is still required for system passkey requests."
                : "Windows WebAuthn client API is unavailable; a packaged credential-provider extension is required for system passkey requests.");
    }

    public PlatformIntegrationCapability Capability => _capability;
    public NativePasskeySupport Support { get; }

    public static PlatformIntegrationCapability CreateCapability()
    {
        return PlatformIntegrationService.PlatformLimited(
            PlatformFeatureKeys.NativePasskey,
            "Windows WebAuthn client API availability is probed on demand; Monica is not a packaged system credential provider.");
    }
}
