using System.Security.Cryptography;
using System.Text;

namespace Monica.Core.ImportExport;

/// <summary>
/// Reads the encrypted archive written by Monica Android:
/// [MONICA_ENC_V1][32-byte salt][12-byte nonce][AES-256-GCM ciphertext][16-byte tag].
/// </summary>
internal static class MonicaEncryptedArchive
{
    private const int SaltLength = 32;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int KeyLength = 32;
    private const int Iterations = 100_000;
    private const long MaximumArchiveBytes = 256L * 1024 * 1024;

    private static ReadOnlySpan<byte> Magic => "MONICA_ENC_V1"u8;
    private static int HeaderLength => Magic.Length + SaltLength + NonceLength;

    internal static bool IsEncrypted(ReadOnlySpan<byte> archive) => archive.StartsWith(Magic);

    internal static byte[] Decrypt(ReadOnlyMemory<byte> archive, string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new MonicaJsonImportException(MonicaJsonImportError.PasswordRequired,
                "The Monica backup is encrypted and requires its export password.");
        }

        var source = archive.Span;
        if (source.Length < HeaderLength + TagLength || source.Length > MaximumArchiveBytes)
        {
            throw new MonicaJsonImportException(MonicaJsonImportError.DecryptionFailed,
                "The encrypted Monica backup is truncated or too large to decrypt.");
        }

        var salt = source.Slice(Magic.Length, SaltLength).ToArray();
        var nonce = source.Slice(Magic.Length + SaltLength, NonceLength);
        var tag = source.Slice(source.Length - TagLength, TagLength);
        var ciphertext = source.Slice(HeaderLength, source.Length - HeaderLength - TagLength);
        var plaintext = new byte[ciphertext.Length];
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[]? key = null;

        try
        {
            key = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, Iterations, HashAlgorithmName.SHA256, KeyLength);
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new MonicaJsonImportException(MonicaJsonImportError.DecryptionFailed,
                "The Monica backup password is incorrect or the archive is damaged.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(salt);
            if (key is not null)
                CryptographicOperations.ZeroMemory(key);
        }
    }
}
