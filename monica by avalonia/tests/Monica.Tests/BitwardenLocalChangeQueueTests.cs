using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Monica.Core.Bitwarden;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Bitwarden;
using Monica.Data.Repositories;

namespace Monica.Tests;

/// <summary>
/// A local edit only reaches the server if something turns the changed content into queue work before
/// the pull runs. These drive the real pull so the baseline is whatever a completed synchronization
/// actually left behind, then edit through the real repository and assert what the next sync uploads.
/// </summary>
public sealed class BitwardenLocalChangeQueueTests
{
    private const string BaselineRevision = "2026-07-22T03:00:00Z";
    private const string NextRevision = "2026-07-22T04:00:00Z";

    [Fact]
    public async Task The_baseline_left_by_a_pull_fingerprints_the_same_as_the_stored_row()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);

        var stored = (await harness.Repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true))
            .Single(entry => entry.BitwardenCipherId == "cipher-edit");
        var baseline = await harness.SyncState.GetPayloadHashesAsync(harness.VaultId);

        // A different hash space on either side would make every entry look drifted forever.
        Assert.Equal(
            BitwardenPayloadFingerprint.ForPassword(stored, [], []),
            Assert.Contains("cipher-edit", baseline));
    }

    [Fact]
    public async Task Nothing_is_queued_for_a_vault_that_matches_its_baseline()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);

        var result = await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow);

        Assert.Equal(0, result.Enqueued);
        Assert.Empty(await harness.Pending.GetAsync(harness.VaultId));
    }

    [Fact]
    public async Task An_edit_after_a_sync_is_queued_once_and_carries_the_revision_the_server_had()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);
        var entry = await RenameAsync(harness, "Renamed on this device");
        var expectedHash = BitwardenPayloadFingerprint.ForPassword(entry, [], []);

        var first = await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow);
        var second = await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow);

        Assert.Equal(1, first.Enqueued);
        Assert.Equal(1, second.Enqueued);
        var queued = Assert.Single(await harness.Pending.GetAsync(harness.VaultId));
        Assert.Equal("cipher-edit", queued.CipherId);
        Assert.Equal(BitwardenMutationOperationType.Update, queued.OperationType);
        Assert.Equal(BaselineRevision, queued.ExpectedRemoteRevision);
        Assert.Equal(expectedHash, queued.LocalPayloadHash);
        Assert.Equal(expectedHash, queued.IdempotencyKey.Split(':').Last());
    }

    // Folders are how a Bitwarden vault is organized, and the library page reorganizes by rewriting the
    // local category. The remote folder a row sits in is a projection of that category, so a move has to be
    // read back through the binding table before it is compared with the baseline; measured against the
    // column the move never touched, the reorganization a user did with their hands is the one change the
    // drift scan cannot see.
    [Fact]
    public async Task Moving_an_entry_into_another_remote_folder_is_owed_an_update()
    {
        var harness = await CreateHarnessAsync();
        var remote = FolderCipher("cipher-foldered", "Kept in order", "folder-a");
        await harness.Pull.ApplyAsync(
            harness.VaultId,
            Snapshot([RemoteFolder("folder-a", "First folder"), RemoteFolder("folder-b", "Second folder")], [remote]),
            [remote]);
        var stored = await ReadFolderedAsync(harness);
        var categories = await harness.Repository.GetCategoriesAsync();
        Assert.Equal(
            categories.Single(category => category.BitwardenFolderId == "folder-a").Id,
            stored.CategoryId);

        stored.CategoryId = categories.Single(category => category.BitwardenFolderId == "folder-b").Id;
        await harness.Repository.SavePasswordAsync(stored);

        var result = await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow);

        Assert.Equal(1, result.Enqueued);
        var queued = Assert.Single(await harness.Pending.GetAsync(harness.VaultId));
        Assert.Equal("cipher-foldered", queued.CipherId);
        Assert.Equal(BitwardenMutationOperationType.Update, queued.OperationType);
        Assert.Contains("\"folderId\":\"folder-b\"", queued.PayloadJson, StringComparison.Ordinal);

        // Once the server has taken it, the same move cannot be owed twice: the baseline has to be able to
        // say the server holds what the local category resolves to.
        const string movedRevision = "2026-07-22T05:00:00Z";
        var batch = await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            new AcceptedTransport(movedRevision));
        Assert.Equal(1, batch.Completed);
        Assert.Equal(0, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);

        // The next pull carries the folder the server agreed to. It must leave the row where the user put it
        // and file no conflict for a change the server has already accepted.
        var asServerHolds = stored.CreateDetachedCopy();
        asServerHolds.BitwardenFolderId = "folder-b";
        var asRemote = new BitwardenDecodedCipher(
            remote.Metadata with
            {
                FolderId = "folder-b",
                RevisionDate = movedRevision,
                PayloadHash = BitwardenPayloadFingerprint.ForPassword(asServerHolds, [], [])
            },
            remote.Password,
            null,
            [],
            []);
        var pull = await harness.Pull.ApplyAsync(
            harness.VaultId,
            Snapshot([RemoteFolder("folder-a", "First folder"), RemoteFolder("folder-b", "Second folder")], [asRemote]),
            [asRemote]);

        Assert.Equal(0, pull.ConflictsBackedUp);
        Assert.Equal(
            categories.Single(category => category.BitwardenFolderId == "folder-b").Id,
            (await ReadFolderedAsync(harness)).CategoryId);
    }

    // Moving an entry out of a folder is the other half of the same reorganization, and this one does have a
    // remote answer: Monica's root and Bitwarden's root are the same place. Measured with the payload carrying
    // no folder at all, because a folder id left in the payload would put it back where the user took it from.
    [Fact]
    public async Task Taking_an_entry_out_of_a_folder_owes_the_server_a_move_to_root()
    {
        var harness = await CreateHarnessAsync();
        var remote = FolderCipher("cipher-foldered", "Kept in order", "folder-a");
        await harness.Pull.ApplyAsync(
            harness.VaultId,
            Snapshot([RemoteFolder("folder-a", "First folder")], [remote]),
            [remote]);

        var stored = await ReadFolderedAsync(harness);
        stored.CategoryId = null;
        await harness.Repository.SavePasswordAsync(stored);

        Assert.Equal(1, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
        var queued = Assert.Single(await harness.Pending.GetAsync(harness.VaultId));
        using (var document = JsonDocument.Parse(queued.PayloadJson))
        {
            Assert.False(document.RootElement.TryGetProperty("folderId", out _));
        }

        const string rootRevision = "2026-07-22T05:00:00Z";
        Assert.Equal(1, (await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            new AcceptedTransport(rootRevision))).Completed);

        // The stored row keeps the folder column the server confirmed until a pull rewrites it, so the scan
        // has to keep reading the location through the category. Reading the column here would owe this move
        // again on every synchronization, forever.
        Assert.Equal(0, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);

        var atRoot = stored.CreateDetachedCopy();
        atRoot.BitwardenFolderId = null;
        var asRemote = new BitwardenDecodedCipher(
            remote.Metadata with
            {
                FolderId = null,
                RevisionDate = rootRevision,
                PayloadHash = BitwardenPayloadFingerprint.ForPassword(atRoot, [], [])
            },
            remote.Password,
            null,
            [],
            []);
        var pull = await harness.Pull.ApplyAsync(
            harness.VaultId,
            Snapshot([RemoteFolder("folder-a", "First folder")], [asRemote]),
            [asRemote]);

        Assert.Equal(0, pull.ConflictsBackedUp);
        Assert.Null((await ReadFolderedAsync(harness)).CategoryId);
    }

    // Notes, cards and identities are filed in folders the same way logins are, and their payload is built by
    // a different encoder, so the location they travel with has to be resolved before that encoder reads it.
    [Fact]
    public async Task Moving_a_note_into_another_remote_folder_is_owed_an_update()
    {
        var harness = await CreateHarnessAsync();
        var remote = FolderNoteCipher("cipher-note-foldered", "Remote note", "folder-a");
        await harness.Pull.ApplyAsync(
            harness.VaultId,
            Snapshot([RemoteFolder("folder-a", "First folder"), RemoteFolder("folder-b", "Second folder")], [remote]),
            [remote]);

        var categories = await harness.Repository.GetCategoriesAsync();
        var stored = await ReadFolderedNoteAsync(harness);
        Assert.Equal(
            categories.Single(category => category.BitwardenFolderId == "folder-a").Id,
            stored.CategoryId);
        stored.CategoryId = categories.Single(category => category.BitwardenFolderId == "folder-b").Id;
        await harness.Repository.SaveSecureItemAsync(stored);

        Assert.Equal(1, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
        var queued = Assert.Single(await harness.Pending.GetAsync(harness.VaultId));
        Assert.Equal(BitwardenMutationOperationType.Update, queued.OperationType);
        Assert.Contains("\"folderId\":\"folder-b\"", queued.PayloadJson, StringComparison.Ordinal);

        const string movedRevision = "2026-07-22T05:00:00Z";
        Assert.Equal(1, (await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            new AcceptedTransport(movedRevision))).Completed);
        Assert.Equal(0, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
    }

    // A folder the user made on this device has no remote counterpart and Monica does not speak the
    // folder-create API, so the location it names has exactly one honest remote answer: none. The entry is
    // reported at root and keeps living in that folder locally. The alternative - holding on to whatever the
    // folder column says - was measured against a real Vaultwarden and dropped the entry out of its server
    // folder anyway, because a move that was pushed rather than pulled never stamps that column. What this
    // row asserts is the other half of the bargain: the pull has to respect the local choice rather than
    // fight it back out of the folder the user put it in.
    [Fact]
    public async Task A_move_into_a_folder_with_no_remote_counterpart_is_reported_at_root()
    {
        var harness = await CreateHarnessAsync();
        var remote = FolderCipher("cipher-foldered", "Kept in order", "folder-a");
        await harness.Pull.ApplyAsync(
            harness.VaultId,
            Snapshot([RemoteFolder("folder-a", "First folder")], [remote]),
            [remote]);
        var localOnly = new Category { Name = "Only on this device" };
        await harness.Repository.SaveCategoryAsync(localOnly);

        var stored = await ReadFolderedAsync(harness);
        stored.CategoryId = localOnly.Id;
        await harness.Repository.SavePasswordAsync(stored);

        Assert.Equal(1, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
        var queued = Assert.Single(await harness.Pending.GetAsync(harness.VaultId));
        Assert.Equal(BitwardenMutationOperationType.Update, queued.OperationType);
        using (var document = JsonDocument.Parse(queued.PayloadJson))
        {
            Assert.False(document.RootElement.TryGetProperty("folderId", out _));
        }

        const string rootRevision = "2026-07-22T05:00:00Z";
        Assert.Equal(1, (await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            new AcceptedTransport(rootRevision))).Completed);
        Assert.Equal(0, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);

        var atRoot = stored.CreateDetachedCopy();
        atRoot.BitwardenFolderId = null;
        var asRemote = new BitwardenDecodedCipher(
            remote.Metadata with
            {
                FolderId = null,
                RevisionDate = rootRevision,
                PayloadHash = BitwardenPayloadFingerprint.ForPassword(atRoot, [], [])
            },
            remote.Password,
            null,
            [],
            []);
        var pull = await harness.Pull.ApplyAsync(
            harness.VaultId,
            Snapshot([RemoteFolder("folder-a", "First folder")], [asRemote]),
            [asRemote]);

        Assert.Equal(0, pull.ConflictsBackedUp);
        Assert.Equal(localOnly.Id, (await ReadFolderedAsync(harness)).CategoryId);
    }

    [Fact]
    public async Task An_identity_the_remote_snapshot_never_confirmed_is_not_uploaded()
    {
        var harness = await CreateHarnessAsync();
        await harness.Repository.SavePasswordAsync(new PasswordEntry
        {
            Title = "Local only",
            Username = "nobody",
            Password = "nothing",
            BitwardenVaultId = harness.VaultId,
            BitwardenCipherId = "cipher-never-confirmed",
            BitwardenRevisionDate = BaselineRevision,
            BitwardenCipherType = 1
        });
        await harness.SyncState.ReplaceForVaultAsync(
            harness.VaultId,
            [new BitwardenSyncedCipher("cipher-other", new string('a', 64))],
            DateTimeOffset.UtcNow);

        var result = await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow);

        // Pushing an identity the server dropped would resurrect it; only tracked ciphers may drift.
        Assert.Equal(0, result.Enqueued);
        Assert.Empty(await harness.Pending.GetAsync(harness.VaultId));
    }

    [Fact]
    public async Task A_published_entry_reaches_the_server_once_and_comes_back_with_its_cipher()
    {
        var harness = await CreateHarnessAsync();
        // No baseline at all: a pull has never spoken about this vault, which is exactly the state a
        // freshly published entry is in. The old scan gave up here and the entry never left the device.
        var entry = await SavePublishedAsync(harness, "Published on this device");

        var queued = await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow);

        var operation = Assert.Single(await harness.Pending.GetAsync(harness.VaultId));
        Assert.Equal(1, queued.Enqueued);
        Assert.Equal(BitwardenMutationOperationType.Create, operation.OperationType);
        // The queue row cannot carry a cipher id the server has not handed out yet.
        Assert.Equal(BitwardenLocalCipherIdentity.ForPassword(entry.Id), operation.CipherId);
        Assert.Null(operation.ExpectedRemoteRevision);
        Assert.StartsWith("local-create:", operation.IdempotencyKey, StringComparison.Ordinal);

        var pushed = await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            new AcceptedTransport(NextRevision, assignedCipherId: "cipher-from-server"));
        Assert.Equal(1, pushed.Completed);

        var saved = await ReadByIdAsync(harness, entry.Id);
        Assert.Equal("cipher-from-server", saved.BitwardenCipherId);
        Assert.Equal(NextRevision, saved.BitwardenRevisionDate);

        // The pull that follows must not read this entry as local work again, and a second create would
        // put a duplicate of it on the server.
        Assert.Equal(0, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
        Assert.Equal(
            BitwardenPayloadFingerprint.ForPassword(saved, [], []),
            Assert.Contains("cipher-from-server", await harness.SyncState.GetPayloadHashesAsync(harness.VaultId)));
    }

    [Fact]
    public async Task Edits_before_the_first_upload_stay_one_promised_cipher()
    {
        var harness = await CreateHarnessAsync();
        var entry = await SavePublishedAsync(harness, "First draft");
        await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow);

        entry.Title = "Second draft";
        await harness.Repository.SavePasswordAsync(entry);
        var again = await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow);

        // A content-scoped key would queue the same cipher twice and post both, because a create has no
        // remote revision to compare against. One row, carrying the newest content, is the honest shape.
        Assert.Equal(1, again.Enqueued);
        var operation = Assert.Single(await harness.Pending.GetAsync(harness.VaultId));
        Assert.Equal(
            BitwardenPayloadFingerprint.ForPassword(entry, [], []),
            operation.LocalPayloadHash);
    }

    [Fact]
    public async Task A_published_entry_trashed_before_its_first_upload_owes_nothing()
    {
        var harness = await CreateHarnessAsync();
        var entry = await SavePublishedAsync(harness, "Deleted before it left");
        entry.IsDeleted = true;
        await harness.Repository.SavePasswordAsync(entry);

        Assert.Equal(0, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
        Assert.Empty(await harness.Pending.GetAsync(harness.VaultId));
    }

    // A wallet row is a different table with a different id, so publishing one has to travel the same create
    // route without borrowing the password entry's identity - otherwise the response would name a cipher for
    // a row nothing can find back.
    [Fact]
    public async Task A_published_card_reaches_the_server_once_and_comes_back_with_its_cipher()
    {
        var harness = await CreateHarnessAsync();
        var card = PublishedCard(harness.VaultId);
        await harness.Repository.SaveSecureItemAsync(card);

        var queued = await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow);

        var operation = Assert.Single(await harness.Pending.GetAsync(harness.VaultId));
        Assert.Equal(1, queued.Enqueued);
        Assert.Equal(BitwardenMutationOperationType.Create, operation.OperationType);
        Assert.Equal(BitwardenLocalCipherIdentity.ForSecureItem(card.Id), operation.CipherId);
        Assert.Null(operation.ExpectedRemoteRevision);
        Assert.Contains("\"type\":3", operation.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain(CardNumber, operation.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Example Credit Union", operation.PayloadJson, StringComparison.Ordinal);

        var pushed = await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            new AcceptedTransport(NextRevision, assignedCipherId: "cipher-card-from-server"));

        Assert.Equal(1, pushed.Completed);
        var saved = (await harness.Repository.GetSecureItemsAsync(itemType: null, includeDeleted: true))
            .Single(item => item.Id == card.Id);
        Assert.Equal("cipher-card-from-server", saved.BitwardenCipherId);
        Assert.Equal(NextRevision, saved.BitwardenRevisionDate);
        Assert.Equal(0, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
    }

    [Fact]
    public async Task A_published_card_the_encoder_refuses_is_counted_but_never_posted()
    {
        var harness = await CreateHarnessAsync();
        var card = PublishedCard(harness.VaultId);
        // The shape a locally created card defaults to: Bitwarden has no field for a debit designation.
        var data = WalletItemDataCodec.DecodeBankCard(card);
        data.CardTypeString = "DEBIT";
        card.ItemData = WalletItemDataCodec.EncodeBankCard(data);
        await harness.Repository.SaveSecureItemAsync(card);

        var queued = await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow);

        Assert.Equal(0, queued.Enqueued);
        Assert.Equal(1, queued.Refused);
        Assert.Empty(await harness.Pending.GetAsync(harness.VaultId));
    }

    [Fact]
    public async Task A_trashed_entry_is_queued_as_a_delete_and_stays_trashed_through_a_whole_sync()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);
        var trashed = await ReadAsync(harness);
        trashed.IsDeleted = true;
        trashed.DeletedAt = new DateTimeOffset(2026, 7, 22, 3, 30, 0, TimeSpan.Zero);
        await harness.Repository.SavePasswordAsync(trashed);

        Assert.Equal(1, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
        var queued = Assert.Single(await harness.Pending.GetAsync(harness.VaultId));
        Assert.Equal(BitwardenMutationOperationType.SoftDelete, queued.OperationType);
        // The trash has to guard the revision it deleted, or a server-side edit that landed first would
        // be erased by a delete the user aimed at the version they were looking at.
        Assert.Equal(BaselineRevision, queued.ExpectedRemoteRevision);

        var batch = await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            new AcceptedTransport(NextRevision));

        Assert.Equal(1, batch.Completed);
        Assert.True((await ReadAsync(harness)).IsDeleted);
        Assert.Equal(0, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);

        // The pull that follows carries the server's side of that deletion, and the server keeps a
        // trashed cipher's payload as it was - so its fingerprint is the local trashed one.
        var deletedRemote = TrashedCopyOf(await ReadAsync(harness), remote);

        var result = await harness.Pull.ApplyAsync(
            harness.VaultId,
            Snapshot([deletedRemote]),
            [deletedRemote]);

        Assert.Equal(1, result.Unchanged);
        Assert.Equal(0, result.ConflictsBackedUp);
        Assert.True((await ReadAsync(harness)).IsDeleted);
        Assert.Empty(await harness.ConflictStore.GetUnresolvedAsync(harness.VaultId));
    }

    // Undoing a trash has to travel too. The delete push left the baseline holding the trashed
    // fingerprint, so a restore is drift again and this time the owed work is an update carrying the
    // entry back - without that the server would keep the cipher in its trash and the next pull would
    // quietly delete it here again.
    [Fact]
    public async Task Restoring_a_trashed_entry_from_the_vault_trash_uploads_it_again()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);
        var trashed = await ReadAsync(harness);
        trashed.IsDeleted = true;
        trashed.DeletedAt = new DateTimeOffset(2026, 7, 22, 3, 30, 0, TimeSpan.Zero);
        await harness.Repository.SavePasswordAsync(trashed);
        await harness.Queue.EnqueueDriftedAsync(harness.VaultId, harness.VaultKey, DateTimeOffset.UtcNow);
        await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            new AcceptedTransport(NextRevision));

        var restored = await ReadAsync(harness);
        restored.IsDeleted = false;
        restored.DeletedAt = null;
        await harness.Repository.SavePasswordAsync(restored);

        Assert.Equal(1, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
        var queued = (await harness.Pending.GetAsync(harness.VaultId))
            .Single(operation => operation.OperationType == BitwardenMutationOperationType.Update);
        Assert.Equal(NextRevision, queued.ExpectedRemoteRevision);

        var batch = await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            new AcceptedTransport("2026-07-22T05:00:00Z"));

        Assert.Equal(1, batch.Completed);
        Assert.False((await ReadAsync(harness)).IsDeleted);
        Assert.Equal(0, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
    }

    // The pull that confirms a deletion also rewrites the sync baseline, and the trashed row has to stay
    // in that rewrite. Drop it and a later restore has no baseline left to be drift against, so the
    // resurrection never leaves the device - while every following pull re-trashes it here and leaves
    // another conflict backup behind.
    [Fact]
    public async Task A_restore_after_the_pull_that_confirmed_the_delete_still_uploads()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);
        var trashed = await ReadAsync(harness);
        trashed.IsDeleted = true;
        trashed.DeletedAt = new DateTimeOffset(2026, 7, 22, 3, 30, 0, TimeSpan.Zero);
        await harness.Repository.SavePasswordAsync(trashed);
        await harness.Queue.EnqueueDriftedAsync(harness.VaultId, harness.VaultKey, DateTimeOffset.UtcNow);
        await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            new AcceptedTransport(NextRevision));
        var afterPush = await ReadAsync(harness);
        await harness.Pull.ApplyAsync(
            harness.VaultId,
            Snapshot([TrashedCopyOf(afterPush, remote)]),
            [TrashedCopyOf(afterPush, remote)]);

        var restored = await ReadAsync(harness);
        restored.IsDeleted = false;
        restored.DeletedAt = null;
        await harness.Repository.SavePasswordAsync(restored);

        Assert.Equal(1, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
        var queued = (await harness.Pending.GetAsync(harness.VaultId))
            .Single(operation => operation.OperationType == BitwardenMutationOperationType.Update);
        Assert.Equal(NextRevision, queued.ExpectedRemoteRevision);

        var revivedRevision = "2026-07-22T05:00:00Z";
        var batch = await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            new AcceptedTransport(revivedRevision));

        Assert.Equal(1, batch.Completed);
        Assert.False((await ReadAsync(harness)).IsDeleted);
        Assert.Equal(0, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
    }

    // What the decoder really hands over for a cipher the server keeps in its trash: no payload at all,
    // and a `deleted:` marker where a content fingerprint would be. Comparing that marker with the local
    // row's fingerprint can never match, so a second pull re-decided the very same trash as a conflict and
    // the drift scan kept owing a delete that the server had already granted.
    [Fact]
    public async Task A_remote_trash_lands_once_and_stays_quiet_afterwards()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);
        var trashed = new BitwardenDecodedCipher(
            remote.Metadata with
            {
                RevisionDate = NextRevision,
                IsDeleted = true,
                PayloadHash = BitwardenPayloadFingerprint.ForRemoteDeletion(NextRevision)
            },
            null,
            null,
            [],
            []);

        var first = await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([trashed]), [trashed]);
        Assert.Equal(1, first.Deleted);
        Assert.True((await ReadAsync(harness)).IsDeleted);

        Assert.Equal(0, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);

        var second = await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([trashed]), [trashed]);
        Assert.Equal(1, second.Unchanged);
        Assert.Equal(0, second.ConflictsBackedUp);
        Assert.Empty(await harness.ConflictStore.GetUnresolvedAsync(harness.VaultId));
    }

    // The mirror of that fact on the way out: the entry came back out of Monica's recycle bin while the
    // server still keeps its cipher in the trash. An update is accepted for a trashed cipher without
    // clearing the deletion, so the queue owes the restore route instead - and it owes it with no cipher
    // payload, which is also why reviving a note needs no encoder.
    [Fact]
    public async Task An_entry_restored_over_a_remote_trash_is_queued_as_a_restore_with_no_payload()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);
        var trashed = RemoteTrashOf(remote, NextRevision);
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([trashed]), [trashed]);

        var restored = await ReadAsync(harness);
        restored.IsDeleted = false;
        restored.DeletedAt = null;
        await harness.Repository.SavePasswordAsync(restored);

        Assert.Equal(1, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
        var queued = Assert.Single(await harness.Pending.GetAsync(harness.VaultId));
        Assert.Equal(BitwardenMutationOperationType.Restore, queued.OperationType);
        Assert.Equal("{}", queued.PayloadJson);
        Assert.StartsWith("local-restore:", queued.IdempotencyKey, StringComparison.Ordinal);
        Assert.Equal(NextRevision, queued.ExpectedRemoteRevision);
        // Nothing about the content travelled, so the baseline cannot claim the server holds it.
        Assert.Null(queued.LocalPayloadHash);

        const string revivedRevision = "2026-07-22T05:00:00Z";
        var batch = await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            new AcceptedTransport(revivedRevision));

        Assert.Equal(1, batch.Completed);
        var pushed = await ReadAsync(harness);
        Assert.False(pushed.IsDeleted);
        Assert.Equal(revivedRevision, pushed.BitwardenRevisionDate);

        // The pull that follows reports the cipher alive at the revision the restore handed back. It has to
        // find nothing to overwrite: a restore the server grants must not come back as a conflict backup.
        var revived = remote.Metadata.RevisionDate == revivedRevision
            ? remote
            : new BitwardenDecodedCipher(
                remote.Metadata with { RevisionDate = revivedRevision },
                remote.Password,
                null,
                [],
                []);
        var pull = await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([revived]), [revived]);
        Assert.Equal(0, pull.ConflictsBackedUp);
        Assert.Empty(await harness.ConflictStore.GetUnresolvedAsync(harness.VaultId));
        Assert.Equal(0, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
    }

    // A create, an update and a delete for one cipher are three different promises to the server. They key
    // apart, and the completed delete must not be the row a restore reopens - the store raises a finished
    // operation back to pending when an enqueue lands on its key.
    [Fact]
    public async Task A_restore_keeps_its_own_row_beside_the_delete_that_already_completed()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);
        var trashed = await ReadAsync(harness);
        trashed.IsDeleted = true;
        trashed.DeletedAt = new DateTimeOffset(2026, 7, 22, 3, 30, 0, TimeSpan.Zero);
        await harness.Repository.SavePasswordAsync(trashed);
        await harness.Queue.EnqueueDriftedAsync(harness.VaultId, harness.VaultKey, DateTimeOffset.UtcNow);
        await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            new AcceptedTransport(NextRevision));
        var afterPush = await ReadAsync(harness);
        await harness.Pull.ApplyAsync(
            harness.VaultId,
            Snapshot([RemoteTrashOf(remote, NextRevision)]),
            [RemoteTrashOf(remote, NextRevision)]);

        var restored = await ReadAsync(harness);
        restored.IsDeleted = false;
        restored.DeletedAt = null;
        await harness.Repository.SavePasswordAsync(restored);
        Assert.Equal(1, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);

        var rows = await harness.Pending.GetAsync(harness.VaultId);
        Assert.Equal(2, rows.Count);
        var delete = Assert.Single(rows, row => row.OperationType == BitwardenMutationOperationType.SoftDelete);
        var restore = Assert.Single(rows, row => row.OperationType == BitwardenMutationOperationType.Restore);
        Assert.Equal(BitwardenMutationStatus.Completed, delete.Status);
        Assert.Equal(BitwardenMutationStatus.Pending, restore.Status);
        Assert.NotEqual(delete.IdempotencyKey, restore.IdempotencyKey);
        Assert.Equal(afterPush.BitwardenCipherId, restore.CipherId);
    }

    // Undoing a restore before it leaves has to be refused the same way undoing a delete is: reviving the
    // remote copy of an entry the user has since thrown away here would hand back what was just removed.
    [Fact]
    public async Task A_restore_the_user_undid_before_it_left_the_device_is_never_sent()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);
        await harness.Pull.ApplyAsync(
            harness.VaultId,
            Snapshot([RemoteTrashOf(remote, NextRevision)]),
            [RemoteTrashOf(remote, NextRevision)]);

        var restored = await ReadAsync(harness);
        restored.IsDeleted = false;
        restored.DeletedAt = null;
        await harness.Repository.SavePasswordAsync(restored);
        Assert.Equal(1, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);

        var reTrashed = await ReadAsync(harness);
        reTrashed.IsDeleted = true;
        reTrashed.DeletedAt = new DateTimeOffset(2026, 7, 22, 4, 30, 0, TimeSpan.Zero);
        await harness.Repository.SavePasswordAsync(reTrashed);

        var transport = new AcceptedTransport("2026-07-22T05:00:00Z");
        var batch = await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            transport);

        Assert.Equal(1, batch.Completed);
        Assert.Equal(0, transport.Sends);
        Assert.True((await ReadAsync(harness)).IsDeleted);
        // Dropping the promise without sending it leaves both sides holding the cipher in their trash,
        // which is settled: the next scan owes nothing, and the completed restore row must not be reopened.
        Assert.Equal(0, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
        var row = Assert.Single(await harness.Pending.GetAsync(harness.VaultId));
        Assert.Equal(BitwardenMutationOperationType.Restore, row.OperationType);
        Assert.Equal(BitwardenMutationStatus.Completed, row.Status);
    }

    // A note has no login encoder, and reviving one must not need one: the route alone decides it.
    [Fact]
    public async Task A_restored_note_owes_only_the_restore_route_and_no_cipher_payload()
    {
        var harness = await CreateHarnessAsync();
        var note = BoundNoteCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([note]), [note]);
        var remoteTrash = new BitwardenDecodedCipher(
            note.Metadata with
            {
                RevisionDate = NextRevision,
                IsDeleted = true,
                PayloadHash = BitwardenPayloadFingerprint.ForRemoteDeletion(NextRevision)
            },
            null,
            null,
            [],
            []);
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remoteTrash]), [remoteTrash]);
        Assert.True((await ReadNoteAsync(harness)).IsDeleted);

        var restored = await ReadNoteAsync(harness);
        restored.IsDeleted = false;
        restored.DeletedAt = null;
        await harness.Repository.SaveSecureItemAsync(restored);

        Assert.Equal(1, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
        var queued = Assert.Single(await harness.Pending.GetAsync(harness.VaultId));
        Assert.Equal(BitwardenMutationOperationType.Restore, queued.OperationType);
        Assert.Equal("{}", queued.PayloadJson);
        Assert.Equal("cipher-note", queued.CipherId);

        var batch = await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            new AcceptedTransport("2026-07-22T05:00:00Z"));

        Assert.Equal(1, batch.Completed);
        Assert.False((await ReadNoteAsync(harness)).IsDeleted);
    }

    // A deletion is decided by the route alone and travels no payload at all. Without this a trashed note
    // is handed back by the next pull and leaves another conflict backup behind, on every synchronization,
    // for as long as the vault exists.
    [Fact]
    public async Task A_trashed_secure_item_owes_only_the_delete_route_and_no_cipher_payload()
    {
        var harness = await CreateHarnessAsync();
        var note = BoundNoteCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([note]), [note]);
        var trashed = await ReadNoteAsync(harness);
        trashed.IsDeleted = true;
        trashed.DeletedAt = new DateTimeOffset(2026, 7, 22, 3, 30, 0, TimeSpan.Zero);
        await harness.Repository.SaveSecureItemAsync(trashed);

        Assert.Equal(1, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
        var queued = Assert.Single(await harness.Pending.GetAsync(harness.VaultId));
        Assert.Equal(BitwardenMutationOperationType.SoftDelete, queued.OperationType);
        Assert.Equal("{}", queued.PayloadJson);
        Assert.Equal(BaselineRevision, queued.ExpectedRemoteRevision);

        var batch = await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            new AcceptedTransport(NextRevision));

        Assert.Equal(1, batch.Completed);
        Assert.True((await ReadNoteAsync(harness)).IsDeleted);
        Assert.Equal(0, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
    }

    // The encoder refuses anything it cannot reproduce, so an update that reaches the store here is one the
    // next pull reads back as the same row: a type-2 cipher carrying cipher strings, and a drift the push
    // settles instead of one that comes round again.
    [Fact]
    public async Task An_edited_note_is_queued_as_a_note_cipher_and_pushes()
    {
        var harness = await CreateHarnessAsync();
        var note = BoundNoteCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([note]), [note]);
        var stored = await ReadNoteAsync(harness);
        var saved = NoteContentCodec.BuildSavePayload("Renamed on this device", stored.Notes, "", false);
        stored.Title = saved.Title;
        stored.Notes = saved.NotesCache;
        stored.ItemData = saved.ItemData;
        stored.ImagePaths = saved.ImagePaths;
        await harness.Repository.SaveSecureItemAsync(stored);

        Assert.Equal(1, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
        var queued = Assert.Single(await harness.Pending.GetAsync(harness.VaultId));
        Assert.Equal(BitwardenMutationOperationType.Update, queued.OperationType);
        Assert.Contains("\"type\":2", queued.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Renamed on this device", queued.PayloadJson, StringComparison.Ordinal);

        var batch = await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            new AcceptedTransport(NextRevision));

        Assert.Equal(1, batch.Completed);
        Assert.Equal(0, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
    }

    [Fact]
    public async Task A_delete_the_user_undid_before_it_left_the_device_is_never_sent()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);
        var trashed = await ReadAsync(harness);
        trashed.IsDeleted = true;
        trashed.DeletedAt = new DateTimeOffset(2026, 7, 22, 3, 30, 0, TimeSpan.Zero);
        await harness.Repository.SavePasswordAsync(trashed);
        await harness.Queue.EnqueueDriftedAsync(harness.VaultId, harness.VaultKey, DateTimeOffset.UtcNow);

        var restored = await ReadAsync(harness);
        restored.IsDeleted = false;
        restored.DeletedAt = null;
        await harness.Repository.SavePasswordAsync(restored);

        var transport = new AcceptedTransport(NextRevision);
        var batch = await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            transport);

        Assert.Equal(1, batch.Completed);
        Assert.Equal(0, transport.Sends);
        Assert.False((await ReadAsync(harness)).IsDeleted);
        // The row is settled, not retried: a second attempt would trash it after all.
        Assert.Equal(
            BitwardenMutationStatus.Completed,
            Assert.Single(await harness.Pending.GetAsync(harness.VaultId)).Status);
        // The baseline still holds the live content the user restored, so nothing is owed any more.
        Assert.Equal(0, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
    }

    [Fact]
    public async Task A_refusal_names_the_entry_and_the_reason_the_encoder_gave()
    {
        var harness = await CreateHarnessAsync();
        // A card carrying a debit designation: Bitwarden's card shape has no field for it, so the encoder's
        // projection gate refuses it - and that gate names its field without classifying it, which is the
        // case the catch-all reason exists for.
        var card = PublishedCard(harness.VaultId);
        var data = WalletItemDataCodec.DecodeBankCard(card);
        data.CardTypeString = "DEBIT";
        card.ItemData = WalletItemDataCodec.EncodeBankCard(data);
        await harness.Repository.SaveSecureItemAsync(card);

        var result = await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow);

        // The screen shows exactly this row, so it is asserted as the row: what stayed behind is named by
        // the title the user reads in the library, and the reason is the code the encoder decided on, not a
        // guess made afterwards from the fact that nothing was booked.
        var refusal = Assert.Single(result.Unsyncable);
        Assert.Equal("Published card", refusal.Title);
        Assert.False(refusal.IsPassword);
        Assert.Equal(BitwardenPayloadRefusal.UnsupportedContent, refusal.Reason);
        Assert.Equal(result.Refused, result.Unsyncable.Count);
        Assert.Empty(await harness.Pending.GetAsync(harness.VaultId));
    }

    [Fact]
    public async Task A_row_with_no_remote_revision_is_refused_for_that_reason_alone()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);
        // Drift plus a missing revision: the shape is one Bitwarden would take, so the only thing standing
        // in the way is that an update would have to go out unguarded. Written as an empty string because a
        // save cannot clear the column to null - which is the state this guard exists for, since a create
        // response that names a cipher without reporting its revision leaves the row exactly like this.
        var stored = await RenameAsync(harness, "Renamed while unguarded");
        stored.BitwardenRevisionDate = "";
        await harness.Repository.SavePasswordAsync(stored);

        var result = await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow);

        var refusal = Assert.Single(result.Unsyncable);
        Assert.Equal(BitwardenPayloadRefusal.MissingRemoteRevision, refusal.Reason);
        Assert.Equal(0, result.Enqueued);
        Assert.Equal(1, result.Refused);
        Assert.Equal(result.Refused, result.Unsyncable.Count);
        Assert.Empty(await harness.Pending.GetAsync(harness.VaultId));
    }

    [Fact]
    public async Task A_row_that_stops_being_refused_stops_being_listed()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);
        var stored = await ReadAsync(harness);
        stored.LoginType = PasswordLoginType.SshKey;
        await harness.Repository.SavePasswordAsync(stored);

        Assert.Single((await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Unsyncable);

        // Trashing it needs no encoder at all, so the very next scan books the delete and owes nothing:
        // the list is a fresh read of one scan, not a log something has to remember to clear.
        var trashed = await ReadAsync(harness);
        trashed.IsDeleted = true;
        trashed.DeletedAt = new DateTimeOffset(2026, 9, 25, 3, 30, 0, TimeSpan.Zero);
        await harness.Repository.SavePasswordAsync(trashed);

        var result = await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow);

        Assert.Equal(1, result.Enqueued);
        Assert.Empty(result.Unsyncable);
        Assert.Equal(
            BitwardenMutationOperationType.SoftDelete,
            Assert.Single(await harness.Pending.GetAsync(harness.VaultId)).OperationType);
    }

    [Fact]
    public async Task A_shape_bitwarden_cannot_carry_is_counted_rather_than_failing_the_scan()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);
        var stored = (await harness.Repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true))
            .Single(entry => entry.BitwardenCipherId == "cipher-edit");
        stored.LoginType = PasswordLoginType.SshKey;
        await harness.Repository.SavePasswordAsync(stored);

        var result = await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow);

        Assert.Equal(0, result.Enqueued);
        Assert.Equal(1, result.Refused);
        Assert.Equal(1, result.Drifted);
        Assert.Empty(await harness.Pending.GetAsync(harness.VaultId));
    }

    [Fact]
    public async Task A_successful_push_advances_the_baseline_so_the_next_scan_is_quiet()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);
        await RenameAsync(harness, "Renamed on this device");
        Assert.Equal(1, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);

        var batch = await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            new AcceptedTransport(NextRevision));

        Assert.Equal(1, batch.Completed);
        Assert.Equal(0, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);

        // A second edit must still be owed, even though the first upload consumed the same cipher.
        await RenameAsync(harness, "Renamed again on this device");
        Assert.Equal(1, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
        Assert.Equal(2, (await harness.Pending.GetAsync(harness.VaultId)).Count);
    }

    [Fact]
    public async Task A_push_that_the_server_rejects_leaves_the_change_owed()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);
        await RenameAsync(harness, "Renamed on this device");
        await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow);

        var batch = await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            new AcceptedTransport(NextRevision, reject: true));

        Assert.Equal(1, batch.Conflicts);
        var baseline = await harness.SyncState.GetPayloadHashesAsync(harness.VaultId);
        Assert.Equal(
            BitwardenPayloadFingerprint.ForPassword(remote.Password!, [], []),
            baseline["cipher-edit"]);
        // The server never took the edit, so the next sync has to try again.
        Assert.Equal(1, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
    }

    [Fact]
    public async Task Migration_v77_restores_the_baseline_table_and_column_without_losing_owed_work()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);
        await RenameAsync(harness, "Renamed on this device");
        Assert.Equal(1, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);

        await using (var connection = harness.Factory.CreateConnection())
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                DROP TABLE bitwarden_sync_state;
                ALTER TABLE bitwarden_pending_operations DROP COLUMN local_payload_hash;
                PRAGMA user_version=76;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await harness.Migrator.MigrateAsync();

        await using (var connection = harness.Factory.CreateConnection())
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM bitwarden_sync_state;";
            Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync()));
            command.CommandText = "SELECT local_payload_hash FROM bitwarden_pending_operations LIMIT 1;";
            Assert.Equal(DBNull.Value, await command.ExecuteScalarAsync());
        }

        // A schema upgrade must not drop the upload a user was already owed.
        var owed = Assert.Single(await harness.Pending.GetAsync(harness.VaultId));
        Assert.Null(owed.LocalPayloadHash);
        Assert.Equal("cipher-edit", owed.CipherId);

        await harness.SyncState.ReplaceForVaultAsync(
            harness.VaultId,
            [new BitwardenSyncedCipher(
                "cipher-edit",
                BitwardenPayloadFingerprint.ForPassword(remote.Password!, [], []))],
            DateTimeOffset.UtcNow);
        await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow);

        // Re-establishing the baseline re-queues the same owed upload and refills the new column.
        var rescanned = Assert.Single(await harness.Pending.GetAsync(harness.VaultId));
        Assert.NotNull(rescanned.LocalPayloadHash);
        Assert.Equal(rescanned.LocalPayloadHash!, rescanned.IdempotencyKey.Split(':').Last());
    }

    [Fact]
    [Trait("Category", "perf-budget")]
    public async Task Scanning_a_large_bound_vault_for_drift_stays_within_its_budget()
    {
        // Wiring the upload path makes every synchronization pay for one extra full read + fingerprint
        // pass before it pulls, so that cost carries a ceiling rather than being a comment. Measured at
        // 214-272 ms here; 800 leaves a noisy CI box room without hiding the two batched reads turning
        // back into per-row ones.
        var harness = await CreateHarnessAsync();
        var remotes = Enumerable.Range(1, 2_000)
            .Select(index => BoundCipher($"cipher-{index:D4}", $"Account {index:D4}", $"user{index:D4}"))
            .ToList();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot(remotes), remotes);

        for (var pass = 0; pass < 3; pass++)
        {
            Assert.Equal(0, (await harness.Queue.EnqueueDriftedAsync(
                harness.VaultId,
                harness.VaultKey,
                DateTimeOffset.UtcNow)).Enqueued);
        }

        var stopwatch = Stopwatch.StartNew();
        var result = await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow);
        stopwatch.Stop();

        Assert.Equal(0, result.Drifted);
        Assert.True(
            stopwatch.ElapsedMilliseconds < 800,
            $"A 2,000-item drift scan over an unchanged vault took {stopwatch.ElapsedMilliseconds:0.##} ms.");
    }

    [Fact]
    public async Task Rejected_pushes_do_not_stack_conflict_backups_for_content_nothing_destroyed()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);
        await RenameAsync(harness, "Renamed on this device");

        for (var sync = 1; sync <= 3; sync++)
        {
            await harness.Queue.EnqueueDriftedAsync(
                harness.VaultId,
                harness.VaultKey,
                DateTimeOffset.UtcNow);
            var batch = await harness.Processor.ProcessReadyAsync(
                harness.VaultId,
                DateTimeOffset.UtcNow,
                new AcceptedTransport(NextRevision, reject: true));
            Assert.Equal(1, batch.Conflicts);
        }

        // A rejected upload destroys nothing local - the edit is still on screen and still owed - so
        // there is nothing to recover. Keeping the backup here would append one copy per sync forever.
        Assert.Empty(await harness.ConflictStore.GetUnresolvedAsync(harness.VaultId));
    }

    [Fact]
    public async Task The_backup_a_restore_reads_is_the_one_the_pull_writes_before_overwriting()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);
        await RenameAsync(harness, "Renamed on this device");
        await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow);
        await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            new AcceptedTransport(NextRevision, reject: true));

        // The pull that follows the failed push finds local content differing at the remote revision
        // and backs it up right before overwriting it.
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);

        // A restore list is keyed on item_kind, so one destroyed edit has to yield exactly one row of
        // the entry shape this app stores - not a second copy of the outgoing ciphertext.
        var backup = Assert.Single(await harness.ConflictStore.GetUnresolvedAsync(harness.VaultId));
        Assert.Equal("password", backup.ItemKind);
        var root = JsonDocument.Parse(backup.PayloadJson).RootElement;
        Assert.Equal(
            ["customFields", "password", "passwordHistory"],
            root.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
        Assert.Equal("Renamed on this device", root.GetProperty("password").GetProperty("Title").GetString());
    }

    [Fact]
    public async Task A_restored_conflict_comes_back_whole_and_the_next_sync_uploads_it()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);
        var edited = await RenameAsync(harness, "Renamed on this device");
        await harness.Repository.ReplaceCustomFieldsAsync(
            edited.Id,
            [new CustomField { EntryId = edited.Id, Title = "Backup code", Value = "recovery-6-words" }],
            CancellationToken.None);
        await harness.Repository.SavePasswordHistoryAsync(
            new PasswordHistoryEntry { EntryId = edited.Id, Password = "previous-password" },
            CancellationToken.None);
        await harness.Queue.EnqueueDriftedAsync(harness.VaultId, harness.VaultKey, DateTimeOffset.UtcNow);
        await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            new AcceptedTransport(NextRevision, reject: true));
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);

        // The second pull won: this device now shows the remote content.
        Assert.Equal("Remote baseline", (await ReadAsync(harness)).Title);
        var summary = Assert.Single(await harness.Restore.GetSummariesAsync(harness.VaultId));
        Assert.Equal("Renamed on this device", summary.Title);
        Assert.True(summary.IsPassword);

        await harness.Restore.RestoreAsync(harness.VaultId, summary.BackupId);

        var restored = await ReadAsync(harness);
        Assert.Equal("Renamed on this device", restored.Title);
        Assert.True(restored.BitwardenLocalModified);
        Assert.Equal(BaselineRevision, restored.BitwardenRevisionDate);
        Assert.Equal(
            ["Backup code"],
            (await harness.Repository.GetCustomFieldsByEntryIdsAsync([restored.Id]))
                .Values.SelectMany(fields => fields).Select(field => field.Title));
        Assert.Equal(
            ["previous-password"],
            (await harness.Repository.GetPasswordHistoryByEntryIdsAsync([restored.Id]))
                .Values.SelectMany(history => history).Select(history => history.Password));
        Assert.Empty(await harness.ConflictStore.GetUnresolvedAsync(harness.VaultId));

        // Recovering the content is only worth anything if it stops the server from overwriting it again.
        Assert.Equal(1, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
        var batch = await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            new AcceptedTransport(NextRevision));
        Assert.Equal(1, batch.Completed);
        Assert.Equal(0, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
    }

    [Fact]
    public async Task Discarding_a_conflict_leaves_the_remote_version_alone_and_owes_nothing()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);
        await RenameAsync(harness, "Renamed on this device");
        await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow);
        await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            new AcceptedTransport(NextRevision, reject: true));
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);
        var summary = Assert.Single(await harness.Restore.GetSummariesAsync(harness.VaultId));

        await harness.Restore.DiscardAsync(harness.VaultId, summary.BackupId);

        Assert.Empty(await harness.Restore.GetSummariesAsync(harness.VaultId));
        Assert.Equal("Remote baseline", (await ReadAsync(harness)).Title);
        Assert.Equal(0, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
    }

    // A permanent purge is the one local change the drift scan cannot discover: the row it reads to work
    // out what the server owes is the row the purge destroys, and the tombstone the writer leaves behind
    // keeps nothing but its ids. So the erase has to be booked off the entry still in memory, and the
    // first assertion below is the negative control - with the producer removed the vault would quietly
    // keep a copy the user threw away forever.
    [Fact]
    public async Task A_purged_entry_is_queued_as_an_erase_no_later_scan_can_invent()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);
        var doomed = await ReadAsync(harness);
        await harness.Repository.DeletePasswordPermanentlyAsync(doomed.Id);

        Assert.Equal(0, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
        Assert.Empty(await harness.Pending.GetAsync(harness.VaultId));

        Assert.True(await harness.Purge.EnqueuePasswordAsync(doomed));
        var queued = Assert.Single(await harness.Pending.GetAsync(harness.VaultId));
        Assert.Equal(BitwardenMutationOperationType.Delete, queued.OperationType);
        Assert.Equal("cipher-edit", queued.CipherId);
        // The erase guards the version the user was looking at: without it a copy edited elsewhere would
        // be erased by a delete aimed at a state nobody saw.
        Assert.Equal(BaselineRevision, queued.ExpectedRemoteRevision);
        Assert.Equal("{}", queued.PayloadJson);
        Assert.StartsWith("local-purge:", queued.IdempotencyKey, StringComparison.Ordinal);
        // No content travelled, so the processor must be left with nothing to record as what the server
        // now holds.
        Assert.Null(queued.LocalPayloadHash);

        var batch = await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            new AcceptedTransport(NextRevision));

        Assert.Equal(1, batch.Completed);
        Assert.Empty(await harness.Repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true));
        Assert.Equal(BitwardenMutationStatus.Completed, Assert.Single(await harness.Pending.GetAsync(harness.VaultId)).Status);
        Assert.Equal(0, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);

        // The pull that follows retires the erased cipher's baseline with the rest of the snapshot, so
        // nothing is left behind claiming the server still holds it.
        var kept = BoundCipher("cipher-kept", "Still on the server", "kept-user");
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([kept]), [kept]);
        var baseline = await harness.SyncState.GetPayloadHashesAsync(harness.VaultId);
        Assert.False(baseline.ContainsKey("cipher-edit"));
        Assert.True(baseline.ContainsKey("cipher-kept"));
    }

    // A purge is the second decision about a cipher the user already threw away, and the store raises a
    // completed row back to pending when an enqueue lands on its key. Reusing the trash's key would reopen
    // a deletion the server granted - and leave the erase queued under a row that is already gone.
    [Fact]
    public async Task A_permanent_erase_keys_apart_from_the_trash_that_came_before_it()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);
        var trashed = await ReadAsync(harness);
        trashed.IsDeleted = true;
        trashed.DeletedAt = new DateTimeOffset(2026, 7, 22, 3, 30, 0, TimeSpan.Zero);
        await harness.Repository.SavePasswordAsync(trashed);
        await harness.Queue.EnqueueDriftedAsync(harness.VaultId, harness.VaultKey, DateTimeOffset.UtcNow);
        await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            new AcceptedTransport(NextRevision));
        var doomed = await ReadAsync(harness);
        await harness.Repository.DeletePasswordPermanentlyAsync(doomed.Id);

        Assert.True(await harness.Purge.EnqueuePasswordAsync(doomed));
        var rows = await harness.Pending.GetAsync(harness.VaultId);
        Assert.Equal(2, rows.Count);
        var trash = Assert.Single(rows, row => row.OperationType == BitwardenMutationOperationType.SoftDelete);
        var erase = Assert.Single(rows, row => row.OperationType == BitwardenMutationOperationType.Delete);
        Assert.Equal(BitwardenMutationStatus.Completed, trash.Status);
        Assert.Equal(BitwardenMutationStatus.Pending, erase.Status);
        Assert.NotEqual(trash.IdempotencyKey, erase.IdempotencyKey);

        var batch = await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            new AcceptedTransport("2026-07-22T05:00:00Z"));

        Assert.Equal(1, batch.Completed);
        Assert.Equal(0, (await harness.Queue.EnqueueDriftedAsync(
            harness.VaultId,
            harness.VaultKey,
            DateTimeOffset.UtcNow)).Enqueued);
    }

    // Two rows an erase cannot be aimed at: one the server has never been told about, and one that never
    // learned which version of itself the server holds. Sending either would be a guess, and a wrong guess
    // about a deletion cannot be taken back.
    [Fact]
    public async Task An_erase_with_nothing_to_erase_or_nothing_to_guard_is_refused()
    {
        var harness = await CreateHarnessAsync();
        var published = await SavePublishedAsync(harness, "Published, never uploaded");
        var untracked = new PasswordEntry
        {
            Title = "Bound without a revision",
            Username = "whoever",
            Password = "a local secret",
            BitwardenVaultId = harness.VaultId,
            BitwardenCipherId = "cipher-no-revision",
            BitwardenCipherType = 1
        };
        await harness.Repository.SavePasswordAsync(untracked);

        Assert.False(await harness.Purge.EnqueuePasswordAsync(published));
        Assert.False(await harness.Purge.EnqueuePasswordAsync(untracked));
        Assert.Empty(await harness.Pending.GetAsync(harness.VaultId));
    }

    // The server answers 404 for a cipher another client already erased. Today that lands as a validation
    // failure the retry policy never re-tries, so the queue would carry a permanently failed row for a
    // change that is, from the server's point of view, fully granted.
    [Fact]
    public async Task An_erase_the_server_no_longer_has_is_settled_instead_of_parked()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);
        var doomed = await ReadAsync(harness);
        await harness.Repository.DeletePasswordPermanentlyAsync(doomed.Id);
        Assert.True(await harness.Purge.EnqueuePasswordAsync(doomed));

        var transport = new AcceptedTransport(NextRevision, failureStatus: 404);
        var batch = await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            transport);

        Assert.Equal(1, transport.Sends);
        Assert.Equal(1, batch.Completed);
        Assert.Equal(0, batch.Failed);
        var row = Assert.Single(await harness.Pending.GetAsync(harness.VaultId));
        Assert.Equal(BitwardenMutationStatus.Completed, row.Status);
        // Settling is not the same as succeeding: the cipher may be held by nobody now, so the erase must
        // not leave the baseline claiming the server has it.
        Assert.Equal(
            BitwardenPayloadFingerprint.ForPassword(remote.Password!, [], []),
            Assert.Contains("cipher-edit", await harness.SyncState.GetPayloadHashesAsync(harness.VaultId)));
    }

    // Measured on a live Vaultwarden rather than assumed: a cipher the server does not hold answers HTTP 400
    // - never 404 - on GET, DELETE and PUT /delete alike, and an id that never existed answers the same. So
    // the settle above has to recognize 400 too, or every erase of a cipher another client removed stays a
    // failed row for the rest of the vault's life. The 400 an update earns is a payload this client got
    // wrong and must keep failing: the deletions are the only operations whose request carries no body.
    [Fact]
    public async Task An_erase_answered_400_settles_while_an_update_answered_400_still_fails()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        var kept = BoundCipher("cipher-keep", "Kept remote", "kept-user");
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote, kept]), [remote, kept]);
        var doomed = await ReadAsync(harness);
        await harness.Repository.DeletePasswordPermanentlyAsync(doomed.Id);
        Assert.True(await harness.Purge.EnqueuePasswordAsync(doomed));
        var standing = (await harness.Repository.GetPasswordsAsync(true, true))
            .Single(entry => entry.BitwardenCipherId == "cipher-keep");
        standing.Title = "Edited here, never uploaded";
        await harness.Repository.SavePasswordAsync(standing);
        await harness.Queue.EnqueueDriftedAsync(harness.VaultId, harness.VaultKey, DateTimeOffset.UtcNow);

        var transport = new AcceptedTransport(NextRevision, failureStatus: 400);
        var batch = await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            transport);

        Assert.Equal(2, transport.Sends);
        Assert.Equal(1, batch.Completed);
        Assert.Equal(1, batch.Failed);
        var rows = await harness.Pending.GetAsync(harness.VaultId);
        Assert.Equal(
            BitwardenMutationStatus.Completed,
            rows.Single(row => row.OperationType == BitwardenMutationOperationType.Delete).Status);
        var update = rows.Single(row => row.OperationType == BitwardenMutationOperationType.Update);
        Assert.Equal(BitwardenMutationStatus.Failed, update.Status);
        Assert.Equal(BitwardenFailureClass.Validation, update.LastFailureClass);
    }

    // Undoing a trash before it leaves the device cancels the delete, because a live row is the user's
    // later word. A purge has no such later word available - nothing restores an erased entry here - so a
    // row standing under an owed erase can only be a copy the pull brought back while the erase waited.
    // Cancelling on that would quietly undo a decision the user cannot take back on screen.
    [Fact]
    public async Task A_permanent_erase_is_not_cancelled_by_a_row_the_pull_put_back()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        await harness.Pull.ApplyAsync(harness.VaultId, Snapshot([remote]), [remote]);
        var doomed = await ReadAsync(harness);
        await harness.Repository.DeletePasswordPermanentlyAsync(doomed.Id);
        Assert.True(await harness.Purge.EnqueuePasswordAsync(doomed));
        await harness.Repository.SavePasswordAsync(doomed);

        var transport = new AcceptedTransport(NextRevision);
        var batch = await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            DateTimeOffset.UtcNow,
            transport);

        Assert.Equal(1, transport.Sends);
        Assert.Equal(1, batch.Completed);
        Assert.Equal(
            BitwardenMutationStatus.Completed,
            Assert.Single(await harness.Pending.GetAsync(harness.VaultId)).Status);
    }

    private const string CardNumber = "4111111111111111";

    // What the library's publish command leaves behind for a wallet row: it belongs to this vault by
    // account, the server has never named it, and every field is one the encoder can hand back unchanged -
    // the bank name echoes the brand and the type reads CREDIT, which is what a pull would have stored.
    private static SecureItem PublishedCard(long vaultId)
    {
        var data = new BankCardWalletData
        {
            CardholderName = "A Holder",
            Brand = "Example Credit Union",
            CardNumber = CardNumber,
            ExpiryMonth = "04",
            ExpiryYear = "2030",
            Cvv = "123",
            BankName = "Example Credit Union",
            CardTypeString = "CREDIT"
        };
        return new SecureItem
        {
            ItemType = VaultItemType.BankCard,
            Title = "Published card",
            Notes = "",
            ItemData = WalletItemDataCodec.EncodeBankCard(data),
            ImagePaths = "[]",
            BitwardenVaultId = vaultId
        };
    }

    private static async Task<PasswordEntry> SavePublishedAsync(Harness harness, string title)
    {
        // What the publish command leaves behind: the entry belongs to this vault by account, and the
        // server has not confirmed an identity for it.
        var entry = new PasswordEntry
        {
            Title = title,
            Username = "whoever",
            Password = "a local secret",
            BitwardenVaultId = harness.VaultId,
            BitwardenCipherType = 1
        };
        await harness.Repository.SavePasswordAsync(entry);
        return entry;
    }

    private static async Task<PasswordEntry> ReadByIdAsync(Harness harness, long id) =>
        (await harness.Repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true))
            .Single(entry => entry.Id == id);

    private static async Task<PasswordEntry> ReadAsync(Harness harness)
    {
        var stored = (await harness.Repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true))
            .Single(entry => entry.BitwardenCipherId == "cipher-edit");
        return stored;
    }

    private static async Task<PasswordEntry> RenameAsync(Harness harness, string title)
    {
        var stored = (await harness.Repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true))
            .Single(entry => entry.BitwardenCipherId == "cipher-edit");
        stored.Title = title;
        await harness.Repository.SavePasswordAsync(stored);
        return stored;
    }

    private static BitwardenDecodedCipher BaselineCipher() => BoundCipher(
        "cipher-edit",
        "Remote baseline",
        "baseline-user");

    // What the server reports about a cipher it moved to its own trash: the payload it kept, now marked
    // deleted at the revision the delete returned.
    private static BitwardenDecodedCipher TrashedCopyOf(
        PasswordEntry localTrashed,
        BitwardenDecodedCipher remote) =>
        new(
            remote.Metadata with
            {
                RevisionDate = NextRevision,
                IsDeleted = true,
                PayloadHash = BitwardenPayloadFingerprint.ForPassword(localTrashed, [], [])
            },
            remote.Password,
            null,
            [],
            []);

    // What the decoder really hands over for a cipher the server keeps in its trash: no payload at all, and
    // a `deleted:` marker where a content fingerprint would be. That marker is the only signal a later
    // restore has to be measured against.
    private static BitwardenDecodedCipher RemoteTrashOf(BitwardenDecodedCipher remote, string revision) =>
        new(
            remote.Metadata with
            {
                RevisionDate = revision,
                IsDeleted = true,
                PayloadHash = BitwardenPayloadFingerprint.ForRemoteDeletion(revision)
            },
            null,
            null,
            [],
            []);

    private static BitwardenRemoteFolder RemoteFolder(string id, string name) => new(id, name, null);

    // What the server reports for a cipher it keeps inside one of its folders: the folder id travels in the
    // metadata in clear, and the fingerprint the decoder promises covers it.
    private static BitwardenDecodedCipher FolderCipher(string cipherId, string title, string folderId)
    {
        var password = new PasswordEntry
        {
            Title = title,
            Username = "whoever",
            Password = $"baseline-password-{cipherId}",
            BitwardenCipherId = cipherId,
            BitwardenFolderId = folderId,
            BitwardenRevisionDate = BaselineRevision,
            BitwardenCipherType = 1
        };
        var metadata = new BitwardenRemoteCipherMetadata(
            cipherId,
            folderId,
            BaselineRevision,
            1,
            false,
            BitwardenPayloadFingerprint.ForPassword(password, [], []));
        return new BitwardenDecodedCipher(metadata, password, null, [], []);
    }

    private static async Task<PasswordEntry> ReadFolderedAsync(Harness harness) =>
        (await harness.Repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true))
            .Single(entry => entry.BitwardenCipherId == "cipher-foldered");

    // The same fact about a note: the folder id sits in the metadata in clear and is part of the fingerprint,
    // and the note's own content is carried by the save payload the codec derives from it.
    private static BitwardenDecodedCipher FolderNoteCipher(string cipherId, string title, string folderId)
    {
        var saved = NoteContentCodec.BuildSavePayload(title, "written on another device", "", isMarkdown: false);
        var item = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = saved.Title,
            Notes = saved.NotesCache,
            ItemData = saved.ItemData,
            ImagePaths = saved.ImagePaths,
            BitwardenCipherId = cipherId,
            BitwardenFolderId = folderId,
            BitwardenRevisionDate = BaselineRevision
        };
        var metadata = new BitwardenRemoteCipherMetadata(
            cipherId,
            folderId,
            BaselineRevision,
            2,
            false,
            BitwardenPayloadFingerprint.ForSecureItem(item));
        return new BitwardenDecodedCipher(metadata, null, item, [], []);
    }

    private static async Task<SecureItem> ReadFolderedNoteAsync(Harness harness) =>
        (await harness.Repository.GetSecureItemsAsync(itemType: null, includeDeleted: true))
            .Single(item => item.BitwardenCipherId == "cipher-note-foldered");

    private static BitwardenDecodedCipher BoundCipher(string cipherId, string title, string username)
    {
        var password = new PasswordEntry
        {
            Title = title,
            Username = username,
            Password = $"baseline-password-{cipherId}",
            BitwardenCipherId = cipherId,
            BitwardenRevisionDate = BaselineRevision,
            BitwardenCipherType = 1
        };
        var metadata = new BitwardenRemoteCipherMetadata(
            cipherId,
            null,
            BaselineRevision,
            1,
            false,
            BitwardenPayloadFingerprint.ForPassword(password, [], []));
        return new BitwardenDecodedCipher(metadata, password, null, [], []);
    }

    private static async Task<SecureItem> ReadNoteAsync(Harness harness) =>
        (await harness.Repository.GetSecureItemsAsync(itemType: null, includeDeleted: true))
            .Single(item => item.BitwardenCipherId == "cipher-note");

    // What the pull leaves behind for a note: Monica's own item type, the server's cipher number for it,
    // and a fingerprint computed the same way the stored row will be read back.
    private static BitwardenDecodedCipher BoundNoteCipher()
    {
        // A note is not stored as the text that was typed but as the save payload the codec derives from
        // it, which is the only shape the write-back encoder can promise to hand back.
        var saved = NoteContentCodec.BuildSavePayload(
            "Remote note",
            "written on another device",
            "",
            isMarkdown: false);
        var item = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = saved.Title,
            Notes = saved.NotesCache,
            ItemData = saved.ItemData,
            ImagePaths = saved.ImagePaths,
            BitwardenCipherId = "cipher-note",
            BitwardenRevisionDate = BaselineRevision
        };
        var metadata = new BitwardenRemoteCipherMetadata(
            "cipher-note",
            null,
            BaselineRevision,
            2,
            false,
            BitwardenPayloadFingerprint.ForSecureItem(item));
        return new BitwardenDecodedCipher(metadata, null, item, [], []);
    }

    private static BitwardenPullSnapshot Snapshot(IReadOnlyList<BitwardenDecodedCipher> ciphers) =>
        Snapshot([], ciphers);

    private static BitwardenPullSnapshot Snapshot(
        IReadOnlyList<BitwardenRemoteFolder> folders,
        IReadOnlyList<BitwardenDecodedCipher> ciphers) =>
        new(
            folders,
            ciphers.Select(cipher => cipher.Metadata).ToList(),
            BaselineRevision,
            true,
            new DateTimeOffset(2026, 7, 22, 3, 5, 0, TimeSpan.Zero));

    private static async Task<Harness> CreateHarnessAsync()
    {
        var factory = new SqliteConnectionFactory(TestTempPaths.CreateFilePath(".db"));
        var migrator = new DatabaseMigrator(factory);
        var crypto = new CryptoService();
        var hash = crypto.HashMasterPassword("vault password");
        crypto.InitializeSession("vault password", hash.Salt);
        var protector = new VaultDataProtector(crypto);
        var repository = new MonicaRepository(factory, migrator, protector);
        var accountStore = new BitwardenAccountStore(factory, migrator, crypto);
        var endpoints = BitwardenEndpointSet.UnitedStates;
        using var secrets = new BitwardenAccountSecrets(
            Encoding.UTF8.GetBytes("access"),
            Encoding.UTF8.GetBytes("refresh"),
            new byte[32],
            Enumerable.Repeat((byte)1, 32).ToArray(),
            Enumerable.Repeat((byte)2, 32).ToArray());
        var account = await accountStore.SaveConnectedAsync(new BitwardenAccount
        {
            Email = "local-change-queue@example.com",
            AccountKey = BitwardenAccountIdentity.CreateAccountKey("local-change-queue@example.com", endpoints),
            Endpoints = endpoints,
            Kdf = BitwardenKdfParameters.Pbkdf2()
        }, secrets);
        var folderStore = new BitwardenRemoteFolderStore(factory, migrator, crypto);
        var conflictStore = new BitwardenConflictBackupStore(factory, migrator, crypto);
        var syncState = new BitwardenSyncStateStore(factory, migrator);
        var pending = new BitwardenPendingOperationStore(factory, migrator, crypto);
        return new Harness(
            repository,
            syncState,
            pending,
            conflictStore,
            new BitwardenPullMergeService(repository, folderStore, conflictStore, syncState, pending),
            new BitwardenLocalChangeQueue(repository, syncState, pending, folderStore),
            new BitwardenMutationProcessor(pending, syncState, repository),
            new BitwardenConflictRestoreService(repository, conflictStore),
            new BitwardenPurgeQueue(pending),
            new BitwardenSymmetricKey(
                Enumerable.Repeat((byte)1, 32).ToArray(),
                Enumerable.Repeat((byte)2, 32).ToArray()),
            factory,
            migrator,
            account.Id);
    }

    private sealed record Harness(
        IMonicaRepository Repository,
        IBitwardenSyncStateStore SyncState,
        IBitwardenPendingOperationStore Pending,
        IBitwardenConflictBackupStore ConflictStore,
        BitwardenPullMergeService Pull,
        IBitwardenLocalChangeQueue Queue,
        IBitwardenMutationProcessor Processor,
        IBitwardenConflictRestoreService Restore,
        IBitwardenPurgeQueue Purge,
        BitwardenSymmetricKey VaultKey,
        SqliteConnectionFactory Factory,
        DatabaseMigrator Migrator,
        long VaultId);

    private sealed class AcceptedTransport(
        string revision,
        bool reject = false,
        string? assignedCipherId = null,
        int? failureStatus = null) : IBitwardenMutationTransport
    {
        public int Sends { get; private set; }

        public Task<BitwardenMutationResponse> SendAsync(
            BitwardenMutationRequest request,
            CancellationToken cancellationToken = default)
        {
            Sends++;
            int? failure = failureStatus ?? (reject ? (int?)409 : null);
            if (failure is { } status)
            {
                return Task.FromResult(new BitwardenMutationResponse(
                    false,
                    request.CipherId,
                    null,
                    status,
                    $"Bitwarden mutation answered HTTP {status}."));
            }

            return Task.FromResult(
                new BitwardenMutationResponse(true, assignedCipherId ?? request.CipherId, revision));
        }
    }
}
