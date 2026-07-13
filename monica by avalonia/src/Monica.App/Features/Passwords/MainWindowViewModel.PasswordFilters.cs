using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool MatchesPasswordFilters(PasswordEntry item)
    {
        if (SelectedPasswordFolderFilter is { } folderFilter)
        {
            if (!MatchesPasswordFolderFilter(item, folderFilter))
            {
                return false;
            }
        }

        return MatchesPasswordNonFolderFilters(item);
    }

    private bool MatchesPasswordFolderFilter(PasswordEntry item, PasswordFolderFilterChoice folderFilter)
    {
        if (folderFilter.Id == -2)
        {
            return item.IsFavorite;
        }

        if (folderFilter.Id == -1)
        {
            return item.CategoryId is null;
        }

        if (!string.IsNullOrWhiteSpace(folderFilter.PathPrefix))
        {
            return PasswordMatchesFolderPath(item, folderFilter.PathPrefix);
        }

        return folderFilter.Id is not { } folderId || item.CategoryId == folderId;
    }

    private bool MatchesPasswordNonFolderFilters(PasswordEntry item)
    {
        if (QuickFilterFavorite && !item.IsFavorite)
        {
            return false;
        }

        if (QuickFilter2Fa && !item.HasAuthenticator)
        {
            return false;
        }

        if (QuickFilterNotes && string.IsNullOrWhiteSpace(item.Notes))
        {
            return false;
        }
        if (QuickFilterPasskey && string.IsNullOrWhiteSpace(item.PasskeyBindings))
        {
            return false;
        }

        if (QuickFilterBoundNote && item.BoundNoteId is null)
        {
            return false;
        }

        if (QuickFilterUncategorized && item.CategoryId is not null)
        {
            return false;
        }

        if (QuickFilterLocalOnly && !IsLocalOnlyPassword(item))
        {
            return false;
        }

        if (QuickFilterAttachments && !item.HasAttachments)
        {
            return false;
        }

        return MatchesPasswordSearch(item, GetEffectivePasswordSearchQuery());
    }

    private string GetEffectivePasswordSearchQuery() =>
        string.IsNullOrWhiteSpace(PasswordSearchQuery) ? SearchText : PasswordSearchQuery;

    private bool PasswordMatchesFolderPath(PasswordEntry item, string pathPrefix)
    {
        if (item.CategoryId is null)
        {
            return false;
        }

        var category = Categories.FirstOrDefault(category => category.Id == item.CategoryId.Value);
        if (category is null)
        {
            return false;
        }

        var categoryPath = string.Join("/", SplitFolderPath(category.Name));
        return string.Equals(categoryPath, pathPrefix, StringComparison.OrdinalIgnoreCase) ||
               categoryPath.StartsWith($"{pathPrefix}/", StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesPasswordSearch(PasswordEntry item, string query)
    {
        var term = query.Trim();
        if (term.Length == 0)
        {
            return true;
        }

        if (ContainsAny(term,
            item.Title,
            item.Username,
            item.Website,
            item.Notes,
            item.AuthenticatorKey,
            item.AppName,
            item.AppPackageName,
            item.Email,
            item.Phone,
            item.AddressLine,
            item.City,
            item.State,
            item.ZipCode,
            item.Country,
            item.CreditCardHolder,
            item.CreditCardExpiry,
            item.SsoProvider,
            item.PasskeyBindings,
            item.WifiMetadata,
            item.SshKeyData,
            item.KeepassGroupPath ?? "",
            item.MdbxFolderId ?? "",
            item.BitwardenFolderId ?? ""))
        {
            return true;
        }

        if (_passwordCustomFields.TryGetValue(item.Id, out var fields) &&
            fields.Any(field => ContainsAny(term, field.Title, field.Value)))
        {
            return true;
        }

        return _passwordAttachments.TryGetValue(item.Id, out var attachments) &&
            attachments.Any(attachment => ContainsAny(
                term,
                attachment.FileName,
                attachment.ContentType,
                attachment.StoragePath,
                attachment.KeepassBinaryRef ?? ""));
    }

    private static bool IsLocalOnlyPassword(PasswordEntry item)
    {
        return item.BitwardenVaultId is null &&
            item.KeepassDatabaseId is null &&
            item.MdbxDatabaseId is null;
    }

    private static bool ContainsAny(string query, params string[] values) =>
        values.Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase));

    private IEnumerable<PasswordEntry> GetPasswordSiblings(PasswordEntry entry)
    {
        var key = BuildSiblingGroupKey(entry);
        return Passwords
            .Where(item => BuildSiblingGroupKey(item) == key)
            .OrderBy(item => item.Id == 0 ? long.MaxValue : item.Id);
    }

    private IEnumerable<PasswordEntry> GetDeletedPasswordSiblings(PasswordEntry entry)
    {
        var key = BuildSiblingGroupKey(entry);
        return DeletedPasswords
            .Where(item => BuildSiblingGroupKey(item) == key)
            .OrderBy(item => item.Id == 0 ? long.MaxValue : item.Id);
    }

    private IEnumerable<PasswordEntry> GetArchivedPasswordSiblings(PasswordEntry entry)
    {
        var key = BuildSiblingGroupKey(entry);
        return ArchivedPasswords
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
