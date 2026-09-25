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

/// <summary>
/// The outcome of one synchronization. <paramref name="Unsyncable"/> carries the local rows Bitwarden could
/// not take, because a sync that booked nothing and merged nothing still told the truth only if the reason is
/// said: without it the vault looks synchronized while the same edit is refused on every round from here.
/// </summary>
public sealed record BitwardenSyncResult(
    BitwardenAccount Account,
    BitwardenMutationBatchResult Mutations,
    BitwardenPullMergeResult Merge,
    IReadOnlyList<BitwardenUnsyncableLocalChange> Unsyncable)
{
    public BitwardenSyncResult(
        BitwardenAccount Account,
        BitwardenMutationBatchResult Mutations,
        BitwardenPullMergeResult Merge)
        : this(Account, Mutations, Merge, [])
    {
    }
}

public interface IBitwardenSyncCoordinator
{
    event EventHandler<BitwardenSyncState>? StateChanged;

    BitwardenSyncState GetState(long accountId);

    Task<BitwardenSyncResult> SyncAsync(
        long accountId,
        BitwardenSyncTrigger trigger,
        CancellationToken cancellationToken = default);
}
