using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Monica.Core.Bitwarden;
using Monica.Core.Models;
using Monica.Platform.Bitwarden;

namespace Monica.Tests;

public sealed class BitwardenSyncTransportTests
{
    [Fact]
    public async Task DownloadAsync_DecryptsSupportedCipherTypesAndItemKeys()
    {
        var vaultEncryption = Enumerable.Repeat((byte)11, 32).ToArray();
        var vaultMac = Enumerable.Repeat((byte)22, 32).ToArray();
        using var vaultKey = new BitwardenSymmetricKey(vaultEncryption, vaultMac);
        var itemEncryption = Enumerable.Repeat((byte)33, 32).ToArray();
        var itemMac = Enumerable.Repeat((byte)44, 32).ToArray();
        using var itemKey = new BitwardenSymmetricKey(itemEncryption, itemMac);
        var protectedItemKey = EncryptCombinedKey(itemEncryption, itemMac, vaultKey);
        var revision = "2026-07-22T10:00:00.0000000Z";
        var payload = new
        {
            Profile = new { Id = "user-id", Name = "Monica User", Email = "test@example.com" },
            Folders = new[]
            {
                new { Id = "folder-1", Name = Enc("Work/Production", vaultKey), RevisionDate = revision }
            },
            Ciphers = new object[]
            {
                new
                {
                    Id = "login-1", FolderId = "folder-1", Type = 1,
                    Name = Enc("Example", itemKey), Notes = Enc("Login notes", itemKey), Key = protectedItemKey,
                    RevisionDate = revision, CreationDate = revision, Favorite = true,
                    Login = new
                    {
                        Username = Enc("alice", itemKey), Password = Enc("secret", itemKey),
                        Totp = Enc("JBSWY3DPEHPK3PXP", itemKey),
                        Uris = new[] { new { Uri = Enc("https://example.com", itemKey), Match = 0 } }
                    },
                    Fields = new[] { new { Name = Enc("Environment", itemKey), Value = Enc("Production", itemKey), Type = 0 } },
                    PasswordHistory = new[] { new { Password = Enc("old-secret", itemKey), LastUsedDate = revision } }
                },
                new
                {
                    Id = "note-1", Type = 2, Name = Enc("Deploy note", vaultKey), Notes = Enc("note body", vaultKey),
                    RevisionDate = revision, CreationDate = revision, SecureNote = new { Type = 0 }
                },
                new
                {
                    Id = "card-1", Type = 3, Name = Enc("Company card", vaultKey), Notes = Enc("card notes", vaultKey),
                    RevisionDate = revision, CreationDate = revision,
                    Card = new
                    {
                        CardholderName = Enc("Alice", vaultKey), Brand = Enc("Visa", vaultKey),
                        Number = Enc("4111111111111111", vaultKey), ExpMonth = Enc("12", vaultKey),
                        ExpYear = Enc("2030", vaultKey), Code = Enc("123", vaultKey)
                    }
                },
                new
                {
                    Id = "identity-1", Type = 4, Name = Enc("Passport", vaultKey), Notes = Enc("identity notes", vaultKey),
                    RevisionDate = revision, CreationDate = revision,
                    Identity = new
                    {
                        FirstName = Enc("Alice", vaultKey), LastName = Enc("Example", vaultKey),
                        PassportNumber = Enc("P123", vaultKey), Country = Enc("CN", vaultKey)
                    }
                },
                new
                {
                    Id = "ssh-1", Type = 5, Name = Enc("Server key", vaultKey), Notes = Enc("ssh notes", vaultKey),
                    RevisionDate = revision, CreationDate = revision,
                    SshKey = new
                    {
                        PrivateKey = Enc("PRIVATE", vaultKey), PublicKey = Enc("PUBLIC", vaultKey),
                        KeyFingerprint = Enc("SHA256:test", vaultKey)
                    }
                },
                new
                {
                    Id = "deleted-1", Type = 1, RevisionDate = revision, DeletedDate = revision
                }
            }
        };
        var handler = new CaptureHandler(_ => Json(HttpStatusCode.OK, payload));
        var account = CreateAccount();
        using var secrets = CreateSecrets(vaultEncryption, vaultMac);
        var transport = new BitwardenSyncTransport(new FakeFactory(handler));

        var result = await transport.DownloadAsync(account, secrets);

        Assert.True(result.Snapshot.IsComplete);
        Assert.Equal(6, result.Snapshot.Ciphers.Count);
        Assert.Equal(5, result.DecodedCiphers.Count);
        Assert.Equal("Work/Production", Assert.Single(result.Snapshot.Folders).Name);
        var login = Assert.Single(result.DecodedCiphers, item => item.Metadata.CipherId == "login-1");
        Assert.Equal("alice", login.Password!.Username);
        Assert.Equal("secret", login.Password.Password);
        Assert.Equal("https://example.com", login.Password.Website);
        Assert.Equal("Production", Assert.Single(login.CustomFields).Value);
        Assert.Equal("old-secret", Assert.Single(login.PasswordHistory).Password);
        var card = Assert.Single(result.DecodedCiphers, item => item.Metadata.CipherId == "card-1");
        Assert.Equal("4111111111111111", WalletItemDataCodec.DecodeBankCard(card.SecureItem!).CardNumber);
        var ssh = Assert.Single(result.DecodedCiphers, item => item.Metadata.CipherId == "ssh-1");
        Assert.Equal(PasswordLoginType.SshKey, ssh.Password!.LoginType);
        Assert.Contains("PRIVATE", ssh.Password.SshKeyData, StringComparison.Ordinal);
        Assert.Equal("Bearer access-token", Assert.Single(handler.Requests).Authorization);
    }

