using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.App.Services;

internal static class BrowserCredentialMatcher
{
    private static readonly char[] WebsiteSeparators = [',', ';', '\r', '\n'];

    public static IReadOnlyList<BrowserBridgeCredential> Match(IEnumerable<PasswordEntry> entries, Uri origin)
    {
        var host = origin.DnsSafeHost;
        return entries
            .Where(entry => !entry.IsDeleted && !entry.IsArchived)
            .Where(entry => entry.LoginType != PasswordLoginType.ApiKey)
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Username) || !string.IsNullOrWhiteSpace(entry.Password))
            .Where(entry => EntryMatchesHost(entry.Website, host))
            .Select(entry => new BrowserBridgeCredential(
                entry.Id,
                entry.Title,
                entry.Username,
                entry.Password,
                entry.Website))
            .ToArray();
    }

    internal static bool EntryMatchesHost(string websites, string requestedHost)
    {
        if (string.IsNullOrWhiteSpace(websites) || string.IsNullOrWhiteSpace(requestedHost))
        {
            return false;
        }

        return GetWebHosts(websites)
            .Any(storedHost => string.Equals(requestedHost, storedHost, StringComparison.OrdinalIgnoreCase) ||
                requestedHost.EndsWith('.' + storedHost, StringComparison.OrdinalIgnoreCase));
    }

    internal static IReadOnlyList<string> GetWebHosts(string websites) =>
        string.IsNullOrWhiteSpace(websites)
            ? []
            : websites.Split(WebsiteSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(TryGetWebHost)
                .Where(host => host is not null)
                .Select(host => host!)
                .ToArray();

    private static string? TryGetWebHost(string value)
    {
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "https://" + value;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               uri.Scheme is "https" or "http" &&
               !string.IsNullOrWhiteSpace(uri.DnsSafeHost)
            ? uri.DnsSafeHost
            : null;
    }
}
