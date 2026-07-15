using System.Text.Json;
using Monica.Core.Models;

namespace Monica.Core.ImportExport;

public sealed partial class BitwardenJsonImporter
{
    private readonly BitwardenJsonImportLimits _limits;

    public BitwardenJsonImporter(BitwardenJsonImportLimits? limits = null)
    {
        _limits = limits ?? new BitwardenJsonImportLimits();
        ValidateLimits(_limits);
    }

    public BitwardenJsonImportSnapshot Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw InvalidExport();
        }

        if (json.Length > _limits.MaximumJsonCharacters)
        {
            throw ResourceLimitExceeded();
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw InvalidExport();
            }

            if (GetBoolean(root, "encrypted"))
            {
                throw new BitwardenJsonImportException(
                    BitwardenJsonImportError.EncryptedExport,
                    "Encrypted Bitwarden exports are not supported by the local importer.");
            }

            var folders = ParseFolders(root);
            var items = RequireArray(root, "items");
            EnsureCount(items.GetArrayLength(), _limits.MaximumItems);
            return ParseItems(items, folders);
        }
        catch (BitwardenJsonImportException)
        {
            throw;
        }
        catch (Exception)
        {
            throw InvalidExport();
        }
    }

    private IReadOnlyList<BitwardenFolderSnapshot> ParseFolders(JsonElement root)
    {
        if (!root.TryGetProperty("folders", out var foldersElement) ||
            foldersElement.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (foldersElement.ValueKind != JsonValueKind.Array)
        {
            throw InvalidExport();
        }

        EnsureCount(foldersElement.GetArrayLength(), _limits.MaximumFolders);
        var folders = new List<BitwardenFolderSnapshot>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in foldersElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw InvalidExport();
            }

            var id = GetString(element, "id").Trim();
            var name = GetString(element, "name").Trim();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!ids.Add(id))
            {
                throw InvalidExport();
            }

            folders.Add(new BitwardenFolderSnapshot(id, name));
        }

        return folders;
    }

    private BitwardenJsonImportSnapshot ParseItems(
        JsonElement items,
        IReadOnlyList<BitwardenFolderSnapshot> folders)
    {
        var passwords = new List<PasswordEntry>();
        var secureItems = new List<SecureItem>();
        var customFields = new List<PasswordCustomFieldExportGroup>();
        var passwordHistory = new List<PasswordHistoryExportGroup>();
        var sourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unsupported = 0;
        var attachmentMetadataCount = 0;
        long sourceModelId = 0;
        var itemIndex = 0;
        foreach (var item in items.EnumerateArray())
        {
            itemIndex++;
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw InvalidExport();
            }

            var sourceId = GetString(item, "id").Trim();
            sourceId = string.IsNullOrWhiteSpace(sourceId) ? $"missing-{itemIndex}" : sourceId;
            if (!sourceIds.Add(sourceId))
            {
                throw InvalidExport();
            }

            attachmentMetadataCount += CountArray(item, "attachments", _limits.MaximumFieldsPerItem);
            var type = GetInt32(item, "type");
            if (type is 1 or 5)
            {
                sourceModelId++;
                var mapped = MapPassword(item, sourceId, sourceModelId, type);
                passwords.Add(mapped.Entry);
                if (mapped.CustomFields.Count > 0)
                {
                    customFields.Add(new PasswordCustomFieldExportGroup(sourceModelId, mapped.CustomFields));
                }

                if (mapped.History.Count > 0)
                {
                    passwordHistory.Add(new PasswordHistoryExportGroup(sourceModelId, mapped.History));
                }
            }
            else if (type is 2 or 3 or 4)
            {
                sourceModelId++;
                secureItems.Add(MapSecureItem(item, sourceId, sourceModelId, type));
            }
            else
            {
                unsupported++;
            }
        }

        return new BitwardenJsonImportSnapshot(
            folders,
            passwords,
            secureItems,
            customFields,
            passwordHistory,
            unsupported,
            attachmentMetadataCount);
    }

    private static void ValidateLimits(BitwardenJsonImportLimits limits)
    {
        if (limits.MaximumJsonCharacters <= 0 ||
            limits.MaximumFolders <= 0 ||
            limits.MaximumItems <= 0 ||
            limits.MaximumFieldsPerItem <= 0 ||
            limits.MaximumHistoryEntriesPerItem <= 0 ||
            limits.MaximumUrisPerLogin <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limits));
        }
    }

    private static void EnsureCount(int count, int maximum)
    {
        if (count > maximum)
        {
            throw ResourceLimitExceeded();
        }
    }

    private static BitwardenJsonImportException InvalidExport() =>
        new(BitwardenJsonImportError.InvalidExport, "The Bitwarden export is invalid or damaged.");

    private static BitwardenJsonImportException ResourceLimitExceeded() =>
        new(BitwardenJsonImportError.ResourceLimitExceeded, "The Bitwarden export exceeds the safe import limits.");
}
