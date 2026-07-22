using System.Security.Cryptography;
using Dapper;
using Monica.Core.Bitwarden;
using Monica.Core.Services;

namespace Monica.Data.Bitwarden;

public interface IBitwardenAccountStore
{
    Task<IReadOnlyList<BitwardenAccount>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<BitwardenAccount?> GetAsync(long accountId, CancellationToken cancellationToken = default);
    Task<BitwardenAccount> SaveConnectedAsync(
        BitwardenAccount account,
        BitwardenAccountSecrets secrets,
        CancellationToken cancellationToken = default);
    Task<BitwardenAccountSecrets?> LoadSecretsAsync(long accountId, CancellationToken cancellationToken = default);
    Task DisconnectAsync(long accountId, CancellationToken cancellationToken = default);
    Task DeleteAsync(long accountId, CancellationToken cancellationToken = default);
}

public sealed partial class BitwardenAccountStore(
    ISqliteConnectionFactory connectionFactory,
    IDatabaseMigrator migrator,
    ICryptoService cryptoService) : IBitwardenAccountStore
{
    private readonly BitwardenAccountSecretProtector _protector = new(cryptoService);

    public async Task<IReadOnlyList<BitwardenAccount>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<BitwardenAccountRow>(
            new CommandDefinition(AccountSelect + " ORDER BY is_default DESC, updated_at DESC", cancellationToken: cancellationToken));
        return rows.Select(MapAccount).ToList();
    }

    public async Task<BitwardenAccount?> GetAsync(
        long accountId,
        CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<BitwardenAccountRow>(
            new CommandDefinition(
                AccountSelect + " WHERE id = @AccountId",
                new { AccountId = accountId },
                cancellationToken: cancellationToken));
        return row is null ? null : MapAccount(row);
    }

    public async Task<BitwardenAccount> SaveConnectedAsync(
        BitwardenAccount account,
        BitwardenAccountSecrets secrets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(secrets);
        if (account.Tls.Mode != BitwardenTlsMode.MutualTls && secrets.HasClientCertificatePassword)
        {
            throw new BitwardenProtocolException(
                "A Bitwarden client-certificate password is only valid for mutual TLS.");
        }

        var normalized = ValidateAndNormalize(account);
        var protectedValues = Protect(normalized, secrets);

        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (normalized.IsDefault)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE bitwarden_vaults SET is_default = 0 WHERE is_default = 1",
                transaction: transaction,
                cancellationToken: cancellationToken));
        }

        var now = DateTimeOffset.UtcNow;
        var saved = await connection.QuerySingleAsync<SavedAccountRow>(
            new CommandDefinition(
                SaveConnectedSql,
                CreateSaveParameters(normalized, protectedValues, now),
                transaction,
                cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);

        return normalized with
        {
            Id = saved.Id,
            IsConnected = true,
            CreatedAt = FromUnixMilliseconds(saved.CreatedAt) ?? now,
            UpdatedAt = now
        };
    }

    public async Task<BitwardenAccountSecrets?> LoadSecretsAsync(
        long accountId,
        CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<BitwardenSecretRow>(
            new CommandDefinition(
                SecretSelect + " WHERE id = @AccountId AND is_connected = 1",
                new { AccountId = accountId },
                cancellationToken: cancellationToken));
        return row is null ? null : Unprotect(row);
    }

    public Task DisconnectAsync(long accountId, CancellationToken cancellationToken = default) =>
        ExecuteAccountMutationAsync(
            """
            UPDATE bitwarden_vaults SET
                encrypted_access_token = NULL,
                encrypted_refresh_token = NULL,
                access_token_expires_at = NULL,
                encrypted_master_key = NULL,
                encrypted_enc_key = NULL,
                encrypted_mac_key = NULL,
                encrypted_client_certificate_password = NULL,
                is_locked = 1,
                is_connected = 0,
                updated_at = @Now
            WHERE id = @AccountId
            """,
            accountId,
            cancellationToken);

    public Task DeleteAsync(long accountId, CancellationToken cancellationToken = default) =>
        ExecuteAccountMutationAsync(
            "DELETE FROM bitwarden_vaults WHERE id = @AccountId",
            accountId,
            cancellationToken);

    private async Task ExecuteAccountMutationAsync(
        string sql,
        long accountId,
        CancellationToken cancellationToken)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { AccountId = accountId, Now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
            cancellationToken: cancellationToken));
    }

    private ProtectedAccountValues Protect(BitwardenAccount account, BitwardenAccountSecrets secrets)
    {
        var accessToken = secrets.CopyAccessToken();
        var refreshToken = secrets.CopyRefreshToken();
        var masterKey = secrets.CopyMasterKey();
        var encryptionKey = secrets.CopyEncryptionKey();
        var macKey = secrets.CopyMacKey();
        var certificatePassword = secrets.CopyClientCertificatePassword();
        try
        {
            return new ProtectedAccountValues(
                _protector.ProtectString(account.Email),
                _protector.ProtectNullableString(account.UserId),
                _protector.ProtectNullableString(account.DisplayName),
                _protector.ProtectBytes(accessToken),
                _protector.ProtectBytes(refreshToken),
                _protector.ProtectBytes(masterKey),
                _protector.ProtectBytes(encryptionKey),
                _protector.ProtectBytes(macKey),
                certificatePassword is null ? null : _protector.ProtectBytes(certificatePassword),
                _protector.ProtectNullableString(account.LastSyncError),
                _protector.ProtectNullableString(account.Tls.CustomCaCertificatePath),
                _protector.ProtectNullableString(account.Tls.ClientCertificatePath));
        }
        finally
        {
            Zero(accessToken);
            Zero(refreshToken);
            Zero(masterKey);
            Zero(encryptionKey);
            Zero(macKey);
            Zero(certificatePassword);
        }
    }

    private static void Zero(byte[]? value)
    {
        if (value is not null)
        {
            CryptographicOperations.ZeroMemory(value);
        }
    }
}
