using System.Security.Cryptography;
using System.Text;
using Monica.Core.Bitwarden;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Bitwarden;

namespace Monica.Tests;

public sealed class BitwardenAccountStoreTests
{
    [Fact]
    public async Task ConnectedAccountIsAeadProtectedAndRoundTrips()
    {
        var path = TestTempPaths.CreateFilePath(".db");
        var factory = new SqliteConnectionFactory(path);
        var migrator = new DatabaseMigrator(factory);
        var crypto = CreateUnlockedCrypto();
        var store = new BitwardenAccountStore(factory, migrator, crypto);
        var endpoints = BitwardenEndpointSet.UnitedStates;
        var account = new BitwardenAccount
        {
            Email = " Alice@Example.com ",
            UserId = "remote-user-id",
            DisplayName = "Alice",
            AccountKey = BitwardenAccountIdentity.CreateAccountKey("alice@example.com", endpoints),
            Endpoints = endpoints,
            Kdf = BitwardenKdfParameters.Pbkdf2(),
            Tls = new BitwardenTlsOptions(
                BitwardenTlsMode.MutualTls,
                @"C:\certs\ca.pem",
                @"C:\certs\client.pfx"),
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            LastSyncStatus = "failed",
            LastSyncError = "private server detail",
            IsDefault = true
        };
        using var secrets = CreateSecrets("access-token", "refresh-token", "certificate-password");

        var saved = await store.SaveConnectedAsync(account, secrets);

        Assert.True(saved.Id > 0);
        Assert.Equal("alice@example.com", saved.Email);
        var raw = await ReadRawAccountAsync(factory, saved.Id);
        Assert.All(raw.ProtectedValues, value => Assert.StartsWith("vault:v1:", value));
        Assert.DoesNotContain("alice@example.com", raw.Joined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access-token", raw.Joined, StringComparison.Ordinal);
        Assert.DoesNotContain("private server detail", raw.Joined, StringComparison.Ordinal);
        Assert.Equal(64, raw.CanonicalEmail.Length);
        Assert.StartsWith("bw:v1:", raw.AccountKey);

        var loaded = Assert.Single(await store.GetAllAsync());
        Assert.Equal("alice@example.com", loaded.Email);
        Assert.Equal("Alice", loaded.DisplayName);
        Assert.Equal("private server detail", loaded.LastSyncError);
        Assert.Equal(BitwardenTlsMode.MutualTls, loaded.Tls.Mode);
        Assert.Equal(@"C:\certs\client.pfx", loaded.Tls.ClientCertificatePath);

        using var loadedSecrets = Assert.IsType<BitwardenAccountSecrets>(await store.LoadSecretsAsync(saved.Id));
        Assert.Equal("access-token", ReadUtf8AndZero(loadedSecrets.CopyAccessToken()));
        Assert.Equal("refresh-token", ReadUtf8AndZero(loadedSecrets.CopyRefreshToken()));
        Assert.Equal("certificate-password", ReadUtf8AndZero(loadedSecrets.CopyClientCertificatePassword()!));

        crypto.Lock();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetAllAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.LoadSecretsAsync(saved.Id));
    }

    [Fact]
    public async Task DisconnectRemovesEveryPersistedCredential()
    {
        var factory = new SqliteConnectionFactory(TestTempPaths.CreateFilePath(".db"));
        var crypto = CreateUnlockedCrypto();
        var store = new BitwardenAccountStore(factory, new DatabaseMigrator(factory), crypto);
        var account = CreateAccount();
        using var secrets = CreateSecrets("access", "refresh");
        var saved = await store.SaveConnectedAsync(account, secrets);

        await store.DisconnectAsync(saved.Id);

        Assert.Null(await store.LoadSecretsAsync(saved.Id));
        var loaded = Assert.Single(await store.GetAllAsync());
        Assert.False(loaded.IsConnected);
    }

