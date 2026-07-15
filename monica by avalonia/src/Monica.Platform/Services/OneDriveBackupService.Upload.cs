using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Monica.Platform.Services;

public sealed partial class OneDriveBackupService
{
    private const int SimpleUploadLimit = 4 * 1024 * 1024;
    private const int UploadChunkSize = 5 * 1024 * 1024;

    public async Task<RemoteFileVersion> UploadBinaryConditionallyAsync(
        string accountId,
        string remotePath,
        Stream content,
        RemoteWriteCondition condition,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(condition);
        if (!content.CanRead || !content.CanSeek)
        {
            throw new ArgumentException("OneDrive uploads require a readable, seekable stream.", nameof(content));
        }

        var startPosition = content.Position;
        var length = content.Length - startPosition;
        if (length < 0)
        {
            throw new ArgumentException("The upload stream position is outside its content length.", nameof(content));
        }

        return length <= SimpleUploadLimit
            ? await UploadSmallFileAsync(accountId, remotePath, content, startPosition, checked((int)length), condition, cancellationToken)
            : await UploadLargeFileAsync(accountId, remotePath, content, startPosition, length, condition, cancellationToken);
    }

    private async Task<RemoteFileVersion> UploadSmallFileAsync(
        string accountId,
        string remotePath,
        Stream content,
        long startPosition,
        int length,
        RemoteWriteCondition condition,
        CancellationToken cancellationToken)
    {
        var payload = new byte[length];
        content.Position = startPosition;
        await content.ReadExactlyAsync(payload, cancellationToken);

        var response = await SendGraphAsync(
            accountId,
            token =>
            {
                var request = CreateGraphRequest(HttpMethod.Put, BuildSimpleUploadUri(remotePath, condition), token);
                ApplyWriteCondition(request, condition);
                request.Content = new ByteArrayContent(payload);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                return request;
            },
            cancellationToken);
        using (response)
        {
            await EnsureGraphSuccessAsync(response, cancellationToken);
            return ToVersion(await ReadDriveItemAsync(response, cancellationToken));
        }
    }

    private async Task<RemoteFileVersion> UploadLargeFileAsync(
        string accountId,
        string remotePath,
        Stream content,
        long startPosition,
        long length,
        RemoteWriteCondition condition,
        CancellationToken cancellationToken)
    {
        var uploadUri = await CreateUploadSessionAsync(accountId, remotePath, condition, cancellationToken);
        var buffer = new byte[UploadChunkSize];
        long offset = 0;
        content.Position = startPosition;

        while (offset < length)
        {
            var count = checked((int)Math.Min(buffer.Length, length - offset));
            await content.ReadExactlyAsync(buffer.AsMemory(0, count), cancellationToken);
            var response = await SendUploadChunkAsync(uploadUri, buffer, count, offset, length, cancellationToken);
            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Accepted)
                {
                    offset = await ReadNextExpectedOffsetAsync(
                        response,
                        fallbackOffset: offset + count,
                        totalLength: length,
                        cancellationToken);
                    content.Position = startPosition + offset;
                    continue;
                }

                await EnsureGraphSuccessAsync(response, cancellationToken);
                return ToVersion(await ReadDriveItemAsync(response, cancellationToken));
            }
        }

        throw new InvalidDataException("OneDrive upload session completed without returning file metadata.");
    }

    private async Task<Uri> CreateUploadSessionAsync(
        string accountId,
        string remotePath,
        RemoteWriteCondition condition,
        CancellationToken cancellationToken)
    {
        var conflictBehavior = condition.RequireMissing ? "fail" : "replace";
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new OneDriveUploadSessionRequestDto(new OneDriveUploadSessionItemDto(conflictBehavior)),
            OneDriveGraphJsonContext.Default.OneDriveUploadSessionRequestDto);
        var response = await SendGraphAsync(
            accountId,
            token =>
            {
                var request = CreateGraphRequest(
                    HttpMethod.Post,
                    new Uri($"{BuildItemBaseUrl(remotePath)}:/createUploadSession", UriKind.Absolute),
                    token);
                ApplyWriteCondition(request, condition);
                request.Content = new ByteArrayContent(payload);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                return request;
            },
            cancellationToken);
        using (response)
        {
            await EnsureGraphSuccessAsync(response, cancellationToken);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var session = await JsonSerializer.DeserializeAsync(
                              stream,
                              OneDriveGraphJsonContext.Default.OneDriveUploadSessionDto,
                              cancellationToken)
                          ?? throw new InvalidDataException("OneDrive returned an empty upload session.");
            if (!Uri.TryCreate(session.UploadUrl, UriKind.Absolute, out var uploadUri) ||
                uploadUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidDataException("OneDrive did not return a secure upload-session URL.");
            }

            return uploadUri;
        }
    }

    private async Task<HttpResponseMessage> SendUploadChunkAsync(
        Uri uploadUri,
        byte[] buffer,
        int count,
        long offset,
        long totalLength,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaximumGraphAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, uploadUri);
            request.Content = new ByteArrayContent(buffer, 0, count);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            request.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, offset + count - 1, totalLength);
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (IsRetryable(response.StatusCode) && attempt + 1 < MaximumGraphAttempts)
            {
                var delay = GetRetryDelay(response, attempt);
                response.Dispose();
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
            {
                response.Dispose();
                throw new RemoteFileConflictException("The OneDrive file changed on another device.");
            }

            return response;
        }

        throw new HttpRequestException("OneDrive upload session exhausted its retry budget.");
    }

    private static Uri BuildSimpleUploadUri(string remotePath, RemoteWriteCondition condition)
    {
        var suffix = condition.RequireMissing ? "?@microsoft.graph.conflictBehavior=fail" : "";
        return new Uri($"{BuildItemBaseUrl(remotePath)}:/content{suffix}", UriKind.Absolute);
    }

    private static void ApplyWriteCondition(HttpRequestMessage request, RemoteWriteCondition condition)
    {
        if (condition.RequireMissing)
        {
            request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Any);
            return;
        }

        var etag = condition.ExpectedVersion?.ETag;
        if (string.IsNullOrWhiteSpace(etag))
        {
            throw new ArgumentException("OneDrive updates require an ETag validator.", nameof(condition));
        }

        request.Headers.IfMatch.Add(EntityTagHeaderValue.Parse(etag));
    }

    private static async Task<long> ReadNextExpectedOffsetAsync(
        HttpResponseMessage response,
        long fallbackOffset,
        long totalLength,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("nextExpectedRanges", out var ranges) ||
            ranges.ValueKind != JsonValueKind.Array ||
            ranges.GetArrayLength() == 0)
        {
            return fallbackOffset;
        }

        var range = ranges[0].GetString();
        var separator = range?.IndexOf('-', StringComparison.Ordinal) ?? -1;
        if (separator <= 0 ||
            !long.TryParse(range.AsSpan(0, separator), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var offset) ||
            offset < 0 || offset >= totalLength)
        {
            throw new InvalidDataException("OneDrive returned an invalid next upload range.");
        }

        return offset;
    }
}
