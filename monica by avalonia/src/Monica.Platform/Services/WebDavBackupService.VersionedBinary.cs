using System.Net;
using System.Net.Http.Headers;
using Monica.Core.Models;

namespace Monica.Platform.Services;

public sealed partial class WebDavBackupService
{
    public async Task UploadBinaryAsync(
        WebDavProfile profile,
        string relativePath,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        _ = await UploadBinaryConditionallyAsync(
            profile,
            relativePath,
            content,
            RemoteWriteCondition.CreateOnly,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<RemoteFileVersion> UploadBinaryConditionallyAsync(
        WebDavProfile profile,
        string relativePath,
        Stream content,
        RemoteWriteCondition condition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(condition);
        using var client = CreateClient(profile);
        var path = NormalizeRemotePath(profile.RootPath, relativePath);
        await EnsureCollectionAsync(client, path, cancellationToken).ConfigureAwait(false);
        using var payload = new StreamContent(content);
        payload.Headers.ContentType = new("application/octet-stream");
        using var request = new HttpRequestMessage(HttpMethod.Put, ToRequestPath(path))
        {
            Content = payload
        };
        ApplyWriteCondition(request, condition);
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw new RemoteFileConflictException($"WebDAV remote file '{path}' changed before upload.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"WebDAV binary PUT failed for '{path}' with status {(int)response.StatusCode}.");
        }

        var version = ReadVersion(response);
        return version.HasValidator
            ? version
            : await GetFileVersionWithClientAsync(client, path, cancellationToken).ConfigureAwait(false) ?? version;
    }

    public async Task DownloadBinaryAsync(
        WebDavProfile profile,
        string relativePath,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        _ = await DownloadBinaryVersionedAsync(profile, relativePath, destination, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RemoteFileVersion> DownloadBinaryVersionedAsync(
        WebDavProfile profile,
        string relativePath,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        using var client = CreateClient(profile);
        var path = NormalizeRemotePath(profile.RootPath, relativePath);
        using var response = await client.GetAsync(
            ToRequestPath(path),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"WebDAV binary GET failed for '{path}' with status {(int)response.StatusCode}.");
        }

        await response.Content.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        var version = ReadVersion(response);
        return version.HasValidator
            ? version
            : await GetFileVersionWithClientAsync(client, path, cancellationToken).ConfigureAwait(false) ?? version;
    }

    public async Task<RemoteFileVersion?> GetFileVersionAsync(
        WebDavProfile profile,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(profile);
        var path = NormalizeRemotePath(profile.RootPath, relativePath);
        return await GetFileVersionWithClientAsync(client, path, cancellationToken).ConfigureAwait(false);
    }

    private static void ApplyWriteCondition(HttpRequestMessage request, RemoteWriteCondition condition)
    {
        if (condition.RequireMissing)
        {
            request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Any);
            return;
        }

        var expected = condition.ExpectedVersion
            ?? throw new ArgumentException("An expected remote version is required for update writes.", nameof(condition));
        if (!string.IsNullOrWhiteSpace(expected.ETag))
        {
            if (EntityTagHeaderValue.TryParse(expected.ETag, out var entityTag) && !entityTag.IsWeak)
            {
                request.Headers.IfMatch.Add(entityTag);
                return;
            }
        }

        if (expected.LastModified is not null)
        {
            request.Headers.IfUnmodifiedSince = expected.LastModified;
            return;
        }

        throw new RemoteFileConflictException(
            "The WebDAV server did not provide a strong ETag or Last-Modified validator for a safe update.");
    }

    private static RemoteFileVersion ReadVersion(HttpResponseMessage response) => new(
        response.Headers.ETag?.ToString(),
        response.Content.Headers.LastModified,
        response.Content.Headers.ContentLength);

    private static async Task<RemoteFileVersion?> GetFileVersionWithClientAsync(
        HttpClient client,
        string normalizedPath,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ToRequestPath(normalizedPath));
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"WebDAV version probe failed for '{normalizedPath}' with status {(int)response.StatusCode}.");
        }

        return ReadVersion(response);
    }
}
