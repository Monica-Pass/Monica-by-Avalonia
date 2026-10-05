using System.Globalization;
using System.Text;
using Monica.Core.Bitwarden;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Bitwarden;
using Monica.Data.Repositories;

namespace Monica.Tests;

public sealed class BitwardenStuckEraseServiceTests
{
    // The list is a decision prompt, so what it leaves out matters as much as what it shows. A delete still
    // in the queue is not stuck - the next synchronization may yet carry it - and a trashing that would not
    // land leaves the local row in the recycle bin bound to its cipher, where the pull still sees the user's
    // choice. "Stuck" names the terminal state only; what the user may abandon is wider (a still-retrying
    // erase is a decision they can take back too), and the id re-read keeps both honest without conflating
    // the two.
    [Fact]
    public async Task Only_a_hard_delete_the_server_would_not_take_is_listed()
    {
        var harness = await CreateHarnessAsync();
        var service = new BitwardenStuckEraseService(harness.Pending);
        var now = new DateTimeOffset(2026, 7, 22, 7, 0, 0, TimeSpan.Zero);
        var stuckId = await BookAsync(harness, "cipher-stuck", BitwardenMutationOperationType.Delete, now);
        var pendingId = await BookAsync(
            harness,
            "cipher-pending",
            BitwardenMutationOperationType.Delete,
            now,
            now + TimeSpan.FromHours(1));
        var flyingId = await BookAsync(harness, "cipher-flying", BitwardenMutationOperationType.Delete, now);
        var grantedId = await BookAsync(harness, "cipher-granted", BitwardenMutationOperationType.Delete, now);
        var trashId = await BookAsync(harness, "cipher-trash", BitwardenMutationOperationType.SoftDelete, now);
        await harness.Pending.RecordFailureAsync(stuckId, BitwardenFailureClass.Conflict, "edited elsewhere", now);
        await harness.Pending.RecordFailureAsync(trashId, BitwardenFailureClass.Conflict, "edited elsewhere", now);
        await harness.Pending.CompleteAsync(grantedId);
        _ = await harness.Pending.ClaimReadyAsync(harness.VaultId, now);

        var row = Assert.Single(await service.GetStuckAsync(harness.VaultId));

        Assert.Equal(stuckId, row.OperationId);
        Assert.Equal("cipher-stuck", row.CipherId);
        Assert.Equal(BitwardenMutationStatus.Conflict, row.Status);
        Assert.Equal(now.ToUniversalTime(), row.LastAttemptAt);

        // A still-retrying erase is abandonable elsewhere (that is a decision of its own), so the rows this
        // test still expects to refuse are the ones that are no live decision at all from this list: the one
        // the queue mid-send owns, the soft delete whose local row still sits in the trash, the one already
        // granted, and the right row asked against the wrong vault.
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.AbandonAsync(harness.VaultId, flyingId));
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.AbandonAsync(harness.VaultId, trashId));
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.AbandonAsync(harness.VaultId, grantedId));
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.AbandonAsync(harness.VaultId + 1, stuckId));

        Assert.Empty(await service.GetStuckAsync(harness.VaultId + 1));
        var rows = await harness.Pending.GetAsync(harness.VaultId);
        Assert.Equal(
            BitwardenMutationStatus.Pending,
            rows.Single(operation => operation.Id == pendingId).Status);
        Assert.Equal(
            BitwardenMutationStatus.InFlight,
            rows.Single(operation => operation.Id == flyingId).Status);
        Assert.Equal(
            BitwardenMutationStatus.Conflict,
            rows.Single(operation => operation.Id == trashId).Status);

        await service.AbandonAsync(harness.VaultId, stuckId);

        Assert.Empty(await service.GetStuckAsync(harness.VaultId));
        var after = await harness.Pending.GetAsync(harness.VaultId);
        Assert.Equal(
            BitwardenMutationStatus.Completed,
            after.Single(operation => operation.Id == stuckId).Status);
    }

    // The whole point of the decision: the queue row is what #110's suppression reads, so dropping the debt
    // has to hand the entry back through the ordinary pull rather than leave it in limbo. Measured against a
    // real Vaultwarden, this is the state a guarded erase lands in when another client edits the cipher
    // first - the server refuses the delete, the entry stays gone here, and nothing retries.
    [Fact]
    public async Task Abandoning_an_erase_hands_the_server_copy_back_on_the_next_pull()
    {
        var harness = await CreateHarnessAsync();
        var merge = new BitwardenPullMergeService(
            harness.Repository,
            harness.FolderStore,
            harness.ConflictStore,
            harness.SyncState,
            harness.Pending);
        var service = new BitwardenStuckEraseService(harness.Pending);
        var now = new DateTimeOffset(2026, 7, 22, 7, 0, 0, TimeSpan.Zero);
        var erase = await PurgeAndBookTheEraseAsync(harness, "cipher-erase", "Erased on this device");
        await harness.Pending.RecordFailureAsync(erase.Id, BitwardenFailureClass.Conflict, null, now);
        var remote = DecodedPassword("cipher-erase", "Kept by the server", "2026-07-22T05:00:00Z");

        var suppressed = await merge.ApplyAsync(
            harness.VaultId,
            Snapshot([remote.Metadata], "2026-07-22T05:01:00Z"),
            [remote]);
        Assert.Equal(0, suppressed.Added);
        Assert.Equal(1, suppressed.SuppressedResurrections);
        Assert.Empty(await harness.Repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true));
        Assert.Single(await service.GetStuckAsync(harness.VaultId));

        await service.AbandonAsync(harness.VaultId, erase.Id);

        Assert.Empty(await service.GetStuckAsync(harness.VaultId));
        var restored = await merge.ApplyAsync(
            harness.VaultId,
            Snapshot([remote.Metadata], "2026-07-22T05:02:00Z"),
            [remote]);
        Assert.Equal(1, restored.Added);
        Assert.Equal(0, restored.SuppressedResurrections);
        var entry = Assert.Single(
            await harness.Repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true));
        Assert.Equal("Kept by the server", entry.Title);

        // And the entry is erasable again for real: what stuck the first delete was a revision the server
        // had already moved past, so the copy the pull wrote back carries one it will accept.
        await new BitwardenPurgeQueue(harness.Pending).EnqueuePasswordAsync(entry);
        await harness.Repository.DeletePasswordPermanentlyAsync(entry.Id);
        Assert.Empty(await harness.Repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true));
        var rebooked = (await harness.Pending.GetAsync(harness.VaultId))
            .Single(operation => operation.CipherId == "cipher-erase" &&
                                 operation.Status != BitwardenMutationStatus.Completed);
        Assert.Equal(BitwardenMutationStatus.Pending, rebooked.Status);
        Assert.Equal(entry.BitwardenRevisionDate, rebooked.ExpectedRemoteRevision);
    }

    // The suppressed list is wider than "stuck": it is the full set of erases that can currently stand
    // between the server's copy and the user's screen. A genuinely stuck erase and a still-pending one that
    // just held back a resurrection are the same kind of decision - "the server keeps a copy you deleted
    // here" - and both have to be visible, because the pending one's suppression is exactly as durable.
    [Fact]
    public async Task The_suppressed_list_covers_stuck_and_still_retrying_erasures_this_round_held_back()
    {
        var harness = await CreateHarnessAsync();
        var service = new BitwardenStuckEraseService(harness.Pending);
        var now = new DateTimeOffset(2026, 7, 22, 7, 0, 0, TimeSpan.Zero);
        var stuckId = await BookAsync(harness, "cipher-stuck", BitwardenMutationOperationType.Delete, now);
        var pendingId = await BookAsync(
            harness,
            "cipher-pending",
            BitwardenMutationOperationType.Delete,
            now,
            now + TimeSpan.FromHours(1));
        var untouchedId = await BookAsync(harness, "cipher-untouched", BitwardenMutationOperationType.Delete, now, now + TimeSpan.FromHours(1));
        var trashId = await BookAsync(harness, "cipher-trash", BitwardenMutationOperationType.SoftDelete, now);
        await harness.Pending.RecordFailureAsync(stuckId, BitwardenFailureClass.Conflict, "edited elsewhere", now);
        await harness.Pending.RecordFailureAsync(trashId, BitwardenFailureClass.Conflict, "edited elsewhere", now);

        // This round the merge engine held back two ciphers: the stuck one and the still-pending one. The
        // untouched pending delete and the soft delete are unrelated to what was suppressed, so neither
        // belongs on the list.
        var rows = await service.GetSuppressedAsync(
            harness.VaultId,
            new HashSet<string>(StringComparer.Ordinal) { "cipher-stuck", "cipher-pending" });

        Assert.Equal(2, rows.Count);
        var stuck = Assert.Single(rows, row => row.CipherId == "cipher-stuck");
        Assert.True(stuck.SuppressedThisRound);
        Assert.Equal(stuckId, stuck.OperationId);
        var pending = Assert.Single(rows, row => row.CipherId == "cipher-pending");
        Assert.True(pending.SuppressedThisRound);
        Assert.Equal(pendingId, pending.OperationId);
        Assert.DoesNotContain(rows, row => row.CipherId == "cipher-untouched");
        Assert.DoesNotContain(rows, row => row.CipherId == "cipher-trash");
    }

    // The suppression flag belongs to this round's merge, not to the erase. A stuck erase that held nothing
    // back this time still renders - that is its whole point - but unflagged, so the reason text reads as
    // the durable refusal it is rather than a resurrection the user never saw. Without the distinction the
    // two stories flatten into one and the list lies about which entries are actively being held back.
    [Fact]
    public async Task A_stuck_erase_that_held_nothing_back_is_listed_unflagged()
    {
        var harness = await CreateHarnessAsync();
        var service = new BitwardenStuckEraseService(harness.Pending);
        var now = new DateTimeOffset(2026, 7, 22, 7, 0, 0, TimeSpan.Zero);
        var erase = await PurgeAndBookTheEraseAsync(harness, "cipher-erase", "Erased on this device");
        await harness.Pending.RecordFailureAsync(erase.Id, BitwardenFailureClass.Conflict, null, now);

        var flagged = await service.GetSuppressedAsync(
            harness.VaultId,
            new HashSet<string>(StringComparer.Ordinal));
        var unflaggedView = Assert.Single(flagged);
        Assert.False(unflaggedView.SuppressedThisRound);
    }

    // The decision is per cipher, and so is the flag: one erase may have just held back a resurrection while
    // another, unrelated stuck erase did not. Both belong on the list, but only the one that actually
    // suppressed a copy reads as "the server still holds it" - conflating them would tell the user a second
    // entry is being held back that plainly is not.
    [Fact]
    public async Task The_flag_tracks_which_cipher_was_actually_held_back_this_round()
    {
        var harness = await CreateHarnessAsync();
        var service = new BitwardenStuckEraseService(harness.Pending);
        var now = new DateTimeOffset(2026, 7, 22, 7, 0, 0, TimeSpan.Zero);
        var heldBackId = await BookAsync(harness, "cipher-held", BitwardenMutationOperationType.Delete, now);
        var justStuckId = await BookAsync(harness, "cipher-other", BitwardenMutationOperationType.Delete, now);
        await harness.Pending.RecordFailureAsync(heldBackId, BitwardenFailureClass.Conflict, null, now);
        await harness.Pending.RecordFailureAsync(justStuckId, BitwardenFailureClass.Conflict, null, now);

        var rows = await service.GetSuppressedAsync(
            harness.VaultId,
            new HashSet<string>(StringComparer.Ordinal) { "cipher-held" });

        Assert.True(Assert.Single(rows, row => row.CipherId == "cipher-held").SuppressedThisRound);
        Assert.False(Assert.Single(rows, row => row.CipherId == "cipher-other").SuppressedThisRound);
    }

    // A still-pending erase can be abandoned only while it is alive in the queue - completing it retires
    // the suppression, which is the whole point. But an in-flight erase is mid-send, and completing it from
    // under the queue would drop a delete the server is about to answer. The re-read by id, against the live
    // operations rather than a stale screen, is what refuses the one the queue owns.
    [Fact]
    public async Task Abandoning_a_pending_erase_works_but_an_in_flight_one_is_refused()
    {
        var harness = await CreateHarnessAsync();
        var service = new BitwardenStuckEraseService(harness.Pending);
        var now = new DateTimeOffset(2026, 7, 22, 7, 0, 0, TimeSpan.Zero);
        var pendingId = await BookAsync(
            harness,
            "cipher-pending",
            BitwardenMutationOperationType.Delete,
            now,
            now + TimeSpan.FromHours(1));
        var flyingId = await BookAsync(harness, "cipher-flying", BitwardenMutationOperationType.Delete, now);
        _ = await harness.Pending.ClaimReadyAsync(harness.VaultId, now);

        await service.AbandonAsync(harness.VaultId, pendingId);
        Assert.Equal(
            BitwardenMutationStatus.Completed,
            (await harness.Pending.GetAsync(harness.VaultId))
            .Single(operation => operation.Id == pendingId).Status);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.AbandonAsync(harness.VaultId, flyingId));
    }

    // The list is rendered while a synchronization is running, and a second purge of the same cipher reuses
    // the queue's idempotency key - which lifts a Conflict row back to pending. The screen is then showing a
    // stale row: it still reads "the server refused it" while the queue is about to try the delete again.
    // Re-reading by id is what stops an abandon made against that stale text from landing on a row whose
    // meaning already changed under it.
    [Fact]
    public async Task A_rebooked_erase_disappears_from_the_stuck_list_it_no_longer_matches()
    {
        var harness = await CreateHarnessAsync();
        var service = new BitwardenStuckEraseService(harness.Pending);
        var now = new DateTimeOffset(2026, 7, 22, 7, 0, 0, TimeSpan.Zero);
        var erase = await PurgeAndBookTheEraseAsync(harness, "cipher-erase", "Erased on this device");
        await harness.Pending.RecordFailureAsync(erase.Id, BitwardenFailureClass.Conflict, null, now);
        Assert.Single(await service.GetStuckAsync(harness.VaultId));

        var carried = new PasswordEntry
        {
            Title = "Erased on this device",
            Password = "local-password",
            BitwardenVaultId = harness.VaultId,
            BitwardenCipherId = "cipher-erase",
            BitwardenRevisionDate = "2026-07-21T00:00:00Z",
            BitwardenCipherType = 1
        };
        Assert.True(await new BitwardenPurgeQueue(harness.Pending).EnqueuePasswordAsync(carried));

        // The re-purge lifted the row back to pending, so it is no longer a stuck erase: the list of
        // decisions the server refused shrinks to nothing, because the queue is now set to retry the very
        // delete the refused row described.
        Assert.Empty(await service.GetStuckAsync(harness.VaultId));
        Assert.Equal(
            BitwardenMutationStatus.Pending,
            Assert.Single(await harness.Pending.GetAsync(harness.VaultId)).Status);
    }

    // The re-read by id is not what refuses a re-booked erase - a re-purged pending erase is, from the
    // user's seat, just another live delete they are allowed to take back, so abandoning it is the same
    // decision as abandoning any still-retrying one. What the id re-read refuses is the cases that are not
    // a live decision at all: a cipher the queue mid-send owns, and an operation id that no longer belongs
    // to this vault. Those cannot be taken back from here without dropping work the server is answering.
    [Fact]
    public async Task The_id_re_read_refuses_only_what_is_not_a_live_decision()
    {
        var harness = await CreateHarnessAsync();
        var service = new BitwardenStuckEraseService(harness.Pending);
        var now = new DateTimeOffset(2026, 7, 22, 7, 0, 0, TimeSpan.Zero);
        var flyingId = await BookAsync(harness, "cipher-flying", BitwardenMutationOperationType.Delete, now);
        var completedId = await BookAsync(harness, "cipher-done", BitwardenMutationOperationType.Delete, now);
        await harness.Pending.CompleteAsync(completedId);
        _ = await harness.Pending.ClaimReadyAsync(harness.VaultId, now + TimeSpan.FromMinutes(1));

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.AbandonAsync(harness.VaultId, flyingId));
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.AbandonAsync(harness.VaultId, completedId));
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.AbandonAsync(harness.VaultId + 1, flyingId));
    }

    private static async Task<long> BookAsync(
        Harness harness,
        string cipherId,
        BitwardenMutationOperationType operationType,
        DateTimeOffset now,
        DateTimeOffset? nextAttemptAt = null) =>
        await harness.Pending.EnqueueAsync(new BitwardenPendingOperation(
            Id: 0,
            VaultId: harness.VaultId,
            CipherId: cipherId,
            OperationType: operationType,
            ExpectedRemoteRevision: "2026-07-21T00:00:00Z",
            PayloadJson: "{}",
            IdempotencyKey: $"test:{harness.VaultId}:{cipherId}",
            Status: BitwardenMutationStatus.Pending,
            LastFailureClass: BitwardenFailureClass.None,
            AttemptCount: 0,
            NextAttemptAt: nextAttemptAt ?? now,
            ClaimedAt: null,
            LastError: null,
            CreatedAt: now,
            UpdatedAt: now));

    private static async Task<BitwardenPendingOperation> PurgeAndBookTheEraseAsync(
        Harness harness,
        string cipherId,
        string title)
    {
        var entry = new PasswordEntry
        {
            Title = title,
            Password = "local-password",
            BitwardenVaultId = harness.VaultId,
            BitwardenCipherId = cipherId,
            BitwardenRevisionDate = "2026-07-21T00:00:00Z",
            BitwardenCipherType = 1
        };
        await harness.Repository.SavePasswordAsync(entry);
        Assert.True(await new BitwardenPurgeQueue(harness.Pending).EnqueuePasswordAsync(entry));
        await harness.Repository.DeletePasswordPermanentlyAsync(entry.Id);
        return (await harness.Pending.GetAsync(harness.VaultId))
            .Single(operation => operation.CipherId == cipherId);
    }

    private static BitwardenPullSnapshot Snapshot(
        IReadOnlyList<BitwardenRemoteCipherMetadata> ciphers,
        string revision) =>
        new([], ciphers, revision, true, DateTimeOffset.Parse(revision, CultureInfo.InvariantCulture));

    private static BitwardenDecodedCipher DecodedPassword(string cipherId, string title, string revision)
    {
        var password = new PasswordEntry
        {
            Title = title,
            Username = "remote-user",
            Password = "remote-password",
            BitwardenCipherId = cipherId,
            BitwardenRevisionDate = revision,
            BitwardenCipherType = 1
        };
        return new BitwardenDecodedCipher(
            new BitwardenRemoteCipherMetadata(
                cipherId,
                null,
                revision,
                1,
                false,
                BitwardenPayloadFingerprint.ForPassword(password, [], [])),
            password,
            null,
            [],
            []);
    }

    private static async Task<Harness> CreateHarnessAsync()
    {
        var factory = new SqliteConnectionFactory(TestTempPaths.CreateFilePath(".db"));
        var migrator = new DatabaseMigrator(factory);
        var crypto = new CryptoService();
        var hash = crypto.HashMasterPassword("vault password");
        crypto.InitializeSession("vault password", hash.Salt);
        var protector = new VaultDataProtector(crypto);
        var repository = new MonicaRepository(factory, migrator, protector);
        var endpoints = BitwardenEndpointSet.UnitedStates;
        using var secrets = new BitwardenAccountSecrets(
            Encoding.UTF8.GetBytes("access"),
            Encoding.UTF8.GetBytes("refresh"),
            new byte[32],
            Enumerable.Repeat((byte)1, 32).ToArray(),
            Enumerable.Repeat((byte)2, 32).ToArray());
        var account = await new BitwardenAccountStore(factory, migrator, crypto).SaveConnectedAsync(
            new BitwardenAccount
            {
                Email = "stuck@example.com",
                AccountKey = BitwardenAccountIdentity.CreateAccountKey("stuck@example.com", endpoints),
                Endpoints = endpoints,
                Kdf = BitwardenKdfParameters.Pbkdf2()
            },
            secrets);
        return new Harness(
            repository,
            new BitwardenRemoteFolderStore(factory, migrator, crypto),
            new BitwardenConflictBackupStore(factory, migrator, crypto),
            new BitwardenSyncStateStore(factory, migrator),
            new BitwardenPendingOperationStore(factory, migrator, crypto),
            account.Id);
    }

    private sealed record Harness(
        IMonicaRepository Repository,
        IBitwardenRemoteFolderStore FolderStore,
        IBitwardenConflictBackupStore ConflictStore,
        IBitwardenSyncStateStore SyncState,
        IBitwardenPendingOperationStore Pending,
        long VaultId);
}
