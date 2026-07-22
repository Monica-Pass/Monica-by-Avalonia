using Monica.Core.Bitwarden;

namespace Monica.Tests;

public sealed class BitwardenMergeTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 7, 22, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SafetyBlocksIncompleteEmptyAndSharpReductionSnapshots()
    {
        var incomplete = Snapshot([], false);
        Assert.Equal(
            BitwardenPullBlockReason.IncompleteSnapshot,
            BitwardenPullSafetyEvaluator.Evaluate(incomplete, 4).BlockReason);

        var empty = Snapshot([], true);
        Assert.Equal(
            BitwardenPullBlockReason.EmptyRemoteVault,
            BitwardenPullSafetyEvaluator.Evaluate(empty, 1).BlockReason);

        var reduced = Snapshot(
            Enumerable.Range(0, 4).Select(value => Remote($"remote-{value}")).ToArray(),
            true);
        var result = BitwardenPullSafetyEvaluator.Evaluate(reduced, 10);
        Assert.False(result.CanApply);
        Assert.Equal(BitwardenPullBlockReason.SharpDataReduction, result.BlockReason);
    }

    [Fact]
    public void SafetyRejectsDuplicateInvalidAndUnsupportedRemoteCiphers()
    {
        var duplicate = Snapshot([Remote("same"), Remote("same")], true);
        Assert.Equal(
            BitwardenPullBlockReason.DuplicateRemoteCipher,
            BitwardenPullSafetyEvaluator.Evaluate(duplicate, 0).BlockReason);

        var invalid = Snapshot([Remote("invalid", revision: "not-a-date")], true);
        Assert.Equal(
            BitwardenPullBlockReason.InvalidRemoteRevision,
            BitwardenPullSafetyEvaluator.Evaluate(invalid, 0).BlockReason);

        var unsupported = Snapshot([Remote("unsupported", cipherType: 99)], true);
        Assert.Equal(
            BitwardenPullBlockReason.UnsupportedCipherType,
            BitwardenPullSafetyEvaluator.Evaluate(unsupported, 0).BlockReason);
    }

    [Fact]
    public void MergePlanIsStableAndPreservesDirtyLocalDataThroughConflictBackup()
    {
        var remote = new[]
        {
            Remote("a-new", revision: "2026-07-22T00:00:01Z"),
            Remote("b-update", revision: "2026-07-22T00:00:02Z"),
            Remote("c-conflict", revision: "2026-07-22T00:00:03Z"),
            Remote("d-delete", revision: "2026-07-22T00:00:04Z", isDeleted: true),
            Remote("e-clean", revision: "2026-07-22T00:00:05Z", payloadHash: "same"),
            Remote("remote-deleted", revision: "2026-07-22T00:00:06Z", isDeleted: true)
        };
        var local = new[]
        {
            Local(10, "b-update", revision: "2026-07-21T00:00:00Z"),
            Local(11, "c-conflict", revision: "2026-07-21T00:00:00Z", modified: true),
            Local(12, "d-delete", revision: "2026-07-21T00:00:00Z"),
            Local(13, "e-clean", revision: "2026-07-22T00:00:05Z", payloadHash: "same", modified: true),
            Local(14, "local-only", revision: "2026-07-20T00:00:00Z")
        };

        var decisions = BitwardenMergeEngine.Plan(Snapshot(remote, true), local);

        Assert.Equal(
            ["a-new", "b-update", "c-conflict", "d-delete", "e-clean", "local-only"],
            decisions.Select(decision => decision.CipherId));
        Assert.Equal(BitwardenMergeAction.AddRemote, decisions[0].Action);
        Assert.Equal(BitwardenMergeAction.ApplyRemoteUpdate, decisions[1].Action);
        Assert.Equal(BitwardenMergeAction.CreateConflictBackupThenApplyRemote, decisions[2].Action);
        Assert.Equal(BitwardenMergeAction.ApplyRemoteDeletion, decisions[3].Action);
        Assert.Equal(BitwardenMergeAction.MarkLocalClean, decisions[4].Action);
        Assert.Equal(BitwardenMergeAction.PreserveLocalUnmatched, decisions[5].Action);
    }

    private static BitwardenPullSnapshot Snapshot(
        IReadOnlyList<BitwardenRemoteCipherMetadata> ciphers,
        bool isComplete) =>
        new([], ciphers, "2026-07-22T00:00:00Z", isComplete, ReceivedAt);

    private static BitwardenRemoteCipherMetadata Remote(
        string id,
        string revision = "2026-07-22T00:00:00Z",
        int cipherType = 1,
        bool isDeleted = false,
        string payloadHash = "remote") =>
        new(id, null, revision, cipherType, isDeleted, payloadHash);

    private static BitwardenLocalCipherReference Local(
        long id,
        string cipherId,
        string revision,
        bool modified = false,
        string payloadHash = "local") =>
        new(id, cipherId, null, revision, 1, false, modified, payloadHash, "password", ReceivedAt);
}
