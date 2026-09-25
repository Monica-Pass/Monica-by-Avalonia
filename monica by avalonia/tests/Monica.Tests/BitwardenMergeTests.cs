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

    // The guard is there for a payload that went silent, not for a vault the user cleaned out: a complete
    // response still names every trashed cipher, and the merge can only move the matching local rows into
    // Monica's own recoverable trash. Blocking those snapshots failed every later synchronization.
    [Fact]
    public void SafetyAllowsRemoteDeletionsAndStillBlocksSilentPayloads()
    {
        var allTrashed = Snapshot(
            [Remote("a", isDeleted: true, payloadHash: "deleted:2026-07-22T00:00:00Z")],
            true);
        Assert.True(BitwardenPullSafetyEvaluator.Evaluate(allTrashed, 5).CanApply);

        Assert.Equal(
            BitwardenPullBlockReason.EmptyRemoteVault,
            BitwardenPullSafetyEvaluator.Evaluate(Snapshot([], true), 1).BlockReason);

        var sharpReduction = Snapshot(
            Enumerable.Range(0, 4)
                .Select(value => Remote($"remote-{value}", isDeleted: true))
                .ToArray(),
            true);
        Assert.Equal(
            BitwardenPullBlockReason.SharpDataReduction,
            BitwardenPullSafetyEvaluator.Evaluate(sharpReduction, 10).BlockReason);
    }

    [Fact]
    public void BothSidesHoldingTheSameTrashIsNoChange()
    {
        var remote = Remote(
            "a",
            revision: "2026-07-22T00:00:01Z",
            isDeleted: true,
            payloadHash: "deleted:2026-07-22T00:00:01Z");
        var trashedLocal = Local(1, "a", revision: "2026-07-22T00:00:01Z") with { IsDeleted = true };

        var decision = Assert.Single(BitwardenMergeEngine.Plan(Snapshot([remote], true), [trashedLocal]));

        Assert.Equal(BitwardenMergeAction.NoChange, decision.Action);
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

    // A bound row with no remote revision cannot prove the server has not moved, so the merge has nothing
    // to compare its content against. Measured against Vaultwarden 1.37.3, an edit made while in that state
    // came back as merge[Updated=1/ConflictsBackedUp=0] with zero conflict records for the cipher: the pull
    // destroyed the local content on the same round the queue refused to upload it, silently.
    [Fact]
    public void AnUnguardedLocalRowIsBackedUpBeforeTheRemoteOverwritesIt()
    {
        var decisions = BitwardenMergeEngine.Plan(
            Snapshot([Remote("a-unguarded", revision: "2026-07-22T00:00:01Z")], true),
            [Local(20, "a-unguarded", revision: "")]);

        Assert.Equal(
            BitwardenMergeAction.CreateConflictBackupThenApplyRemote,
            decisions.Single().Action);
    }

    // The guard is only worth a backup when there is something to keep. Content identical to the server's
    // needs no record of a change nobody made, and filing one every round would fill the conflict list with
    // noise the user has to dismiss.
    [Fact]
    public void AnUnguardedLocalRowWithNothingToKeepTakesTheRemoteWithoutABackup()
    {
        var decisions = BitwardenMergeEngine.Plan(
            Snapshot([Remote("a-same", revision: "2026-07-22T00:00:01Z", payloadHash: "same")], true),
            [Local(21, "a-same", revision: "", payloadHash: "same")]);

        Assert.Equal(BitwardenMergeAction.ApplyRemoteUpdate, decisions.Single().Action);
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
