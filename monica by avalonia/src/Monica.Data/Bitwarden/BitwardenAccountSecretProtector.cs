using Monica.Core.Services;

namespace Monica.Data.Bitwarden;

internal sealed class BitwardenAccountSecretProtector(ICryptoService cryptoService)
{
    private const string ProtectedPrefix = "vault:v1:";

    public string ProtectString(string value)
    {
        EnsureUnlocked();
        return ProtectedPrefix + cryptoService.EncryptString(value);
    }

    public string? ProtectNullableString(string? value) =>
        value is null ? null : ProtectString(value);

    public string ProtectBytes(ReadOnlySpan<byte> value)
    {
        EnsureUnlocked();
        return ProtectedPrefix + cryptoService.EncryptBytes(value);
    }

    public string UnprotectString(string value)
    {
        EnsureProtected(value);
        EnsureUnlocked();
        return cryptoService.DecryptString(value[ProtectedPrefix.Length..]);
    }

    public string? UnprotectNullableString(string? value) =>
        value is null ? null : UnprotectString(value);

    public byte[] UnprotectBytes(string value)
    {
        EnsureProtected(value);
        EnsureUnlocked();
        return cryptoService.DecryptBytes(value[ProtectedPrefix.Length..]);
    }

    public byte[]? UnprotectNullableBytes(string? value) =>
        value is null ? null : UnprotectBytes(value);

    private void EnsureUnlocked()
    {
        if (!cryptoService.IsUnlocked)
        {
            throw new InvalidOperationException("The Monica vault must be unlocked to access Bitwarden account data.");
        }
    }

    private static void EnsureProtected(string value)
    {
        if (!value.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
        {
            throw new System.Security.Cryptography.CryptographicException(
                "Bitwarden account data is not protected by the Monica vault.");
        }
    }
}
