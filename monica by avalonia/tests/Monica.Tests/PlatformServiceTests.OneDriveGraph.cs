using System.Net;
using System.Net.Http.Headers;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed partial class PlatformServiceTests
{
    [Fact]
    public async Task OneDrive_small_upload_uses_create_and_update_preconditions()
    {
        var handler = new RecordingOneDriveHandler(request =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            return GraphItemResponse("\"remote-v2\"");
        });
        var tokens = new RecordingOneDriveTokenProvider();
        var service = new OneDriveBackupService(new HttpClient(handler), tokens);

        await using var createContent = new MemoryStream([1, 2, 3]);
        var created = await service.UploadBinaryConditionallyAsync(
            tokens.Account.AccountId,
            "/Monica/local.mdbx",
            createContent,
            RemoteWriteCondition.CreateOnly);
        await using var updateContent = new MemoryStream([4, 5, 6]);
        var updated = await service.UploadBinaryConditionallyAsync(
            tokens.Account.AccountId,
            "/Monica/local.mdbx",
            updateContent,
            RemoteWriteCondition.Match(new RemoteFileVersion("\"remote-v2\"", null, null)));

        Assert.Equal("\"remote-v2\"", created.ETag);
        Assert.Equal("\"remote-v2\"", updated.ETag);
        Assert.Equal("*", handler.Requests[0].IfNoneMatch);
        Assert.Equal("\"remote-v2\"", handler.Requests[1].IfMatch);
        Assert.All(handler.Requests, request => Assert.Equal("Bearer access-token", request.Authorization));
        Assert.Contains("@microsoft.graph.conflictBehavior=fail", handler.Requests[0].Uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OneDrive_large_upload_uses_conditioned_session_and_unauthenticated_chunks()
    {
        var chunkNumber = 0;
        var handler = new RecordingOneDriveHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return JsonResponse("""{"uploadUrl":"https://upload.example/session-secret"}""");
            }

            chunkNumber++;
            return chunkNumber == 1
                ? JsonResponse("""{"nextExpectedRanges":["5242880-"]}""", HttpStatusCode.Accepted)
                : GraphItemResponse("\"large-v2\"");
        });
        var tokens = new RecordingOneDriveTokenProvider();
        var service = new OneDriveBackupService(new HttpClient(handler), tokens);
        await using var content = new MemoryStream(new byte[(5 * 1024 * 1024) + 7]);

        var version = await service.UploadBinaryConditionallyAsync(
            tokens.Account.AccountId,
            "/Monica/large.mdbx",
            content,
            RemoteWriteCondition.Match(new RemoteFileVersion("\"large-v1\"", null, null)));

        Assert.Equal("\"large-v2\"", version.ETag);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("\"large-v1\"", handler.Requests[0].IfMatch);
        Assert.Equal("Bearer access-token", handler.Requests[0].Authorization);
        Assert.Null(handler.Requests[1].Authorization);
        Assert.Null(handler.Requests[2].Authorization);
        Assert.Equal("bytes 0-5242879/5242887", handler.Requests[1].ContentRange);
        Assert.Equal("bytes 5242880-5242886/5242887", handler.Requests[2].ContentRange);
    }

    [Fact]
    public async Task OneDrive_download_uses_versioned_preauthenticated_url_without_bearer_token()
    {
        var handler = new RecordingOneDriveHandler(request =>
            request.RequestUri!.Host == "graph.microsoft.com"
                ? JsonResponse(
                    """{"id":"item-1","eTag":"\"download-v1\"","lastModifiedDateTime":"2026-07-15T04:00:00Z","size":4,"@microsoft.graph.downloadUrl":"https://download.example/version-1"}""")
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([9, 8, 7, 6]) });
        var tokens = new RecordingOneDriveTokenProvider();
        var service = new OneDriveBackupService(new HttpClient(handler), tokens);
        await using var destination = new MemoryStream();

        var version = await service.DownloadBinaryVersionedAsync(
            tokens.Account.AccountId,
            "/Monica/local.mdbx",
            destination);

        Assert.Equal("\"download-v1\"", version.ETag);
        Assert.Equal([9, 8, 7, 6], destination.ToArray());
        Assert.Equal("Bearer access-token", handler.Requests[0].Authorization);
        Assert.Null(handler.Requests[1].Authorization);
    }

    [Fact]
    public async Task OneDrive_maps_graph_precondition_failures_to_remote_conflicts()
    {
        var handler = new RecordingOneDriveHandler(_ => new HttpResponseMessage(HttpStatusCode.PreconditionFailed));
        var tokens = new RecordingOneDriveTokenProvider();
        var service = new OneDriveBackupService(new HttpClient(handler), tokens);
        await using var content = new MemoryStream([1]);

        await Assert.ThrowsAsync<RemoteFileConflictException>(() =>
            service.UploadBinaryConditionallyAsync(
                tokens.Account.AccountId,
                "/Monica/local.mdbx",
                content,
                RemoteWriteCondition.Match(new RemoteFileVersion("\"stale\"", null, null))));
    }

    [Fact]
    public async Task OneDrive_retries_unauthorized_request_once_with_force_refresh()
    {
        var calls = 0;
        var handler = new RecordingOneDriveHandler(_ =>
            ++calls == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : GraphItemResponse("\"fresh\""));
        var tokens = new RecordingOneDriveTokenProvider();
        var service = new OneDriveBackupService(new HttpClient(handler), tokens);

        var version = await service.GetFileVersionAsync(tokens.Account.AccountId, "/Monica/local.mdbx");

        Assert.Equal("\"fresh\"", version?.ETag);
        Assert.Equal([false, true], tokens.ForceRefreshRequests);
    }

    private static HttpResponseMessage GraphItemResponse(string etag) =>
        JsonResponse($$"""{"id":"item-1","eTag":{{etag}},"lastModifiedDateTime":"2026-07-15T04:00:00Z","size":3}""");

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private sealed class RecordingOneDriveTokenProvider : IOneDriveAccessTokenProvider
    {
        public OneDriveAccountInfo Account { get; } = new("account-1", "Monica User", "user@example.com");
        public List<bool> ForceRefreshRequests { get; } = [];

        public Task<OneDriveAccountInfo?> GetCachedAccountAsync(string? accountId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<OneDriveAccountInfo?>(accountId is null || accountId == Account.AccountId ? Account : null);

        public Task<OneDriveSignInChallenge> BeginSignInAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> GetAccessTokenAsync(string accountId, bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            Assert.Equal(Account.AccountId, accountId);
            ForceRefreshRequests.Add(forceRefresh);
            return Task.FromResult(forceRefresh ? "fresh-token" : "access-token");
        }

        public Task SignOutAsync(string? accountId = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearSensitiveCacheAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingOneDriveHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<RecordedOneDriveRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedOneDriveRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                request.Headers.IfMatch.SingleOrDefault()?.ToString(),
                request.Headers.IfNoneMatch.SingleOrDefault()?.ToString(),
                request.Content?.Headers.ContentRange?.ToString(),
                request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken)));
            return responseFactory(request);
        }
    }

    private sealed record RecordedOneDriveRequest(
        HttpMethod Method,
        Uri Uri,
        string? Authorization,
        string? IfMatch,
        string? IfNoneMatch,
        string? ContentRange,
        byte[] Content);
}
