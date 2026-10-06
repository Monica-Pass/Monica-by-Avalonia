using System.Security.Cryptography;
using System.Text;
using Monica.Core.Models;
using Monica.Core.Passkeys;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Passkeys;
using Monica.Data.Services;

namespace Monica.Tests;

public sealed class PasskeyMasterPasswordMaintenanceTests
{
    private const string OldPassword = "old passkey vault password";
    private const string NewPassword = "new passkey vault password";

    [Theory]
    [InlineData(false, PasskeyAlgorithm.Es256)]
    [InlineData(false, PasskeyAlgorithm.Rs256)]
    [InlineData(true, PasskeyAlgorithm.Es256)]
    [InlineData(true, PasskeyAlgorithm.Rs256)]
    public async Task ChangingOrResettingTheMasterPasswordKeepsSoftwareCredentialsUsableInANewSession(
        bool reset, int algorithm)
    {
        var fixture = await Fixture.CreateAsync();
        var entry = await fixture.CreateSoftwareAsync(algorithm);
        var originalReference = entry.PrivateKeyAlias;
        var privateKey = await fixture.Store.ResolvePrivateKeyAsync(entry);
        var before = Assert.Single(await fixture.ReadEncryptedKeysAsync());
        var oldHash = await fixture.Credentials.GetAsync();

        var result = reset
            ? await fixture.Maintenance.ResetMasterPasswordFromUnlockedVaultAsync(NewPassword)
            : await fixture.Maintenance.ChangeMasterPasswordAsync(OldPassword, NewPassword);

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.PasskeySecretsReencrypted);
        Assert.Equal(1, result.TotalSecretsReencrypted);
        var after = Assert.Single(await fixture.ReadEncryptedKeysAsync());
        Assert.Equal(before.Key, after.Key);
        Assert.NotEqual(before.Value, after.Value);
        Assert.False(after.Value.StartsWith("vault:v1:", StringComparison.Ordinal));
        var newHash = await fixture.Credentials.GetAsync();
        Assert.NotNull(newHash);
        fixture.Crypto.Lock();

        var newCrypto = new CryptoService();
        Assert.True(newCrypto.VerifyMasterPassword(NewPassword, newHash));
        var newStore = fixture.CreateStore(newCrypto);
        var reloaded = await newStore.GetAsync(entry.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(originalReference, reloaded.PrivateKeyAlias);
        Assert.Equal(privateKey, await newStore.ResolvePrivateKeyAsync(reloaded));
        var challenge = RandomNumberGenerator.GetBytes(32);
        var assertion = await new PasskeyService(newStore).AssertAsync(entry.CredentialId, challenge, entry.RpId);
        Assert.NotNull(assertion);
        Assert.True(PasskeyVerifier.TryVerifyAssertion(assertion!, entry.PublicKeyAlgorithm, entry.PublicKey,
            challenge, entry.RpId, out _, "https://example.com"));

        var oldCrypto = new CryptoService();
        oldCrypto.InitializeSession(OldPassword, oldHash!.Salt);
        Assert.ThrowsAny<Exception>(() => oldCrypto.DecryptString(after.Value));
    }

    [Fact]
    public async Task AFailedCredentialCommitRollsBackEveryReencryptedPrivateKey()
    {
        var fixture = await Fixture.CreateAsync();
        var first = await fixture.CreateSoftwareAsync(PasskeyAlgorithm.Es256);
        var second = await fixture.CreateSoftwareAsync(PasskeyAlgorithm.Rs256);
        var firstKey = await fixture.Store.ResolvePrivateKeyAsync(first);
        var secondKey = await fixture.Store.ResolvePrivateKeyAsync(second);
        var before = await fixture.ReadEncryptedKeysAsync();
        await fixture.ExecuteAsync("""
            CREATE TRIGGER reject_master_password_commit BEFORE UPDATE ON vault_credentials
            BEGIN SELECT RAISE(ABORT, 'simulated master password commit failure'); END;
            """);

        var result = await fixture.Maintenance.ChangeMasterPasswordAsync(OldPassword, NewPassword);

        Assert.False(result.Success);
        Assert.Equal(MasterPasswordMaintenanceFailureReason.PersistenceFailed, result.FailureReason);
        Assert.Equal(before.OrderBy(pair => pair.Key), (await fixture.ReadEncryptedKeysAsync()).OrderBy(pair => pair.Key));
        Assert.Equal(firstKey, await fixture.Store.ResolvePrivateKeyAsync(first));
        Assert.Equal(secondKey, await fixture.Store.ResolvePrivateKeyAsync(second));
        var hash = await fixture.Credentials.GetAsync();
        Assert.True(new CryptoService().VerifyMasterPassword(OldPassword, hash!));
        Assert.False(new CryptoService().VerifyMasterPassword(NewPassword, hash!));
        Assert.NotNull(await fixture.Service.AssertAsync(first.CredentialId, RandomNumberGenerator.GetBytes(32), first.RpId));
    }

