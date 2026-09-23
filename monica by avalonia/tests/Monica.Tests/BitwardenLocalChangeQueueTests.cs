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

    private static BitwardenPullSnapshot Snapshot(IReadOnlyList<BitwardenDecodedCipher> ciphers) =>
        new(
            [],
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
            new BitwardenPullMergeService(repository, folderStore, conflictStore, syncState),
            new BitwardenLocalChangeQueue(repository, syncState, pending),
            new BitwardenMutationProcessor(pending, syncState, repository),
            new BitwardenConflictRestoreService(repository, conflictStore),
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
        BitwardenSymmetricKey VaultKey,
        SqliteConnectionFactory Factory,
        DatabaseMigrator Migrator,
        long VaultId);

    private sealed class AcceptedTransport(
        string revision,
        bool reject = false,
        string? assignedCipherId = null) : IBitwardenMutationTransport
    {
        public Task<BitwardenMutationResponse> SendAsync(
            BitwardenMutationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(reject
                ? new BitwardenMutationResponse(false, request.CipherId, null, 409, "revision conflict")
                : new BitwardenMutationResponse(true, assignedCipherId ?? request.CipherId, revision));
    }
}
