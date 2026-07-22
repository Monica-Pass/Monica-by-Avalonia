using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed class BrowserBridgeServiceTests
{
    [Fact]
    public async Task Authenticated_extension_request_returns_origin_scoped_credentials()
    {
        if (!OperatingSystem.IsWindows()) return;
        var port = ReservePort();
        using var service = new WindowsBrowserBridgeService(new PlatformIntegrationService());
        var requestedOrigins = new List<Uri>();
        Assert.True(service.TryStart(port, (origin, _) =>
        {
            requestedOrigins.Add(origin);
            IReadOnlyList<BrowserBridgeCredential> items =
            [new(7, "Example", "person@example.com", "secret", "https://example.com")];
            return Task.FromResult(items);
        }), service.LastError);

        using var sessionCheck = await SendRequestAsync(port, service.SessionToken, "/v1/session/check");

        using var response = await SendQueryAsync(port, service.SessionToken, "https://accounts.example.com");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, sessionCheck.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("chrome-extension://abcdefghijklmnop", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Single(requestedOrigins);
        Assert.Equal("accounts.example.com", requestedOrigins[0].DnsSafeHost);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("person@example.com", document.RootElement.GetProperty("items")[0].GetProperty("username").GetString());
    }

    [Fact]
    public async Task Bridge_rejects_invalid_token_extension_origin_and_insecure_target()
    {
        if (!OperatingSystem.IsWindows()) return;
        var port = ReservePort();
        using var service = new WindowsBrowserBridgeService(new PlatformIntegrationService());
        Assert.True(service.TryStart(port, (_, _) => Task.FromResult<IReadOnlyList<BrowserBridgeCredential>>([])));

        using var wrongToken = await SendQueryAsync(port, "wrong-token", "https://example.com");
        using var webCaller = await SendQueryAsync(port, service.SessionToken, "https://example.com", "https://attacker.example");
        using var insecureTarget = await SendQueryAsync(port, service.SessionToken, "http://example.com");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongToken.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, webCaller.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, insecureTarget.StatusCode);
    }

    [Fact]
    public async Task Stopping_bridge_revokes_session_and_closes_listener()
    {
        if (!OperatingSystem.IsWindows()) return;
        var port = ReservePort();
        using var service = new WindowsBrowserBridgeService(new PlatformIntegrationService());
        Assert.True(service.TryStart(port, (_, _) => Task.FromResult<IReadOnlyList<BrowserBridgeCredential>>([])));
        Assert.True(service.SessionToken.Length >= 43);

        service.Stop();

        Assert.False(service.IsRunning);
        Assert.Empty(service.SessionToken);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var exception = await Record.ExceptionAsync(() => client.GetAsync($"http://127.0.0.1:{port}/"));
        Assert.True(exception is HttpRequestException or TaskCanceledException, exception?.ToString());
    }

    private static async Task<HttpResponseMessage> SendQueryAsync(
        int port,
        string token,
        string targetOrigin,
        string extensionOrigin = "chrome-extension://abcdefghijklmnop")
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/v1/credentials/query");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Origin", extensionOrigin);
        request.Content = new StringContent(JsonSerializer.Serialize(new { origin = targetOrigin }), Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendRequestAsync(int port, string token, string path)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Origin", "chrome-extension://abcdefghijklmnop");
        return await client.SendAsync(request);
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
