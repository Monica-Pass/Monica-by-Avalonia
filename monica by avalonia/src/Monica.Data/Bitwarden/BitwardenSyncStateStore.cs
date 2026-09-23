using Dapper;
using Monica.Core.Bitwarden;
using Monica.Core.Services;

namespace Monica.Data.Bitwarden;

public sealed record BitwardenSyncedCipher(string CipherId, string PayloadHash);

public interface IBitwardenSyncStateStore
{
    Task ReplaceForVaultAsync(
        long vaultId,
        IReadOnlyList<BitwardenSyncedCipher> ciphers,
        DateTimeOffset syncedAt,
        CancellationToken cancellationToken = default);

    Task AdvanceAsync(
        long vaultId,
        string cipherId,
        string payloadHash,
        DateTimeOffset syncedAt,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string>> GetPayloadHashesAsync(
        long vaultId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Baseline of the local content that matches the remote vault. A push is only due when a bound
/// entry drifts away from this baseline, so the comparison never depends on the dirty flag, which
/// ordinary edits do not set.
/// </summary>
public sealed class BitwardenSyncStateStore(
    ISqliteConnectionFactory connectionFactory,
    IDatabaseMigrator migrator) : IBitwardenSyncStateStore
{
    public async Task ReplaceForVaultAsync(
        long vaultId,
        IReadOnlyList<BitwardenSyncedCipher> ciphers,
        DateTimeOffset syncedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ciphers);
        Validate(vaultId, ciphers);
        var syncedAtUnix = syncedAt.ToUniversalTime().ToUnixTimeMilliseconds();

        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM bitwarden_sync_state WHERE bitwarden_vault_id = @VaultId",
            new { VaultId = vaultId },
            transaction,
            cancellationToken: cancellationToken));
        foreach (var cipher in ciphers)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO bitwarden_sync_state (
                    bitwarden_vault_id, cipher_id, payload_hash, synced_at)
                VALUES (@VaultId, @CipherId, @PayloadHash, @SyncedAt)
                """,
                new
                {
                    VaultId = vaultId,
                    cipher.CipherId,
                    cipher.PayloadHash,
                    SyncedAt = syncedAtUnix
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task AdvanceAsync(
        long vaultId,
        string cipherId,
        string payloadHash,
        DateTimeOffset syncedAt,
        CancellationToken cancellationToken = default)
    {
        Validate(vaultId, [new BitwardenSyncedCipher(cipherId, payloadHash)]);
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO bitwarden_sync_state (
                bitwarden_vault_id, cipher_id, payload_hash, synced_at)
            VALUES (@VaultId, @CipherId, @PayloadHash, @SyncedAt)
            ON CONFLICT(bitwarden_vault_id, cipher_id) DO UPDATE SET
                payload_hash = excluded.payload_hash,
                synced_at = excluded.synced_at
            """,
            new
            {
                VaultId = vaultId,
                CipherId = cipherId,
                PayloadHash = payloadHash,
                SyncedAt = syncedAt.ToUniversalTime().ToUnixTimeMilliseconds()
            },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyDictionary<string, string>> GetPayloadHashesAsync(
        long vaultId,
        CancellationToken cancellationToken = default)
    {
        if (vaultId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(vaultId));
        }

        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<SyncStateRow>(new CommandDefinition(
            """
            SELECT cipher_id AS CipherId,
                   payload_hash AS PayloadHash
            FROM bitwarden_sync_state
            WHERE bitwarden_vault_id = @VaultId
            ORDER BY cipher_id ASC
            """,
            new { VaultId = vaultId },
            cancellationToken: cancellationToken));
        return rows.ToDictionary(row => row.CipherId, row => row.PayloadHash, StringComparer.Ordinal);
    }

    private static void Validate(long vaultId, IReadOnlyList<BitwardenSyncedCipher> ciphers)
    {
        if (vaultId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(vaultId));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cipher in ciphers)
        {
            if (string.IsNullOrWhiteSpace(cipher.CipherId) || cipher.CipherId.Length > 256 ||
                string.IsNullOrWhiteSpace(cipher.PayloadHash) || cipher.PayloadHash.Length > 128 ||
                !seen.Add(cipher.CipherId))
            {
                throw new BitwardenProtocolException("Bitwarden sync state identities must be unique and bounded.");
            }
        }
    }

    private sealed class SyncStateRow
    {
        public string CipherId { get; init; } = "";
        public string PayloadHash { get; init; } = "";
    }
}
