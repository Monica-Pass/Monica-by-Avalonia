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
public static class MonicaZipImportParser
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
                    using var document = JsonDocument.Parse(bytes);
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
                    using var document = JsonDocument.Parse(bytes);
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

                if (name.Equals("attachments_portable/attachments_portable.json", StringComparison.OrdinalIgnoreCase))
                {
                    ParseAttachmentManifest(bytes, zip, attachments, ref expandedBytes);
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

    private static PasswordParseResult ParsePassword(JsonElement root, IDictionary<string, Category> categories)
    {
        var categoryName = StringOrNull(root, "categoryName");
        var categoryId = ResolveCategory(categoryName, categories);
        var entry = new PasswordEntry
        {
            Id = Long(root, "id"),
            Title = String(root, "title"),
            Username = String(root, "username"),
            Password = String(root, "password"),
            Website = String(root, "website"),
            Notes = String(root, "notes"),
            IsFavorite = Bool(root, "isFavorite"),
            SortOrder = Int(root, "sortOrder"),
            CategoryId = categoryId,
            AppPackageName = String(root, "appPackageName"),
            AppName = String(root, "appName"),
            Email = String(root, "email"),
            Phone = String(root, "phone"),
            AddressLine = String(root, "addressLine"),
            City = String(root, "city"),
            State = String(root, "state"),
            ZipCode = String(root, "zipCode"),
            Country = String(root, "country"),
            CreditCardNumber = String(root, "creditCardNumber"),
            CreditCardHolder = String(root, "creditCardHolder"),
            CreditCardExpiry = String(root, "creditCardExpiry"),
            CreditCardCvv = String(root, "creditCardCVV"),
            AuthenticatorKey = String(root, "authenticatorKey"),
            PasskeyBindings = String(root, "passkeyBindings"),
            SshKeyData = String(root, "sshKeyData"),
            LoginType = ParseLoginType(String(root, "loginType")),
            SsoProvider = String(root, "ssoProvider"),
            SsoRefEntryId = NullableLong(root, "ssoRefEntryId"),
            WifiMetadata = String(root, "wifiMetadata"),
            CustomIconType = String(root, "customIconType", "NONE"),
            CustomIconValue = StringOrNull(root, "customIconValue"),
            CustomIconUpdatedAt = Long(root, "customIconUpdatedAt"),
            BoundNoteId = NullableLong(root, "boundNoteId"),
            IsArchived = Bool(root, "isArchived"),
            ArchivedAt = NullableUnixTime(root, "archivedAt"),
            CreatedAt = UnixTime(root, "createdAt"),
            UpdatedAt = UnixTime(root, "updatedAt")
        };
        var fields = new List<CustomField>();
        if (root.TryGetProperty("customFields", out var custom) && custom.ValueKind == JsonValueKind.Array)
        {
            var order = 0;
            foreach (var row in custom.EnumerateArray())
                fields.Add(new CustomField { EntryId = entry.Id, Title = String(row, "title"), Value = String(row, "value"), IsProtected = Bool(row, "isProtected"), SortOrder = order++ });
        }
        return new PasswordParseResult(entry, fields);
    }

    private static SecureItem ParseSecureItem(JsonElement root, ItemKind kind, IDictionary<string, Category> categories)
    {
        var categoryId = ResolveCategory(StringOrNull(root, "categoryName"), categories);
        var itemData = String(root, "itemData", "{}");
        var item = new SecureItem
        {
            Id = Long(root, "id"),
            ItemType = kind switch
            {
                ItemKind.Note => VaultItemType.Note,
                ItemKind.Totp => VaultItemType.Totp,
                ItemKind.BankCard => VaultItemType.BankCard,
                ItemKind.Document => VaultItemType.Document,
                ItemKind.BillingAddress => VaultItemType.BillingAddress,
                _ => VaultItemType.PaymentAccount
            },
            Title = String(root, "title"),
            ItemData = itemData,
            Notes = String(root, "notes"),
            IsFavorite = Bool(root, "isFavorite"),
            SortOrder = Int(root, "sortOrder"),
            ImagePaths = String(root, "imagePaths", "[]"),
            CategoryId = categoryId,
            BoundPasswordId = ExtractJsonLong(itemData, "boundPasswordId"),
            CreatedAt = UnixTime(root, "createdAt"),
            UpdatedAt = UnixTime(root, "updatedAt")
        };
        // Android imagePaths are installation-local absolute paths. Portable
        // attachment entries are restored separately by the import use case.
        StripSecureItemImages(item);
        return item;
    }

    private static void ParseAttachmentManifest(byte[] bytes, ZipArchive zip, ICollection<PortableAttachment> output, ref long expandedBytes)
    {
        using var document = JsonDocument.Parse(bytes);
        if (!document.RootElement.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return;
        foreach (var row in entries.EnumerateArray())
        {
            var payloadPath = String(row, "payloadPath").Replace('\\', '/');
            if (payloadPath.Length == 0 || payloadPath.Contains("..", StringComparison.Ordinal)) continue;
            var entry = zip.GetEntry(payloadPath);
            if (entry is null || entry.Length < 0 || entry.Length > MaximumEntryBytes || (expandedBytes += entry.Length) > MaximumArchiveBytes)
                throw new MonicaJsonImportException(MonicaJsonImportError.ResourceLimitExceeded, "The Monica ZIP attachment exceeds the safe size limit.");
            var payload = ReadEntry(entry);
            var attachment = new Attachment
            {
                FileName = String(row, "fileName", "attachment"),
                ContentType = String(row, "mimeType", "application/octet-stream"),
                SizeBytes = Long(row, "sizeBytes"),
                CreatedAt = UnixTime(row, "createdAt")
            };
            output.Add(new PortableAttachment(NullableLong(row, "parentPasswordId"), NullableLong(row, "parentSecureItemId"), attachment, Convert.ToBase64String(payload)));
        }
    }

    private static void StripSecureItemImages(SecureItem item)
    {
        item.ImagePaths = "[]";
        try
        {
            switch (item.ItemType)
            {
                case VaultItemType.Note:
                    var note = NoteContentCodec.DecodeFromItem(item);
                    item.ItemData = NoteContentCodec.BuildSavePayload(item.Title, note.Content,
                        string.Join(',', note.Tags), note.IsMarkdown, []).ItemData;
                    break;
                case VaultItemType.Document:
                    var document = WalletItemDataCodec.DecodeDocument(item);
                    document.ImagePaths.Clear();
                    item.ItemData = WalletItemDataCodec.EncodeDocument(document);
                    break;
                case VaultItemType.BankCard:
                    var card = WalletItemDataCodec.DecodeBankCard(item);
                    card.ImagePaths.Clear();
                    item.ItemData = WalletItemDataCodec.EncodeBankCard(card);
                    break;
                case VaultItemType.BillingAddress:
                    var address = WalletItemDataCodec.DecodeBillingAddress(item);
                    address.ImagePaths.Clear();
                    item.ItemData = WalletItemDataCodec.EncodeBillingAddress(address);
                    break;
                case VaultItemType.PaymentAccount:
                    var account = WalletItemDataCodec.DecodePaymentAccount(item);
                    account.ImagePaths.Clear();
                    item.ItemData = WalletItemDataCodec.EncodePaymentAccount(account);
                    break;
            }
        }
        catch (Exception)
        {
            // Preserve unknown Android payloads even when their optional image
            // shape is newer than this desktop build.
        }
    }

    private static long? ExtractJsonLong(string json, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return NullableLong(document.RootElement, propertyName);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int ResolveCategory(string? name, IDictionary<string, Category> categories)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;
        name = name.Trim();
        if (!categories.TryGetValue(name, out var category))
        {
            category = new Category { Id = -(categories.Count + 1), Name = name, SortOrder = categories.Count };
            categories[name] = category;
        }
        return (int)category.Id;
    }

    private static bool TryGetItemKind(string name, out ItemKind kind)
    {
        var slash = name.IndexOf('/');
        var folder = slash > 0 ? name[..slash] : "";
        kind = folder.ToLowerInvariant() switch
        {
            "passwords" => ItemKind.Password,
            "notes" => ItemKind.Note,
            "totp" or "authenticators" => ItemKind.Totp,
            "bank_cards" => ItemKind.BankCard,
            "documents" => ItemKind.Document,
            "billing_addresses" => ItemKind.BillingAddress,
            "payment_accounts" => ItemKind.PaymentAccount,
            _ => default
        };
        return slash > 0 && kind != default || (slash > 0 && folder.Equals("passwords", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeEntryName(string name)
    {
        name = name.Replace('\\', '/');
        if (name.StartsWith('/') || name.Split('/').Any(part => part == "..")) throw InvalidFormat();
        return name.TrimStart('.', '/');
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var input = entry.Open();
        using var output = new MemoryStream((int)Math.Min(entry.Length, MaximumEntryBytes));
        input.CopyTo(output);
        if (output.Length > MaximumEntryBytes) throw new MonicaJsonImportException(MonicaJsonImportError.ResourceLimitExceeded, "The Monica ZIP entry exceeds the safe size limit.");
        return output.ToArray();
    }

    private static string String(JsonElement row, string name, string fallback = "") => row.TryGetProperty(name, out var value) ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : value.ToString() : fallback;
    private static string? StringOrNull(JsonElement row, string name) => row.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? String(row, name) : null;
    private static long Long(JsonElement row, string name) => row.TryGetProperty(name, out var value) && (value.TryGetInt64(out var number) || long.TryParse(value.ToString(), out number)) ? number : 0;
    private static long? NullableLong(JsonElement row, string name) => Long(row, name) is var value && value != 0 ? value : null;
    private static int Int(JsonElement row, string name) => (int)Long(row, name);
    private static bool Bool(JsonElement row, string name) => row.TryGetProperty(name, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed);
    private static DateTimeOffset UnixTime(JsonElement row, string name) => DateTimeOffset.TryParse(String(row, name), out var parsed) ? parsed : (Long(row, name) > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(Long(row, name)) : DateTimeOffset.UtcNow);
    private static DateTimeOffset? NullableUnixTime(JsonElement row, string name) => NullableLong(row, name) is { } value ? DateTimeOffset.FromUnixTimeMilliseconds(value) : null;
    private static PasswordLoginType ParseLoginType(string value) => value.ToUpperInvariant() switch { "API_KEY" or "APIKEY" => PasswordLoginType.ApiKey, "SSO" => PasswordLoginType.Sso, "WIFI" => PasswordLoginType.Wifi, "SSH_KEY" or "SSH" => PasswordLoginType.SshKey, "BARCODE" => PasswordLoginType.Barcode, _ => PasswordLoginType.Password };
    private static MonicaJsonImportException InvalidFormat() => new(MonicaJsonImportError.InvalidFormat, "The Monica ZIP import format is invalid.");

    private enum ItemKind { Password = 1, Note, Totp, BankCard, Document, BillingAddress, PaymentAccount }
    private sealed record PasswordParseResult(PasswordEntry Entry, IReadOnlyList<CustomField> CustomFields);
    private sealed record PortableAttachment(long? ParentPasswordId, long? ParentSecureItemId, Attachment Metadata, string ContentBase64)
    {
        public PasswordAttachmentExport ToPasswordExport() => new(Metadata, ContentBase64);
        public SecureItemAttachmentExport ToSecureExport() => new(Metadata, ContentBase64);
    }
}
