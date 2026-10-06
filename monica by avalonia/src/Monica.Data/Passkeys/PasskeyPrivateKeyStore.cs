using Dapper;
using Microsoft.Data.Sqlite;
using Monica.Core.Passkeys;
using Monica.Core.Services;

namespace Monica.Data.Passkeys;

/// <summary>
/// Keeps passkey private keys out of the credential row. The row stores a
/// <see cref="PasskeyPrivateKeyRef"/> reference; the key itself is encrypted with the vault session
/// key in its own table, which is what makes a credential row safe to hand to an export or sync path
/// that has no idea how to read a Monica-protected blob.
/// </summary>
public interface IPasskeyPrivateKeyStore
{
    Task<string> ProtectAsync(
        string credentialId,
        string rpId,
        string userId,
        string? privateKeyPkcs8Base64,
        CancellationToken cancellationToken = default);

    Task<string?> ResolveAsync(
        string? keyReferenceOrMaterial,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        string? keyReferenceOrMaterial,
        CancellationToken cancellationToken = default);

    // Credential rows and their protected keys must commit or roll back together.
    Task<string> ProtectInTransactionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string credentialId,
        string rpId,
        string userId,
        string? privateKeyPkcs8Base64,
        CancellationToken cancellationToken = default);

    Task RemoveInTransactionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string? keyReferenceOrMaterial,
        CancellationToken cancellationToken = default);
}

public sealed class PasskeyPrivateKeyStore(
    ISqliteConnectionFactory connectionFactory,
    IDatabaseMigrator migrator,
    ICryptoService cryptoService) : IPasskeyPrivateKeyStore
{
    public async Task<string> ProtectAsync(
        string credentialId,
        string rpId,
        string userId,
        string? privateKeyPkcs8Base64,
        CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        var reference = await ProtectInTransactionAsync(
            connection, transaction, credentialId, rpId, userId, privateKeyPkcs8Base64, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return reference;
    }

    public async Task<string> ProtectInTransactionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string credentialId,
        string rpId,
        string userId,
        string? privateKeyPkcs8Base64,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var material = privateKeyPkcs8Base64?.Trim() ?? string.Empty;
        if (material.Length == 0)
        {
            return material;
        }

        if (PasskeyPrivateKeyRef.IsProtectedReference(material))
        {
            var exists = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM passkey_private_keys WHERE storage_key = @StorageKey",
                new { StorageKey = PasskeyPrivateKeyRef.StorageKeyFrom(material) },
                transaction,
                cancellationToken: cancellationToken));
            if (exists == 0)
            {
                throw new InvalidOperationException("This passkey's protected private key is not available.");
            }

            return material;
        }

        var storageKey = PasskeyPrivateKeyRef.StorageKeyFor(credentialId, rpId, userId, material);
        var protectedMaterial = cryptoService.EncryptString(material);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO passkey_private_keys (storage_key, encrypted_pkcs8, created_at)
            VALUES (@StorageKey, @EncryptedPkcs8, @CreatedAt)
            ON CONFLICT(storage_key) DO UPDATE SET encrypted_pkcs8 = excluded.encrypted_pkcs8
            """,
            new
            {
                StorageKey = storageKey,
                EncryptedPkcs8 = protectedMaterial,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            },
            transaction,
            cancellationToken: cancellationToken));
        return PasskeyPrivateKeyRef.ToReference(storageKey);
    }

    public async Task<string?> ResolveAsync(
        string? keyReferenceOrMaterial,
        CancellationToken cancellationToken = default)
    {
        var value = keyReferenceOrMaterial?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            return null;
        }

        if (!PasskeyPrivateKeyRef.IsProtectedReference(value))
        {
            return value;
        }

        var storageKey = PasskeyPrivateKeyRef.StorageKeyFrom(value);
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var protectedMaterial = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT encrypted_pkcs8 FROM passkey_private_keys WHERE storage_key = @StorageKey",
            new { StorageKey = storageKey },
            cancellationToken: cancellationToken));
        return string.IsNullOrEmpty(protectedMaterial) ? null : cryptoService.DecryptString(protectedMaterial);
    }

    public async Task RemoveAsync(
        string? keyReferenceOrMaterial,
        CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await RemoveInTransactionAsync(connection, transaction, keyReferenceOrMaterial, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RemoveInTransactionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string? keyReferenceOrMaterial,
        CancellationToken cancellationToken = default)
    {
        var reference = keyReferenceOrMaterial?.Trim();
        var storageKey = PasskeyPrivateKeyRef.StorageKeyFrom(reference);
        if (storageKey is null)
        {
            return;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM passkey_private_keys
            WHERE storage_key = @StorageKey
              AND NOT EXISTS (SELECT 1 FROM passkeys WHERE private_key_alias = @Reference)
            """,
            new { StorageKey = storageKey, Reference = reference },
            transaction,
            cancellationToken: cancellationToken));
    }
}
