using System.Text;
using Monica.Core.Bitwarden;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Bitwarden;
using Monica.Data.Repositories;

namespace Monica.Tests;

public sealed class BitwardenPullMergeServiceTests
{
    [Fact]
    public async Task PullMergeBacksUpDirtyDataAndAppliesSupportedRemoteItems()
    {
        var harness = await CreateHarnessAsync();
        var category = new Category
        {
            Name = "Remote folder",
            BitwardenVaultId = harness.VaultId,
            BitwardenFolderId = "folder-1"
        };
        await harness.Repository.SaveCategoryAsync(category);
        await harness.FolderStore.ReplaceCompleteSnapshotAsync(
            harness.VaultId,
            [new BitwardenRemoteFolder("folder-1", "Remote folder", null)],
            DateTimeOffset.UtcNow);
        await harness.FolderStore.BindLocalCategoryAsync(harness.VaultId, "folder-1", category.Id);

        var clean = await SaveLocalPasswordAsync(harness, "cipher-update", "Local clean", false);
        var dirty = await SaveLocalPasswordAsync(harness, "cipher-conflict", "Local dirty secret", true);
        var deleted = await SaveLocalPasswordAsync(harness, "cipher-delete", "Delete me", false);
        var unmatched = await SaveLocalPasswordAsync(harness, "cipher-local", "Keep local", false);

        var update = DecodedPassword("cipher-update", "Remote updated", "2026-07-22T03:00:00Z", "folder-1");
        var conflict = DecodedPassword("cipher-conflict", "Remote conflict winner", "2026-07-22T03:01:00Z", null);
        var note = DecodedNote("cipher-note", "Remote note", "2026-07-22T03:02:00Z", "folder-1");
        var autoNote = DecodedNote("cipher-auto-note", "Auto folder note", "2026-07-22T03:02:30Z", "folder-2");
        var deletion = new BitwardenRemoteCipherMetadata(
            "cipher-delete",
            null,
            "2026-07-22T03:03:00Z",
            1,
            true,
            "deleted");
        var snapshot = new BitwardenPullSnapshot(
            [
                new BitwardenRemoteFolder("folder-1", "Remote folder", null),
                new BitwardenRemoteFolder("folder-2", "Nested folder", "folder-1")
            ],
            [update.Metadata, conflict.Metadata, note.Metadata, autoNote.Metadata, deletion],
            "2026-07-22T03:04:00Z",
            true,
            new DateTimeOffset(2026, 7, 22, 3, 5, 0, TimeSpan.Zero));
        var service = new BitwardenPullMergeService(
            harness.Repository,
            harness.FolderStore,
            harness.ConflictStore);

        var result = await service.ApplyAsync(
            harness.VaultId,
            snapshot,
            [update, conflict, note, autoNote]);

        Assert.Equal(2, result.Added);
        Assert.Equal(2, result.Updated);
        Assert.Equal(1, result.Deleted);
        Assert.Equal(1, result.ConflictsBackedUp);
        Assert.Equal(1, result.PreservedLocalOnly);
        var passwords = await harness.Repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true);
        Assert.Equal("Remote updated", passwords.Single(entry => entry.Id == clean.Id).Title);
        Assert.Equal("Remote conflict winner", passwords.Single(entry => entry.Id == dirty.Id).Title);
        Assert.True(passwords.Single(entry => entry.Id == deleted.Id).IsDeleted);
        Assert.Equal("Keep local", passwords.Single(entry => entry.Id == unmatched.Id).Title);

        var backup = Assert.Single(await harness.ConflictStore.GetUnresolvedAsync(harness.VaultId));
        Assert.Contains("Local dirty secret", backup.PayloadJson, StringComparison.Ordinal);
        var savedNote = Assert.Single(
            await harness.Repository.GetSecureItemsAsync(),
            item => item.BitwardenCipherId == "cipher-note");
        Assert.Equal(category.Id, savedNote.CategoryId);
        Assert.Equal("Remote note", savedNote.Title);
        var nestedCategory = Assert.Single(
            await harness.Repository.GetCategoriesAsync(),
            item => item.BitwardenFolderId == "folder-2");
        Assert.Equal(category.Id, nestedCategory.ParentCategoryId);
        var autoFolderNote = Assert.Single(
            await harness.Repository.GetSecureItemsAsync(),
            item => item.BitwardenCipherId == "cipher-auto-note");
        Assert.Equal(nestedCategory.Id, autoFolderNote.CategoryId);
    }

    private static async Task<PasswordEntry> SaveLocalPasswordAsync(
        Harness harness,
        string cipherId,
        string title,
        bool modified)
    {
        var entry = new PasswordEntry
        {
            Title = title,
            Password = "local-password",
            BitwardenVaultId = harness.VaultId,
            BitwardenCipherId = cipherId,
            BitwardenRevisionDate = "2026-07-21T00:00:00Z",
            BitwardenCipherType = 1,
            BitwardenLocalModified = modified
        };
        await harness.Repository.SavePasswordAsync(entry);
        return entry;
    }

    private static BitwardenDecodedCipher DecodedPassword(
        string cipherId,
        string title,
        string revision,
        string? folderId)
    {
        var password = new PasswordEntry
        {
            Title = title,
            Username = "remote-user",
            Password = "remote-password",
            BitwardenCipherId = cipherId,
            BitwardenFolderId = folderId,
            BitwardenRevisionDate = revision,
            BitwardenCipherType = 1
        };
        var metadata = new BitwardenRemoteCipherMetadata(
            cipherId,
            folderId,
            revision,
            1,
            false,
            BitwardenPayloadFingerprint.ForPassword(password, [], []));
        return new BitwardenDecodedCipher(metadata, password, null, [], []);
    }

    private static BitwardenDecodedCipher DecodedNote(
        string cipherId,
        string title,
        string revision,
        string? folderId)
    {
        var note = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = title,
            Notes = "remote note body",
            ItemData = "{}",
            BitwardenCipherId = cipherId,
            BitwardenFolderId = folderId,
            BitwardenRevisionDate = revision
        };
        var metadata = new BitwardenRemoteCipherMetadata(
            cipherId,
            folderId,
            revision,
            2,
            false,
            BitwardenPayloadFingerprint.ForSecureItem(note));
        return new BitwardenDecodedCipher(metadata, null, note, [], []);
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
            Email = "merge@example.com",
            AccountKey = BitwardenAccountIdentity.CreateAccountKey("merge@example.com", endpoints),
            Endpoints = endpoints,
            Kdf = BitwardenKdfParameters.Pbkdf2()
        }, secrets);
        var folderStore = new BitwardenRemoteFolderStore(factory, migrator, crypto);
        var conflictStore = new BitwardenConflictBackupStore(factory, migrator, crypto);
        return new Harness(repository, folderStore, conflictStore, account.Id);
    }

    private sealed record Harness(
        IMonicaRepository Repository,
        IBitwardenRemoteFolderStore FolderStore,
        IBitwardenConflictBackupStore ConflictStore,
        long VaultId);
}
