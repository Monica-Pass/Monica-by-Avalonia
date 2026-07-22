using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Monica.Platform.Services;

internal static class BrowserBridgeProtocol
{
    private const int MaxHeaderBytes = 16 * 1024;
    private const int MaxBodyBytes = 8 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task HandleClientAsync(
        TcpClient client,
        string sessionToken,
        Func<Uri, CancellationToken, Task<IReadOnlyList<BrowserBridgeCredential>>> queryCredentials,
        CancellationToken serverCancellation)
    {
        using (client)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(serverCancellation);
            timeout.CancelAfter(RequestTimeout);
            try
            {
                client.NoDelay = true;
                var request = await BrowserBridgeRequest.ReadAsync(client.GetStream(), MaxHeaderBytes, MaxBodyBytes, timeout.Token);
                var response = await CreateResponseAsync(request, sessionToken, queryCredentials, timeout.Token);
                await response.WriteAsync(client.GetStream(), timeout.Token);
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or JsonException or FormatException)
            {
                try
                {
                    await BrowserBridgeResponse.Error(400, "invalid_request").WriteAsync(client.GetStream(), CancellationToken.None);
                }
                catch
                {
                }
            }
            catch
            {
                try
                {
                    await BrowserBridgeResponse.Error(500, "internal_error").WriteAsync(client.GetStream(), CancellationToken.None);
                }
                catch
                {
                }
            }
        }
    }

    private static async Task<BrowserBridgeResponse> CreateResponseAsync(
        BrowserBridgeRequest request,
        string sessionToken,
        Func<Uri, CancellationToken, Task<IReadOnlyList<BrowserBridgeCredential>>> queryCredentials,
        CancellationToken cancellationToken)
    {
        if (!TryGetExtensionOrigin(request.Headers, out var extensionOrigin))
        {
            return BrowserBridgeResponse.Error(403, "extension_origin_required");
        }

        if (request.Method == "OPTIONS")
        {
            return BrowserBridgeResponse.Preflight(extensionOrigin);
        }

        if (request.Method != "POST" ||
            request.Path is not ("/v1/session/check" or "/v1/credentials/query"))
        {
            return BrowserBridgeResponse.Error(404, "not_found", extensionOrigin);
        }

        if (!HasValidBearerToken(request.Headers, sessionToken))
        {
            return BrowserBridgeResponse.Error(401, "unauthorized", extensionOrigin);
        }

        if (request.Path == "/v1/session/check")
        {
            return BrowserBridgeResponse.Json(200, new { ready = true, protocolVersion = 1 }, extensionOrigin);
        }

        if (!request.Headers.TryGetValue("content-type", out var contentType) ||
            !contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return BrowserBridgeResponse.Error(400, "json_content_type_required", extensionOrigin);
        }

        var query = JsonSerializer.Deserialize<BrowserBridgeQuery>(request.Body, JsonOptions);
        if (query is null || !TryNormalizeWebOrigin(query.Origin, out var webOrigin))
        {
            return BrowserBridgeResponse.Error(400, "https_origin_required", extensionOrigin);
        }

        var credentials = await queryCredentials(webOrigin, cancellationToken);
        return BrowserBridgeResponse.Json(200, new BrowserBridgeQueryResponse(credentials), extensionOrigin);
    }

    private static bool HasValidBearerToken(IReadOnlyDictionary<string, string> headers, string expected)
    {
        if (!headers.TryGetValue("authorization", out var authorization) ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var provided = Encoding.UTF8.GetBytes(authorization[7..].Trim());
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return provided.Length == expectedBytes.Length && CryptographicOperations.FixedTimeEquals(provided, expectedBytes);
    }

    private static bool TryGetExtensionOrigin(IReadOnlyDictionary<string, string> headers, out string origin)
    {
        origin = "";
        if (!headers.TryGetValue("origin", out var value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme is not ("chrome-extension" or "moz-extension" or "ms-browser-extension") ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        origin = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }

    private static bool TryNormalizeWebOrigin(string value, out Uri origin)
    {
        origin = null!;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            parsed.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(parsed.DnsSafeHost) ||
            !string.IsNullOrEmpty(parsed.UserInfo))
        {
            return false;
        }

        origin = new UriBuilder(Uri.UriSchemeHttps, parsed.DnsSafeHost, parsed.IsDefaultPort ? -1 : parsed.Port).Uri;
        return true;
    }

    private sealed record BrowserBridgeQuery(string Origin);
    private sealed record BrowserBridgeQueryResponse(IReadOnlyList<BrowserBridgeCredential> Items);
}
