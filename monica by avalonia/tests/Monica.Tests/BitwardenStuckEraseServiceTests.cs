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
    // choice. Offering either as "the server did not take your delete" would invite the user to drop work
    // that was about to land, so the command must refuse them by id as well.
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

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.AbandonAsync(harness.VaultId, pendingId));
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

    // The list is rendered while a synchronization is running, and a second purge of the same cipher reuses
    // the queue's idempotency key - which lifts a Conflict row back to pending. Completing the row the screen
    // is still showing would retire the erase the user just asked for a second time, so the row has to be
    // re-read and the stale decision refused.
    [Fact]
    public async Task A_rebooked_erase_is_not_the_row_the_screen_was_showing()
    {
        var harness = await CreateHarnessAsync();
        var service = new BitwardenStuckEraseService(harness.Pending);
        var now = new DateTimeOffset(2026, 7, 22, 7, 0, 0, TimeSpan.Zero);
        var erase = await PurgeAndBookTheEraseAsync(harness, "cipher-erase", "Erased on this device");
        await harness.Pending.RecordFailureAsync(erase.Id, BitwardenFailureClass.Conflict, null, now);
        var row = Assert.Single(await service.GetStuckAsync(harness.VaultId));

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

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.AbandonAsync(harness.VaultId, row.OperationId));
        Assert.Empty(await service.GetStuckAsync(harness.VaultId));
        Assert.Equal(
            BitwardenMutationStatus.Pending,
            Assert.Single(await harness.Pending.GetAsync(harness.VaultId)).Status);
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
