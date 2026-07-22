namespace Monica.Core.Bitwarden;

public sealed record BitwardenEndpointSet(Uri WebVault, Uri Identity, Uri Api)
{
    public static BitwardenEndpointSet UnitedStates { get; } = new(
        new Uri("https://vault.bitwarden.com/"),
        new Uri("https://identity.bitwarden.com/"),
        new Uri("https://api.bitwarden.com/"));

    public static BitwardenEndpointSet Europe { get; } = new(
        new Uri("https://vault.bitwarden.eu/"),
        new Uri("https://identity.bitwarden.eu/"),
        new Uri("https://api.bitwarden.eu/"));
}

public static class BitwardenEndpointPolicy
{
    public const int MaximumEndpointLength = 2048;

    public static BitwardenEndpointSet CreateSelfHosted(
        string serverUrl,
        string? identityUrl = null,
        string? apiUrl = null)
    {
        var webVault = ValidateBaseAddress(serverUrl, nameof(serverUrl));
        var identity = string.IsNullOrWhiteSpace(identityUrl)
            ? AppendPath(webVault, "identity/")
            : ValidateBaseAddress(identityUrl, nameof(identityUrl));
        var api = string.IsNullOrWhiteSpace(apiUrl)
            ? AppendPath(webVault, "api/")
            : ValidateBaseAddress(apiUrl, nameof(apiUrl));

        return new BitwardenEndpointSet(webVault, identity, api);
    }

    public static BitwardenEndpointSet Validate(BitwardenEndpointSet endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return new BitwardenEndpointSet(
            ValidateBaseAddress(endpoints.WebVault, nameof(endpoints.WebVault)),
            ValidateBaseAddress(endpoints.Identity, nameof(endpoints.Identity)),
            ValidateBaseAddress(endpoints.Api, nameof(endpoints.Api)));
    }

    public static Uri ValidateBaseAddress(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BitwardenProtocolException($"Bitwarden {parameterName} is required.");
        }

        if (value.Length > MaximumEndpointLength)
        {
            throw new BitwardenProtocolException($"Bitwarden {parameterName} is too long.");
        }

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            value.Contains('\\'))
        {
            throw new BitwardenProtocolException($"Bitwarden {parameterName} contains an ambiguous address.");
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new BitwardenProtocolException($"Bitwarden {parameterName} is not an absolute URI.");
        }

        return ValidateBaseAddress(uri, parameterName);
    }

    public static Uri ValidateBaseAddress(Uri uri, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new BitwardenProtocolException($"Bitwarden {parameterName} must use HTTPS.");
        }

        if (string.IsNullOrWhiteSpace(uri.Host) || uri.HostNameType == UriHostNameType.Unknown)
        {
            throw new BitwardenProtocolException($"Bitwarden {parameterName} must include a valid host.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new BitwardenProtocolException($"Bitwarden {parameterName} must not contain embedded credentials.");
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new BitwardenProtocolException($"Bitwarden {parameterName} must not contain a query or fragment.");
        }

        if (uri.OriginalString.Length > MaximumEndpointLength ||
            uri.OriginalString.Contains('\\'))
        {
            throw new BitwardenProtocolException($"Bitwarden {parameterName} contains an ambiguous address.");
        }

        var escapedAddress = uri.OriginalString.ToLowerInvariant();
        if (escapedAddress.Contains("%2e", StringComparison.Ordinal) ||
            escapedAddress.Contains("%2f", StringComparison.Ordinal) ||
            escapedAddress.Contains("%5c", StringComparison.Ordinal))
        {
            throw new BitwardenProtocolException($"Bitwarden {parameterName} contains an encoded path separator.");
        }

        var builder = new UriBuilder(uri)
        {
            Path = EnsureTrailingSlash(uri.AbsolutePath),
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri;
    }

    private static Uri AppendPath(Uri baseAddress, string relativePath) =>
        new(baseAddress, relativePath);

    private static string EnsureTrailingSlash(string path) =>
        path.EndsWith('/') ? path : $"{path}/";
}
