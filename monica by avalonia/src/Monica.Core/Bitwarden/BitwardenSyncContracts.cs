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
