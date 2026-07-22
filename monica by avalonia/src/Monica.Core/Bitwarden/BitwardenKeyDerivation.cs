using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Monica.Core.Bitwarden;

public static class BitwardenKeyDerivation
{
    private const int MasterKeySize = 32;
    private static readonly byte[] EncryptionInfo = "enc"u8.ToArray();
    private static readonly byte[] MacInfo = "mac"u8.ToArray();

    public static byte[] DeriveMasterKey(
        string password,
        string email,
        BitwardenKdfParameters parameters)
    {
        BitwardenKdfPolicy.Validate(parameters);
        BitwardenKdfPolicy.ValidatePassword(password);
        var canonicalEmail = BitwardenKdfPolicy.CanonicalizeEmail(email);

        return parameters.Algorithm switch
        {
            BitwardenKdfAlgorithm.Pbkdf2Sha256 =>
                DerivePbkdf2(password, canonicalEmail, parameters.Iterations),
            BitwardenKdfAlgorithm.Argon2id =>
                DeriveArgon2id(
                    password,
                    canonicalEmail,
                    parameters.Iterations,
                    parameters.MemoryMb!.Value,
                    parameters.Parallelism!.Value),
            _ => throw new BitwardenProtocolException(
                $"Unsupported Bitwarden KDF type: {(int)parameters.Algorithm}.")
        };
    }

    public static string DeriveMasterPasswordHash(ReadOnlySpan<byte> masterKey, string password)
    {
        if (masterKey.Length != MasterKeySize)
        {
            throw new ArgumentException("Bitwarden master key must be 32 bytes.", nameof(masterKey));
        }

        BitwardenKdfPolicy.ValidatePassword(password);
        var masterKeyBytes = masterKey.ToArray();
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[]? hash = null;
        try
        {
            hash = Rfc2898DeriveBytes.Pbkdf2(
                masterKeyBytes,
                passwordBytes,
                1,
                HashAlgorithmName.SHA256,
                MasterKeySize);
            return Convert.ToBase64String(hash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKeyBytes);
            CryptographicOperations.ZeroMemory(passwordBytes);
            if (hash is not null)
            {
                CryptographicOperations.ZeroMemory(hash);
            }
        }
    }

    public static BitwardenSymmetricKey StretchMasterKey(ReadOnlySpan<byte> masterKey)
    {
        if (masterKey.Length != MasterKeySize)
        {
            throw new ArgumentException("Bitwarden master key must be 32 bytes.", nameof(masterKey));
        }

        var encryptionKey = HkdfExpandSingleBlock(masterKey, EncryptionInfo);
        var macKey = HkdfExpandSingleBlock(masterKey, MacInfo);
        try
        {
            return new BitwardenSymmetricKey(encryptionKey, macKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptionKey);
            CryptographicOperations.ZeroMemory(macKey);
        }
    }

    private static byte[] DerivePbkdf2(string password, string canonicalEmail, int iterations)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var saltBytes = Encoding.UTF8.GetBytes(canonicalEmail);
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes,
                saltBytes,
                iterations,
                HashAlgorithmName.SHA256,
                MasterKeySize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(saltBytes);
        }
    }

    private static byte[] DeriveArgon2id(
        string password,
        string canonicalEmail,
        int iterations,
        int memoryMb,
        int parallelism)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var emailBytes = Encoding.UTF8.GetBytes(canonicalEmail);
        var saltHash = SHA256.HashData(emailBytes);
        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = saltHash,
                Iterations = iterations,
                MemorySize = checked(memoryMb * 1024),
                DegreeOfParallelism = parallelism
            };
            return argon2.GetBytes(MasterKeySize);
        }
        catch (OverflowException exception)
        {
            throw new BitwardenProtocolException("Bitwarden Argon2 memory parameter is too large.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(emailBytes);
            CryptographicOperations.ZeroMemory(saltHash);
        }
    }

    private static byte[] HkdfExpandSingleBlock(ReadOnlySpan<byte> pseudoRandomKey, ReadOnlySpan<byte> info)
    {
        var input = new byte[info.Length + 1];
        info.CopyTo(input);
        input[^1] = 1;
        try
        {
            return HMACSHA256.HashData(pseudoRandomKey, input);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }
}
