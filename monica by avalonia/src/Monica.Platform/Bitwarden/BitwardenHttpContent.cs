using System.Text.Json;
using Monica.Core.Bitwarden;

namespace Monica.Platform.Bitwarden;

internal static class BitwardenHttpContent
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 64
    };

    public static async Task<byte[]> ReadLimitedAsync(
        HttpResponseMessage response,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > maximumBytes)
        {
            throw new BitwardenProtocolException("Bitwarden response exceeds the supported size.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maximumBytes)
            {
                throw new BitwardenProtocolException("Bitwarden response exceeds the supported size.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    public static T Deserialize<T>(byte[] payload)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(payload, JsonOptions) ??
                   throw new BitwardenProtocolException("Bitwarden returned an empty JSON document.");
        }
        catch (JsonException exception)
        {
            throw new BitwardenProtocolException("Bitwarden returned malformed JSON.", exception);
        }
    }
}
