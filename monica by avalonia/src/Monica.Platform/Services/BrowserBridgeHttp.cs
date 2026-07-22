using System.Text;
using System.Text.Json;

namespace Monica.Platform.Services;

internal sealed record BrowserBridgeRequest(
    string Method,
    string Path,
    IReadOnlyDictionary<string, string> Headers,
    byte[] Body)
{
    public static async Task<BrowserBridgeRequest> ReadAsync(
        Stream stream,
        int maxHeaderBytes,
        int maxBodyBytes,
        CancellationToken cancellationToken)
    {
        var headerBuffer = new List<byte>(1024);
        var ending = new byte[] { 13, 10, 13, 10 };
        var singleByteBuffer = new byte[1];
        while (headerBuffer.Count < maxHeaderBytes)
        {
            var value = await ReadByteAsync(stream, singleByteBuffer, cancellationToken);
            if (value < 0)
            {
                throw new IOException("Unexpected end of request headers.");
            }

            headerBuffer.Add((byte)value);
            if (headerBuffer.Count >= ending.Length && headerBuffer.TakeLast(ending.Length).SequenceEqual(ending))
            {
                break;
            }
        }

        if (headerBuffer.Count >= maxHeaderBytes)
        {
            throw new FormatException("Request headers are too large.");
        }

        var headerText = Encoding.ASCII.GetString([.. headerBuffer]);
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        var requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length != 3 || requestLine[2] is not ("HTTP/1.1" or "HTTP/1.0"))
        {
            throw new FormatException("Invalid HTTP request line.");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1).Where(line => line.Length > 0))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                throw new FormatException("Invalid HTTP header.");
            }

            var name = line[..separator].Trim().ToLowerInvariant();
            if (!headers.TryAdd(name, line[(separator + 1)..].Trim()))
            {
                throw new FormatException("Duplicate HTTP header.");
            }
        }

        var contentLength = 0;
        if (headers.TryGetValue("content-length", out var lengthText) &&
            (!int.TryParse(lengthText, out contentLength) || contentLength < 0 || contentLength > maxBodyBytes))
        {
            throw new FormatException("Invalid request body length.");
        }

        if (headers.ContainsKey("transfer-encoding"))
        {
            throw new FormatException("Transfer encoding is not supported.");
        }

        var body = new byte[contentLength];
        await stream.ReadExactlyAsync(body, cancellationToken);
        return new BrowserBridgeRequest(requestLine[0].ToUpperInvariant(), requestLine[1], headers, body);
    }

    private static async Task<int> ReadByteAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        return await stream.ReadAsync(buffer, cancellationToken) == 0 ? -1 : buffer[0];
    }
}

internal sealed record BrowserBridgeResponse(int StatusCode, byte[] Body, string ContentType, string ExtensionOrigin)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static BrowserBridgeResponse Json(int statusCode, object value, string extensionOrigin = "") =>
        new(statusCode, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions), "application/json; charset=utf-8", extensionOrigin);

    public static BrowserBridgeResponse Error(int statusCode, string code, string extensionOrigin = "") =>
        Json(statusCode, new { error = code }, extensionOrigin);

    public static BrowserBridgeResponse Preflight(string extensionOrigin) =>
        new(204, [], "application/json; charset=utf-8", extensionOrigin);

    public async Task WriteAsync(Stream stream, CancellationToken cancellationToken)
    {
        var reason = StatusCode switch
        {
            200 => "OK",
            204 => "No Content",
            400 => "Bad Request",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not Found",
            500 => "Internal Server Error",
            _ => "Error"
        };
        var headers = new StringBuilder()
            .Append("HTTP/1.1 ").Append(StatusCode).Append(' ').Append(reason).Append("\r\n")
            .Append("Connection: close\r\n")
            .Append("Cache-Control: no-store\r\n")
            .Append("X-Content-Type-Options: nosniff\r\n")
            .Append("Content-Security-Policy: default-src 'none'\r\n")
            .Append("Content-Type: ").Append(ContentType).Append("\r\n")
            .Append("Content-Length: ").Append(Body.Length).Append("\r\n");
        if (!string.IsNullOrEmpty(ExtensionOrigin))
        {
            headers.Append("Access-Control-Allow-Origin: ").Append(ExtensionOrigin).Append("\r\n")
                .Append("Access-Control-Allow-Methods: POST, OPTIONS\r\n")
                .Append("Access-Control-Allow-Headers: Authorization, Content-Type\r\n")
                .Append("Vary: Origin\r\n");
        }
        headers.Append("\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers.ToString()), cancellationToken);
        await stream.WriteAsync(Body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
