using System.Security.Cryptography;
using System.Text;

namespace Monica.Platform.Services;

public sealed class WindowsSecretProtector(IPlatformIntegrationService platformIntegrationService) : ISecretProtector
{
    public PlatformIntegrationCapability Capability =>
        platformIntegrationService.GetCapability(PlatformFeatureKeys.SecretProtection);

    public Task<string> ProtectAsync(string plainText, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            throw CreateUnsupportedException("Windows DPAPI is only available on Windows.");
        }

        EnsureCapabilityAvailable();

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var protectedBytes = ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Task.FromResult(Convert.ToBase64String(protectedBytes));
    }

    public Task<string> UnprotectAsync(string protectedText, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            throw CreateUnsupportedException("Windows DPAPI is only available on Windows.");
        }

        EnsureCapabilityAvailable();

        var protectedBytes = Convert.FromBase64String(protectedText);
        var plainBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Task.FromResult(Encoding.UTF8.GetString(plainBytes));
    }

    private void EnsureCapabilityAvailable()
    {
        if (!Capability.IsUsable)
        {
            throw CreateUnsupportedException(Capability.UnsupportedReason ?? "Windows DPAPI is not available for this platform adapter.");
        }
    }

    private InvalidOperationException CreateUnsupportedException(string message) => new(message);
}

public static class SecretProtectorFactory
{
    public static ISecretProtector Create(IPlatformIntegrationService platformIntegrationService) =>
        OperatingSystem.IsWindows()
            ? new WindowsSecretProtector(platformIntegrationService)
            : new UnsupportedSecretProtector(platformIntegrationService);
}