    [Fact]
    public void VaultLockClearsManagerAndEveryOutstandingLease()
    {
        using var vaultSession = new VaultSessionService();
        vaultSession.MarkUnlocked();
        using var manager = new BitwardenSessionManager(vaultSession);
        using var sourceSecrets = CreateSecrets("access", "refresh");
        manager.Open(7, sourceSecrets, DateTimeOffset.UtcNow.AddMinutes(5));
        sourceSecrets.Dispose();

        Assert.True(manager.TryCreateLease(7, out var lease));
        Assert.NotNull(lease);
        Assert.Equal("access", ReadUtf8AndZero(lease.Secrets.CopyAccessToken()));

        vaultSession.MarkLocked();

        Assert.False(manager.HasSession(7));
        Assert.Throws<ObjectDisposedException>(() => _ = lease.Secrets);
        using var lockedSecrets = CreateSecrets("new-access", "new-refresh");
        Assert.Throws<InvalidOperationException>(() => manager.Open(7, lockedSecrets, null));
    }

    [Fact]
    public async Task AccountPolicyRejectsUnboundedMetadataAndMisplacedCertificateSecrets()
    {
        var factory = new SqliteConnectionFactory(TestTempPaths.CreateFilePath(".db"));
        var store = new BitwardenAccountStore(factory, new DatabaseMigrator(factory), CreateUnlockedCrypto());
        var account = CreateAccount();
        using var certificateSecrets = CreateSecrets("access", "refresh", "certificate-password");
        await Assert.ThrowsAsync<BitwardenProtocolException>(() =>
            store.SaveConnectedAsync(account, certificateSecrets));

        using var normalSecrets = CreateSecrets("access", "refresh");
        await Assert.ThrowsAsync<BitwardenProtocolException>(() =>
            store.SaveConnectedAsync(account with { DisplayName = new string('x', 4097) }, normalSecrets));
    }

    private static CryptoService CreateUnlockedCrypto()
    {
        var crypto = new CryptoService();
        var hash = crypto.HashMasterPassword("local vault password");
        crypto.InitializeSession("local vault password", hash.Salt);
        return crypto;
    }

    private static BitwardenAccount CreateAccount()
    {
        var endpoints = BitwardenEndpointSet.Europe;
        return new BitwardenAccount
        {
            Email = "sync@example.com",
            AccountKey = BitwardenAccountIdentity.CreateAccountKey("sync@example.com", endpoints),
            Endpoints = endpoints,
            Kdf = BitwardenKdfParameters.Argon2id(),
            IsConnected = true
        };
    }

    private static BitwardenAccountSecrets CreateSecrets(
        string accessToken,
        string refreshToken,
        string? certificatePassword = null) => new(
        Encoding.UTF8.GetBytes(accessToken),
        Encoding.UTF8.GetBytes(refreshToken),
        Enumerable.Range(0, 32).Select(value => (byte)value).ToArray(),
        Enumerable.Range(32, 32).Select(value => (byte)value).ToArray(),
        Enumerable.Range(64, 32).Select(value => (byte)value).ToArray(),
        certificatePassword is null ? [] : Encoding.UTF8.GetBytes(certificatePassword));

    private static string ReadUtf8AndZero(byte[] value)
    {
        try
        {
            return Encoding.UTF8.GetString(value);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(value);
        }
    }

    private static async Task<RawAccount> ReadRawAccountAsync(
        ISqliteConnectionFactory factory,
        long accountId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT email, user_id, display_name, canonical_email, account_key,
                   encrypted_access_token, encrypted_refresh_token, encrypted_master_key,
                   encrypted_enc_key, encrypted_mac_key, last_sync_error,
                   custom_ca_certificate_path, client_certificate_path,
                   encrypted_client_certificate_password
            FROM bitwarden_vaults WHERE id = $id
            """;
        command.Parameters.AddWithValue("$id", accountId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var protectedValues = new List<string>();
        for (var index = 0; index <= 2; index++)
        {
            protectedValues.Add(reader.GetString(index));
        }

        for (var index = 5; index <= 13; index++)
        {
            protectedValues.Add(reader.GetString(index));
        }

        return new RawAccount(
            reader.GetString(3),
            reader.GetString(4),
            protectedValues,
            string.Join('|', Enumerable.Range(0, reader.FieldCount).Select(reader.GetString)));
    }

    private sealed record RawAccount(
        string CanonicalEmail,
        string AccountKey,
        IReadOnlyList<string> ProtectedValues,
        string Joined);
}
