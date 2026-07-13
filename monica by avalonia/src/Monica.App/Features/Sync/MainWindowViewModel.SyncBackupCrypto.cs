using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
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
        const int saltSize = 16;
        const int nonceSize = 12;
        const int tagSize = 16;
        const int iterations = 300_000;

        var salt = RandomNumberGenerator.GetBytes(saltSize);
        var nonce = RandomNumberGenerator.GetBytes(nonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(json);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[tagSize];
        var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);

        using var aes = new AesGcm(key, tagSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        return JsonSerializer.Serialize(new WebDavEncryptedBackupPackage(
            1,
            "pbkdf2-sha256",
            iterations,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag),
            Convert.ToBase64String(cipherBytes)));
    }

    private static string DecryptWebDavBackupPayload(string content, string password)
    {
        const int tagSize = 16;
        var package = JsonSerializer.Deserialize<WebDavEncryptedBackupPackage>(content)
            ?? throw new InvalidOperationException("Invalid encrypted Monica backup payload.");
        if (!string.Equals(package.Kdf, "pbkdf2-sha256", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Unsupported Monica backup encryption KDF.");
        }

        var salt = Convert.FromBase64String(package.Salt);
        var nonce = Convert.FromBase64String(package.Nonce);
        var tag = Convert.FromBase64String(package.Tag);
        var cipherBytes = Convert.FromBase64String(package.CipherText);
        var plainBytes = new byte[cipherBytes.Length];
        var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, package.Iterations, HashAlgorithmName.SHA256, 32);

        using var aes = new AesGcm(key, tagSize);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
