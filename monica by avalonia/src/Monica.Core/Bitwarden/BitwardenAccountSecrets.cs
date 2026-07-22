using System.Security.Cryptography;

namespace Monica.Core.Bitwarden;

public sealed class BitwardenAccountSecrets : IDisposable
{
    public const int MaximumTokenLength = 64 * 1024;
    public const int MaximumCertificatePasswordLength = 4096;

    private readonly object _sync = new();
    private byte[]? _accessToken;
    private byte[]? _refreshToken;
    private byte[]? _masterKey;
    private byte[]? _encryptionKey;
    private byte[]? _macKey;
    private byte[]? _clientCertificatePassword;

    public BitwardenAccountSecrets(
        ReadOnlySpan<byte> accessToken,
        ReadOnlySpan<byte> refreshToken,
        ReadOnlySpan<byte> masterKey,
        ReadOnlySpan<byte> encryptionKey,
        ReadOnlySpan<byte> macKey,
        ReadOnlySpan<byte> clientCertificatePassword = default)
    {
        ValidateLength(accessToken, 1, MaximumTokenLength, nameof(accessToken));
        ValidateLength(refreshToken, 1, MaximumTokenLength, nameof(refreshToken));
        ValidateLength(masterKey, 32, 32, nameof(masterKey));
        ValidateLength(encryptionKey, 32, 32, nameof(encryptionKey));
        ValidateLength(macKey, 32, 32, nameof(macKey));
        ValidateLength(
            clientCertificatePassword,
            0,
            MaximumCertificatePasswordLength,
            nameof(clientCertificatePassword));

        _accessToken = accessToken.ToArray();
        _refreshToken = refreshToken.ToArray();
        _masterKey = masterKey.ToArray();
        _encryptionKey = encryptionKey.ToArray();
        _macKey = macKey.ToArray();
        _clientCertificatePassword = clientCertificatePassword.IsEmpty
            ? null
            : clientCertificatePassword.ToArray();
    }

    public bool IsDisposed
    {
        get
        {
            lock (_sync)
            {
                return _accessToken is null;
            }
        }
    }

    public bool HasClientCertificatePassword
    {
        get
        {
            lock (_sync)
            {
                _ = Required(_accessToken);
                return _clientCertificatePassword is not null;
            }
        }
    }

    public byte[] CopyAccessToken() => CopyLocked(_accessToken);
    public byte[] CopyRefreshToken() => CopyLocked(_refreshToken);
    public byte[] CopyMasterKey() => CopyLocked(_masterKey);
    public byte[] CopyEncryptionKey() => CopyLocked(_encryptionKey);
    public byte[] CopyMacKey() => CopyLocked(_macKey);

    public byte[]? CopyClientCertificatePassword()
    {
        lock (_sync)
        {
            _ = Required(_accessToken);
            return _clientCertificatePassword?.ToArray();
        }
    }

    public BitwardenAccountSecrets Clone()
    {
        lock (_sync)
        {
            return new BitwardenAccountSecrets(
                Required(_accessToken),
                Required(_refreshToken),
                Required(_masterKey),
                Required(_encryptionKey),
                Required(_macKey),
                _clientCertificatePassword ?? []);
        }
    }

    public BitwardenSymmetricKey CreateVaultKey()
    {
        lock (_sync)
        {
            return new BitwardenSymmetricKey(Required(_encryptionKey), Required(_macKey));
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            ZeroAndRelease(ref _accessToken);
            ZeroAndRelease(ref _refreshToken);
            ZeroAndRelease(ref _masterKey);
            ZeroAndRelease(ref _encryptionKey);
            ZeroAndRelease(ref _macKey);
            ZeroAndRelease(ref _clientCertificatePassword);
        }
    }

    private static ReadOnlySpan<byte> Required(byte[]? value) =>
        value ?? throw new ObjectDisposedException(nameof(BitwardenAccountSecrets));

    private byte[] CopyLocked(byte[]? value)
    {
        lock (_sync)
        {
            return Required(value).ToArray();
        }
    }

    private static void ZeroAndRelease(ref byte[]? value)
    {
        if (value is null)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(value);
        value = null;
    }

    private static void ValidateLength(
        ReadOnlySpan<byte> value,
        int minimum,
        int maximum,
        string parameterName)
    {
        if (value.Length < minimum || value.Length > maximum)
        {
            throw new ArgumentException(
                $"Bitwarden {parameterName} length must be between {minimum} and {maximum} bytes.",
                parameterName);
        }
    }
}
