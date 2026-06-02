using Monica.Core.Models;

namespace Monica.Data.Mdbx;

public sealed class UnavailableMdbxNativeBridge : IMdbxNativeBridge
{
    public bool IsAvailable => false;

    public Task<IMdbxNativeVault> CreateVaultAsync(string path, string password, string deviceId, MdbxTigaMode mode, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("MDBX UniFFI native bridge is not available.");

    public Task<IMdbxNativeVault> OpenVaultAsync(string path, string password, string deviceId, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("MDBX UniFFI native bridge is not available.");
}
