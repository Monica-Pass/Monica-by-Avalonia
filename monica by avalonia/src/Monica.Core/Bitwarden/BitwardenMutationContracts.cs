namespace Monica.Core.Bitwarden;

public enum BitwardenMutationOperationType
{
    Create = 0,
    Update,
    Delete
}

public enum BitwardenMutationStatus
{
    Pending = 0,
    InFlight,
    Completed,
    Conflict,
    Failed
}

public enum BitwardenFailureClass
{
    None = 0,
    TransientNetwork,
    RateLimited,
    Unauthorized,
    Conflict,
    Validation,
    Permanent
}

public sealed record BitwardenPendingOperation(
    long Id,
    long VaultId,
    string CipherId,
    BitwardenMutationOperationType OperationType,
    string? ExpectedRemoteRevision,
    string PayloadJson,
    string IdempotencyKey,
    BitwardenMutationStatus Status,
    BitwardenFailureClass LastFailureClass,
    int AttemptCount,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? ClaimedAt,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record BitwardenMutationRequest(
    long OperationId,
    long VaultId,
    string CipherId,
    BitwardenMutationOperationType OperationType,
    string? ExpectedRemoteRevision,
    string PayloadJson,
    string IdempotencyKey);

public sealed record BitwardenMutationResponse(
    bool Succeeded,
    string? RemoteCipherId,
    string? RemoteRevision,
    int? HttpStatusCode = null,
    string? ErrorMessage = null,
    TimeSpan? RetryAfter = null);

public interface IBitwardenMutationTransport
{
    Task<BitwardenMutationResponse> SendAsync(
        BitwardenMutationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record BitwardenMutationBatchResult(
    int Claimed,
    int Completed,
    int Deferred,
    int Conflicts,
    int Failed);

public static class BitwardenMutationGuard
{
    public static void ValidateForQueue(BitwardenPendingOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.VaultId <= 0 || string.IsNullOrWhiteSpace(operation.CipherId) ||
            string.IsNullOrWhiteSpace(operation.IdempotencyKey))
        {
            throw new BitwardenProtocolException("Bitwarden mutation identity is incomplete.");
        }

        if (operation.OperationType is BitwardenMutationOperationType.Update or BitwardenMutationOperationType.Delete &&
            string.IsNullOrWhiteSpace(operation.ExpectedRemoteRevision))
        {
            throw new BitwardenProtocolException(
                "Bitwarden update and delete mutations require an expected remote revision.");
        }

        if (operation.OperationType == BitwardenMutationOperationType.Create &&
            operation.ExpectedRemoteRevision is not null)
        {
            throw new BitwardenProtocolException("Bitwarden create mutations must not include a remote revision guard.");
        }
    }

    public static void ValidateResponse(
        BitwardenPendingOperation operation,
        BitwardenMutationResponse response)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(response);
        if (!response.Succeeded)
        {
            return;
        }

        if (operation.OperationType != BitwardenMutationOperationType.Delete &&
            string.IsNullOrWhiteSpace(response.RemoteRevision))
        {
            throw new BitwardenProtocolException(
                "A successful Bitwarden create or update response must include a remote revision.");
        }
    }
}
