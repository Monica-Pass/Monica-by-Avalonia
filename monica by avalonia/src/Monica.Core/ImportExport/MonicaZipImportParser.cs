using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Monica.Core.Models;

namespace Monica.Core.ImportExport;

/// <summary>
/// Reads the portable ZIP produced by Monica Android (database_export.json plus
/// one JSON file per item). The parser deliberately returns the same package used
/// by the JSON import workflow, so the normal import validation and ID remapping
/// still apply on desktop.
/// </summary>
public static partial class MonicaZipImportParser
{
    private const long MaximumArchiveBytes = 256L * 1024 * 1024;
    private const long MaximumEntryBytes = 64L * 1024 * 1024;
    private const int MaximumEntries = 100_000;

    public static MonicaExportPackage Import(ReadOnlyMemory<byte> archive)
    {
        if (archive.Length == 0)
            throw InvalidFormat();
        if (archive.Length > MaximumArchiveBytes)
            throw new MonicaJsonImportException(MonicaJsonImportError.ResourceLimitExceeded,
                "The Monica ZIP import exceeds the safe size limit.");

        try
        {
            using var stream = new MemoryStream(archive.ToArray(), writable: false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            if (zip.Entries.Count == 0 || zip.Entries.Count > MaximumEntries)
                throw InvalidFormat();

            var passwords = new List<PasswordEntry>();
            var secureItems = new List<SecureItem>();
            var customFields = new List<PasswordCustomFieldExportGroup>();
            var categoriesByName = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
            var history = new List<PasswordHistoryExportGroup>();
            var attachments = new List<PortableAttachment>();
            long expandedBytes = 0;

            foreach (var entry in zip.Entries)
            {
                var name = NormalizeEntryName(entry.FullName);
                if (name.Length == 0 || entry.FullName.EndsWith('/'))
                    continue;
                if (entry.Length < 0 || entry.Length > MaximumEntryBytes ||
                    (expandedBytes += entry.Length) > MaximumArchiveBytes)
                    throw new MonicaJsonImportException(MonicaJsonImportError.ResourceLimitExceeded,
                        "The Monica ZIP import exceeds the safe size limit.");

                // This is intentionally bounded even when the central directory lies.
                var bytes = ReadEntry(entry);
                if (name.Equals("database_export.json", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("monica.json", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("export.json", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (TryGetItemKind(name, out var kind))
                {
                    using var document = ParseJson(bytes);
                    var root = document.RootElement;
                    switch (kind)
                    {
                        case ItemKind.Password:
                            var password = ParsePassword(root, categoriesByName);
                            passwords.Add(password.Entry);
                            if (password.CustomFields.Count > 0)
                                customFields.Add(new PasswordCustomFieldExportGroup(password.Entry.Id, password.CustomFields));
                            break;
                        case ItemKind.Note:
                        case ItemKind.Totp:
                        case ItemKind.BankCard:
                        case ItemKind.Document:
                        case ItemKind.BillingAddress:
                        case ItemKind.PaymentAccount:
                            secureItems.Add(ParseSecureItem(root, kind, categoriesByName));
                            break;
                    }
                    continue;
                }

                if (name.Equals("password_history.json", StringComparison.OrdinalIgnoreCase))
                {
                    using var document = ParseJson(bytes);
                    var byEntry = new Dictionary<long, List<PasswordHistoryEntry>>();
                    if (document.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var row in document.RootElement.EnumerateArray())
                        {
                            var id = Long(row, "entryId");
                            if (id <= 0) continue;
                            if (!byEntry.TryGetValue(id, out var values))
                                byEntry[id] = values = [];
                            values.Add(new PasswordHistoryEntry
                            {
                                EntryId = id,
                                Password = String(row, "password"),
                                LastUsedAt = UnixTime(row, "lastUsedAt")
                            });
                        }
                    }
                    history.AddRange(byEntry.Select(item => new PasswordHistoryExportGroup(item.Key, item.Value)));
                    continue;
                }

                if (name.Equals("categories.json", StringComparison.OrdinalIgnoreCase))
                {
                    using var document = ParseJson(bytes);
                    if (document.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var row in document.RootElement.EnumerateArray())
                        {
                            var categoryName = StringOrNull(row, "name")?.Trim();
                            if (string.IsNullOrWhiteSpace(categoryName) || categoriesByName.ContainsKey(categoryName))
                                continue;
                            categoriesByName[categoryName] = new Category
                            {
                                Id = -(categoriesByName.Count + 1),
                                Name = categoryName,
                                SortOrder = Int(row, "sortOrder")
                            };
                        }
                    }
                    continue;
                }

                if (name.Equals("attachments_portable/attachments_portable.json", StringComparison.OrdinalIgnoreCase))
                {
                    ParseAttachmentManifest(bytes, zip, attachments);
                }
            }

            if (passwords.Count == 0 && secureItems.Count == 0)
                throw InvalidFormat();

            return new MonicaExportPackage(
                SchemaVersion: 71,
                Passwords: passwords,
                SecureItems: secureItems,
                Categories: categoriesByName.Values.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToArray(),
                PasswordCustomFields: customFields,
                PasswordHistory: history,
                PasswordAttachments: attachments.Where(a => a.ParentPasswordId is not null)
                    .GroupBy(a => a.ParentPasswordId!.Value)
                    .Select(g => new PasswordAttachmentExportGroup(g.Key, g.Select(a => a.ToPasswordExport()).ToArray())).ToArray(),
                SecureItemAttachments: attachments.Where(a => a.ParentSecureItemId is not null)
                    .GroupBy(a => a.ParentSecureItemId!.Value)
                    .Select(g => new SecureItemAttachmentExportGroup(g.Key, g.Select(a => a.ToSecureExport()).ToArray())).ToArray());
        }
        catch (MonicaJsonImportException)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            throw InvalidFormat();
        }
        catch (JsonException)
        {
            throw InvalidFormat();
        }
        catch (IOException)
        {
            throw InvalidFormat();
        }
    }

}
