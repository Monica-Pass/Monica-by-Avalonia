using System.Text;
using System.Text.Json;
using Dapper;
using Monica.Core.Bitwarden;
using Monica.Core.Services;

namespace Monica.Data.Bitwarden;

public interface IBitwardenConflictBackupStore
{
    Task<long> SaveAsync(
        BitwardenConflictBackup backup,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BitwardenConflictBackup>> GetUnresolvedAsync(
        long vaultId,
        CancellationToken cancellationToken = default);

    Task ResolveAsync(long backupId, CancellationToken cancellationToken = default);
}

public sealed class BitwardenConflictBackupStore(
    ISqliteConnectionFactory connectionFactory,
    IDatabaseMigrator migrator,
    ICryptoService cryptoService) : IBitwardenConflictBackupStore
{
    public const int MaximumPayloadUtf8Bytes = 4 * 1024 * 1024;
    private readonly BitwardenAccountSecretProtector _protector = new(cryptoService);

    public async Task<long> SaveAsync(
        BitwardenConflictBackup backup,
        CancellationToken cancellationToken = default)
    {
        Validate(backup);
        var protectedPayload = _protector.ProtectString(backup.PayloadJson);
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO bitwarden_conflict_backups (
                bitwarden_vault_id, cipher_id, item_kind, local_item_id,
                local_revision_date, remote_revision_date, encrypted_payload_json,
                reason, created_at, resolved_at)
            VALUES (
                @VaultId, @CipherId, @ItemKind, @LocalItemId,
                @LocalRevisionDate, @RemoteRevisionDate, @EncryptedPayloadJson,
                @Reason, @CreatedAt, NULL);
            SELECT last_insert_rowid();
            """,
            new
            {
                backup.VaultId,
                backup.CipherId,
                backup.ItemKind,
                backup.LocalItemId,
                backup.LocalRevisionDate,
                backup.RemoteRevisionDate,
                EncryptedPayloadJson = protectedPayload,
                backup.Reason,
                CreatedAt = backup.CreatedAt.ToUniversalTime().ToUnixTimeMilliseconds()
            },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<BitwardenConflictBackup>> GetUnresolvedAsync(
        long vaultId,
        CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<ConflictBackupRow>(new CommandDefinition(
            """
            SELECT id AS Id,
                   bitwarden_vault_id AS VaultId,
                   cipher_id AS CipherId,
                   item_kind AS ItemKind,
                   local_item_id AS LocalItemId,
                   local_revision_date AS LocalRevisionDate,
                   remote_revision_date AS RemoteRevisionDate,
                   encrypted_payload_json AS EncryptedPayloadJson,
                   reason AS Reason,
                   created_at AS CreatedAt,
                   resolved_at AS ResolvedAt
            FROM bitwarden_conflict_backups
            WHERE bitwarden_vault_id = @VaultId AND resolved_at IS NULL
            ORDER BY created_at DESC, id DESC
            """,
            new { VaultId = vaultId },
            cancellationToken: cancellationToken));

        return rows.Select(row => new BitwardenConflictBackup(
            row.Id,
            row.VaultId,
            row.CipherId,
            row.ItemKind,
            row.LocalItemId,
            row.LocalRevisionDate,
            row.RemoteRevisionDate,
            _protector.UnprotectString(row.EncryptedPayloadJson),
            row.Reason,
            DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAt),
            row.ResolvedAt is null
                ? null
                : DateTimeOffset.FromUnixTimeMilliseconds(row.ResolvedAt.Value))).ToList();
    }

    public async Task ResolveAsync(long backupId, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE bitwarden_conflict_backups SET resolved_at = @ResolvedAt WHERE id = @BackupId AND resolved_at IS NULL",
            new
            {
                BackupId = backupId,
                ResolvedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            },
            cancellationToken: cancellationToken));
    }

    private static void Validate(BitwardenConflictBackup backup)
    {
        ArgumentNullException.ThrowIfNull(backup);
        if (backup.VaultId <= 0 || backup.LocalItemId <= 0 || string.IsNullOrWhiteSpace(backup.CipherId))
        {
            throw new ArgumentException("Bitwarden conflict backup identities are required.", nameof(backup));
        }

        if (backup.CipherId.Length > 256 || backup.ItemKind.Length > 64 || backup.Reason.Length > 1024 ||
            Encoding.UTF8.GetByteCount(backup.PayloadJson) > MaximumPayloadUtf8Bytes)
        {
            throw new BitwardenProtocolException("Bitwarden conflict backup exceeds the supported size.");
        }

        try
        {
            using var _ = JsonDocument.Parse(backup.PayloadJson);
        }
        catch (JsonException exception)
        {
            throw new BitwardenProtocolException("Bitwarden conflict backup payload is not valid JSON.", exception);
        }
    }

    private sealed class ConflictBackupRow
    {
        public long Id { get; init; }
        public long VaultId { get; init; }
        public string CipherId { get; init; } = "";
        public string ItemKind { get; init; } = "";
        public long LocalItemId { get; init; }
        public string? LocalRevisionDate { get; init; }
        public string? RemoteRevisionDate { get; init; }
        public string EncryptedPayloadJson { get; init; } = "";
        public string Reason { get; init; } = "";
        public long CreatedAt { get; init; }
        public long? ResolvedAt { get; init; }
    }
}
