using System.Net;
using System.Text;
using Monica.Core.Models;
using WebDav;

namespace Monica.Platform.Services;

public static class WebDavEndpointPolicy
{
    public static void EnsureSecure(Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        if (!string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("WebDAV endpoints must use HTTPS.");
        }

        if (!string.IsNullOrEmpty(baseUri.UserInfo))
        {
            throw new InvalidOperationException("WebDAV credentials must not be embedded in the endpoint URI.");
        }
    }
}

public sealed class WebDavBackupService(IHttpClientFactory httpClientFactory) : IWebDavBackupService
{
    public string NormalizeRemotePath(string rootPath, string relativePath)
    {
        var root = string.IsNullOrWhiteSpace(rootPath) ? "/" : rootPath.Trim();
        root = "/" + root.Trim('/');
        var normalizedRelative = string.IsNullOrWhiteSpace(relativePath)
            ? "/"
            : "/" + relativePath.Trim('/');
        EnsureSafeRemotePath(root, nameof(rootPath));
        EnsureSafeRemotePath(normalizedRelative, nameof(relativePath));
        if (root == "/")
        {
            return normalizedRelative;
        }

        if (normalizedRelative.Equals(root, StringComparison.Ordinal) ||
            normalizedRelative.StartsWith(root + "/", StringComparison.Ordinal))
        {
            return normalizedRelative;
        }

        return normalizedRelative == "/"
            ? root
            : $"{root}/{normalizedRelative.TrimStart('/')}";
    }

    public async Task<IReadOnlyList<RemoteFileEntry>> ListAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(profile);
        var webDavClient = new WebDavClient(client);
        var path = NormalizeRemotePath(profile.RootPath, relativePath);
        var response = await webDavClient.Propfind(ToRequestPath(path)).ConfigureAwait(false);
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
        using var response = await client.PutAsync(ToRequestPath(path), payload, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"WebDAV PUT failed for '{path}' with status {(int)response.StatusCode}.");
        }
    }

    public async Task<string> DownloadTextAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(profile);
        var path = NormalizeRemotePath(profile.RootPath, relativePath);
        using var response = await client.GetAsync(ToRequestPath(path), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"WebDAV GET failed for '{path}' with status {(int)response.StatusCode}.");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UploadBinaryAsync(WebDavProfile profile, string relativePath, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var client = CreateClient(profile);
        var path = NormalizeRemotePath(profile.RootPath, relativePath);
        await EnsureCollectionAsync(client, path, cancellationToken).ConfigureAwait(false);
        using var payload = new StreamContent(content);
        payload.Headers.ContentType = new("application/octet-stream");
        using var response = await client.PutAsync(ToRequestPath(path), payload, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"WebDAV binary PUT failed for '{path}' with status {(int)response.StatusCode}.");
        }
    }

    public async Task DownloadBinaryAsync(WebDavProfile profile, string relativePath, Stream destination, CancellationToken cancellationToken = default)
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
    }

    public async Task DeleteAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(profile);
        var webDavClient = new WebDavClient(client);
        var path = NormalizeRemotePath(profile.RootPath, relativePath);
        var response = await webDavClient.Delete(ToRequestPath(path)).ConfigureAwait(false);
        if (!response.IsSuccessful && response.StatusCode != (int)HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"WebDAV DELETE failed for '{path}' with status {(int)response.StatusCode}.");
        }
    }

    private HttpClient CreateClient(WebDavProfile profile)
    {
        WebDavEndpointPolicy.EnsureSecure(profile.BaseUri);
        var client = httpClientFactory.CreateClient("webdav");
        client.BaseAddress = new Uri(profile.BaseUri.ToString().TrimEnd('/') + "/", UriKind.Absolute);
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

        var segments = normalizedPath[..slashIndex]
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var collectionPath = "";
        foreach (var segment in segments)
        {
            collectionPath += "/" + segment;
            using var request = new HttpRequestMessage(new HttpMethod("MKCOL"), ToRequestPath(collectionPath));
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.MethodNotAllowed)
            {
                continue;
            }

            throw new InvalidOperationException($"WebDAV MKCOL failed for '{collectionPath}' with status {(int)response.StatusCode}.");
        }
    }

    private static string ToRequestPath(string normalizedPath) => normalizedPath.TrimStart('/');

    private static void EnsureSafeRemotePath(string path, string parameterName)
    {
        if (path.Contains('\\'))
        {
            throw new ArgumentException("WebDAV paths must use forward slashes.", parameterName);
        }

        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var decoded = segment;
            for (var pass = 0; pass < 3; pass++)
            {
                var next = Uri.UnescapeDataString(decoded);
                if (string.Equals(next, decoded, StringComparison.Ordinal))
                {
                    break;
                }

                decoded = next;
            }

            if (decoded is "." or ".." ||
                decoded.Contains('/') ||
                decoded.Contains('\\') ||
                decoded.Any(char.IsControl))
            {
                throw new ArgumentException("WebDAV paths cannot contain escaping or control segments.", parameterName);
            }
        }
    }
}
