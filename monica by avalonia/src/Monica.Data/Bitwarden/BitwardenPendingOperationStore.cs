using System.Text;
using System.Text.Json;
using Dapper;
using Monica.Core.Bitwarden;
using Monica.Core.Services;

namespace Monica.Data.Bitwarden;

public interface IBitwardenPendingOperationStore
{
    Task<long> EnqueueAsync(
        BitwardenPendingOperation operation,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BitwardenPendingOperation>> ClaimReadyAsync(
        long vaultId,
        DateTimeOffset now,
        int limit = 25,
        CancellationToken cancellationToken = default);

    Task<BitwardenMutationStatus> RecordFailureAsync(
        long operationId,
        BitwardenFailureClass failureClass,
        string? error,
        DateTimeOffset now,
        TimeSpan? retryAfter = null,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(long operationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BitwardenPendingOperation>> GetAsync(
        long vaultId,
        CancellationToken cancellationToken = default);
}

public sealed partial class BitwardenPendingOperationStore(
    ISqliteConnectionFactory connectionFactory,
    IDatabaseMigrator migrator,
    ICryptoService cryptoService) : IBitwardenPendingOperationStore
{
    public const int MaximumPayloadUtf8Bytes = 4 * 1024 * 1024;
    public static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(10);
    private readonly BitwardenAccountSecretProtector _protector = new(cryptoService);

    public async Task<long> EnqueueAsync(
        BitwardenPendingOperation operation,
        CancellationToken cancellationToken = default)
    {
        ValidateForEnqueue(operation);
        var now = operation.UpdatedAt == default ? DateTimeOffset.UtcNow : operation.UpdatedAt;
        var createdAt = operation.CreatedAt == default ? now : operation.CreatedAt;
        var nextAttempt = operation.NextAttemptAt == default ? now : operation.NextAttemptAt;
        var protectedPayload = _protector.ProtectString(operation.PayloadJson);

        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO bitwarden_pending_operations (
                bitwarden_vault_id, cipher_id, operation_type, expected_remote_revision,
                encrypted_payload_json, idempotency_key, status, failure_class,
                attempt_count, next_attempt_at, claimed_at, encrypted_last_error,
                created_at, updated_at)
            VALUES (
                @VaultId, @CipherId, @OperationType, @ExpectedRemoteRevision,
                @EncryptedPayloadJson, @IdempotencyKey, 'pending', 'none',
                0, @NextAttemptAt, NULL, NULL, @CreatedAt, @UpdatedAt)
            ON CONFLICT(idempotency_key) DO UPDATE SET
                bitwarden_vault_id = excluded.bitwarden_vault_id,
                cipher_id = excluded.cipher_id,
                operation_type = excluded.operation_type,
                expected_remote_revision = excluded.expected_remote_revision,
                encrypted_payload_json = excluded.encrypted_payload_json,
                status = 'pending',
                failure_class = 'none',
                attempt_count = 0,
                next_attempt_at = excluded.next_attempt_at,
                claimed_at = NULL,
                encrypted_last_error = NULL,
                updated_at = excluded.updated_at
            RETURNING id
            """,
            new
            {
                operation.VaultId,
                operation.CipherId,
                OperationType = FormatOperationType(operation.OperationType),
                operation.ExpectedRemoteRevision,
                EncryptedPayloadJson = protectedPayload,
                operation.IdempotencyKey,
                NextAttemptAt = nextAttempt.ToUniversalTime().ToUnixTimeMilliseconds(),
                CreatedAt = createdAt.ToUniversalTime().ToUnixTimeMilliseconds(),
                UpdatedAt = now.ToUniversalTime().ToUnixTimeMilliseconds()
            },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<BitwardenPendingOperation>> ClaimReadyAsync(
        long vaultId,
        DateTimeOffset now,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        if (vaultId <= 0 || limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(vaultId <= 0 ? nameof(vaultId) : nameof(limit));
        }

        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var nowUnix = now.ToUniversalTime().ToUnixTimeMilliseconds();
        var staleBefore = (now - ClaimTimeout).ToUniversalTime().ToUnixTimeMilliseconds();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE bitwarden_pending_operations
            SET status = 'pending', claimed_at = NULL, updated_at = @Now
            WHERE bitwarden_vault_id = @VaultId
              AND status = 'in-flight'
              AND claimed_at <= @StaleBefore
            """,
            new { VaultId = vaultId, Now = nowUnix, StaleBefore = staleBefore },
            transaction,
            cancellationToken: cancellationToken));
        var ids = (await connection.QueryAsync<long>(new CommandDefinition(
            """
            SELECT id
            FROM bitwarden_pending_operations
            WHERE bitwarden_vault_id = @VaultId
              AND status = 'pending'
              AND next_attempt_at <= @Now
              AND attempt_count < @MaximumAttempts
            ORDER BY CASE operation_type
                         WHEN 'delete' THEN 0
                         WHEN 'create' THEN 1
                         WHEN 'update' THEN 2
                         ELSE 3
                     END,
                     next_attempt_at ASC,
                     id ASC
            LIMIT @Limit
            """,
            new
            {
                VaultId = vaultId,
                Now = nowUnix,
                BitwardenRetryPolicy.MaximumAttempts,
                Limit = limit
            },
            transaction,
            cancellationToken: cancellationToken))).ToArray();

        var claimedIds = new List<long>(ids.Length);
        foreach (var id in ids)
        {
            var updated = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE bitwarden_pending_operations
                SET status = 'in-flight', attempt_count = attempt_count + 1,
                    claimed_at = @Now, updated_at = @Now
                WHERE id = @Id AND status = 'pending' AND next_attempt_at <= @Now
                """,
                new { Id = id, Now = nowUnix },
                transaction,
                cancellationToken: cancellationToken));
            if (updated == 1)
            {
                claimedIds.Add(id);
            }
        }

        var rows = claimedIds.Count == 0
            ? []
            : (await connection.QueryAsync<PendingOperationRow>(new CommandDefinition(
                SelectSql + " WHERE id IN @Ids ORDER BY CASE operation_type WHEN 'delete' THEN 0 WHEN 'create' THEN 1 WHEN 'update' THEN 2 ELSE 3 END, next_attempt_at ASC, id ASC",
                new { Ids = claimedIds },
                transaction,
                cancellationToken: cancellationToken))).ToList();
        await transaction.CommitAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<BitwardenMutationStatus> RecordFailureAsync(
        long operationId,
        BitwardenFailureClass failureClass,
        string? error,
        DateTimeOffset now,
        TimeSpan? retryAfter = null,
        CancellationToken cancellationToken = default)
    {
        if (failureClass == BitwardenFailureClass.None)
        {
            throw new ArgumentException("A Bitwarden failure class is required.", nameof(failureClass));
        }

        if (error is not null && Encoding.UTF8.GetByteCount(error) > 16 * 1024)
        {
            error = error[..Math.Min(error.Length, 4096)];
        }

        var protectedError = string.IsNullOrWhiteSpace(error) ? null : _protector.ProtectString(error);
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var attemptCount = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            "SELECT attempt_count FROM bitwarden_pending_operations WHERE id = @Id",
            new { Id = operationId },
            transaction,
            cancellationToken: cancellationToken)) ?? throw new KeyNotFoundException("Bitwarden operation was not found.");
        var canRetry = BitwardenRetryPolicy.CanRetry(failureClass, attemptCount);
        var status = failureClass == BitwardenFailureClass.Conflict
            ? BitwardenMutationStatus.Conflict
            : canRetry ? BitwardenMutationStatus.Pending : BitwardenMutationStatus.Failed;
        var nextAttempt = BitwardenRetryPolicy.GetNextAttemptAt(now, attemptCount, failureClass, retryAfter);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE bitwarden_pending_operations
            SET status = @Status, failure_class = @FailureClass,
                next_attempt_at = @NextAttemptAt, claimed_at = NULL,
                encrypted_last_error = @EncryptedLastError, updated_at = @UpdatedAt
            WHERE id = @Id
            """,
            new
            {
                Id = operationId,
                Status = FormatStatus(status),
                FailureClass = FormatFailureClass(failureClass),
                NextAttemptAt = nextAttempt.ToUniversalTime().ToUnixTimeMilliseconds(),
                EncryptedLastError = protectedError,
                UpdatedAt = now.ToUniversalTime().ToUnixTimeMilliseconds()
            },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return status;
    }

    public Task CompleteAsync(long operationId, CancellationToken cancellationToken = default) =>
        UpdateTerminalStatusAsync(operationId, cancellationToken);

    public async Task<IReadOnlyList<BitwardenPendingOperation>> GetAsync(
        long vaultId,
        CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<PendingOperationRow>(new CommandDefinition(
            SelectSql + " WHERE bitwarden_vault_id = @VaultId ORDER BY created_at ASC, id ASC",
            new { VaultId = vaultId },
            cancellationToken: cancellationToken));
        return rows.Select(Map).ToList();
    }

    private async Task UpdateTerminalStatusAsync(long operationId, CancellationToken cancellationToken)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE bitwarden_pending_operations
            SET status = 'completed', failure_class = 'none', encrypted_payload_json = NULL,
                encrypted_last_error = NULL, claimed_at = NULL, updated_at = @UpdatedAt
            WHERE id = @Id
            """,
            new { Id = operationId, UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
            cancellationToken: cancellationToken));
    }

    private static void ValidateForEnqueue(BitwardenPendingOperation operation)
    {
        BitwardenMutationGuard.ValidateForQueue(operation);
        if (operation.CipherId.Length > 256 || operation.IdempotencyKey.Length > 512 ||
            Encoding.UTF8.GetByteCount(operation.PayloadJson) > MaximumPayloadUtf8Bytes)
        {
            throw new BitwardenProtocolException("Bitwarden pending operation exceeds the supported size.");
        }

        try
        {
            using var _ = JsonDocument.Parse(operation.PayloadJson);
        }
        catch (JsonException exception)
        {
            throw new BitwardenProtocolException("Bitwarden pending operation payload is not valid JSON.", exception);
        }
    }
}
