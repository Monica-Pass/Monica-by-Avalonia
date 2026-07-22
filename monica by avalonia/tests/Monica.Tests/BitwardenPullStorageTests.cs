using System.Security.Cryptography;
using System.Text;
using Monica.Core.Bitwarden;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Bitwarden;
using Monica.Data.Repositories;
using Monica.Data.Services;

namespace Monica.Tests;

public sealed class BitwardenPullStorageTests
{
    [Fact]
    public async Task CompleteFolderSnapshotPreservesHierarchyAndTombstonesMissingFolders()
    {
        var harness = await CreateHarnessAsync();
        var store = new BitwardenRemoteFolderStore(harness.Factory, harness.Migrator, harness.Crypto);
        var seenAt = new DateTimeOffset(2026, 7, 22, 1, 0, 0, TimeSpan.Zero);
        await store.ReplaceCompleteSnapshotAsync(
            harness.VaultId,
            [
                new BitwardenRemoteFolder("parent", "Work", null),
                new BitwardenRemoteFolder("child", "Production", "parent")
            ],
            seenAt);

        var first = await store.GetAsync(harness.VaultId);
        Assert.Equal(2, first.Count);
        Assert.Equal("parent", first.Single(folder => folder.RemoteFolderId == "child").ParentRemoteFolderId);
        Assert.All(first, folder => Assert.False(folder.IsDeleted));
        Assert.DoesNotContain("Production", await ReadRawFolderDataAsync(harness.Factory), StringComparison.Ordinal);

        var repository = new MonicaRepository(harness.Factory, harness.Migrator);
        var category = new Category { Name = "Production" };
        await repository.SaveCategoryAsync(category);
        await store.BindLocalCategoryAsync(harness.VaultId, "child", category.Id);
        await store.ReplaceCompleteSnapshotAsync(
            harness.VaultId,
            [new BitwardenRemoteFolder("parent", "Work", null)],
            seenAt.AddMinutes(1));

        var second = await store.GetAsync(harness.VaultId);
        var missing = second.Single(folder => folder.RemoteFolderId == "child");
        Assert.True(missing.IsDeleted);
        Assert.Equal(category.Id, missing.LocalCategoryId);
    }

    [Fact]
    public async Task FolderSnapshotRejectsCyclesBeforePersistence()
    {
        var harness = await CreateHarnessAsync();
        var store = new BitwardenRemoteFolderStore(harness.Factory, harness.Migrator, harness.Crypto);

        await Assert.ThrowsAsync<BitwardenProtocolException>(() =>
            store.ReplaceCompleteSnapshotAsync(
                harness.VaultId,
                [
                    new BitwardenRemoteFolder("a", "A", "b"),
                    new BitwardenRemoteFolder("b", "B", "a")
                ],
                DateTimeOffset.UtcNow));

        Assert.Empty(await store.GetAsync(harness.VaultId));
    }

    [Fact]
    public async Task ConflictBackupIsAeadProtectedAndCanBeResolved()
    {
        var harness = await CreateHarnessAsync();
        var store = new BitwardenConflictBackupStore(harness.Factory, harness.Migrator, harness.Crypto);
        var createdAt = new DateTimeOffset(2026, 7, 22, 2, 0, 0, TimeSpan.Zero);
        var backup = new BitwardenConflictBackup(
            0,
            harness.VaultId,
            "cipher-1",
            "password",
            42,
            "2026-07-21T00:00:00Z",
            "2026-07-22T00:00:00Z",
            "{\"title\":\"Private portal\",\"password\":\"local-secret\"}",
            "remote revision changed",
            createdAt);

        var backupId = await store.SaveAsync(backup);

        var raw = await ReadRawConflictDataAsync(harness.Factory, backupId);
        Assert.StartsWith("vault:v1:", raw);
        Assert.DoesNotContain("local-secret", raw, StringComparison.Ordinal);
        var loaded = Assert.Single(await store.GetUnresolvedAsync(harness.VaultId));
        Assert.Equal(backup.PayloadJson, loaded.PayloadJson);
        Assert.Equal(createdAt, loaded.CreatedAt);

        await store.ResolveAsync(backupId);
        Assert.Empty(await store.GetUnresolvedAsync(harness.VaultId));
    }

