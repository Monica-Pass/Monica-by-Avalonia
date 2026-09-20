using Monica.App.Features.Vault;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool MatchesPasswordSearch(PasswordEntry item, string query)
    {
        var term = query.Trim();
        if (term.Length == 0)
        {
            return true;
        }

        if (VaultSearchFields.MatchesPassword(item, term))
        {
            return true;
        }

        if (_passwordCustomFieldSearchMatches.Contains(item.Id))
        {
            return true;
        }

        if (_passwordCustomFields.TryGetValue(item.Id, out var fields) &&
            fields.Any(field => ContainsAny(term, field.Title, field.Value)))
        {
            return true;
        }

        return _passwordAttachmentSearchMatches.Contains(item.Id);
    }

    private static bool IsLocalOnlyPassword(PasswordEntry item) => VaultQuickFilters.IsLocalOnly(item);

    private static bool ContainsAny(string query, params string[] values) =>
        values.Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase));

    private IEnumerable<PasswordEntry> GetPasswordSiblings(PasswordEntry entry)
    {
        var key = BuildSiblingGroupKey(entry);
        return Passwords
            .Where(item => BuildSiblingGroupKey(item) == key)
            .OrderBy(item => item.Id == 0 ? long.MaxValue : item.Id);
    }

    private static string BuildSiblingGroupKey(PasswordEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.ReplicaGroupId))
        {
            return $"replica:{entry.ReplicaGroupId.Trim()}";
        }

        if (entry.BitwardenVaultId is not null && !string.IsNullOrWhiteSpace(entry.BitwardenCipherId))
        {
            return $"bw:{entry.BitwardenVaultId}:{entry.BitwardenCipherId.Trim()}";
        }

        if (entry.KeepassDatabaseId is not null && !string.IsNullOrWhiteSpace(entry.KeepassEntryUuid))
        {
            return $"kp:{entry.KeepassDatabaseId}:{entry.KeepassEntryUuid.Trim()}";
        }

        return $"entry:{entry.Id}";
    }

    private static string NormalizeWebsiteForSiblingGroupKey(string value)
    {
        var normalized = value
            .Trim()
            .ToLowerInvariant();

        if (normalized.StartsWith("http://", StringComparison.Ordinal))
        {
            normalized = normalized["http://".Length..];
        }
        else if (normalized.StartsWith("https://", StringComparison.Ordinal))
        {
            normalized = normalized["https://".Length..];
        }

        if (normalized.StartsWith("www.", StringComparison.Ordinal))
        {
            normalized = normalized["www.".Length..];
        }

        return normalized.TrimEnd('/');
    }

    private static IEnumerable<string> SplitAndNormalizeWebsites(string value)
    {
        return value
            .Split([',', ';', '\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeWebsiteForSecurityAnalysis)
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeWebsiteForSecurityAnalysis(string value)
    {
        var normalized = NormalizeWebsiteForSiblingGroupKey(value);
        var slashIndex = normalized.IndexOf('/', StringComparison.Ordinal);
        if (slashIndex >= 0)
        {
            normalized = normalized[..slashIndex];
        }

        var queryIndex = normalized.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex >= 0)
        {
            normalized = normalized[..queryIndex];
        }

        var fragmentIndex = normalized.IndexOf('#', StringComparison.Ordinal);
        if (fragmentIndex >= 0)
        {
            normalized = normalized[..fragmentIndex];
        }

        return normalized.TrimEnd('.');
    }
}
