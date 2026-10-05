using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data.Mdbx;
using Monica.Data;
using Monica.Data.Repositories;
using Monica.Mdbx.Ffi;
using Monica.Platform.Services;

// Compiled in memory by verify-snapshots.ps1 against product assemblies only.
public static class SnapshotVerification
{
    public static string CurrentCheck { get; private set; } = "initialization";
    public static int Checks { get; private set; }

    private static void Check(bool condition, string name)
    {
        CurrentCheck = name;
        if (!condition) throw new InvalidOperationException("Snapshot verification check failed.");
        Checks++;
        Console.WriteLine("PASS " + name);
    }

    private static async Task Reject(Func<Task> action, string name, string reason = null)
    {
        CurrentCheck = name;
        try { await action(); }
        catch (MdbxSnapshotException exception)
        {
            Check(reason == null || exception.ReasonCode == reason, name);
            return;
        }
        throw new InvalidOperationException("Expected snapshot refusal.");
    }

    private static byte[] Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return SHA256.HashData(stream);
    }

    private static bool Equal(byte[] left, byte[] right) => left.SequenceEqual(right);

    private static async Task<byte[]> Bytes(Stream stream)
    {
        stream.Position = 0;
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        return copy.ToArray();
    }

    private static LocalMdbxDatabase Database(string path, string credential) => new()
    {
        FilePath = path, WorkingCopyPath = path, EncryptedPassword = credential,
        StorageLocation = MdbxStorageLocation.Internal
    };

    private static async Task<MdbxVault> ReadCopy(MdbxUniffiNativeBridge bridge, string path, string credential)
    {
        var copy = Path.Combine(Path.GetDirectoryName(path), "read-" + Guid.NewGuid().ToString("N") + ".mdbx");
        await bridge.CreatePortableBackupAsync(path, copy);
        return MdbxFfi.OpenVault(copy, credential, "snapshot-verification-read");
    }

    private static void ExecuteSql(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public static async Task RunAsync(string directory)
    {
        var credential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var bridge = new MdbxUniffiNativeBridge();
        Check(bridge.IsAvailable, "native-runtime-available");
        using var session = new VaultSessionService();
        session.MarkUnlocked();
        using var store = new MdbxVaultStore(bridge, null, session);
        var service = new MdbxVaultService(null, bridge, store);
        var source = Path.Combine(directory, "source.mdbx");
        var snapshot = Path.Combine(directory, "frozen.mdbx");
        var database = Database(source, credential);
        var attachmentBytes = RandomNumberGenerator.GetBytes(257);
        string projectId, objectId, knownId, attachmentId;

        CurrentCheck = "live-wal-snapshot";
        using (var native = MdbxFfi.CreateVaultWithTigaMode(source, credential,
            "snapshot-verification", Monica.Mdbx.Ffi.MdbxTigaMode.Multi))
        {
            projectId = native.CreateProject("Snapshot fixture").ProjectId;
            objectId = native.CreateObject(projectId, "com.example.future.v9", "Frozen",
                "{\"integer\":9007199254740993,\"nested\":[null,true]}", 9).ObjectId;
            knownId = native.CreateObject(projectId, "login", "Known frozen",
                "{\"kind\":\"password\",\"room_id\":101,\"username\":\"fixture\"}", 1).ObjectId;
            attachmentId = Guid.NewGuid().ToString();
            native.CreateAttachmentWithContent(Guid.NewGuid().ToString(),
                new MdbxAttachmentCreateRequest(attachmentId, projectId, knownId, "inline.bin", "application/octet-stream"),
                attachmentBytes, MdbxFfi.DefaultAttachmentContentLimits());
            Check(File.Exists(source + "-wal") && new FileInfo(source + "-wal").Length > 32, "committed-wal-fixture");
            var mainBefore = Hash(source);
            var walBefore = Hash(source + "-wal");
            await using var stream = await service.OpenSnapshotStreamAsync(database);
            Check(stream.CanRead && stream.CanSeek && !stream.CanWrite, "snapshot-stream-read-only");
            var frozen = await Bytes(stream);
            File.WriteAllBytes(snapshot, frozen);
            Check(Equal(mainBefore, Hash(source)) && Equal(walBefore, Hash(source + "-wal")), "validation-does-not-change-source-main-or-wal");
            native.UpdateObject(projectId, objectId, "com.example.future.v9", "Local changed", "{\"changed\":true}", 9);
            native.UpdateObject(projectId, knownId, "login", "Known changed",
                "{\"kind\":\"password\",\"room_id\":101,\"username\":\"changed\"}", 1);
            Check(Equal(frozen, await Bytes(stream)), "frozen-stream-unchanged-after-source-write");
        }

        Check(!File.Exists(snapshot + "-wal") && !File.Exists(snapshot + "-shm"), "single-file-snapshot-has-no-sidecars");
        using (var read = await ReadCopy(bridge, snapshot, credential))
        {
            var unknown = read.GetObject(projectId, objectId);
            Check(unknown.Title == "Frozen" && unknown.PayloadSchemaVersion == 9 &&
                unknown.PayloadJson.Contains("9007199254740993"), "unknown-payload-and-version-retained");
            Check(read.GetObject(projectId, knownId).Title == "Known frozen", "snapshot-includes-committed-wal-object");
            Check(Equal(read.ReadAttachmentContent(attachmentId, 4096), attachmentBytes), "inline-attachment-preserved");
        }

        CurrentCheck = "snapshot-export";
        var exported = Path.Combine(directory, "exported.mdbx");
        Check(!File.Exists(source + "-wal") && !File.Exists(source + "-shm"), "inactive-source-has-no-sidecars");
        await service.CreateSnapshotAsync(database, exported);
        Check(!File.Exists(source + "-wal") && !File.Exists(source + "-shm"), "export-does-not-create-source-sidecars");
        var exportedHash = Hash(exported);
        await Reject(() => service.CreateSnapshotAsync(database, exported), "export-does-not-clobber", "destination-exists");
        Check(Equal(exportedHash, Hash(exported)), "existing-export-bytes-preserved");
        var journalDestination = Path.Combine(directory, "journal-destination.mdbx");
        File.WriteAllText(journalDestination + "-journal", "owned fixture marker");
        await Reject(() => service.CreateSnapshotAsync(database, journalDestination), "export-refuses-destination-journal", "destination-exists");

        CurrentCheck = "upload-freshness";
        await using (var uploaded = await service.OpenSnapshotStreamAsync(database))
        {
            bool? current = null;
            await service.CommitSnapshotUploadAsync(database, uploaded, (value, token) =>
            {
                current = value;
                return Task.CompletedTask;
            });
            Check(current == true, "unchanged-upload-marks-current");
            using (var native = MdbxFfi.OpenVault(source, credential, "snapshot-verification"))
                native.UpdateObject(projectId, objectId, "com.example.future.v9", "After upload", "{}", 9);
            await service.CommitSnapshotUploadAsync(database, uploaded, (value, token) =>
            {
                current = value;
                return Task.CompletedTask;
            });
            Check(current == false, "concurrent-local-change-keeps-upload-pending");
        }

        CurrentCheck = "cached-handle-restore";
        var before = await store.GetUnknownEntriesAsync(database);
        Check(before.Any(value => value.Title == "After upload"), "store-cached-original-handle");
        using (await store.AcquireFileReplacementAsync(source))
            Check(!File.Exists(source + "-wal") && !File.Exists(source + "-shm"), "coordinator-close-removes-native-sidecars");
        await store.GetUnknownEntriesAsync(database);
        CurrentCheck = "cached-handle-restore";
        var commits = 0;
        var receipt = await service.RestoreSnapshotAsync(database, snapshot, token =>
        {
            commits++;
            return Task.CompletedTask;
        });
        Check(commits == 1 && File.Exists(receipt.RecoveryPath), "restore-commits-with-fresh-recovery");
        var after = await store.GetUnknownEntriesAsync(database);
        Check(after.Any(value => value.Title == "Frozen") && !after.Any(value => value.Title == "After upload"), "store-reopens-replaced-database");
        using (var read = await ReadCopy(bridge, receipt.RecoveryPath, credential))
            Check(read.GetObject(projectId, objectId).Title == "After upload", "recovery-preserves-latest-local-data");

        CurrentCheck = "metadata-failure-rollback";
        using (await store.AcquireFileReplacementAsync(source)) { }
        var originalHash = Hash(source);
        var recoveryCount = Directory.GetFiles(directory, "*.local-conflict-*.mdbx").Length;
        var failed = false;
        try
        {
            await service.RestoreSnapshotAsync(database, exported,
                token => Task.FromException(new InvalidOperationException("Injected fixture metadata failure.")));
        }
        catch (InvalidOperationException) { failed = true; }
        Check(failed && Equal(originalHash, Hash(source)), "metadata-failure-restores-original-bytes");
        Check(Directory.GetFiles(directory, "*.local-conflict-*.mdbx").Length == recoveryCount + 1, "rollback-keeps-consistent-recovery");

        CurrentCheck = "refusal-preserves-target";
        var wrongPassword = Database(source, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var incomingBefore = Hash(exported);
        await Reject(() => service.RestoreSnapshotAsync(wrongPassword, exported, token => Task.CompletedTask),
            "wrong-password-refused", "credential-or-integrity");
        Check(Equal(originalHash, Hash(source)) && Equal(incomingBefore, Hash(exported)), "failed-validation-preserves-target-and-incoming");
        var corrupt = Path.Combine(directory, "corrupt.mdbx");
        File.WriteAllBytes(corrupt, RandomNumberGenerator.GetBytes(64));
        await Reject(() => service.RestoreSnapshotAsync(database, corrupt, token => Task.CompletedTask), "corrupt-snapshot-refused");
        var foreign = Path.Combine(directory, "foreign.mdbx");
        using (MdbxFfi.CreateVaultWithTigaMode(foreign, credential, "foreign-fixture", Monica.Mdbx.Ffi.MdbxTigaMode.Multi)) { }
        await Reject(() => service.RestoreSnapshotAsync(database, foreign, token => Task.CompletedTask), "foreign-vault-refused", "mismatched-vault");
        var future = Path.Combine(directory, "future.mdbx");
        await bridge.CreatePortableBackupAsync(exported, future);
        ExecuteSql(future, "UPDATE vault_meta SET format_version = 'MDBX-FUTURE'");
        await Reject(() => service.RestoreSnapshotAsync(database, future, token => Task.CompletedTask), "unsupported-format-refused");
        Check(Equal(originalHash, Hash(source)), "all-refusals-preserve-original-bytes");

        var absentPath = Path.Combine(directory, "previously-bound.mdbx");
        var absent = Database(absentPath, credential);
        absent.LastSyncedAt = DateTimeOffset.UtcNow;
        await Reject(() => service.RestoreSnapshotAsync(absent, exported, token => Task.CompletedTask),
            "bound-vault-with-missing-cache-refused", "identity-unavailable");
        Check(!File.Exists(absentPath), "missing-bound-cache-stays-absent");
        var initialPath = Path.Combine(directory, "initial.mdbx");
        var initial = Database(initialPath, credential);
        var initialReceipt = await service.RestoreSnapshotAsync(initial, exported, token => Task.CompletedTask);
        Check(File.Exists(initialPath) && initialReceipt.RecoveryPath == null, "initial-unbound-restore-creates-target");
        var failedInitialPath = Path.Combine(directory, "initial-failed.mdbx");
        try
        {
            await service.RestoreSnapshotAsync(Database(failedInitialPath, credential), exported,
                token => Task.FromException(new InvalidOperationException("Injected fixture metadata failure.")));
        }
        catch (InvalidOperationException) { }
        Check(!File.Exists(failedInitialPath), "initial-restore-metadata-failure-removes-uncommitted-target");

        CurrentCheck = "external-blobs";
        Directory.CreateDirectory(source + ".blobs");
        File.WriteAllText(Path.Combine(source + ".blobs", "orphan"), "owned fixture marker");
        await Reject(async () => { await using var ignored = await service.OpenSnapshotStreamAsync(database); },
            "nonempty-blob-directory-refused", "external-blobs");
        Directory.Delete(source + ".blobs", true);
        var external = Path.Combine(directory, "external.mdbx");
        using (var native = MdbxFfi.CreateVaultWithTigaMode(external, credential,
            "external-fixture", Monica.Mdbx.Ffi.MdbxTigaMode.Multi))
        {
            var project = native.CreateProject("External fixture");
            native.CreateAttachmentWithExternalContent(Guid.NewGuid().ToString(),
                new MdbxAttachmentCreateRequest(Guid.NewGuid().ToString(), project.ProjectId, null,
                    "external.bin", "application/octet-stream"), attachmentBytes, MdbxFfi.DefaultAttachmentContentLimits());
        }
        var externalBackup = Path.Combine(directory, "external-no-blobs.mdbx");
        await bridge.CreatePortableBackupAsync(external, externalBackup);
        await Reject(async () => { await using var ignored = await service.OpenSnapshotStreamAsync(Database(externalBackup, credential)); },
            "external-reference-without-files-refused", "external-blobs");

        CurrentCheck = "busy-vault-guard";
        using (var writable = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
            await Reject(() => service.RestoreSnapshotAsync(database, exported, token => Task.CompletedTask), "external-writable-handle-refused", "vault-busy");
        File.WriteAllText(source + "-journal", "owned fixture marker");
        await Reject(() => service.RestoreSnapshotAsync(database, exported, token => Task.CompletedTask), "live-journal-refused", "vault-busy");
        Check(File.Exists(source + "-journal") && Equal(originalHash, Hash(source)), "unexpected-sidecar-not-deleted");
        File.Delete(source + "-journal");

        CurrentCheck = "queued-generation";
        Task<System.Collections.Generic.IReadOnlyList<MdbxUnknownEntryDescriptor>> queued;
        using (await store.AcquireFileReplacementAsync(source))
        {
            queued = store.GetUnknownEntriesAsync(database);
            Check(!queued.IsCompleted, "native-call-waits-for-replacement-gate");
        }
        await Reject(async () => { await queued; }, "queued-call-rejects-replaced-generation", "vault-changed");
        using (await store.AcquireSnapshotCommitAsync(source))
            queued = store.GetUnknownEntriesAsync(database);
        Check((await queued).Count > 0, "upload-commit-does-not-invalidate-queued-crud");
        using (await store.AcquireFileReplacementAsync(source)) { }
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            var canceled = false;
            try { await service.RestoreSnapshotAsync(database, exported, token => Task.CompletedTask, cancellation.Token); }
            catch (OperationCanceledException) { canceled = true; }
            Check(canceled && Equal(originalHash, Hash(source)), "cancellation-before-publication-preserves-original");
        }
        await VerifyPendingMetadataAsync(directory, database, store);
        Check(!Directory.GetDirectories(directory, ".monica-snapshot-*").Any(), "owned-staging-cleaned-after-all-outcomes");
        Console.WriteLine("Snapshot verification passed: " + Checks + " checks.");
    }

    private static async Task VerifyPendingMetadataAsync(string directory, LocalMdbxDatabase database, MdbxVaultStore store)
    {
        CurrentCheck = "repository-pending-metadata";
        var factory = new SqliteConnectionFactory(Path.Combine(directory, "metadata.db"));
        var inner = new MonicaRepository(factory, new DatabaseMigrator(factory), null, null);
        var repository = new MdbxBackedMonicaRepository(inner, store, null);
        database.IsDefault = true;
        foreach (var location in new[] { MdbxStorageLocation.RemoteWebDav, MdbxStorageLocation.RemoteOneDrive })
        {
            database.StorageLocation = location;
            database.LastSyncStatus = SyncStatus.PendingUpload;
            database.RemoteETag = "old-fixture-version";
            await repository.SaveMdbxDatabaseAsync(database);
            var entry = (await repository.GetPasswordsAsync()).Single();
            entry.Title = "Repository changed " + location;
            Task<long> write;
            using (await store.AcquireSnapshotCommitAsync(database.WorkingCopyPath))
            {
                // The writer has already obtained its cached Pending metadata and must wait for
                // the native gate. Model a successful upload commit while that writer is queued.
                write = repository.SavePasswordAsync(entry);
                Check(!write.IsCompleted, "repository-writer-queued-" + location);
                var uploaded = (await inner.GetMdbxDatabasesAsync()).Single();
                uploaded.LastSyncStatus = SyncStatus.Synced;
                uploaded.RemoteETag = "new-fixture-version";
                uploaded.LastSyncedAt = DateTimeOffset.UtcNow;
                await repository.SaveMdbxDatabaseAsync(uploaded);
            }
            await write;
            var current = (await inner.GetMdbxDatabasesAsync()).Single();
            Check(current.LastSyncStatus == SyncStatus.PendingUpload, "post-upload-write-marked-pending-" + location);
            Check(current.RemoteETag == "new-fixture-version" && current.LastSyncedAt != null,
                "post-upload-write-preserves-validator-" + location);
        }
    }
}
