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
        // Both sides holding the same deletion is the end of the story: the remote carries no content to
        // adopt, so the marker it does carry cannot be compared with the local fingerprint. Reading it as a
        // content difference made every later pull re-decide the identical trash as a conflict and back the
        // same row up again.
        var bothTrashed = local.IsDeleted && remote.IsDeleted;
        var sameState = local.IsDeleted == remote.IsDeleted &&
                        local.CipherType == remote.CipherType &&
                        string.Equals(local.FolderId, remote.FolderId, StringComparison.Ordinal) &&
                        (bothTrashed || samePayload);

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

        if (sameRevision && !sameState)
        {
            // The server has not moved past the revision we already hold, so a content difference can
            // only be ours: an edit, move or delete this client made and never uploaded. Back it up
            // before remote wins instead of dropping it - no upload path exists to reconcile it with.
            return new(
                BitwardenMergeAction.CreateConflictBackupThenApplyRemote,
                remote.CipherId,
                local.LocalId,
                remote.CipherType,
                local.RevisionDate,
                remote.RevisionDate,
                "Local state differs at the remote revision; keep the local change recoverable, then apply remote.");
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

        // A row with no remote revision cannot say the server has moved past it, so nothing here proves the
        // differing content is the server's rather than this device's. The change may also be one the upload
        // queue just refused and therefore cannot send, which is how a refusing round used to overwrite an
        // edit with no record left behind. Content identical to the remote is the exception: there is no
        // change to keep, and backing it up would file a conflict that reports nothing.
        if (string.IsNullOrWhiteSpace(local.RevisionDate) && !sameState)
        {
            return new(
                BitwardenMergeAction.CreateConflictBackupThenApplyRemote,
                remote.CipherId,
                local.LocalId,
                remote.CipherType,
                local.RevisionDate,
                remote.RevisionDate,
                "The local row has no remote revision to compare against; keep its content recoverable, then apply remote.");
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
