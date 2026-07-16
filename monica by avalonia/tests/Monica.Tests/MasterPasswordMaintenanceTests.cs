using Microsoft.Data.Sqlite;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Repositories;
using Monica.Data.Services;

namespace Monica.Tests;

public sealed class MasterPasswordMaintenanceTests
{
    [Fact]
    public async Task ChangeMasterPassword_reencrypts_vault_database_secrets()
    {
        var harness = await CreateHarnessAsync("old password");
        var password = new PasswordEntry
        {
            Title = "Portal",
            Password = harness.Crypto.EncryptString("current-secret")
        };
        await harness.Repository.SavePasswordAsync(password);
        await harness.Repository.SavePasswordHistoryAsync(new PasswordHistoryEntry
        {
            EntryId = password.Id,
            Password = harness.Crypto.EncryptString("old-secret"),
            LastUsedAt = DateTimeOffset.UtcNow
        });
        await harness.Repository.SaveMdbxDatabaseAsync(new LocalMdbxDatabase
        {
            Name = "Local vault",
            FilePath = Path.Combine(Path.GetTempPath(), "local.mdbx"),
            StorageLocation = MdbxStorageLocation.Internal,
            SourceType = "LOCAL_INTERNAL",
            EncryptedPassword = harness.Crypto.EncryptString("mdbx-secret")
        });
        await InsertSyncSecretsAsync(
            harness.Factory,
            harness.Crypto.EncryptString("dav-user"),
            harness.Crypto.EncryptString("dav-secret"),
            harness.Crypto.EncryptString("access-token"),
            harness.Crypto.EncryptString("refresh-token"),
            harness.Crypto.EncryptString("master-key"),
            harness.Crypto.EncryptString("enc-key"),
            harness.Crypto.EncryptString("mac-key"));

        var result = await harness.Service.ChangeMasterPasswordAsync("old password", "new password");

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.PasswordsReencrypted);
        Assert.Equal(1, result.PasswordHistoryEntriesReencrypted);
        Assert.Equal(1, result.MdbxSecretsReencrypted);
        Assert.Equal(2, result.RemoteSourceSecretsReencrypted);
        Assert.Equal(5, result.BitwardenSecretsReencrypted);
        Assert.Equal(11, result.TotalSecretsReencrypted);

        var loadedCredential = await harness.CredentialStore.GetAsync();
        Assert.NotNull(loadedCredential);
        Assert.True(new CryptoService().VerifyMasterPassword("new password", loadedCredential));
        Assert.False(new CryptoService().VerifyMasterPassword("old password", loadedCredential));

        var reloadedPassword = Assert.Single(await harness.Repository.GetPasswordsAsync());
        var reloadedHistory = Assert.Single(await harness.Repository.GetPasswordHistoryAsync(password.Id));
        var reloadedMdbx = Assert.Single(await harness.Repository.GetMdbxDatabasesAsync());
        var syncSecrets = await LoadSyncSecretsAsync(harness.Factory);

