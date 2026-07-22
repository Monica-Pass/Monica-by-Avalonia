using System.Text;

namespace Monica.Core.Bitwarden;

public enum BitwardenKdfAlgorithm
{
    Pbkdf2Sha256 = 0,
    Argon2id = 1
}

public sealed record BitwardenKdfParameters(
    BitwardenKdfAlgorithm Algorithm,
    int Iterations,
    int? MemoryMb = null,
    int? Parallelism = null)
{
    public static BitwardenKdfParameters Pbkdf2(int iterations = 600_000) =>
        new(BitwardenKdfAlgorithm.Pbkdf2Sha256, iterations);

    public static BitwardenKdfParameters Argon2id(
        int iterations = 3,
        int memoryMb = 64,
        int parallelism = 4) =>
        new(BitwardenKdfAlgorithm.Argon2id, iterations, memoryMb, parallelism);
}

public static class BitwardenKdfPolicy
{
    public const int MaximumPbkdf2Iterations = 2_000_000;
    public const int MaximumArgon2Iterations = 10;
    public const int MaximumArgon2MemoryMb = 256;
    public const int MaximumArgon2Parallelism = 16;
    public const int MaximumEmailUtf8Bytes = 320;
    public const int MaximumPasswordUtf8Bytes = 4096;

    public static void Validate(BitwardenKdfParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        switch (parameters.Algorithm)
        {
            case BitwardenKdfAlgorithm.Pbkdf2Sha256:
                RequireRange(parameters.Iterations, 1, MaximumPbkdf2Iterations, "PBKDF2 iterations");
                if (parameters.MemoryMb is not null || parameters.Parallelism is not null)
                {
                    throw new BitwardenProtocolException("PBKDF2 parameters must not include Argon2 settings.");
                }

                break;

            case BitwardenKdfAlgorithm.Argon2id:
                RequireRange(parameters.Iterations, 1, MaximumArgon2Iterations, "Argon2 iterations");
                RequireRange(parameters.MemoryMb, 1, MaximumArgon2MemoryMb, "Argon2 memory");
                RequireRange(parameters.Parallelism, 1, MaximumArgon2Parallelism, "Argon2 parallelism");
                break;

            default:
                throw new BitwardenProtocolException($"Unsupported Bitwarden KDF type: {(int)parameters.Algorithm}.");
        }
    }

    public static string CanonicalizeEmail(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        var canonical = email.Trim().ToLowerInvariant();
        if (canonical.Length == 0 || Encoding.UTF8.GetByteCount(canonical) > MaximumEmailUtf8Bytes)
        {
            throw new BitwardenProtocolException("Bitwarden email is empty or exceeds the supported length.");
        }

        return canonical;
    }

    public static void ValidatePassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var byteCount = Encoding.UTF8.GetByteCount(password);
        if (byteCount == 0 || byteCount > MaximumPasswordUtf8Bytes)
        {
            throw new BitwardenProtocolException("Bitwarden master password is empty or exceeds the supported length.");
        }
    }

    private static void RequireRange(int? value, int minimum, int maximum, string name)
    {
        if (value is null || value < minimum || value > maximum)
        {
            throw new BitwardenProtocolException(
                $"Bitwarden {name} must be between {minimum} and {maximum}.");
        }
    }
}