    [Fact]
    public async Task DownloadAsync_RejectsOversizedCipherCollections()
    {
        var ciphers = Enumerable.Range(0, 100_001).Select(index => new
        {
            Id = $"id-{index}",
            Type = 1,
            RevisionDate = "2026-07-22T10:00:00Z",
            DeletedDate = "2026-07-22T10:00:00Z"
        });
        var handler = new CaptureHandler(_ => Json(HttpStatusCode.OK, new
        {
            Profile = new { },
            Folders = Array.Empty<object>(),
            Ciphers = ciphers
        }));
        var key = new byte[32];
        using var secrets = CreateSecrets(key, key);

        await Assert.ThrowsAsync<BitwardenProtocolException>(() =>
            new BitwardenSyncTransport(new FakeFactory(handler)).DownloadAsync(CreateAccount(), secrets));
    }

    private static BitwardenAccountSecrets CreateSecrets(byte[] encryptionKey, byte[] macKey) => new(
        Encoding.UTF8.GetBytes("access-token"),
        Encoding.UTF8.GetBytes("refresh-token"),
        new byte[32],
        encryptionKey,
        macKey);

    private static BitwardenAccount CreateAccount()
    {
        var endpoints = new BitwardenEndpointSet(
            new Uri("https://vault.example.test/"),
            new Uri("https://identity.example.test/"),
            new Uri("https://api.example.test/"));
        return new BitwardenAccount
        {
            Id = 7,
            Email = "test@example.com",
            AccountKey = BitwardenAccountIdentity.CreateAccountKey("test@example.com", endpoints),
            Endpoints = endpoints,
            Kdf = new BitwardenKdfParameters(BitwardenKdfAlgorithm.Pbkdf2Sha256, 100_000),
            IsConnected = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private static string Enc(string value, BitwardenSymmetricKey key) =>
        BitwardenCipherStringCrypto.EncryptString(value, key);

    private static string EncryptCombinedKey(byte[] encryptionKey, byte[] macKey, BitwardenSymmetricKey wrappingKey)
    {
        var combined = encryptionKey.Concat(macKey).ToArray();
        try
        {
            return BitwardenCipherStringCrypto.Encrypt(combined, wrappingKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(combined);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object payload) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
    };

    private sealed class FakeFactory(CaptureHandler handler) : IBitwardenHttpClientFactory
    {
        public HttpClient Create(BitwardenTlsOptions tls, string? clientCertificatePassword = null) =>
            new(handler, disposeHandler: false);
    }

    private sealed class CaptureHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.RequestUri!,
                request.Headers.Authorization?.ToString()));
            return Task.FromResult(responder(request));
        }
    }

    private sealed record CapturedRequest(Uri Uri, string? Authorization);
}
