using System.Security.Cryptography;
using System.Text;

namespace Monica.Core.Bitwarden;

public enum BitwardenTlsMode
{
    SystemTrust = 0,
    SystemAndCustomCertificate = 1,
    MutualTls = 2
}

public sealed record BitwardenTlsOptions(
    BitwardenTlsMode Mode = BitwardenTlsMode.SystemTrust,
    string? CustomCaCertificatePath = null,
    string? ClientCertificatePath = null);

public sealed record BitwardenAccount
{
    public long Id { get; init; }
    public required string Email { get; init; }
    public string? UserId { get; init; }
    public string? DisplayName { get; init; }
    public required string AccountKey { get; init; }
    public required BitwardenEndpointSet Endpoints { get; init; }
    public required BitwardenKdfParameters Kdf { get; init; }
    public BitwardenTlsOptions Tls { get; init; } = new();
    public DateTimeOffset? AccessTokenExpiresAt { get; init; }
    public DateTimeOffset? LastSyncAt { get; init; }
    public DateTimeOffset? LastFullSyncAt { get; init; }
    public string? RevisionDate { get; init; }
    public string LastSyncStatus { get; init; } = "never";
    public string? LastSyncError { get; init; }
    public bool IsDefault { get; init; }
    public bool IsConnected { get; init; }
    public bool SyncEnabled { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public static class BitwardenAccountIdentity
{
    private const string AccountKeyPrefix = "bw:v1:";

    public static string CreateAccountKey(string email, BitwardenEndpointSet endpoints)
    {
        var canonicalEmail = BitwardenKdfPolicy.CanonicalizeEmail(email);
        var validatedEndpoints = BitwardenEndpointPolicy.Validate(endpoints);
        return AccountKeyPrefix + HashUtf8($"{canonicalEmail}\n{validatedEndpoints.WebVault.AbsoluteUri}");
    }

    public static string CreateEmailLookupHash(string email) =>
        HashUtf8(BitwardenKdfPolicy.CanonicalizeEmail(email));

    private static string HashUtf8(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            return Convert.ToHexStringLower(SHA256.HashData(bytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
