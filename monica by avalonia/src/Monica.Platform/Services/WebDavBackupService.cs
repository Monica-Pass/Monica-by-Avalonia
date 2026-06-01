using System.Net;
using System.Text;
using Monica.Core.Models;
using WebDav;

namespace Monica.Platform.Services;

public sealed class WebDavBackupService(IHttpClientFactory httpClientFactory) : IWebDavBackupService
{
    public string NormalizeRemotePath(string rootPath, string relativePath)
    {
        var root = string.IsNullOrWhiteSpace(rootPath) ? "/" : rootPath.Trim();
        root = "/" + root.Trim('/');
        var relative = string.IsNullOrWhiteSpace(relativePath) ? "" : relativePath.Trim('/');
        var combined = string.IsNullOrEmpty(relative) ? root : $"{root}/{relative}";
        return combined.Replace("//", "/", StringComparison.Ordinal);
    }

    public async Task<IReadOnlyList<RemoteFileEntry>> ListAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(profile);
        var webDavClient = new WebDavClient(client);
        var path = NormalizeRemotePath(profile.RootPath, relativePath);
        var response = await webDavClient.Propfind(path).ConfigureAwait(false);
        if (!response.IsSuccessful)
        {
            if (response.StatusCode == (int)HttpStatusCode.NotFound)
            {
                return [];
            }

            throw new InvalidOperationException($"WebDAV PROPFIND failed for '{path}' with status {(int)response.StatusCode}.");
        }

        return response.Resources
            .Select(resource => new RemoteFileEntry(resource.Uri ?? "", resource.IsCollection, resource.ContentLength, resource.LastModifiedDate))
            .ToList();
    }

    public async Task UploadTextAsync(WebDavProfile profile, string relativePath, string content, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(profile);
        var path = NormalizeRemotePath(profile.RootPath, relativePath);
        await EnsureCollectionAsync(client, path, cancellationToken).ConfigureAwait(false);
        using var payload = new StringContent(content, Encoding.UTF8, "application/json");
        var response = await client.PutAsync(path, payload, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"WebDAV PUT failed for '{path}' with status {(int)response.StatusCode}.");
        }
    }

    public async Task<string> DownloadTextAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(profile);
        var path = NormalizeRemotePath(profile.RootPath, relativePath);
        var response = await client.GetAsync(path, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"WebDAV GET failed for '{path}' with status {(int)response.StatusCode}.");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(profile);
        var webDavClient = new WebDavClient(client);
        var path = NormalizeRemotePath(profile.RootPath, relativePath);
        var response = await webDavClient.Delete(path).ConfigureAwait(false);
        if (!response.IsSuccessful && response.StatusCode != (int)HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"WebDAV DELETE failed for '{path}' with status {(int)response.StatusCode}.");
        }
    }

    private HttpClient CreateClient(WebDavProfile profile)
    {
        var client = httpClientFactory.CreateClient("webdav");
        client.BaseAddress = profile.BaseUri;
        if (!string.IsNullOrEmpty(profile.Username))
        {
            var credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{profile.Username}:{profile.Password}"));
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
        }

        return client;
    }

    private async Task EnsureCollectionAsync(HttpClient client, string normalizedPath, CancellationToken cancellationToken)
    {
        var slashIndex = normalizedPath.LastIndexOf('/');
        if (slashIndex <= 0)
        {
            return;
        }

        var collectionPath = normalizedPath[..slashIndex];
        using var request = new HttpRequestMessage(new HttpMethod("MKCOL"), collectionPath);
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode ||
            response.StatusCode == HttpStatusCode.MethodNotAllowed ||
            response.StatusCode == HttpStatusCode.Conflict)
        {
            return;
        }

        throw new InvalidOperationException($"WebDAV MKCOL failed for '{collectionPath}' with status {(int)response.StatusCode}.");
    }
}
