using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Monica.App.Services;

public interface IWebDavBackupCryptoService
{
    Task<string> EncryptAsync(
        string json,
        string password,
        CancellationToken cancellationToken = default);

    Task<string> DecryptAsync(
        string content,
        string password,
        CancellationToken cancellationToken = default);
}

public enum WebDavBackupCryptoFailureReason
{
    InvalidPayload,
    UnsupportedVersion,
    UnsupportedKdf,
    UnsupportedIterations,
    InvalidParameters,
    InvalidCiphertext,
    DecryptionFailed
}

public sealed class WebDavBackupCryptoException(
    WebDavBackupCryptoFailureReason reason,
    string message,
    Exception? innerException = null) : InvalidOperationException(message, innerException)
{
    public WebDavBackupCryptoFailureReason Reason { get; } = reason;
}

public sealed class WebDavBackupCryptoService : IWebDavBackupCryptoService
{
    private const int PackageVersion = 1;
    private const string Kdf = "pbkdf2-sha256";
    private const int KdfIterations = 300_000;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    public Task<string> EncryptAsync(
        string json,
        string password,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Encrypt(json, password, cancellationToken), cancellationToken);

    public Task<string> DecryptAsync(
        string content,
        string password,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Decrypt(content, password, cancellationToken), cancellationToken);

    private static string Encrypt(string json, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[]? plainBytes = null;
        byte[]? key = null;

        try
        {
            plainBytes = Encoding.UTF8.GetBytes(json);
            var cipherBytes = new byte[plainBytes.Length];
            var tag = new byte[TagSize];
            key = Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes,
                salt,
                KdfIterations,
                HashAlgorithmName.SHA256,
                KeySize);
            cancellationToken.ThrowIfCancellationRequested();
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);
            cancellationToken.ThrowIfCancellationRequested();

            return JsonSerializer.Serialize(new EncryptedBackupPackage(
                PackageVersion,
                Kdf,
                KdfIterations,
                Convert.ToBase64String(salt),
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(tag),
                Convert.ToBase64String(cipherBytes)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            if (plainBytes is not null)
            {
                CryptographicOperations.ZeroMemory(plainBytes);
            }

            if (key is not null)
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
    }

    private static string Decrypt(string content, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EncryptedBackupPackage? package;
        try
        {
            package = JsonSerializer.Deserialize<EncryptedBackupPackage>(content);
        }
        catch (JsonException ex)
        {
            throw new WebDavBackupCryptoException(
                WebDavBackupCryptoFailureReason.InvalidPayload,
                "Invalid encrypted Monica backup payload.",
                ex);
        }

        if (package is null)
        {
            throw new WebDavBackupCryptoException(
                WebDavBackupCryptoFailureReason.InvalidPayload,
                "Invalid encrypted Monica backup payload.");
        }
        if (package.Version != PackageVersion)
        {
            throw new WebDavBackupCryptoException(
                WebDavBackupCryptoFailureReason.UnsupportedVersion,
                "Unsupported Monica backup encryption version.");
        }

        if (!string.Equals(package.Kdf, Kdf, StringComparison.Ordinal))
        {
            throw new WebDavBackupCryptoException(
                WebDavBackupCryptoFailureReason.UnsupportedKdf,
                "Unsupported Monica backup encryption KDF.");
        }

        if (package.Iterations != KdfIterations)
        {
            throw new WebDavBackupCryptoException(
                WebDavBackupCryptoFailureReason.UnsupportedIterations,
                "Unsupported Monica backup encryption iterations.");
        }

        var salt = DecodeFixedLengthBase64(package.Salt, SaltSize);
        var nonce = DecodeFixedLengthBase64(package.Nonce, NonceSize);
        var tag = DecodeFixedLengthBase64(package.Tag, TagSize);
        byte[] cipherBytes;
        try
        {
            cipherBytes = Convert.FromBase64String(package.CipherText);
        }
        catch (FormatException ex)
        {
            throw new WebDavBackupCryptoException(
                WebDavBackupCryptoFailureReason.InvalidCiphertext,
                "Invalid Monica backup ciphertext.",
                ex);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[]? plainBytes = null;
        byte[]? key = null;

        try
        {
            plainBytes = new byte[cipherBytes.Length];
            key = Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes,
                salt,
                KdfIterations,
                HashAlgorithmName.SHA256,
                KeySize);
            cancellationToken.ThrowIfCancellationRequested();
            using var aes = new AesGcm(key, TagSize);
            try
            {
                aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
            }
            catch (CryptographicException ex)
            {
                throw new WebDavBackupCryptoException(
                    WebDavBackupCryptoFailureReason.DecryptionFailed,
                    "Could not decrypt the Monica backup.",
                    ex);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return Encoding.UTF8.GetString(plainBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            if (plainBytes is not null)
            {
                CryptographicOperations.ZeroMemory(plainBytes);
            }

            if (key is not null)
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
    }

    private static byte[] DecodeFixedLengthBase64(string? value, int expectedByteLength)
    {
        var expectedEncodedLength = ((expectedByteLength + 2) / 3) * 4;
        if (value is null || value.Length != expectedEncodedLength)
        {
            throw new WebDavBackupCryptoException(
                WebDavBackupCryptoFailureReason.InvalidParameters,
                "Invalid Monica backup encryption parameters.");
        }

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(value);
        }
        catch (FormatException ex)
        {
            throw new WebDavBackupCryptoException(
                WebDavBackupCryptoFailureReason.InvalidParameters,
                "Invalid Monica backup encryption parameters.",
                ex);
        }

        if (decoded.Length != expectedByteLength)
        {
            throw new WebDavBackupCryptoException(
                WebDavBackupCryptoFailureReason.InvalidParameters,
                "Invalid Monica backup encryption parameters.");
        }

        return decoded;
    }

    private sealed record EncryptedBackupPackage(
        int Version,
        string Kdf,
        int Iterations,
        string Salt,
        string Nonce,
        string Tag,
        string CipherText);
}
