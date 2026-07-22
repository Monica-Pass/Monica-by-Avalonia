namespace Monica.Core.Bitwarden;

public static class BitwardenPullSafetyEvaluator
{
    public const int MinimumCountForReductionGuard = 10;
    private static readonly HashSet<int> SupportedCipherTypes = [1, 2, 3, 4, 5];

    public static BitwardenPullSafetyResult Evaluate(
        BitwardenPullSnapshot snapshot,
        int localCipherCount)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (localCipherCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(localCipherCount));
        }

        var remoteIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cipher in snapshot.Ciphers)
        {
            if (string.IsNullOrWhiteSpace(cipher.CipherId) || !remoteIds.Add(cipher.CipherId))
            {
                return Block(
                    BitwardenPullBlockReason.DuplicateRemoteCipher,
                    "The remote Bitwarden snapshot contains duplicate or empty cipher identities.",
                    localCipherCount,
                    snapshot.ActiveCipherCount);
            }

            if (!SupportedCipherTypes.Contains(cipher.CipherType))
            {
                return Block(
                    BitwardenPullBlockReason.UnsupportedCipherType,
                    $"The remote Bitwarden snapshot contains unsupported cipher type {cipher.CipherType}.",
                    localCipherCount,
                    snapshot.ActiveCipherCount);
            }

            if (!DateTimeOffset.TryParse(
                    cipher.RevisionDate,
                    null,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out _))
            {
                return Block(
                    BitwardenPullBlockReason.InvalidRemoteRevision,
                    "The remote Bitwarden snapshot contains an invalid revision date.",
                    localCipherCount,
                    snapshot.ActiveCipherCount);
            }
        }

        if (!snapshot.IsComplete)
        {
            return Block(
                BitwardenPullBlockReason.IncompleteSnapshot,
                "The Bitwarden snapshot is incomplete and cannot replace local state.",
                localCipherCount,
                snapshot.ActiveCipherCount);
        }

        if (localCipherCount > 0 && snapshot.ActiveCipherCount == 0)
        {
            return Block(
                BitwardenPullBlockReason.EmptyRemoteVault,
                "The remote Bitwarden vault is empty while local items exist.",
                localCipherCount,
                snapshot.ActiveCipherCount);
        }

        if (localCipherCount >= MinimumCountForReductionGuard &&
            snapshot.ActiveCipherCount * 2 < localCipherCount)
        {
            return Block(
                BitwardenPullBlockReason.SharpDataReduction,
                "The remote Bitwarden snapshot contains substantially fewer items than local state.",
                localCipherCount,
                snapshot.ActiveCipherCount);
        }

        return new BitwardenPullSafetyResult(
            true,
            BitwardenPullBlockReason.None,
            "The complete Bitwarden snapshot passed data-loss protection checks.",
            localCipherCount,
            snapshot.ActiveCipherCount);
    }

    private static BitwardenPullSafetyResult Block(
        BitwardenPullBlockReason reason,
        string message,
        int localCount,
        int remoteCount) =>
        new(false, reason, message, localCount, remoteCount);
}