    [Fact]
    public async Task MasterPasswordChangeReencryptsFolderAndConflictPayloads()
    {
        var harness = await CreateHarnessAsync();
        var folders = new BitwardenRemoteFolderStore(harness.Factory, harness.Migrator, harness.Crypto);
        var conflicts = new BitwardenConflictBackupStore(harness.Factory, harness.Migrator, harness.Crypto);
        await folders.ReplaceCompleteSnapshotAsync(
            harness.VaultId,
            [new BitwardenRemoteFolder("private", "Private folder", null)],
            DateTimeOffset.UtcNow);
        await conflicts.SaveAsync(new BitwardenConflictBackup(
            0,
            harness.VaultId,
            "cipher-private",
            "password",
            1,
            null,
            "2026-07-22T00:00:00Z",
            "{\"password\":\"before-change\"}",
            "test",
            DateTimeOffset.UtcNow));
        var service = new MasterPasswordMaintenanceService(
            harness.Factory,
            harness.Migrator,
            harness.Crypto);

        var result = await service.ChangeMasterPasswordAsync("vault password", "new vault password");

        Assert.True(result.Success, result.Message);
        Assert.True(result.BitwardenSecretsReencrypted >= 8);
        Assert.Equal("Private folder", Assert.Single(await folders.GetAsync(harness.VaultId)).Name);
        Assert.Contains(
            "before-change",
            Assert.Single(await conflicts.GetUnresolvedAsync(harness.VaultId)).PayloadJson,
            StringComparison.Ordinal);
    }

    private static async Task<Harness> CreateHarnessAsync()
    {
        var factory = new SqliteConnectionFactory(TestTempPaths.CreateFilePath(".db"));
        var migrator = new DatabaseMigrator(factory);
        var crypto = new CryptoService();
        var hash = crypto.HashMasterPassword("vault password");
        await new VaultCredentialStore(factory, migrator).SaveAsync(hash);
        crypto.InitializeSession("vault password", hash.Salt);
        var accountStore = new BitwardenAccountStore(factory, migrator, crypto);
        var endpoints = BitwardenEndpointSet.UnitedStates;
        var account = new BitwardenAccount
        {
            Email = "folders@example.com",
            AccountKey = BitwardenAccountIdentity.CreateAccountKey("folders@example.com", endpoints),
            Endpoints = endpoints,
            Kdf = BitwardenKdfParameters.Pbkdf2()
        };
        using var secrets = new BitwardenAccountSecrets(
            Encoding.UTF8.GetBytes("access"),
            Encoding.UTF8.GetBytes("refresh"),
            Enumerable.Range(0, 32).Select(value => (byte)value).ToArray(),
            Enumerable.Range(32, 32).Select(value => (byte)value).ToArray(),
            Enumerable.Range(64, 32).Select(value => (byte)value).ToArray());
        var saved = await accountStore.SaveConnectedAsync(account, secrets);
        return new Harness(factory, migrator, crypto, saved.Id);
    }

    private static async Task<string> ReadRawFolderDataAsync(ISqliteConnectionFactory factory)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT group_concat(encrypted_name, '|') FROM bitwarden_remote_folders";
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? "";
    }

    private static async Task<string> ReadRawConflictDataAsync(
        ISqliteConnectionFactory factory,
        long backupId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT encrypted_payload_json FROM bitwarden_conflict_backups WHERE id = $id";
        command.Parameters.AddWithValue("$id", backupId);
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? "";
    }

    private sealed record Harness(
        ISqliteConnectionFactory Factory,
        IDatabaseMigrator Migrator,
        CryptoService Crypto,
        long VaultId);
}
