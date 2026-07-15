using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Monica.Core.Services;

public interface IPwnedPasswordService
{
    Task<IReadOnlyList<int>> CheckPasswordsAsync(
        IReadOnlyList<string> plaintextPasswords,
        CancellationToken cancellationToken = default);
}

public sealed class PwnedPasswordService : IPwnedPasswordService
{
    private const string RangeEndpoint = "https://api.pwnedpasswords.com/range/";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, CachedRangeResult> _rangeCache = new(StringComparer.OrdinalIgnoreCase);

    public PwnedPasswordService()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
    {
    }

    public PwnedPasswordService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<int>> CheckPasswordsAsync(
        IReadOnlyList<string> plaintextPasswords,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plaintextPasswords);

        var prepared = new List<PreparedPassword>(plaintextPasswords.Count);
        for (var index = 0; index < plaintextPasswords.Count; index++)
        {
            var password = plaintextPasswords[index];
            if (string.IsNullOrWhiteSpace(password))
            {
                continue;
            }

            var hash = Sha1Hex(password);
            prepared.Add(new PreparedPassword(index, hash[..5], hash[5..]));
        }

        if (prepared.Count == 0)
        {
            return new int[plaintextPasswords.Count];
        }

        var rangeResults = new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var prefix in prepared.Select(item => item.Prefix).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            rangeResults[prefix] = await GetRangeResultAsync(prefix, cancellationToken);
        }

        var exposureCounts = new int[plaintextPasswords.Count];
        foreach (var item in prepared)
        {
            if (rangeResults.TryGetValue(item.Prefix, out var suffixCounts) &&
                suffixCounts.TryGetValue(item.Suffix, out var count))
            {
                exposureCounts[item.Index] = count;
            }
        }

        return exposureCounts;
    }

    private async Task<IReadOnlyDictionary<string, int>> GetRangeResultAsync(string prefix, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (_rangeCache.TryGetValue(prefix, out var cached) && now - cached.FetchedAt <= CacheTtl)
        {
            return cached.SuffixCounts;
        }

        var fetched = await FetchRangeResultAsync(prefix, cancellationToken);
        _rangeCache[prefix] = new CachedRangeResult(now, fetched);
        return fetched;
    }

    private async Task<IReadOnlyDictionary<string, int>> FetchRangeResultAsync(string prefix, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, RangeEndpoint + prefix);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Monica-Password-Manager", "1.0"));
        request.Headers.Add("Add-Padding", "true");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseRangeResponse(content);
    }

    internal static IReadOnlyDictionary<string, int> ParseRangeResponse(string content)
    {
        var results = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in content.Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0 || separator == line.Length - 1)
            {
                continue;
            }

            var suffix = line[..separator].Trim().ToUpperInvariant();
            if (suffix.Length != 35 || !int.TryParse(line[(separator + 1)..].Trim(), out var count))
            {
                continue;
            }

            results[suffix] = Math.Max(0, count);
        }

        return results;
    }

    internal static string Sha1Hex(string value)
    {
        var plainBytes = Encoding.UTF8.GetBytes(value);
        byte[]? hashBytes = null;
        try
        {
            hashBytes = SHA1.HashData(plainBytes);
            return Convert.ToHexString(hashBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
            if (hashBytes is not null)
            {
                CryptographicOperations.ZeroMemory(hashBytes);
            }
        }
    }

    private sealed record PreparedPassword(int Index, string Prefix, string Suffix);
    private sealed record CachedRangeResult(DateTimeOffset FetchedAt, IReadOnlyDictionary<string, int> SuffixCounts);
}
