using Dapper;
using Monica.Core.Services;

namespace Monica.Data.Services;

public interface IMasterPasswordMaintenanceService
{
    Task<MasterPasswordMaintenanceResult> ChangeMasterPasswordAsync(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default);

    Task<MasterPasswordMaintenanceResult> ResetMasterPasswordFromUnlockedVaultAsync(
        string newPassword,
        CancellationToken cancellationToken = default);
}

public sealed record MasterPasswordMaintenanceResult(
    bool Success,
    string Message,
    int PasswordsReencrypted = 0,
    int PasswordHistoryEntriesReencrypted = 0,
    int MdbxSecretsReencrypted = 0,
    int CustomFieldsReencrypted = 0,
    int SecureItemsReencrypted = 0,
    int OperationLogsReencrypted = 0,
    int RemoteSourceSecretsReencrypted = 0,
    int BitwardenSecretsReencrypted = 0)
{
    public int TotalSecretsReencrypted =>
        PasswordsReencrypted +
        PasswordHistoryEntriesReencrypted +
        MdbxSecretsReencrypted +
        CustomFieldsReencrypted +
        SecureItemsReencrypted +
        OperationLogsReencrypted +
        RemoteSourceSecretsReencrypted +
        BitwardenSecretsReencrypted;

    public static MasterPasswordMaintenanceResult Failure(string message) => new(false, message);
}

