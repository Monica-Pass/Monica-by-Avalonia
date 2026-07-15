using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Generators;

namespace Monica.Core.ImportExport;

internal static class AegisEncryptedBackupDecryptor
{
    private const int PasswordSlotType = 1;
    private const int KeyLength = 32;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int MinimumScryptN = 2;
    private const int MaximumScryptN = 1 << 20;
    private const int MaximumScryptR = 32;
    private const int MaximumScryptP = 16;
    private const long MaximumScryptMemoryBytes = 128L * 1024 * 1024;
    private const long MaximumScryptWork = 16L * 1024 * 1024;
    private const int MaximumEncryptedDatabaseBytes = 64 * 1024 * 1024;

    internal static byte[] Decrypt(JsonElement root, string password)
    {
        try
        {
            return DecryptCore(root, password);
        }
        catch (AegisImportException)
        {
            throw;
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or OverflowException)
        {
            throw InvalidFormat();
        }
    }

    private static byte[] DecryptCore(JsonElement root, string password)
    {
        var header = RequireObject(root, "header");
        var slots = RequireArray(header, "slots");
        if (slots.GetArrayLength() == 0)
        {
            throw InvalidFormat();
        }

        var slot = slots.EnumerateArray().FirstOrDefault(IsPasswordSlot);
        if (slot.ValueKind == JsonValueKind.Undefined)
        {
            throw new AegisImportException(
                AegisImportFailureReason.UnsupportedKeySlot,
                "This encrypted Aegis backup does not contain a supported password slot.");
        }

        var n = RequireInt32(slot, "n");
        var r = RequireInt32(slot, "r");
        var p = RequireInt32(slot, "p");
        ValidateScryptParameters(n, r, p);

        var salt = DecodeHex(RequireString(slot, "salt"), minimumLength: 16, maximumLength: 64);
        var wrappedKey = DecodeHex(RequireString(slot, "key"), KeyLength, KeyLength);
        var keyParams = RequireObject(slot, "key_params");
        var keyNonce = DecodeHex(RequireString(keyParams, "nonce"), NonceLength, NonceLength);
        var keyTag = DecodeHex(RequireString(keyParams, "tag"), TagLength, TagLength);
        var databaseParams = RequireObject(header, "params");
        var databaseNonce = DecodeHex(RequireString(databaseParams, "nonce"), NonceLength, NonceLength);
        var databaseTag = DecodeHex(RequireString(databaseParams, "tag"), TagLength, TagLength);
        var encryptedDatabase = DecodeBase64(RequireString(root, "db"));
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[]? derivedKey = null;
        byte[]? masterKey = null;

        try
        {
            derivedKey = SCrypt.Generate(passwordBytes, salt, n, r, p, KeyLength);
            masterKey = DecryptAesGcm(derivedKey, keyNonce, wrappedKey, keyTag);
            if (masterKey.Length != KeyLength)
            {
                throw DecryptionFailed();
            }

            return DecryptAesGcm(masterKey, databaseNonce, encryptedDatabase, databaseTag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(salt);
            if (derivedKey is not null)
            {
                CryptographicOperations.ZeroMemory(derivedKey);
            }

            if (masterKey is not null)
            {
                CryptographicOperations.ZeroMemory(masterKey);
            }
        }
    }

    private static byte[] DecryptAesGcm(byte[] key, byte[] nonce, byte[] ciphertext, byte[] tag)
    {
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw DecryptionFailed();
        }
    }

    private static bool IsPasswordSlot(JsonElement slot) =>
        slot.ValueKind == JsonValueKind.Object &&
        slot.TryGetProperty("type", out var type) &&
        type.TryGetInt32(out var value) &&
        value == PasswordSlotType;

    private static void ValidateScryptParameters(int n, int r, int p)
    {
        var isPowerOfTwo = n > 0 && (n & (n - 1)) == 0;
        if (!isPowerOfTwo || n < MinimumScryptN || n > MaximumScryptN || r is < 1 or > MaximumScryptR || p is < 1 or > MaximumScryptP)
        {
            throw UnsafeParameters();
        }

        var estimatedMemory = checked(128L * n * r);
        var estimatedWork = checked((long)n * r * p);
        if (estimatedMemory > MaximumScryptMemoryBytes || estimatedWork > MaximumScryptWork)
        {
            throw UnsafeParameters();
        }
    }

    private static JsonElement RequireObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw InvalidFormat();
        }

        return value;
    }

    private static JsonElement RequireArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw InvalidFormat();
        }

        return value;
    }

    private static string RequireString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw InvalidFormat();
        }

        return value.GetString() ?? throw InvalidFormat();
    }

    private static int RequireInt32(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result))
        {
            throw InvalidFormat();
        }

        return result;
    }

    private static byte[] DecodeHex(string value, int minimumLength, int maximumLength)
    {
        if ((value.Length & 1) != 0 || value.Length < minimumLength * 2 || value.Length > maximumLength * 2)
        {
            throw InvalidFormat();
        }

        try
        {
            return Convert.FromHexString(value);
        }
        catch (FormatException)
        {
            throw InvalidFormat();
        }
    }

    private static byte[] DecodeBase64(string value)
    {
        if (value.Length == 0 || value.Length > ((MaximumEncryptedDatabaseBytes + 2L) / 3L * 4L))
        {
            throw InvalidFormat();
        }

        try
        {
            var decoded = Convert.FromBase64String(value);
            if (decoded.Length == 0 || decoded.Length > MaximumEncryptedDatabaseBytes)
            {
                throw InvalidFormat();
            }

            return decoded;
        }
        catch (FormatException)
        {
            throw InvalidFormat();
        }
    }

    private static AegisImportException InvalidFormat() =>
        new(AegisImportFailureReason.InvalidFormat, "The Aegis backup format is invalid.");

    private static AegisImportException DecryptionFailed() =>
        new(AegisImportFailureReason.DecryptionFailed, "The Aegis backup password is incorrect or the file is damaged.");

    private static AegisImportException UnsafeParameters() =>
        new(AegisImportFailureReason.UnsafeKeyDerivationParameters, "The Aegis backup uses unsafe key-derivation parameters.");
}