    [Fact]
    public async Task AFailedPrivateKeyUpdatePreservesTheMasterPasswordAndAllCredentials()
    {
        var fixture = await Fixture.CreateAsync();
        var entry = await fixture.CreateSoftwareAsync(PasskeyAlgorithm.Es256);
        var privateKey = await fixture.Store.ResolvePrivateKeyAsync(entry);
        var before = await fixture.ReadEncryptedKeysAsync();
        await fixture.ExecuteAsync("""
            CREATE TRIGGER reject_passkey_reencryption BEFORE UPDATE ON passkey_private_keys
            BEGIN SELECT RAISE(ABORT, 'simulated passkey reencryption failure'); END;
            """);

        var result = await fixture.Maintenance.ResetMasterPasswordFromUnlockedVaultAsync(NewPassword);

        Assert.False(result.Success);
        Assert.Equal(MasterPasswordMaintenanceFailureReason.PersistenceFailed, result.FailureReason);
        Assert.Equal(before.OrderBy(pair => pair.Key), (await fixture.ReadEncryptedKeysAsync()).OrderBy(pair => pair.Key));
        Assert.Equal(privateKey, await fixture.Store.ResolvePrivateKeyAsync(entry));
        Assert.True(new CryptoService().VerifyMasterPassword(OldPassword, (await fixture.Credentials.GetAsync())!));
    }

