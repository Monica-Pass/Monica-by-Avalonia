using System.Text;
using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.Bitwarden;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Bitwarden;
using Monica.Data.Repositories;

namespace Monica.Tests;

/// A pull must never destroy a local edit that the client still owes the server. These drive the real
/// editor path into the real repository, then replay an unchanged remote snapshot over it, because the
/// dirty flag that used to guard that window (BitwardenLocalModified) is not set by any edit path in
/// src/ - the only signal left is that local content differs at a revision the server already has.
public sealed class BitwardenLocalEditSurvivalTests
{
    private const string BaselineRevision = "2026-07-22T03:00:00Z";

    [Fact]
    public async Task Unchanged_remote_snapshot_is_recognised_as_no_change()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        await SaveLocalFromRemoteAsync(harness, remote.Password!);
        var service = new BitwardenPullMergeService(
            harness.Repository,
            harness.FolderStore,
            harness.ConflictStore,
            harness.SyncState);

        var result = await service.ApplyAsync(harness.VaultId, Snapshot(remote), [remote]);

        // Without this green there is no way to tell a fixture mismatch from a real overwrite below.
        Assert.Equal(1, result.Unchanged);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.ConflictsBackedUp);
    }

    [Fact]
    public async Task Pull_keeps_a_local_rename_recoverable_that_the_editor_never_marked_dirty()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        var saved = await SaveLocalFromRemoteAsync(harness, remote.Password!);
        var service = new BitwardenPullMergeService(
            harness.Repository,
            harness.FolderStore,
            harness.ConflictStore,
            harness.SyncState);

        await RenameThroughEditorAsync(harness, saved.Id, "Renamed on this device");

        var result = await service.ApplyAsync(harness.VaultId, Snapshot(remote), [remote]);

        var stored = (await harness.Repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true))
            .Single(entry => entry.Id == saved.Id);
        var backups = await harness.ConflictStore.GetUnresolvedAsync(harness.VaultId);
        var recoverable = stored.Title == "Renamed on this device" ||
            backups.Any(backup => backup.PayloadJson.Contains("Renamed on this device", StringComparison.Ordinal));

        Assert.True(
            recoverable,
            "The edit is gone from the vault and from every backup. " +
            $"titleAfterPull={stored.Title}, updated={result.Updated}, conflictsBackedUp={result.ConflictsBackedUp}, " +
            $"unresolvedBackups={backups.Count}.");
    }

    [Fact]
    public async Task Pull_reports_the_vault_clean_even_though_the_edit_was_never_uploaded()
    {
        var harness = await CreateHarnessAsync();
        var remote = BaselineCipher();
        var saved = await SaveLocalFromRemoteAsync(harness, remote.Password!);
        var pending = new BitwardenPendingOperationStore(harness.Factory, harness.Migrator, harness.Crypto);
        var service = new BitwardenPullMergeService(
            harness.Repository,
            harness.FolderStore,
            harness.ConflictStore,
            harness.SyncState);

        await RenameThroughEditorAsync(harness, saved.Id, "Renamed on this device");
        await service.ApplyAsync(harness.VaultId, Snapshot(remote), [remote]);

        var stored = (await harness.Repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true))
            .Single(entry => entry.Id == saved.Id);

        var local = (await pending.GetAsync(harness.VaultId)).Count;
        var unresolved = (await harness.ConflictStore.GetUnresolvedAsync(harness.VaultId)).Count;
        var reportsFullyInSync = !stored.BitwardenLocalModified && local == 0 && unresolved == 0;
        var editKept = stored.Title == "Renamed on this device";

        // Whatever the merge decides about the edit itself, the account panel may not render
        // "已同步 / 无待处理更改" while a change the server never received has been discarded.
        Assert.True(
            editKept || !reportsFullyInSync,
            $"Pull discarded the edit and still reports the vault in sync. " +
            $"titleAfterPull={stored.Title}, localModified={stored.BitwardenLocalModified}, " +
            $"pendingOperations={local}, unresolvedBackups={unresolved}.");
    }

    private static async Task RenameThroughEditorAsync(Harness harness, long entryId, string newTitle)
    {
        var source = (await harness.Repository.GetPasswordsAsync(includeDeleted: false, includeArchived: false))
            .Single(entry => entry.Id == entryId);
        var editor = new PasswordEditorViewModel(
            new LocalizationService(),
            new PasswordGeneratorService(),
            source,
            [],
            source.Password);
        editor.Title = newTitle;

        // Exactly MainWindowViewModel.PasswordCrudCommands.SavePasswordAsync: build from the edited
        // source and store. Nothing in this path raises a dirty flag for a bound item.
        var edited = editor.BuildEntryFrom(source, editor.GetPasswordRows()[0]);
        await harness.Repository.SavePasswordAsync(edited);
        editor.Dispose();
    }

    private static BitwardenDecodedCipher BaselineCipher()
    {
        var password = new PasswordEntry
        {
            Title = "Remote baseline",
            Username = "baseline-user",
            Password = "baseline-password",
            BitwardenCipherId = "cipher-edit",
            BitwardenRevisionDate = BaselineRevision,
            BitwardenCipherType = 1
        };
        var metadata = new BitwardenRemoteCipherMetadata(
            "cipher-edit",
            null,
            BaselineRevision,
            1,
            false,
            BitwardenPayloadFingerprint.ForPassword(password, [], []));
        return new BitwardenDecodedCipher(metadata, password, null, [], []);
    }

    private static BitwardenPullSnapshot Snapshot(BitwardenDecodedCipher cipher) =>
        new(
            [],
            [cipher.Metadata],
            "2026-07-22T03:04:00Z",
            true,
            new DateTimeOffset(2026, 7, 22, 3, 5, 0, TimeSpan.Zero));

    private static async Task<PasswordEntry> SaveLocalFromRemoteAsync(
        Harness harness,
        PasswordEntry remote)
    {
        var entry = remote.CreateDetachedCopy();
        entry.BitwardenVaultId = harness.VaultId;
        await harness.Repository.SavePasswordAsync(entry);
        return entry;
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
            Email = "edit-survival@example.com",
            AccountKey = BitwardenAccountIdentity.CreateAccountKey("edit-survival@example.com", endpoints),
            Endpoints = endpoints,
            Kdf = BitwardenKdfParameters.Pbkdf2()
        }, secrets);
        var folderStore = new BitwardenRemoteFolderStore(factory, migrator, crypto);
        var conflictStore = new BitwardenConflictBackupStore(factory, migrator, crypto);
        var syncState = new BitwardenSyncStateStore(factory, migrator);
        return new Harness(
            repository,
            folderStore,
            conflictStore,
            syncState,
            factory,
            migrator,
            crypto,
            account.Id);
    }

    private sealed record Harness(
        IMonicaRepository Repository,
        IBitwardenRemoteFolderStore FolderStore,
        IBitwardenConflictBackupStore ConflictStore,
        IBitwardenSyncStateStore SyncState,
        SqliteConnectionFactory Factory,
        DatabaseMigrator Migrator,
        CryptoService Crypto,
        long VaultId);
}