        Assert.Equal("Portal", reloadedPassword.Title);
        Assert.Equal("current-secret", reloadedPassword.Password);
        Assert.Equal("old-secret", reloadedHistory.Password);
        Assert.Equal("mdbx-secret", reloadedMdbx.EncryptedPassword);
        Assert.Equal("dav-user", UnprotectStoredSecret(harness.Crypto, syncSecrets.RemoteUsername));
        Assert.Equal("dav-secret", UnprotectStoredSecret(harness.Crypto, syncSecrets.RemotePassword));
        Assert.Equal("access-token", UnprotectStoredSecret(harness.Crypto, syncSecrets.BitwardenAccessToken));
        Assert.Equal("refresh-token", UnprotectStoredSecret(harness.Crypto, syncSecrets.BitwardenRefreshToken));
        Assert.Equal("master-key", UnprotectStoredSecret(harness.Crypto, syncSecrets.BitwardenMasterKey));
        Assert.Equal("enc-key", UnprotectStoredSecret(harness.Crypto, syncSecrets.BitwardenEncKey));
        Assert.Equal("mac-key", UnprotectStoredSecret(harness.Crypto, syncSecrets.BitwardenMacKey));
    }

    [Fact]
    public async Task ChangeMasterPassword_rejects_wrong_current_password_without_reencrypting()
    {
        var harness = await CreateHarnessAsync("old password");
        var password = new PasswordEntry
        {
            Title = "Portal",
            Password = harness.Crypto.EncryptString("current-secret")
        };
        await harness.Repository.SavePasswordAsync(password);

        var result = await harness.Service.ChangeMasterPasswordAsync("wrong password", "new password");

        Assert.False(result.Success);
        Assert.Equal(MasterPasswordMaintenanceFailureReason.CurrentPasswordIncorrect, result.FailureReason);
        Assert.Contains("incorrect", result.Message, StringComparison.OrdinalIgnoreCase);
        var loadedCredential = await harness.CredentialStore.GetAsync();
        Assert.NotNull(loadedCredential);
        Assert.True(new CryptoService().VerifyMasterPassword("old password", loadedCredential));
        Assert.False(new CryptoService().VerifyMasterPassword("new password", loadedCredential));

        var reloadedPassword = Assert.Single(await harness.Repository.GetPasswordsAsync());
        Assert.Equal("current-secret", reloadedPassword.Password);
    }

    [Fact]
    public async Task ChangeMasterPassword_keeps_old_session_when_existing_ciphertext_is_invalid()
    {
        var harness = await CreateHarnessAsync("old password");
        var password = new PasswordEntry
        {
            Title = "Portal",
            Password = harness.Crypto.EncryptString("current-secret")
        };
        await harness.Repository.SavePasswordAsync(password);
        await InsertInvalidPasswordHistoryAsync(harness.Factory, password.Id);

        var result = await harness.Service.ChangeMasterPasswordAsync("old password", "new password");

        Assert.False(result.Success);
        Assert.Equal(MasterPasswordMaintenanceFailureReason.ExistingDataDecryptionFailed, result.FailureReason);
        Assert.Contains("decrypt", result.Message, StringComparison.OrdinalIgnoreCase);
        var loadedCredential = await harness.CredentialStore.GetAsync();
        Assert.NotNull(loadedCredential);
        Assert.True(new CryptoService().VerifyMasterPassword("old password", loadedCredential));
        Assert.False(new CryptoService().VerifyMasterPassword("new password", loadedCredential));

        var reloadedPassword = Assert.Single(await harness.Repository.GetPasswordsAsync());
        Assert.Equal("current-secret", reloadedPassword.Password);
    }

    [Fact]
    public async Task ResetMasterPasswordFromUnlockedVault_reencrypts_without_current_password()
    {
        var harness = await CreateHarnessAsync("old password");
        var password = new PasswordEntry
        {
            Title = "Portal",
            Password = harness.Crypto.EncryptString("current-secret")
        };
        await harness.Repository.SavePasswordAsync(password);

        var result = await harness.Service.ResetMasterPasswordFromUnlockedVaultAsync("new password");

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.PasswordsReencrypted);
        var loadedCredential = await harness.CredentialStore.GetAsync();
        Assert.NotNull(loadedCredential);
        Assert.True(new CryptoService().VerifyMasterPassword("new password", loadedCredential));
        Assert.False(new CryptoService().VerifyMasterPassword("old password", loadedCredential));
        var reloadedPassword = Assert.Single(await harness.Repository.GetPasswordsAsync());
        Assert.Equal("current-secret", reloadedPassword.Password);
    }

    [Fact]
    public async Task ResetMasterPasswordFromUnlockedVault_requires_unlocked_crypto_session()
    {
        var harness = await CreateHarnessAsync("old password");
        var lockedCrypto = new CryptoService();
        var lockedService = new MasterPasswordMaintenanceService(harness.Factory, new DatabaseMigrator(harness.Factory), lockedCrypto);

        var result = await lockedService.ResetMasterPasswordFromUnlockedVaultAsync("new password");

        Assert.False(result.Success);
        Assert.Equal(MasterPasswordMaintenanceFailureReason.VaultLocked, result.FailureReason);
        Assert.Contains("unlocked", result.Message, StringComparison.OrdinalIgnoreCase);
        var loadedCredential = await harness.CredentialStore.GetAsync();
        Assert.NotNull(loadedCredential);
        Assert.True(new CryptoService().VerifyMasterPassword("old password", loadedCredential));
    }

    private static async Task<TestHarness> CreateHarnessAsync(string masterPassword)
    {
        var factory = new SqliteConnectionFactory(GetTempDatabasePath());
        var migrator = new DatabaseMigrator(factory);
        var crypto = new CryptoService();
        var repository = new MonicaRepository(factory, migrator, new VaultDataProtector(crypto));
        var credentialStore = new VaultCredentialStore(factory, migrator);
        var hash = crypto.HashMasterPassword(masterPassword);
        await credentialStore.SaveAsync(hash);
        Assert.True(crypto.VerifyMasterPassword(masterPassword, hash));
        return new TestHarness(
            factory,
            repository,
            credentialStore,
            crypto,
            new MasterPasswordMaintenanceService(factory, migrator, crypto));
    }

    private static async Task InsertSyncSecretsAsync(
        ISqliteConnectionFactory factory,
        string remoteUsername,
        string remotePassword,
        string bitwardenAccessToken,
        string bitwardenRefreshToken,
        string bitwardenMasterKey,
        string bitwardenEncKey,
        string bitwardenMacKey)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                INSERT INTO mdbx_remote_sources(
                    display_name, remote_path, username_encrypted, password_encrypted, created_at, updated_at)
                VALUES(
                    $display_name, $remote_path, $username_encrypted, $password_encrypted, $created_at, $updated_at)
                """;
            command.Parameters.AddWithValue("$display_name", "WebDAV");
            command.Parameters.AddWithValue("$remote_path", "/Monica");
            command.Parameters.AddWithValue("$username_encrypted", remoteUsername);
            command.Parameters.AddWithValue("$password_encrypted", remotePassword);
            command.Parameters.AddWithValue("$created_at", now);
            command.Parameters.AddWithValue("$updated_at", now);
            await command.ExecuteNonQueryAsync();
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                INSERT INTO bitwarden_vaults(
                    email, account_key, encrypted_access_token, encrypted_refresh_token,
                    encrypted_master_key, encrypted_enc_key, encrypted_mac_key, created_at, updated_at)
                VALUES(
                    $email, $account_key, $encrypted_access_token, $encrypted_refresh_token,
                    $encrypted_master_key, $encrypted_enc_key, $encrypted_mac_key, $created_at, $updated_at)
                """;
            command.Parameters.AddWithValue("$email", "dev@example.com");
            command.Parameters.AddWithValue("$account_key", "dev@example.com|https://vault.bitwarden.com");
            command.Parameters.AddWithValue("$encrypted_access_token", bitwardenAccessToken);
            command.Parameters.AddWithValue("$encrypted_refresh_token", bitwardenRefreshToken);
            command.Parameters.AddWithValue("$encrypted_master_key", bitwardenMasterKey);
            command.Parameters.AddWithValue("$encrypted_enc_key", bitwardenEncKey);
            command.Parameters.AddWithValue("$encrypted_mac_key", bitwardenMacKey);
            command.Parameters.AddWithValue("$created_at", now);
            command.Parameters.AddWithValue("$updated_at", now);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task InsertInvalidPasswordHistoryAsync(ISqliteConnectionFactory factory, long entryId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO password_history_entries(entry_id, password, last_used_at)
            VALUES($entry_id, $password, $last_used_at)
            """;
        command.Parameters.AddWithValue("$entry_id", entryId);
        command.Parameters.AddWithValue("$password", "not-base64");
        command.Parameters.AddWithValue("$last_used_at", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<SyncSecrets> LoadSyncSecretsAsync(ISqliteConnectionFactory factory)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();

        string remoteUsername;
        string remotePassword;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT username_encrypted, password_encrypted
                FROM mdbx_remote_sources
                WHERE display_name = $display_name
                """;
            command.Parameters.AddWithValue("$display_name", "WebDAV");
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            remoteUsername = reader.GetString(0);
            remotePassword = reader.GetString(1);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT encrypted_access_token, encrypted_refresh_token, encrypted_master_key, encrypted_enc_key, encrypted_mac_key
                FROM bitwarden_vaults
                WHERE email = $email
                """;
            command.Parameters.AddWithValue("$email", "dev@example.com");
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return new SyncSecrets(
                remoteUsername,
                remotePassword,
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4));
        }
    }

    private static string GetTempDatabasePath()
    {
        return TestTempPaths.CreateFilePath(".db");
    }

    private static string UnprotectStoredSecret(CryptoService crypto, string value)
    {
        const string prefix = "vault:v1:";
        return value.StartsWith(prefix, StringComparison.Ordinal)
            ? crypto.DecryptString(value[prefix.Length..])
            : crypto.DecryptString(value);
    }

    private sealed record TestHarness(
        SqliteConnectionFactory Factory,
        MonicaRepository Repository,
        VaultCredentialStore CredentialStore,
        CryptoService Crypto,
        MasterPasswordMaintenanceService Service);

    private sealed record SyncSecrets(
        string RemoteUsername,
        string RemotePassword,
        string BitwardenAccessToken,
        string BitwardenRefreshToken,
        string BitwardenMasterKey,
        string BitwardenEncKey,
        string BitwardenMacKey);
}
