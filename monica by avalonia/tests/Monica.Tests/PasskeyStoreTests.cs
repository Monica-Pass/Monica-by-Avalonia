using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Monica.Core.Models;
using Monica.Core.Passkeys;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Passkeys;

namespace Monica.Tests;

/// <summary>
/// Persistence evidence for the passkey engine: which column the private key really lands in, what the
/// database still holds once the vault is locked, and that a signature the store produces is one a
/// relying party would accept.
/// </summary>
public sealed class PasskeyStoreTests
{
    private const string RpId = "example.com";

    [Fact]
    public async Task CreatedPasskeyKeepsThePrivateKeyOutOfTheCredentialRow()
    {
        var fixture = new PasskeyFixture();
        var entry = await fixture.Service.CreateAsync(new PasskeyCreateRequest(
            "Example.COM.",
            RandomNumberGenerator.GetBytes(32),
            Encoding.UTF8.GetBytes("user"),
            " alice@example.com ",
            RpName: "Example",
            UserDisplayName: "Alice",
            Algorithm: PasskeyAlgorithm.Es256));

        Assert.True(entry.Id > 0);
        Assert.Equal(RpId, entry.RpId);
        Assert.Equal(PasskeyModes.BitwardenCompatible, entry.PasskeyMode);
        Assert.Equal(PasskeyAuthenticator.MonicaAaguidText, entry.Aaguid);
        Assert.Equal("alice@example.com", entry.UserName);

        var privateKey = await fixture.Store.ResolvePrivateKeyAsync(entry);
        Assert.False(string.IsNullOrWhiteSpace(privateKey));

        var row = await fixture.ReadRawPasskeyRowAsync(entry.Id);
        Assert.StartsWith(PasskeyPrivateKeyRef.ReferencePrefix, row.Columns["private_key_alias"], StringComparison.Ordinal);
        Assert.DoesNotContain(privateKey!, row.Joined, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE KEY", row.Joined, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAssertionTheStoreProducesPassesRelyingPartyChecks()
    {
        var fixture = new PasskeyFixture();
        var entry = await fixture.CreatePasskeyAsync(PasskeyAlgorithm.Es256);
        var challenge = RandomNumberGenerator.GetBytes(32);

        var assertion = await fixture.Service.AssertAsync(entry.CredentialId, challenge, RpId, origin: OriginOf(RpId));

        Assert.NotNull(assertion);
        Assert.True(PasskeyVerifier.TryVerifyAssertion(
            assertion!,
            entry.PublicKeyAlgorithm,
            entry.PublicKey,
            challenge,
            RpId,
            out var failure,
            OriginOf(RpId)));
        Assert.Null(failure);
        Assert.Equal(PasskeyBase64Url.Encode(Encoding.UTF8.GetBytes("user")), assertion!.UserHandle);

        var row = await fixture.ReadRawPasskeyRowAsync(entry.Id);
        Assert.Equal("1", row.Columns["use_count"]);
        Assert.Equal("0", row.Columns["sign_count"]);
    }

    [Fact]
    public async Task ThePrivateKeyIsStoredEncryptedWithTheVaultKey()
    {
        var fixture = new PasskeyFixture();
        var entry = await fixture.CreatePasskeyAsync(PasskeyAlgorithm.Es256);
        var privateKey = await fixture.Store.ResolvePrivateKeyAsync(entry);
        Assert.NotNull(privateKey);

        var blob = await fixture.ReadPrivateKeyBlobAsync(entry.PrivateKeyAlias);
        Assert.False(string.IsNullOrWhiteSpace(blob));
        Assert.DoesNotContain(privateKey!, blob, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE KEY", blob, StringComparison.Ordinal);
        Assert.Equal(privateKey, fixture.Crypto.DecryptString(blob));

        var otherVault = CreateUnlockedCrypto("a different master password");
        Assert.ThrowsAny<Exception>(() => otherVault.DecryptString(blob));
    }

    [Fact]
    public async Task BothCredentialIdSpellingsResolveTheSameRow()
    {
        var fixture = new PasskeyFixture();
        var entry = await fixture.CreatePasskeyAsync(PasskeyAlgorithm.Es256);
        Assert.True(PasskeyBase64Url.TryDecode(entry.CredentialId, out var rawId));

        var byUuid = await fixture.Store.FindAsync(PasskeyCredentialId.ToUuidText(rawId!));
        var byBase64Url = await fixture.Store.FindAsync(entry.CredentialId);
        var wrongRpId = await fixture.Store.FindAsync(entry.CredentialId, "other.example");

        Assert.NotNull(byUuid);
        Assert.Equal(entry.Id, byUuid!.Id);
        Assert.NotNull(byBase64Url);
        Assert.Equal(entry.Id, byBase64Url!.Id);
        Assert.Null(wrongRpId);
        Assert.Null(await fixture.Store.FindAsync("  "));
    }

    [Fact]
    public async Task SigningForAnotherRelyingPartyIsRefused()
    {
        var fixture = new PasskeyFixture();
        var entry = await fixture.CreatePasskeyAsync(PasskeyAlgorithm.Es256);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.AssertAsync(entry.CredentialId, RandomNumberGenerator.GetBytes(32), "evil.example.com"));
        Assert.Contains("cannot sign for", error.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.AssertAsync("no-such-credential", RandomNumberGenerator.GetBytes(32)));
    }

    [Fact]
    public async Task DeletingAPasskeyAlsoRemovesItsProtectedPrivateKey()
    {
        var fixture = new PasskeyFixture();
        var entry = await fixture.CreatePasskeyAsync(PasskeyAlgorithm.Rs256);

        Assert.True(await fixture.Store.DeleteAsync(entry.Id));

        Assert.Null(await fixture.Store.GetAsync(entry.Id));
        Assert.False(await fixture.Store.DeleteAsync(entry.Id));
        Assert.Equal(0, await fixture.CountPrivateKeysAsync());
        Assert.Equal(0, await fixture.CountPasskeysAsync());
    }

    [Fact]
    public async Task ALockedVaultStillListsCredentialsButCannotReadPrivateKeyMaterial()
    {
        var fixture = new PasskeyFixture();
        var entry = await fixture.CreatePasskeyAsync(PasskeyAlgorithm.Es256);

        fixture.Crypto.Lock();

        var listed = Assert.Single(await fixture.Store.ListByRpIdAsync("Example.com."));
        Assert.Equal(entry.Id, listed.Id);
        Assert.StartsWith(PasskeyPrivateKeyRef.ReferencePrefix, listed.PrivateKeyAlias, StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.ResolvePrivateKeyAsync(listed));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CreateAsync(
            new PasskeyCreateRequest(RpId, RandomNumberGenerator.GetBytes(32), Encoding.UTF8.GetBytes("user"), "bob")));
    }

    [Fact]
    public async Task SavingAnExistingEntryUpdatesTheRowInsteadOfDuplicatingIt()
    {
        var fixture = new PasskeyFixture();
        var entry = await fixture.CreatePasskeyAsync(PasskeyAlgorithm.Es256);
        var privateKey = await fixture.Store.ResolvePrivateKeyAsync(entry);

        entry.Notes = "rotated by hand";
        entry.PasskeyMode = PasskeyModes.Legacy;
        entry.UserName = "alice@example.com";
        var id = await fixture.Store.SaveAsync(entry);

        Assert.Equal(entry.Id, id);
        Assert.Equal(1, await fixture.CountPasskeysAsync());
        Assert.Equal(1, await fixture.CountPrivateKeysAsync());
        var reloaded = await fixture.Store.GetAsync(entry.Id);
        Assert.Equal("rotated by hand", reloaded!.Notes);
        Assert.Equal(PasskeyModes.Legacy, reloaded.PasskeyMode);
        Assert.Equal(SyncStatus.None, reloaded.SyncStatus);
        Assert.Equal(privateKey, await fixture.Store.ResolvePrivateKeyAsync(reloaded));
    }

    [Fact]
    public async Task TheStoreRefusesCredentialsItCouldNeverVerify()
    {
        var fixture = new PasskeyFixture();
        var keyMaterial = PasskeyKeyMaterialGenerator.Generate(PasskeyAlgorithm.Es256);

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.SaveAsync(new PasskeyEntry
        {
            CredentialId = " ",
            RpId = RpId,
            PublicKey = keyMaterial.PublicKeySpkiBase64,
            PublicKeyAlgorithm = PasskeyAlgorithm.Es256
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.SaveAsync(new PasskeyEntry
        {
            CredentialId = "cred",
            RpId = "  .. ",
            PublicKey = keyMaterial.PublicKeySpkiBase64,
            PublicKeyAlgorithm = PasskeyAlgorithm.Es256
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.SaveAsync(new PasskeyEntry
        {
            CredentialId = "cred",
            RpId = RpId,
            PublicKey = "",
            PublicKeyAlgorithm = PasskeyAlgorithm.Es256
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.SaveAsync(new PasskeyEntry
        {
            CredentialId = "cred",
            RpId = RpId,
            PublicKey = keyMaterial.PublicKeySpkiBase64,
            PublicKeyAlgorithm = PasskeyAlgorithm.EdDsa
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CreateAsync(
            new PasskeyCreateRequest("..", RandomNumberGenerator.GetBytes(16), Encoding.UTF8.GetBytes("u"), "carol")));
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Service.CreateAsync(
            new PasskeyCreateRequest(
                RpId,
                RandomNumberGenerator.GetBytes(16),
                Encoding.UTF8.GetBytes("u"),
                "dave",
                Algorithm: PasskeyAlgorithm.EdDsa)));

        // Every write above was refused before the schema was even created, so migrate by hand to prove
        // that nothing reached the tables.
        await fixture.Migrator.MigrateAsync();
        Assert.Equal(0, await fixture.CountPasskeysAsync());
        Assert.Equal(0, await fixture.CountPrivateKeysAsync());
    }

    private static string OriginOf(string rpId) => $"https://{rpId}";

    private static CryptoService CreateUnlockedCrypto(string password = "local vault password")
    {
        var crypto = new CryptoService();
        var hash = crypto.HashMasterPassword(password);
        crypto.InitializeSession(password, hash.Salt);
        return crypto;
    }

    private sealed class PasskeyFixture
    {
        public PasskeyFixture()
        {
            Factory = new SqliteConnectionFactory(TestTempPaths.CreateFilePath(".db"));
            Migrator = new DatabaseMigrator(Factory);
            Crypto = CreateUnlockedCrypto();
            PrivateKeyStore = new PasskeyPrivateKeyStore(Factory, Migrator, Crypto);
            Store = new PasskeyStore(Factory, Migrator, PrivateKeyStore);
            Service = new PasskeyService(Store);
        }

        public ISqliteConnectionFactory Factory { get; }

        public IDatabaseMigrator Migrator { get; }

        public CryptoService Crypto { get; }

        public IPasskeyPrivateKeyStore PrivateKeyStore { get; }

        public IPasskeyStore Store { get; }

        public IPasskeyService Service { get; }

        public async Task<PasskeyEntry> CreatePasskeyAsync(int algorithm) => await Service.CreateAsync(
            new PasskeyCreateRequest(
                RpId,
                RandomNumberGenerator.GetBytes(32),
                Encoding.UTF8.GetBytes("user"),
                "alice@example.com",
                Algorithm: algorithm));

        public async Task<RawRow> ReadRawPasskeyRowAsync(long id) =>
            Assert.Single(await ReadAsync("SELECT * FROM passkeys WHERE id = $id", "$id", id));

        public async Task<string> ReadPrivateKeyBlobAsync(string reference)
        {
            var storageKey = PasskeyPrivateKeyRef.StorageKeyFrom(reference);
            Assert.NotNull(storageKey);
            var row = Assert.Single(await ReadAsync(
                "SELECT encrypted_pkcs8 FROM passkey_private_keys WHERE storage_key = $key",
                "$key",
                storageKey));
            return row.Columns["encrypted_pkcs8"];
        }

        public Task<int> CountPrivateKeysAsync() => CountAsync("SELECT COUNT(*) FROM passkey_private_keys");

        public Task<int> CountPasskeysAsync() => CountAsync("SELECT COUNT(*) FROM passkeys");

        private async Task<List<RawRow>> ReadAsync(string sql, string parameterName, object value)
        {
            await using var connection = Factory.CreateConnection();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue(parameterName, value);
            var rows = new List<RawRow>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var columns = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    columns[reader.GetName(index)] = reader.IsDBNull(index)
                        ? ""
                        : reader.GetValue(index)?.ToString() ?? "";
                }

                rows.Add(new RawRow(columns, string.Join('|', columns.Values)));
            }

            return rows;
        }

        private async Task<int> CountAsync(string sql)
        {
            await using var connection = Factory.CreateConnection();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }
    }

    private sealed record RawRow(IReadOnlyDictionary<string, string> Columns, string Joined);
}
