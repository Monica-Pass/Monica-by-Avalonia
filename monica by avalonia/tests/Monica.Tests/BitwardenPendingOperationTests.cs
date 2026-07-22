using System.Net;
using System.Text;
using Monica.Core.Bitwarden;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Bitwarden;

namespace Monica.Tests;

public sealed class BitwardenPendingOperationTests
{
    [Fact]
    public async Task QueueEncryptsCoalescesClaimsAndStopsOnConflict()
    {
        var harness = await CreateHarnessAsync();
        var now = new DateTimeOffset(2026, 7, 22, 4, 0, 0, TimeSpan.Zero);
        var firstId = await harness.Store.EnqueueAsync(Operation(
            harness.VaultId,
            "cipher-1",
            "{\"password\":\"first-secret\"}",
            now));
        var secondId = await harness.Store.EnqueueAsync(Operation(
            harness.VaultId,
            "cipher-1",
            "{\"password\":\"latest-secret\"}",
            now.AddSeconds(1)));

        Assert.Equal(firstId, secondId);
        var rawPayload = await ReadColumnAsync(harness.Factory, firstId, "encrypted_payload_json");
        Assert.StartsWith("vault:v1:", rawPayload);
        Assert.DoesNotContain("latest-secret", rawPayload, StringComparison.Ordinal);

        var claimed = Assert.Single(await harness.Store.ClaimReadyAsync(harness.VaultId, now.AddSeconds(1)));
        Assert.Equal("{\"password\":\"latest-secret\"}", claimed.PayloadJson);
        Assert.Equal(1, claimed.AttemptCount);
        Assert.Equal(BitwardenMutationStatus.InFlight, claimed.Status);
        Assert.Empty(await harness.Store.ClaimReadyAsync(harness.VaultId, now.AddSeconds(1)));

        var retryStatus = await harness.Store.RecordFailureAsync(
            firstId,
            BitwardenFailureClass.TransientNetwork,
            "private network detail",
            now.AddSeconds(2));
        Assert.Equal(BitwardenMutationStatus.Pending, retryStatus);
        Assert.StartsWith(
            "vault:v1:",
            await ReadColumnAsync(harness.Factory, firstId, "encrypted_last_error"));
        Assert.Empty(await harness.Store.ClaimReadyAsync(harness.VaultId, now.AddSeconds(3)));
        var retried = Assert.Single(await harness.Store.ClaimReadyAsync(harness.VaultId, now.AddSeconds(4)));
        Assert.Equal(2, retried.AttemptCount);

        var conflictStatus = await harness.Store.RecordFailureAsync(
            firstId,
            BitwardenFailureClass.Conflict,
            "revision mismatch",
            now.AddSeconds(5));
        Assert.Equal(BitwardenMutationStatus.Conflict, conflictStatus);
        Assert.Empty(await harness.Store.ClaimReadyAsync(harness.VaultId, now.AddHours(1)));
    }

    [Fact]
    public async Task StaleClaimIsRecoveredAndCompletionClearsPayload()
    {
        var harness = await CreateHarnessAsync();
        var now = new DateTimeOffset(2026, 7, 22, 5, 0, 0, TimeSpan.Zero);
        var operation = Operation(
            harness.VaultId,
            "cipher-stale",
            "{\"name\":\"stale-secret\"}",
            now);
        var operationId = await harness.Store.EnqueueAsync(operation);
        _ = Assert.Single(await harness.Store.ClaimReadyAsync(harness.VaultId, now));

        var recovered = Assert.Single(await harness.Store.ClaimReadyAsync(
            harness.VaultId,
            now + BitwardenPendingOperationStore.ClaimTimeout + TimeSpan.FromSeconds(1)));
        Assert.Equal(2, recovered.AttemptCount);

        await harness.Store.CompleteAsync(operationId);
        var completed = Assert.Single(await harness.Store.GetAsync(harness.VaultId));
        Assert.Equal(BitwardenMutationStatus.Completed, completed.Status);
        Assert.Equal("{}", completed.PayloadJson);
        Assert.Equal("", await ReadColumnAsync(harness.Factory, operationId, "encrypted_payload_json"));
    }

    [Fact]
    public void MutationGuardsAndRetryClassesAreDeterministic()
    {
        var now = DateTimeOffset.UnixEpoch;
        Assert.Equal(
            BitwardenFailureClass.RateLimited,
            BitwardenRetryPolicy.ClassifyHttpStatus(HttpStatusCode.TooManyRequests));
        Assert.Equal(
            BitwardenFailureClass.Conflict,
            BitwardenRetryPolicy.ClassifyHttpStatus(HttpStatusCode.PreconditionFailed));
        Assert.Equal(
            BitwardenFailureClass.TransientNetwork,
            BitwardenRetryPolicy.ClassifyHttpStatus(HttpStatusCode.ServiceUnavailable));
        Assert.Equal(now.AddSeconds(2), BitwardenRetryPolicy.GetNextAttemptAt(
            now,
            1,
            BitwardenFailureClass.TransientNetwork));
        Assert.Equal(now.AddMinutes(3), BitwardenRetryPolicy.GetNextAttemptAt(
            now,
            1,
            BitwardenFailureClass.RateLimited,
            TimeSpan.FromMinutes(3)));

        var invalidUpdate = Operation(1, "cipher", "{}", now) with
        {
            ExpectedRemoteRevision = null
        };
        Assert.Throws<BitwardenProtocolException>(() => BitwardenMutationGuard.ValidateForQueue(invalidUpdate));
    }

    private static BitwardenPendingOperation Operation(
        long vaultId,
        string cipherId,
        string payload,
        DateTimeOffset now) => new(
        0,
        vaultId,
        cipherId,
        BitwardenMutationOperationType.Update,
        "2026-07-21T00:00:00Z",
        payload,
        $"{vaultId}:update:{cipherId}",
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
            Email = "queue@example.com",
            AccountKey = BitwardenAccountIdentity.CreateAccountKey("queue@example.com", endpoints),
            Endpoints = endpoints,
            Kdf = BitwardenKdfParameters.Pbkdf2()
        }, secrets);
        return new Harness(
            factory,
            new BitwardenPendingOperationStore(factory, migrator, crypto),
            account.Id);
    }

    private static async Task<string> ReadColumnAsync(
        ISqliteConnectionFactory factory,
        long operationId,
        string column)
    {
        var allowedColumns = new HashSet<string>(StringComparer.Ordinal)
        {
            "encrypted_payload_json",
            "encrypted_last_error"
        };
        Assert.Contains(column, allowedColumns);
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM bitwarden_pending_operations WHERE id = $id";
        command.Parameters.AddWithValue("$id", operationId);
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? "";
    }

    private sealed record Harness(
        ISqliteConnectionFactory Factory,
        IBitwardenPendingOperationStore Store,
        long VaultId);
}
