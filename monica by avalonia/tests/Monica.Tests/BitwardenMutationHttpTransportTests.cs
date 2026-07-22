using System.Net;
using System.Text;
using System.Text.Json;
using Monica.Core.Bitwarden;
using Monica.Platform.Bitwarden;

namespace Monica.Tests;

public sealed class BitwardenMutationHttpTransportTests
{
    [Fact]
    public async Task Create_SendsEncryptedPayloadAndReturnsServerIdentity()
    {
        var handler = new CaptureHandler(_ => Json(HttpStatusCode.OK, new
        {
            Id = "server-id",
            RevisionDate = "2026-07-22T11:00:00Z"
        }));
        using var transport = CreateTransport(handler);

        var response = await transport.SendAsync(Request(
            BitwardenMutationOperationType.Create,
            "local-id",
            null));

        Assert.True(response.Succeeded);
        Assert.Equal("server-id", response.RemoteCipherId);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("idempotency-key", request.IdempotencyKey);
        Assert.Equal("{\"type\":1,\"name\":\"encrypted\"}", request.Body);
    }

    [Fact]
    public async Task Update_PreflightsRevisionBeforeWriting()
    {
        var calls = 0;
        var handler = new CaptureHandler(request => ++calls == 1
            ? Json(HttpStatusCode.OK, new { Id = "cipher-id", RevisionDate = "rev-1" })
            : Json(HttpStatusCode.OK, new { Id = "cipher-id", RevisionDate = "rev-2" }));
        using var transport = CreateTransport(handler);

        var response = await transport.SendAsync(Request(
            BitwardenMutationOperationType.Update,
            "cipher-id",
            "rev-1"));

        Assert.True(response.Succeeded);
        Assert.Equal("rev-2", response.RemoteRevision);
        Assert.Collection(
            handler.Requests,
            request => Assert.Equal(HttpMethod.Get, request.Method),
            request => Assert.Equal(HttpMethod.Put, request.Method));
    }

    [Fact]
    public async Task Update_StopsWhenRemoteRevisionChanged()
    {
        var handler = new CaptureHandler(_ => Json(HttpStatusCode.OK, new
        {
            Id = "cipher-id",
            RevisionDate = "remote-rev"
        }));
        using var transport = CreateTransport(handler);

        var response = await transport.SendAsync(Request(
            BitwardenMutationOperationType.Update,
            "cipher-id",
            "expected-rev"));

        Assert.False(response.Succeeded);
        Assert.Equal((int)HttpStatusCode.Conflict, response.HttpStatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Delete_PreflightsThenSendsDelete()
    {
        var handler = new CaptureHandler(request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, new { Id = "cipher-id", RevisionDate = "rev-1" })
            : new HttpResponseMessage(HttpStatusCode.NoContent));
        using var transport = CreateTransport(handler);

        var response = await transport.SendAsync(Request(
            BitwardenMutationOperationType.Delete,
            "cipher-id",
            "rev-1"));

        Assert.True(response.Succeeded);
        Assert.Equal(HttpMethod.Delete, handler.Requests[1].Method);
        Assert.Null(handler.Requests[1].Body);
    }

    [Fact]
    public async Task RateLimitResponse_ExposesRetryAfterWithoutErrorBodySecrets()
    {
        var handler = new CaptureHandler(_ =>
        {
            var response = Json(HttpStatusCode.TooManyRequests, new { secret = "server detail" });
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        });
        using var transport = CreateTransport(handler);

        var response = await transport.SendAsync(Request(
            BitwardenMutationOperationType.Create,
            "local-id",
            null));

        Assert.False(response.Succeeded);
        Assert.Equal(TimeSpan.FromSeconds(30), response.RetryAfter);
        Assert.DoesNotContain("server detail", response.ErrorMessage, StringComparison.Ordinal);
    }

    private static IBitwardenOwnedMutationTransport CreateTransport(CaptureHandler handler)
    {
        using var secrets = new BitwardenAccountSecrets(
            Encoding.UTF8.GetBytes("access-token"),
            Encoding.UTF8.GetBytes("refresh-token"),
            new byte[32],
            new byte[32],
            new byte[32]);
        return new BitwardenMutationTransportFactory(new FakeFactory(handler))
            .Create(CreateAccount(), secrets);
    }

    private static BitwardenMutationRequest Request(
        BitwardenMutationOperationType operation,
        string cipherId,
        string? revision) => new(
        1,
        7,
        cipherId,
        operation,
        revision,
        "{\"type\":1,\"name\":\"encrypted\"}",
        "idempotency-key");

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

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!,
                body,
                request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : null));
            return responder(request);
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        string? Body,
        string? IdempotencyKey);
}
