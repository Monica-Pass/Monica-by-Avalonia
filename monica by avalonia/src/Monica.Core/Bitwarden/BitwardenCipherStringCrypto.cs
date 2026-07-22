using System.Security.Cryptography;
using System.Text;

namespace Monica.Core.Bitwarden;

public static class BitwardenCipherStringCrypto
{
    public const int CipherTypeAesCbcHmac = 2;
    public const int MaximumCipherStringLength = 1024 * 1024;
    public const int MaximumPlaintextLength = 700 * 1024;

    private const int IvSize = 16;
    private const int MacSize = 32;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string Encrypt(ReadOnlySpan<byte> plaintext, BitwardenSymmetricKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (plaintext.Length > MaximumPlaintextLength)
        {
            throw new BitwardenProtocolException("Bitwarden plaintext exceeds the supported length.");
        }

        var iv = RandomNumberGenerator.GetBytes(IvSize);
        var aesKey = key.EncryptionKey.ToArray();
        byte[]? ciphertext = null;
        byte[]? mac = null;
        try
        {
            using var aes = Aes.Create();
            aes.Key = aesKey;
            ciphertext = aes.EncryptCbc(plaintext, iv, PaddingMode.PKCS7);
            mac = ComputeMac(iv, ciphertext, key.MacKey);
            return $"{CipherTypeAesCbcHmac}.{Convert.ToBase64String(iv)}|" +
                   $"{Convert.ToBase64String(ciphertext)}|{Convert.ToBase64String(mac)}";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(iv);
            CryptographicOperations.ZeroMemory(aesKey);
            if (ciphertext is not null)
            {
                CryptographicOperations.ZeroMemory(ciphertext);
            }

            if (mac is not null)
            {
                CryptographicOperations.ZeroMemory(mac);
            }
        }
    }

    public static string EncryptString(string plaintext, BitwardenSymmetricKey key)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        if (StrictUtf8.GetByteCount(plaintext) > MaximumPlaintextLength)
        {
            throw new BitwardenProtocolException("Bitwarden plaintext exceeds the supported length.");
        }

        var bytes = StrictUtf8.GetBytes(plaintext);
        try
        {
            return Encrypt(bytes, key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public static byte[] Decrypt(string cipherString, BitwardenSymmetricKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        using var parsed = Parse(cipherString);

        var computedMac = ComputeMac(parsed.Iv, parsed.Ciphertext, key.MacKey);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(computedMac, parsed.Mac))
            {
                throw new CryptographicException("Bitwarden cipher authentication failed.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(computedMac);
        }

        var aesKey = key.EncryptionKey.ToArray();
        try
        {
            using var aes = Aes.Create();
            aes.Key = aesKey;
            return aes.DecryptCbc(parsed.Ciphertext, parsed.Iv, PaddingMode.PKCS7);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(aesKey);
        }
    }

    public static string DecryptToString(string cipherString, BitwardenSymmetricKey key)
    {
        var plaintext = Decrypt(cipherString, key);
        try
        {
            return StrictUtf8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static BitwardenSymmetricKey DecryptSymmetricKey(
        string protectedKey,
        BitwardenSymmetricKey stretchedMasterKey)
    {
        var plaintext = Decrypt(protectedKey, stretchedMasterKey);
        try
        {
            if (plaintext.Length != BitwardenSymmetricKey.CombinedSize)
            {
                throw new CryptographicException("Bitwarden protected key must contain 64 bytes.");
            }

            return new BitwardenSymmetricKey(
                plaintext.AsSpan(0, BitwardenSymmetricKey.ComponentSize),
                plaintext.AsSpan(BitwardenSymmetricKey.ComponentSize, BitwardenSymmetricKey.ComponentSize));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static ParsedCipherString Parse(string cipherString)
    {
        if (string.IsNullOrWhiteSpace(cipherString))
        {
            throw new BitwardenProtocolException("Bitwarden cipher string is empty.");
        }

        if (cipherString.Length > MaximumCipherStringLength)
        {
            throw new BitwardenProtocolException("Bitwarden cipher string exceeds the supported length.");
        }

        var separator = cipherString.IndexOf('.');
        if (separator <= 0 || !int.TryParse(cipherString.AsSpan(0, separator), out var type))
        {
            throw new BitwardenProtocolException("Bitwarden cipher string has an invalid type.");
        }

        if (type != CipherTypeAesCbcHmac)
        {
            throw new BitwardenProtocolException($"Unsupported Bitwarden cipher type: {type}.");
        }

        var parts = cipherString[(separator + 1)..].Split('|');
        if (parts.Length != 3)
        {
            throw new BitwardenProtocolException("Bitwarden Type 2 cipher must contain IV, data, and MAC.");
        }

        byte[]? iv = null;
        byte[]? ciphertext = null;
        byte[]? mac = null;
        try
        {
            iv = DecodeBase64(parts[0], "IV", IvSize);
            ciphertext = DecodeBase64(parts[1], "ciphertext", null);
            mac = DecodeBase64(parts[2], "MAC", MacSize);

            if (ciphertext.Length == 0 || ciphertext.Length % IvSize != 0 ||
                ciphertext.Length > MaximumPlaintextLength + IvSize)
            {
                throw new BitwardenProtocolException("Bitwarden ciphertext has an invalid length.");
            }

            var parsed = new ParsedCipherString(iv, ciphertext, mac);
            iv = null;
            ciphertext = null;
            mac = null;
            return parsed;
        }
        finally
        {
            ZeroIfPresent(iv);
            ZeroIfPresent(ciphertext);
            ZeroIfPresent(mac);
        }
    }

    private static byte[] DecodeBase64(string value, string partName, int? requiredLength)
    {
        if (value.Length == 0 || value.Length > MaximumCipherStringLength || value.Any(char.IsWhiteSpace))
        {
            throw new BitwardenProtocolException($"Bitwarden {partName} encoding is invalid.");
        }

        var normalized = value.Replace('-', '+').Replace('_', '/');
        if (normalized.Length % 4 == 1 || normalized.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '+' or '/' or '=')))
        {
            throw new BitwardenProtocolException($"Bitwarden {partName} encoding is invalid.");
        }

        normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(normalized);
        }
        catch (FormatException exception)
        {
            throw new BitwardenProtocolException($"Bitwarden {partName} encoding is invalid.", exception);
        }

        if (requiredLength is not null && decoded.Length != requiredLength)
        {
            CryptographicOperations.ZeroMemory(decoded);
            throw new BitwardenProtocolException(
                $"Bitwarden {partName} must be {requiredLength.Value} bytes.");
        }

        return decoded;
    }

    private static byte[] ComputeMac(
        ReadOnlySpan<byte> iv,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> macKey)
    {
        var authenticatedData = new byte[iv.Length + ciphertext.Length];
        iv.CopyTo(authenticatedData);
        ciphertext.CopyTo(authenticatedData.AsSpan(iv.Length));
        try
        {
            return HMACSHA256.HashData(macKey, authenticatedData);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(authenticatedData);
        }
    }

    private static void ZeroIfPresent(byte[]? bytes)
    {
        if (bytes is not null)
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private sealed class ParsedCipherString(byte[] iv, byte[] ciphertext, byte[] mac) : IDisposable
    {
        public byte[] Iv { get; } = iv;
        public byte[] Ciphertext { get; } = ciphertext;
        public byte[] Mac { get; } = mac;

        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(Iv);
            CryptographicOperations.ZeroMemory(Ciphertext);
            CryptographicOperations.ZeroMemory(Mac);
        }
    }
}
