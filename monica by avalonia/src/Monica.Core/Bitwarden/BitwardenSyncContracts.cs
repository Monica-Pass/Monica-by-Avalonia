namespace Monica.Core.Bitwarden;

public sealed record BitwardenRemoteSyncResult(
    BitwardenPullSnapshot Snapshot,
    IReadOnlyList<BitwardenDecodedCipher> DecodedCiphers,
    string? UserId,
    string? DisplayName);

public interface IBitwardenSyncTransport
{
    Task<BitwardenRemoteSyncResult> DownloadAsync(
        BitwardenAccount account,
        BitwardenAccountSecrets secrets,
        CancellationToken cancellationToken = default);
}

public interface IBitwardenMutationTransportFactory
{
    IBitwardenOwnedMutationTransport Create(
        BitwardenAccount account,
        BitwardenAccountSecrets secrets);
}

public interface IBitwardenOwnedMutationTransport : IBitwardenMutationTransport, IDisposable;

public enum BitwardenSyncTrigger
{
    Manual = 0,
    Background,
    LocalMutation
}

public enum BitwardenSyncPhase
{
    Idle = 0,
    Preparing,
    RefreshingToken,
    Uploading,
    Downloading,
    Applying,
    Completed,
    Failed,
    Locked
}

public sealed record BitwardenSyncState(
    long AccountId,
    BitwardenSyncPhase Phase,
    BitwardenSyncTrigger Trigger,
    DateTimeOffset UpdatedAt,
    string? Message = null);

public sealed record BitwardenSyncResult(
    BitwardenAccount Account,
    BitwardenMutationBatchResult Mutations,
    BitwardenPullMergeResult Merge);

public interface IBitwardenSyncCoordinator
{
    event EventHandler<BitwardenSyncState>? StateChanged;

    BitwardenSyncState GetState(long accountId);

    Task<BitwardenSyncResult> SyncAsync(
        long accountId,
        BitwardenSyncTrigger trigger,
        CancellationToken cancellationToken = default);
}
