namespace Monica.Core.Bitwarden;

public static class BitwardenMergeEngine
{
    public static IReadOnlyList<BitwardenMergeDecision> Plan(
        BitwardenPullSnapshot snapshot,
        IReadOnlyList<BitwardenLocalCipherReference> localItems)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(localItems);
        var safety = BitwardenPullSafetyEvaluator.Evaluate(snapshot, localItems.Count(item => !item.IsDeleted));
        if (!safety.CanApply)
        {
            throw new BitwardenProtocolException(safety.Message);
        }

        var remoteById = BuildUniqueRemoteMap(snapshot.Ciphers);
        var localById = BuildUniqueLocalMap(localItems);
        var decisions = new List<BitwardenMergeDecision>(remoteById.Count + localById.Count);

        foreach (var remote in remoteById.Values.OrderBy(item => item.CipherId, StringComparer.Ordinal))
        {
            if (!localById.TryGetValue(remote.CipherId, out var local))
            {
                if (!remote.IsDeleted)
                {
                    decisions.Add(new(
                        BitwardenMergeAction.AddRemote,
                        remote.CipherId,
                        null,
                        remote.CipherType,
                        null,
                        remote.RevisionDate,
                        "Remote cipher has no local identity."));
                }

                continue;
            }

            decisions.Add(PlanExisting(local, remote));
        }

        foreach (var local in localById.Values
                     .Where(local => !remoteById.ContainsKey(local.CipherId))
                     .OrderBy(item => item.CipherId, StringComparer.Ordinal))
        {
            decisions.Add(new(
                BitwardenMergeAction.PreserveLocalUnmatched,
                local.CipherId,
                local.LocalId,
                local.CipherType,
                local.RevisionDate,
                null,
                "Remote snapshot does not contain the local identity; local data is preserved without scheduling an upload."));
        }

        return decisions;
    }

    private static BitwardenMergeDecision PlanExisting(
        BitwardenLocalCipherReference local,
        BitwardenRemoteCipherMetadata remote)
    {
        var sameRevision = string.Equals(local.RevisionDate, remote.RevisionDate, StringComparison.Ordinal);
        var samePayload = string.Equals(local.PayloadHash, remote.PayloadHash, StringComparison.Ordinal);
        var sameState = local.IsDeleted == remote.IsDeleted &&
                        local.CipherType == remote.CipherType &&
                        string.Equals(local.FolderId, remote.FolderId, StringComparison.Ordinal) &&
                        samePayload;

        if (sameRevision && sameState)
        {
            return new(
                local.LocalModified
                    ? BitwardenMergeAction.MarkLocalClean
                    : BitwardenMergeAction.NoChange,
                remote.CipherId,
                local.LocalId,
                remote.CipherType,
                local.RevisionDate,
                remote.RevisionDate,
                local.LocalModified
                    ? "Local changes already share the remote revision and can be marked clean."
                    : "Remote revision and content match local state.");
        }

        if (local.LocalModified)
        {
            return new(
                BitwardenMergeAction.CreateConflictBackupThenApplyRemote,
                remote.CipherId,
                local.LocalId,
                remote.CipherType,
                local.RevisionDate,
                remote.RevisionDate,
                "Local changes differ from the remote revision; preserve a conflict backup before applying remote state.");
        }

        return new(
            remote.IsDeleted
                ? BitwardenMergeAction.ApplyRemoteDeletion
                : BitwardenMergeAction.ApplyRemoteUpdate,
            remote.CipherId,
            local.LocalId,
            remote.CipherType,
            local.RevisionDate,
            remote.RevisionDate,
            remote.IsDeleted
                ? "Remote deletion is applied to an unchanged local item."
                : "Remote revision or content differs from unchanged local state.");
    }

    private static Dictionary<string, BitwardenRemoteCipherMetadata> BuildUniqueRemoteMap(
        IReadOnlyList<BitwardenRemoteCipherMetadata> ciphers)
    {
        var map = new Dictionary<string, BitwardenRemoteCipherMetadata>(StringComparer.Ordinal);
        foreach (var cipher in ciphers)
        {
            if (!map.TryAdd(cipher.CipherId, cipher))
            {
                throw new BitwardenProtocolException(
                    $"Duplicate Bitwarden remote cipher identity: {cipher.CipherId}.");
            }
        }

        return map;
    }

    private static Dictionary<string, BitwardenLocalCipherReference> BuildUniqueLocalMap(
        IReadOnlyList<BitwardenLocalCipherReference> items)
    {
        var map = new Dictionary<string, BitwardenLocalCipherReference>(StringComparer.Ordinal);
        foreach (var item in items.Where(item => !string.IsNullOrWhiteSpace(item.CipherId)))
        {
            if (!map.TryAdd(item.CipherId, item))
            {
                throw new BitwardenProtocolException(
                    $"Duplicate Bitwarden local cipher identity: {item.CipherId}.");
            }
        }

        return map;
    }
}