public sealed class MasterPasswordMaintenanceService(
    ISqliteConnectionFactory connectionFactory,
    IDatabaseMigrator migrator,
    ICryptoService cryptoService) : IMasterPasswordMaintenanceService
{
    private const string ProtectedPrefix = "vault:v1:";

    private static readonly SecretColumnSpec[] SecretColumns =
    [
        new("password_entries", "id", "title", SecretBucket.Passwords, true),
        new("password_entries", "id", "website", SecretBucket.Passwords, true),
        new("password_entries", "id", "username", SecretBucket.Passwords, true),
        new("password_entries", "id", "password", SecretBucket.Passwords, false),
        new("password_entries", "id", "notes", SecretBucket.Passwords, true),
        new("password_entries", "id", "app_package_name", SecretBucket.Passwords, true),
        new("password_entries", "id", "app_name", SecretBucket.Passwords, true),
        new("password_entries", "id", "email", SecretBucket.Passwords, true),
        new("password_entries", "id", "phone", SecretBucket.Passwords, true),
        new("password_entries", "id", "address_line", SecretBucket.Passwords, true),
        new("password_entries", "id", "city", SecretBucket.Passwords, true),
        new("password_entries", "id", "state", SecretBucket.Passwords, true),
        new("password_entries", "id", "zip_code", SecretBucket.Passwords, true),
        new("password_entries", "id", "country", SecretBucket.Passwords, true),
        new("password_entries", "id", "credit_card_number", SecretBucket.Passwords, true),
        new("password_entries", "id", "credit_card_holder", SecretBucket.Passwords, true),
        new("password_entries", "id", "credit_card_expiry", SecretBucket.Passwords, true),
        new("password_entries", "id", "credit_card_cvv", SecretBucket.Passwords, true),
        new("password_entries", "id", "authenticator_key", SecretBucket.Passwords, true),
        new("password_entries", "id", "passkey_bindings", SecretBucket.Passwords, true),
        new("password_entries", "id", "ssh_key_data", SecretBucket.Passwords, true),
        new("password_entries", "id", "sso_provider", SecretBucket.Passwords, true),
        new("password_entries", "id", "wifi_metadata", SecretBucket.Passwords, true),
        new("password_entries", "id", "custom_icon_value", SecretBucket.Passwords, true),
        new("password_history_entries", "id", "password", SecretBucket.PasswordHistory, false),
        new("custom_fields", "id", "title", SecretBucket.CustomFields, true),
        new("custom_fields", "id", "value", SecretBucket.CustomFields, true),
        new("secure_items", "id", "title", SecretBucket.SecureItems, true),
        new("secure_items", "id", "notes", SecretBucket.SecureItems, true),
        new("secure_items", "id", "item_data", SecretBucket.SecureItems, true),
        new("secure_items", "id", "image_paths", SecretBucket.SecureItems, true),
        new("local_mdbx_databases", "id", "encrypted_password", SecretBucket.Mdbx, false),
        new("local_mdbx_databases", "id", "key_file_uri", SecretBucket.Mdbx, true),
        new("operation_logs", "id", "item_title", SecretBucket.OperationLogs, true),
        new("operation_logs", "id", "changes_json", SecretBucket.OperationLogs, true),
        new("mdbx_remote_sources", "id", "username_encrypted", SecretBucket.RemoteSources, false),
        new("mdbx_remote_sources", "id", "password_encrypted", SecretBucket.RemoteSources, false),
        new("bitwarden_vaults", "id", "encrypted_access_token", SecretBucket.Bitwarden, false),
        new("bitwarden_vaults", "id", "encrypted_refresh_token", SecretBucket.Bitwarden, false),
        new("bitwarden_vaults", "id", "encrypted_master_key", SecretBucket.Bitwarden, false),
        new("bitwarden_vaults", "id", "encrypted_enc_key", SecretBucket.Bitwarden, false),
        new("bitwarden_vaults", "id", "encrypted_mac_key", SecretBucket.Bitwarden, false)
    ];

    public async Task<MasterPasswordMaintenanceResult> ChangeMasterPasswordAsync(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentPassword))
        {
            return MasterPasswordMaintenanceResult.Failure("Current master password is required.");
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            return MasterPasswordMaintenanceResult.Failure("New master password must be at least 8 characters.");
        }

        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var storedHash = await LoadCredentialAsync(connection, cancellationToken);
        if (storedHash is null)
        {
            return MasterPasswordMaintenanceResult.Failure("Vault credential is not initialized.");
        }

        if (!cryptoService.VerifyMasterPassword(currentPassword, storedHash))
        {
            return MasterPasswordMaintenanceResult.Failure("Current master password is incorrect.");
        }

        return await ReEncryptUnlockedVaultAsync(connection, newPassword, cancellationToken);
    }

    public async Task<MasterPasswordMaintenanceResult> ResetMasterPasswordFromUnlockedVaultAsync(
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            return MasterPasswordMaintenanceResult.Failure("New master password must be at least 8 characters.");
        }

        if (!cryptoService.IsUnlocked)
        {
            return MasterPasswordMaintenanceResult.Failure("Vault must be unlocked before resetting the master password.");
        }

        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var storedHash = await LoadCredentialAsync(connection, cancellationToken);
        if (storedHash is null)
        {
            return MasterPasswordMaintenanceResult.Failure("Vault credential is not initialized.");
        }

        return await ReEncryptUnlockedVaultAsync(connection, newPassword, cancellationToken);
    }

    private async Task<MasterPasswordMaintenanceResult> ReEncryptUnlockedVaultAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        string newPassword,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PlainSecretCell> plainSecrets;
        try
        {
            plainSecrets = await CapturePlainSecretsAsync(connection, cancellationToken);
        }
        catch (Exception ex)
        {
            return MasterPasswordMaintenanceResult.Failure($"Failed to decrypt existing vault data: {ex.Message}");
        }

        var newHash = cryptoService.HashMasterPassword(newPassword);
        IReadOnlyList<EncryptedSecretCell> encryptedSecrets;
        try
        {
            var newSession = new CryptoService();
            newSession.InitializeSession(newPassword, newHash.Salt);
            encryptedSecrets = plainSecrets
                .Select(cell => new EncryptedSecretCell(
                    cell.Spec,
                    cell.Id,
                    ProtectedPrefix + newSession.EncryptString(cell.PlainText)))
                .ToList();
        }
        catch (Exception ex)
        {
            return MasterPasswordMaintenanceResult.Failure($"Failed to encrypt vault data with the new master password: {ex.Message}");
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var cell in encryptedSecrets)
            {
                await UpdateSecretAsync(connection, transaction, cell, cancellationToken);
            }

            await SaveCredentialAsync(connection, transaction, newHash, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return MasterPasswordMaintenanceResult.Failure($"Failed to save re-encrypted vault data: {ex.Message}");
        }

        cryptoService.InitializeSession(newPassword, newHash.Salt);
        return new MasterPasswordMaintenanceResult(
            true,
            "Master password updated and vault data re-encrypted.",
            Count(encryptedSecrets, SecretBucket.Passwords),
            Count(encryptedSecrets, SecretBucket.PasswordHistory),
            Count(encryptedSecrets, SecretBucket.Mdbx),
            Count(encryptedSecrets, SecretBucket.CustomFields),
            Count(encryptedSecrets, SecretBucket.SecureItems),
            Count(encryptedSecrets, SecretBucket.OperationLogs),
            Count(encryptedSecrets, SecretBucket.RemoteSources),
            Count(encryptedSecrets, SecretBucket.Bitwarden));
    }

    private async Task<IReadOnlyList<PlainSecretCell>> CapturePlainSecretsAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var secrets = new List<PlainSecretCell>();
        foreach (var spec in SecretColumns)
        {
            var rows = await connection.QueryAsync<SecretCellRow>(
                new CommandDefinition(
                    $"""
                    SELECT {spec.IdColumn} AS Id, {spec.ValueColumn} AS Value
                    FROM {spec.TableName}
                    WHERE {spec.ValueColumn} IS NOT NULL
                      AND {spec.ValueColumn} <> ''
                    """,
                    cancellationToken: cancellationToken));

            secrets.AddRange(rows.Select(row => new PlainSecretCell(
                spec,
                row.Id,
                UnprotectStoredSecret(spec, row.Value))));
        }

        return secrets;
    }

    private string UnprotectStoredSecret(SecretColumnSpec spec, string value)
    {
        if (value.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
        {
            return cryptoService.DecryptString(value[ProtectedPrefix.Length..]);
        }

        try
        {
            return cryptoService.DecryptString(value);
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException or ArgumentException)
        {
            if (spec.AllowPlaintextFallback)
            {
                return value;
            }

            throw;
        }
    }

    private static async Task<MasterPasswordHash?> LoadCredentialAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<CredentialRow>(
            new CommandDefinition(
                """
                SELECT password_hash AS Hash,
                       salt AS SaltBase64,
                       kdf AS Kdf,
                       iterations AS Iterations,
                       memory_kib AS MemoryKiB,
                       parallelism AS Parallelism
                FROM vault_credentials
                WHERE id = 1
                """,
                cancellationToken: cancellationToken));

        return row is null
            ? null
            : new MasterPasswordHash(
                row.Hash,
                Convert.FromBase64String(row.SaltBase64),
                row.Kdf,
                row.Iterations,
                row.MemoryKiB,
                row.Parallelism);
    }

    private static Task UpdateSecretAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        EncryptedSecretCell cell,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(
            new CommandDefinition(
                $"UPDATE {cell.Spec.TableName} SET {cell.Spec.ValueColumn} = @Value WHERE {cell.Spec.IdColumn} = @Id",
                new { cell.Id, cell.Value },
                transaction,
                cancellationToken: cancellationToken));

    private static Task SaveCredentialAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        MasterPasswordHash hash,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO vault_credentials (
                    id, password_hash, salt, kdf, iterations, memory_kib, parallelism, created_at, updated_at)
                VALUES (
                    1, @Hash, @Salt, @Kdf, @Iterations, @MemoryKiB, @Parallelism, @Now, @Now)
                ON CONFLICT(id) DO UPDATE SET
                    password_hash = excluded.password_hash,
                    salt = excluded.salt,
                    kdf = excluded.kdf,
                    iterations = excluded.iterations,
                    memory_kib = excluded.memory_kib,
                    parallelism = excluded.parallelism,
                    updated_at = excluded.updated_at
                """,
                new
                {
                    hash.Hash,
                    Salt = Convert.ToBase64String(hash.Salt),
                    hash.Kdf,
                    hash.Iterations,
                    hash.MemoryKiB,
                    hash.Parallelism,
                    Now = now
                },
                transaction,
                cancellationToken: cancellationToken));
    }

    private static int Count(IReadOnlyList<EncryptedSecretCell> cells, SecretBucket bucket) =>
        cells.Count(cell => cell.Spec.Bucket == bucket);

    private enum SecretBucket
    {
        Passwords,
        PasswordHistory,
        Mdbx,
        CustomFields,
        SecureItems,
        OperationLogs,
        RemoteSources,
        Bitwarden
    }

    private sealed record SecretColumnSpec(string TableName, string IdColumn, string ValueColumn, SecretBucket Bucket, bool AllowPlaintextFallback);
    private sealed record PlainSecretCell(SecretColumnSpec Spec, long Id, string PlainText);
    private sealed record EncryptedSecretCell(SecretColumnSpec Spec, long Id, string Value);

    private sealed class SecretCellRow
    {
        public long Id { get; init; }
        public string Value { get; init; } = "";
    }

    private sealed class CredentialRow
    {
        public string Hash { get; init; } = "";
        public string SaltBase64 { get; init; } = "";
        public string Kdf { get; init; } = "";
        public int Iterations { get; init; }
        public int MemoryKiB { get; init; }
        public int Parallelism { get; init; }
    }
}
