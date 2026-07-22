using System.Security.Cryptography;

namespace Monica.Core.Bitwarden;

public sealed class BitwardenSymmetricKey : IDisposable
{
    public const int ComponentSize = 32;
    public const int CombinedSize = ComponentSize * 2;

    private byte[]? _encryptionKey;
    private byte[]? _macKey;

    public BitwardenSymmetricKey(ReadOnlySpan<byte> encryptionKey, ReadOnlySpan<byte> macKey)
    {
        if (encryptionKey.Length != ComponentSize || macKey.Length != ComponentSize)
        {
            throw new ArgumentException("Bitwarden encryption and MAC keys must each be 32 bytes.");
        }

        _encryptionKey = encryptionKey.ToArray();
        _macKey = macKey.ToArray();
    }

    public bool IsDisposed => _encryptionKey is null;

    internal ReadOnlySpan<byte> EncryptionKey =>
        _encryptionKey ?? throw new ObjectDisposedException(nameof(BitwardenSymmetricKey));

    internal ReadOnlySpan<byte> MacKey =>
        _macKey ?? throw new ObjectDisposedException(nameof(BitwardenSymmetricKey));

    public byte[] CopyEncryptionKey() =>
        (_encryptionKey ?? throw new ObjectDisposedException(nameof(BitwardenSymmetricKey))).ToArray();

    public byte[] CopyMacKey() =>
        (_macKey ?? throw new ObjectDisposedException(nameof(BitwardenSymmetricKey))).ToArray();

    public void Dispose()
    {
        if (_encryptionKey is not null)
        {
            CryptographicOperations.ZeroMemory(_encryptionKey);
            _encryptionKey = null;
        }

        if (_macKey is not null)
        {
            CryptographicOperations.ZeroMemory(_macKey);
            _macKey = null;
        }
    }
}
