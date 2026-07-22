using Monica.Core.Bitwarden;

namespace Monica.Data.Bitwarden;

public sealed partial class BitwardenPendingOperationStore
{
    private const string SelectSql =
        """
        SELECT id AS Id,
               bitwarden_vault_id AS VaultId,
               cipher_id AS CipherId,
               operation_type AS OperationType,
               expected_remote_revision AS ExpectedRemoteRevision,
               encrypted_payload_json AS EncryptedPayloadJson,
               idempotency_key AS IdempotencyKey,
               status AS Status,
               failure_class AS FailureClass,
               attempt_count AS AttemptCount,
               next_attempt_at AS NextAttemptAt,
               claimed_at AS ClaimedAt,
               encrypted_last_error AS EncryptedLastError,
               created_at AS CreatedAt,
               updated_at AS UpdatedAt
        FROM bitwarden_pending_operations
        """;

    private BitwardenPendingOperation Map(PendingOperationRow row) => new(
        row.Id,
        row.VaultId,
        row.CipherId,
        ParseOperationType(row.OperationType),
        row.ExpectedRemoteRevision,
        row.EncryptedPayloadJson is null ? "{}" : _protector.UnprotectString(row.EncryptedPayloadJson),
        row.IdempotencyKey,
        ParseStatus(row.Status),
        ParseFailureClass(row.FailureClass),
        row.AttemptCount,
        DateTimeOffset.FromUnixTimeMilliseconds(row.NextAttemptAt),
        row.ClaimedAt is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(row.ClaimedAt.Value),
        row.EncryptedLastError is null ? null : _protector.UnprotectString(row.EncryptedLastError),
        DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAt),
        DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAt));

    private static string FormatOperationType(BitwardenMutationOperationType value) => value switch
    {
        BitwardenMutationOperationType.Create => "create",
        BitwardenMutationOperationType.Update => "update",
        BitwardenMutationOperationType.Delete => "delete",
        _ => throw new BitwardenProtocolException($"Unsupported Bitwarden operation type: {value}.")
    };

    private static BitwardenMutationOperationType ParseOperationType(string value) => value switch
    {
        "create" => BitwardenMutationOperationType.Create,
        "update" => BitwardenMutationOperationType.Update,
        "delete" => BitwardenMutationOperationType.Delete,
        _ => throw new BitwardenProtocolException($"Stored Bitwarden operation type is invalid: {value}.")
    };

    private static string FormatStatus(BitwardenMutationStatus value) => value switch
    {
        BitwardenMutationStatus.Pending => "pending",
        BitwardenMutationStatus.InFlight => "in-flight",
        BitwardenMutationStatus.Completed => "completed",
        BitwardenMutationStatus.Conflict => "conflict",
        BitwardenMutationStatus.Failed => "failed",
        _ => throw new BitwardenProtocolException($"Unsupported Bitwarden operation status: {value}.")
    };

    private static BitwardenMutationStatus ParseStatus(string value) => value switch
    {
        "pending" => BitwardenMutationStatus.Pending,
        "in-flight" => BitwardenMutationStatus.InFlight,
        "completed" => BitwardenMutationStatus.Completed,
        "conflict" => BitwardenMutationStatus.Conflict,
        "failed" => BitwardenMutationStatus.Failed,
        _ => throw new BitwardenProtocolException($"Stored Bitwarden operation status is invalid: {value}.")
    };

    private static string FormatFailureClass(BitwardenFailureClass value) => value switch
    {
        BitwardenFailureClass.None => "none",
        BitwardenFailureClass.TransientNetwork => "transient-network",
        BitwardenFailureClass.RateLimited => "rate-limited",
        BitwardenFailureClass.Unauthorized => "unauthorized",
        BitwardenFailureClass.Conflict => "conflict",
        BitwardenFailureClass.Validation => "validation",
        BitwardenFailureClass.Permanent => "permanent",
        _ => throw new BitwardenProtocolException($"Unsupported Bitwarden failure class: {value}.")
    };

    private static BitwardenFailureClass ParseFailureClass(string value) => value switch
    {
        "none" => BitwardenFailureClass.None,
        "transient-network" => BitwardenFailureClass.TransientNetwork,
        "rate-limited" => BitwardenFailureClass.RateLimited,
        "unauthorized" => BitwardenFailureClass.Unauthorized,
        "conflict" => BitwardenFailureClass.Conflict,
        "validation" => BitwardenFailureClass.Validation,
        "permanent" => BitwardenFailureClass.Permanent,
        _ => throw new BitwardenProtocolException($"Stored Bitwarden failure class is invalid: {value}.")
    };

    private sealed class PendingOperationRow
    {
        public long Id { get; init; }
        public long VaultId { get; init; }
        public string CipherId { get; init; } = "";
        public string OperationType { get; init; } = "";
        public string? ExpectedRemoteRevision { get; init; }
        public string? EncryptedPayloadJson { get; init; }
        public string IdempotencyKey { get; init; } = "";
        public string Status { get; init; } = "";
        public string FailureClass { get; init; } = "";
        public int AttemptCount { get; init; }
        public long NextAttemptAt { get; init; }
        public long? ClaimedAt { get; init; }
        public string? EncryptedLastError { get; init; }
        public long CreatedAt { get; init; }
        public long UpdatedAt { get; init; }
    }
}
