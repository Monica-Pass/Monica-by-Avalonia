using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private const int WebDavBackupPackageVersion = 1;
    private const string WebDavBackupKdf = "pbkdf2-sha256";
    private const int WebDavBackupKdfIterations = 300_000;
    private const int WebDavBackupSaltSize = 16;
    private const int WebDavBackupNonceSize = 12;
    private const int WebDavBackupTagSize = 16;
    private const int WebDavBackupKeySize = 32;

    private sealed record WebDavEncryptedBackupPackage(
        int Version,
        string Kdf,
        int Iterations,
        string Salt,
        string Nonce,
        string Tag,
        string CipherText);

    private bool HasSelectedWebDavBackupOptions() =>
        WebDavBackupIncludePasswords ||
        WebDavBackupIncludeTotp ||
        WebDavBackupIncludeNotes ||
        WebDavBackupIncludeCards ||
        WebDavBackupIncludeDocuments ||
        WebDavBackupIncludeImages ||
        WebDavBackupIncludeCategories;

    private int CountSelectedWebDavBackupOptions() =>
        (WebDavBackupIncludePasswords ? 1 : 0) +
        (WebDavBackupIncludeTotp ? 1 : 0) +
        (WebDavBackupIncludeNotes ? 1 : 0) +
        (WebDavBackupIncludeCards ? 1 : 0) +
        (WebDavBackupIncludeDocuments ? 1 : 0) +
        (WebDavBackupIncludeImages ? 1 : 0) +
        (WebDavBackupIncludeCategories ? 1 : 0);

    private static bool IsEncryptedWebDavBackup(string fileName) =>
        fileName.EndsWith(".enc.json", StringComparison.OrdinalIgnoreCase);

    private static string EncryptWebDavBackupPayload(string json, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(WebDavBackupSaltSize);
        var nonce = RandomNumberGenerator.GetBytes(WebDavBackupNonceSize);
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[]? plainBytes = null;
        byte[]? key = null;

        try
        {
            plainBytes = Encoding.UTF8.GetBytes(json);
            var cipherBytes = new byte[plainBytes.Length];
            var tag = new byte[WebDavBackupTagSize];
            key = Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes,
                salt,
                WebDavBackupKdfIterations,
                HashAlgorithmName.SHA256,
                WebDavBackupKeySize);
            using var aes = new AesGcm(key, WebDavBackupTagSize);
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

            return JsonSerializer.Serialize(new WebDavEncryptedBackupPackage(
                WebDavBackupPackageVersion,
                WebDavBackupKdf,
                WebDavBackupKdfIterations,
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

    private static string DecryptWebDavBackupPayload(string content, string password)
    {
        var package = JsonSerializer.Deserialize<WebDavEncryptedBackupPackage>(content)
            ?? throw new InvalidOperationException("Invalid encrypted Monica backup payload.");
        if (package.Version != WebDavBackupPackageVersion)
        {
            throw new InvalidOperationException("Unsupported Monica backup encryption version.");
        }

        if (!string.Equals(package.Kdf, WebDavBackupKdf, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unsupported Monica backup encryption KDF.");
        }

        if (package.Iterations != WebDavBackupKdfIterations)
        {
            throw new InvalidOperationException("Unsupported Monica backup encryption iterations.");
        }

        var salt = DecodeFixedLengthBase64(package.Salt, WebDavBackupSaltSize);
        var nonce = DecodeFixedLengthBase64(package.Nonce, WebDavBackupNonceSize);
        var tag = DecodeFixedLengthBase64(package.Tag, WebDavBackupTagSize);
        byte[] cipherBytes;
        try
        {
            cipherBytes = Convert.FromBase64String(package.CipherText);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Invalid Monica backup ciphertext.", ex);
        }

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[]? plainBytes = null;
        byte[]? key = null;

        try
        {
            plainBytes = new byte[cipherBytes.Length];
            key = Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes,
                salt,
                WebDavBackupKdfIterations,
                HashAlgorithmName.SHA256,
                WebDavBackupKeySize);
            using var aes = new AesGcm(key, WebDavBackupTagSize);
            aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
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
            throw new InvalidOperationException("Invalid Monica backup encryption parameters.");
        }

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(value);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Invalid Monica backup encryption parameters.", ex);
        }

        if (decoded.Length != expectedByteLength)
        {
            throw new InvalidOperationException("Invalid Monica backup encryption parameters.");
        }

        return decoded;
    }
}