    [Fact]
    public async Task ASharedKeyIsReencryptedOnceAndRemainsReadableFromBothCredentials()
    {
        var fixture = await Fixture.CreateAsync();
        var first = await fixture.CreateSoftwareAsync(PasskeyAlgorithm.Es256);
        var key = await fixture.Store.ResolvePrivateKeyAsync(first);
        var second = PublicEntry(first.PublicKey, first.PublicKeyAlgorithm);
        second.PrivateKeyAlias = first.PrivateKeyAlias;
        await fixture.Store.SaveAsync(second);

        var result = await fixture.Maintenance.ChangeMasterPasswordAsync(OldPassword, NewPassword);

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.PasskeySecretsReencrypted);
        Assert.Single(await fixture.ReadEncryptedKeysAsync());
        Assert.Equal(key, await fixture.Store.ResolvePrivateKeyAsync(first));
        Assert.Equal(key, await fixture.Store.ResolvePrivateKeyAsync(second));
        Assert.Equal(2, (await fixture.Store.ListAllAsync()).Count);
    }

    [Fact]
    public async Task SystemManagedCredentialsKeepTheirMetadataWithoutAddingPrivateKeyBlobs()
    {
        var fixture = await Fixture.CreateAsync();
        var material = PasskeyKeyMaterialGenerator.Generate(PasskeyAlgorithm.Es256);
        var nativeEntry = PublicEntry(material.PublicKeySpkiBase64, material.Algorithm);
        nativeEntry.PasskeyMode = PasskeyModes.WindowsHello;
        await fixture.Store.SaveAsync(nativeEntry);

        var result = await fixture.Maintenance.ChangeMasterPasswordAsync(OldPassword, NewPassword);

        Assert.True(result.Success, result.Message);
        Assert.Equal(0, result.PasskeySecretsReencrypted);
        Assert.Equal(0, result.TotalSecretsReencrypted);
        Assert.Empty(await fixture.ReadEncryptedKeysAsync());
        var reloaded = await fixture.Store.GetAsync(nativeEntry.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(nativeEntry.CredentialId, reloaded.CredentialId);
        Assert.Equal(nativeEntry.PublicKey, reloaded.PublicKey);
        Assert.Equal(PasskeyModes.WindowsHello, reloaded.PasskeyMode);
        Assert.Empty(reloaded.PrivateKeyAlias);
    }

    [Fact]
    public async Task AnUnreadablePrivateKeyRefusesThePasswordChangeBeforeAnyWrites()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.CreateSoftwareAsync(PasskeyAlgorithm.Es256);
        await fixture.ExecuteAsync("UPDATE passkey_private_keys SET encrypted_pkcs8 = 'invalid-ciphertext'");
        var before = await fixture.ReadEncryptedKeysAsync();

        var result = await fixture.Maintenance.ChangeMasterPasswordAsync(OldPassword, NewPassword);

        Assert.False(result.Success);
        Assert.Equal(MasterPasswordMaintenanceFailureReason.ExistingDataDecryptionFailed, result.FailureReason);
        Assert.Equal(before.OrderBy(pair => pair.Key), (await fixture.ReadEncryptedKeysAsync()).OrderBy(pair => pair.Key));
        Assert.True(new CryptoService().VerifyMasterPassword(OldPassword, (await fixture.Credentials.GetAsync())!));
    }

    private static PasskeyEntry PublicEntry(string publicKey, int algorithm) => new()
    {
        CredentialId = PasskeyBase64Url.Encode(PasskeyCredentialId.NewRandom()),
        RpId = "example.com",
        RpName = "Example",
        UserName = "alice",
        UserDisplayName = "Alice",
        UserId = PasskeyBase64Url.Encode(Encoding.UTF8.GetBytes("user")),
        PublicKey = publicKey,
        PublicKeyAlgorithm = algorithm,
        PasskeyMode = PasskeyModes.BitwardenCompatible,
        CreatedAt = DateTimeOffset.UtcNow,
        LastUsedAt = DateTimeOffset.UtcNow
    };

    private sealed class Fixture
    {
        private Fixture()
        {
            Factory = new SqliteConnectionFactory(TestTempPaths.CreateFilePath(".db"));
            Migrator = new DatabaseMigrator(Factory);
            Crypto = new CryptoService();
            Credentials = new VaultCredentialStore(Factory, Migrator);
            Store = CreateStore(Crypto);
            Service = new PasskeyService(Store);
            Maintenance = new MasterPasswordMaintenanceService(Factory, Migrator, Crypto);
        }

        public ISqliteConnectionFactory Factory { get; }
        public IDatabaseMigrator Migrator { get; }
        public CryptoService Crypto { get; }
        public IVaultCredentialStore Credentials { get; }
        public IPasskeyStore Store { get; }
        public IPasskeyService Service { get; }
        public IMasterPasswordMaintenanceService Maintenance { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            var hash = fixture.Crypto.HashMasterPassword(OldPassword);
            await fixture.Credentials.SaveAsync(hash);
            fixture.Crypto.InitializeSession(OldPassword, hash.Salt);
            return fixture;
        }

        public IPasskeyStore CreateStore(ICryptoService crypto) => new PasskeyStore(
            Factory, Migrator, new PasskeyPrivateKeyStore(Factory, Migrator, crypto));

        public Task<PasskeyEntry> CreateSoftwareAsync(int algorithm) => Service.CreateAsync(new PasskeyCreateRequest(
            "example.com", RandomNumberGenerator.GetBytes(32), Encoding.UTF8.GetBytes("user"), "alice",
            Algorithm: algorithm));

        public async Task<Dictionary<string, string>> ReadEncryptedKeysAsync()
        {
            await using var connection = Factory.CreateConnection();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT storage_key, encrypted_pkcs8 FROM passkey_private_keys ORDER BY storage_key";
            await using var reader = await command.ExecuteReaderAsync();
            var keys = new Dictionary<string, string>(StringComparer.Ordinal);
            while (await reader.ReadAsync()) keys.Add(reader.GetString(0), reader.GetString(1));
            return keys;
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = Factory.CreateConnection();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
    }
}
