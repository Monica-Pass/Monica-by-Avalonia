using System.Text;
using Monica.Core.Bitwarden;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Bitwarden;
using Monica.Data.Repositories;

namespace Monica.Tests;

public sealed class BitwardenMutationProcessorTests
{
    [Fact]
    public async Task ProcessorCompletesDefersAndBacksUpConflictsWithoutDuplicateClaims()
    {
        var harness = await CreateHarnessAsync();
        var success = await SavePasswordAsync(harness, "cipher-success", "Success item");
        var conflict = await SavePasswordAsync(harness, "cipher-conflict", "Conflict secret");
        var retry = await SavePasswordAsync(harness, "cipher-retry", "Retry item");
        var now = new DateTimeOffset(2026, 7, 22, 6, 0, 0, TimeSpan.Zero);
        foreach (var item in new[] { success, conflict, retry })
        {
            await harness.OperationStore.EnqueueAsync(Operation(harness.VaultId, item, now));
        }

        var transport = new ScriptedTransport(request => request.CipherId switch
        {
            "cipher-success" => new BitwardenMutationResponse(
                true,
                "cipher-success",
                "2026-07-22T06:00:01Z"),
            "cipher-conflict" => new BitwardenMutationResponse(
                false,
                null,
                null,
                412,
                "revision conflict"),
            _ => new BitwardenMutationResponse(
                false,
                null,
                null,
                503,
                "service unavailable")
        });

        var result = await harness.Processor.ProcessReadyAsync(
            harness.VaultId,
            now,
            transport);

        Assert.Equal(3, result.Claimed);
        Assert.Equal(1, result.Completed);
        Assert.Equal(1, result.Conflicts);
        Assert.Equal(1, result.Deferred);
        var queue = await harness.OperationStore.GetAsync(harness.VaultId);
        Assert.Equal(BitwardenMutationStatus.Completed, queue.Single(item => item.CipherId == "cipher-success").Status);
        Assert.Equal(BitwardenMutationStatus.Conflict, queue.Single(item => item.CipherId == "cipher-conflict").Status);
        Assert.Equal(BitwardenMutationStatus.Pending, queue.Single(item => item.CipherId == "cipher-retry").Status);

        var passwords = await harness.Repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true);
        var savedSuccess = passwords.Single(item => item.Id == success.Id);
        Assert.False(savedSuccess.BitwardenLocalModified);
        Assert.Equal("2026-07-22T06:00:01Z", savedSuccess.BitwardenRevisionDate);
        Assert.True(passwords.Single(item => item.Id == conflict.Id).BitwardenLocalModified);
        var backup = Assert.Single(await harness.ConflictStore.GetUnresolvedAsync(harness.VaultId));
        Assert.Contains("Conflict secret", backup.PayloadJson, StringComparison.Ordinal);
        Assert.Equal(3, transport.RequestCount);
    }

    [Fact]
    public async Task ProcessorHandlesCreateAndDeleteRevisionSemantics()
    {
        var harness = await CreateHarnessAsync();
        var created = await SavePasswordAsync(harness, "local-create-id", "Create item");
        created.BitwardenRevisionDate = null;
        await harness.Repository.SavePasswordAsync(created);
        var deleted = await SavePasswordAsync(harness, "cipher-delete", "Delete item");
        deleted.IsDeleted = true;
        await harness.Repository.SavePasswordAsync(deleted);
        var now = new DateTimeOffset(2026, 7, 22, 6, 30, 0, TimeSpan.Zero);
        await harness.OperationStore.EnqueueAsync(new BitwardenPendingOperation(
            0,
            harness.VaultId,
            "local-create-id",
            BitwardenMutationOperationType.Create,
            null,
            "{\"title\":\"Create item\"}",
            $"{harness.VaultId}:create:local-create-id",
            BitwardenMutationStatus.Pending,
            BitwardenFailureClass.None,
            0,
            now,
            null,
            null,
            now,
            now));
        await harness.OperationStore.EnqueueAsync(new BitwardenPendingOperation(
            0,
            harness.VaultId,
            "cipher-delete",
            BitwardenMutationOperationType.Delete,
            deleted.BitwardenRevisionDate,
            "{}",
            $"{harness.VaultId}:delete:cipher-delete",
            BitwardenMutationStatus.Pending,
            BitwardenFailureClass.None,
            0,
            now,
            null,
            null,
            now,
            now));
        var transport = new ScriptedTransport(request => request.OperationType switch
        {
            BitwardenMutationOperationType.Create => new BitwardenMutationResponse(
                true,
                "remote-created-id",
                "2026-07-22T06:30:01Z"),
            _ => new BitwardenMutationResponse(true, "cipher-delete", null)
        });

        var result = await harness.Processor.ProcessReadyAsync(harness.VaultId, now, transport);

        Assert.Equal(2, result.Completed);
        var passwords = await harness.Repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true);
        var savedCreate = passwords.Single(item => item.Id == created.Id);
        Assert.Equal("remote-created-id", savedCreate.BitwardenCipherId);
        Assert.Equal("2026-07-22T06:30:01Z", savedCreate.BitwardenRevisionDate);
        Assert.False(savedCreate.BitwardenLocalModified);
        var savedDelete = passwords.Single(item => item.Id == deleted.Id);
        Assert.True(savedDelete.IsDeleted);
        Assert.False(savedDelete.BitwardenLocalModified);
    }

    private static async Task<PasswordEntry> SavePasswordAsync(
        Harness harness,
        string cipherId,
        string title)
    {
        var password = new PasswordEntry
        {
            Title = title,
            Password = "local-password",
            BitwardenVaultId = harness.VaultId,
            BitwardenCipherId = cipherId,
            BitwardenRevisionDate = "2026-07-21T00:00:00Z",
            BitwardenCipherType = 1,
            BitwardenLocalModified = true
        };
        await harness.Repository.SavePasswordAsync(password);
        return password;
    }

    private static BitwardenPendingOperation Operation(
        long vaultId,
        PasswordEntry password,
        DateTimeOffset now) => new(
        0,
        vaultId,
        password.BitwardenCipherId!,
        BitwardenMutationOperationType.Update,
        password.BitwardenRevisionDate,
        $"{{\"title\":\"{password.Title}\"}}",
        $"{vaultId}:update:{password.BitwardenCipherId}",
        BitwardenMutationStatus.Pending,
        BitwardenFailureClass.None,
        0,
        now,
        null,
        null,
        now,
        now);

    private static async Task<Harness> CreateHarnessAsync()
    {
        var factory = new SqliteConnectionFactory(TestTempPaths.CreateFilePath(".db"));
        var migrator = new DatabaseMigrator(factory);
        var crypto = new CryptoService();
        var hash = crypto.HashMasterPassword("vault password");
        crypto.InitializeSession("vault password", hash.Salt);
        var repository = new MonicaRepository(factory, migrator, new VaultDataProtector(crypto));
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
            Email = "processor@example.com",
            AccountKey = BitwardenAccountIdentity.CreateAccountKey("processor@example.com", endpoints),
            Endpoints = endpoints,
            Kdf = BitwardenKdfParameters.Pbkdf2()
        }, secrets);
        var operationStore = new BitwardenPendingOperationStore(factory, migrator, crypto);
        var conflictStore = new BitwardenConflictBackupStore(factory, migrator, crypto);
        return new Harness(
            repository,
            operationStore,
            conflictStore,
            new BitwardenMutationProcessor(operationStore, conflictStore, repository),
            account.Id);
    }

    private sealed record Harness(
        IMonicaRepository Repository,
        IBitwardenPendingOperationStore OperationStore,
        IBitwardenConflictBackupStore ConflictStore,
        IBitwardenMutationProcessor Processor,
        long VaultId);

    private sealed class ScriptedTransport(
        Func<BitwardenMutationRequest, BitwardenMutationResponse> handler) : IBitwardenMutationTransport
    {
        public int RequestCount { get; private set; }

        public Task<BitwardenMutationResponse> SendAsync(
            BitwardenMutationRequest request,
            CancellationToken cancellationToken = default)
        {
            RequestCount++;
            return Task.FromResult(handler(request));
        }
    }
}
