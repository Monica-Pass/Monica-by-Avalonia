using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Monica.Core.Models;

namespace Monica.Platform.Services;

public sealed partial class OneDriveBackupService : IOneDriveBackupService
{
    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";
    private const int MaximumGraphAttempts = 3;
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient;
    private readonly IOneDriveAccessTokenProvider _tokenProvider;

    public OneDriveBackupService(HttpClient httpClient, IOneDriveAccessTokenProvider tokenProvider)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
    }

    public PlatformCapability GetCapability() => new(
        "onedrive",
        "OneDrive",
        "Microsoft Graph device-code authentication and account-bound MDBX file synchronization.",
        PlatformFeatureStatus.DesktopEquivalent);

    public Task<OneDriveAccountInfo?> GetCachedAccountAsync(
        string? accountId = null,
        CancellationToken cancellationToken = default) =>
        _tokenProvider.GetCachedAccountAsync(accountId, cancellationToken);

    public Task<OneDriveSignInChallenge> BeginSignInAsync(CancellationToken cancellationToken = default) =>
        _tokenProvider.BeginSignInAsync(cancellationToken);

    public Task SignOutAsync(string? accountId = null, CancellationToken cancellationToken = default) =>
        _tokenProvider.SignOutAsync(accountId, cancellationToken);

    public Task ClearSensitiveCacheAsync(CancellationToken cancellationToken = default) =>
        _tokenProvider.ClearSensitiveCacheAsync(cancellationToken);

    public async Task<RemoteFileVersion?> GetFileVersionAsync(
        string accountId,
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        var response = await SendGraphAsync(
            accountId,
            token => CreateGraphRequest(
                HttpMethod.Get,
                BuildMetadataUri(remotePath, includeDownloadUrl: false),
                token),
            cancellationToken);
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            await EnsureGraphSuccessAsync(response, cancellationToken);
            return ToVersion(await ReadDriveItemAsync(response, cancellationToken));
        }
    }

    public async Task<RemoteFileVersion> DownloadBinaryVersionedAsync(
        string accountId,
        string remotePath,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var metadataResponse = await SendGraphAsync(
            accountId,
            token => CreateGraphRequest(
                HttpMethod.Get,
                BuildMetadataUri(remotePath, includeDownloadUrl: true),
                token),
            cancellationToken);
        OneDriveDriveItemDto item;
        using (metadataResponse)
        {
            await EnsureGraphSuccessAsync(metadataResponse, cancellationToken);
            item = await ReadDriveItemAsync(metadataResponse, cancellationToken);
        }

        if (!Uri.TryCreate(item.DownloadUrl, UriKind.Absolute, out var downloadUri) || downloadUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("OneDrive did not return a secure version-specific download URL.");
        }

        using var downloadRequest = new HttpRequestMessage(HttpMethod.Get, downloadUri);
        using var downloadResponse = await _httpClient.SendAsync(
            downloadRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!downloadResponse.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"OneDrive content download failed with HTTP {(int)downloadResponse.StatusCode}.",
                null,
                downloadResponse.StatusCode);
        }

        await using var source = await downloadResponse.Content.ReadAsStreamAsync(cancellationToken);
        var copied = await CopyToAsync(source, destination, cancellationToken);
        if (item.Size is { } expectedSize && copied != expectedSize)
        {
            throw new InvalidDataException($"OneDrive content length mismatch: expected {expectedSize}, received {copied}.");
        }

        return ToVersion(item);
    }

    private async Task<HttpResponseMessage> SendGraphAsync(
        string accountId,
        Func<string, HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        var forceRefresh = false;
        for (var attempt = 0; attempt < MaximumGraphAttempts; attempt++)
        {
            var token = await _tokenProvider.GetAccessTokenAsync(accountId, forceRefresh, cancellationToken);
            using var request = requestFactory(token);
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized && !forceRefresh)
            {
                response.Dispose();
                forceRefresh = true;
                continue;
            }

            if (IsRetryable(response.StatusCode) && attempt + 1 < MaximumGraphAttempts)
            {
                var delay = GetRetryDelay(response, attempt);
                response.Dispose();
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            return response;
        }

        throw new HttpRequestException("OneDrive request exhausted its retry budget.");
    }

    private static HttpRequestMessage CreateGraphRequest(HttpMethod method, Uri uri, string accessToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static async Task EnsureGraphSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
        {
            throw new RemoteFileConflictException("The OneDrive file changed on another device.");
        }

        var code = await ReadGraphErrorCodeAsync(response, cancellationToken);
        throw new HttpRequestException(
            $"OneDrive request failed with HTTP {(int)response.StatusCode}{(code.Length == 0 ? "" : $" ({code})")}.",
            null,
            response.StatusCode);
    }

    private static async Task<string> ReadGraphErrorCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return document.RootElement.TryGetProperty("error", out var error) &&
                   error.TryGetProperty("code", out var code) &&
                   code.ValueKind == JsonValueKind.String
                ? code.GetString() ?? ""
                : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private static async Task<OneDriveDriveItemDto> ReadDriveItemAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync(
                   stream,
                   OneDriveGraphJsonContext.Default.OneDriveDriveItemDto,
                   cancellationToken)
               ?? throw new InvalidDataException("OneDrive returned empty file metadata.");
    }

    private static RemoteFileVersion ToVersion(OneDriveDriveItemDto item)
    {
        if (string.IsNullOrWhiteSpace(item.ETag))
        {
            throw new InvalidDataException("OneDrive file metadata did not include an ETag.");
        }

        return new RemoteFileVersion(NormalizeEntityTag(item.ETag), item.LastModifiedDateTime, item.Size);
    }

    private static string NormalizeEntityTag(string etag)
    {
        var value = etag.Trim();
        if (value.StartsWith('"') || value.StartsWith("W/\"", StringComparison.Ordinal))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "", StringComparison.Ordinal)}\"";
    }

    private static Uri BuildMetadataUri(string remotePath, bool includeDownloadUrl)
    {
        var select = includeDownloadUrl
            ? "id,name,size,eTag,lastModifiedDateTime,@microsoft.graph.downloadUrl"
            : "id,name,size,eTag,lastModifiedDateTime";
        return new Uri($"{BuildItemBaseUrl(remotePath)}:?$select={select}", UriKind.Absolute);
    }

    private static string BuildItemBaseUrl(string remotePath) =>
        $"{GraphBaseUrl}/me/drive/root:/{EncodeRemotePath(remotePath)}";

    internal static string NormalizeRemotePath(string remotePath)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
        {
            throw new ArgumentException("OneDrive remote path is required.", nameof(remotePath));
        }

        var normalized = remotePath.Trim().Replace('\\', '/').Trim('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".." || segment.Contains(':')))
        {
            throw new ArgumentException("OneDrive remote path contains an unsafe segment.", nameof(remotePath));
        }

        return string.Join('/', segments);
    }

    private static string EncodeRemotePath(string remotePath) =>
        string.Join('/', NormalizeRemotePath(remotePath).Split('/').Select(Uri.EscapeDataString));

    private static bool IsRetryable(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        var requested = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
        return requested < TimeSpan.Zero
            ? TimeSpan.Zero
            : requested > MaximumRetryDelay ? MaximumRetryDelay : requested;
    }

    private static async Task<long> CopyToAsync(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) != 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            total += read;
        }

        return total;
    }
}
