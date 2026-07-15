using System.Text;
using System.Text.Json;
using Monica.Core.Models;

namespace Monica.Core.ImportExport;

public sealed partial class BitwardenJsonImporter
{
    private SecureItem MapSecureItem(JsonElement item, string sourceId, long sourceModelId, int type)
    {
        var metadata = ReadMetadata(item, sourceId, type);
        var notes = AppendSecondaryMetadata(item, metadata.Notes);
        var secureItem = type switch
        {
            2 => MapNote(metadata, sourceModelId, notes),
            3 => MapCard(item, metadata, sourceModelId, notes),
            4 => MapIdentity(item, metadata, sourceModelId, notes),
            _ => throw InvalidExport()
        };
        secureItem.ReplicaGroupId = $"bitwarden-json:{metadata.SourceId}";
        secureItem.BitwardenCipherId = metadata.SourceId;
        secureItem.BitwardenFolderId = EmptyToNull(metadata.FolderId);
        secureItem.BitwardenRevisionDate = EmptyToNull(metadata.RevisionDate);
        secureItem.BitwardenLocalModified = false;
        secureItem.SyncStatus = SyncStatus.None;
        return secureItem;
    }

    private static SecureItem MapNote(
        SourceMetadata metadata,
        long sourceModelId,
        string content)
    {
        var payload = NoteContentCodec.BuildSavePayload(
            ResolveTitle(metadata.Title, "Bitwarden note"),
            content,
            "",
            isMarkdown: false);
        return CreateSecureItem(
            metadata,
            sourceModelId,
            VaultItemType.Note,
            payload.Title,
            payload.NotesCache,
            payload.ItemData,
            payload.ImagePaths);
    }

    private static SecureItem MapCard(
        JsonElement item,
        SourceMetadata metadata,
        long sourceModelId,
        string notes)
    {
        var card = GetObject(item, "card");
        var data = new BankCardWalletData
        {
            CardholderName = card is { } value ? GetString(value, "cardholderName") : "",
            Brand = card is { } brand ? GetString(brand, "brand") : "",
            CardNumber = card is { } number ? GetString(number, "number") : "",
            ExpiryMonth = card is { } month ? GetString(month, "expMonth") : "",
            ExpiryYear = card is { } year ? GetString(year, "expYear") : "",
            Cvv = card is { } code ? GetString(code, "code") : "",
            BankName = card is { } bank ? GetString(bank, "brand") : "",
            CardTypeString = "CREDIT"
        };
        return CreateSecureItem(
            metadata,
            sourceModelId,
            VaultItemType.BankCard,
            ResolveTitle(metadata.Title, "Bitwarden card"),
            notes,
            WalletItemDataCodec.EncodeBankCard(data),
            "[]");
    }

    private static SecureItem MapIdentity(
        JsonElement item,
        SourceMetadata metadata,
        long sourceModelId,
        string notes)
    {
        var identity = GetObject(item, "identity");
        var passport = identity is { } passportData ? GetString(passportData, "passportNumber") : "";
        var license = identity is { } licenseData ? GetString(licenseData, "licenseNumber") : "";
        var ssn = identity is { } ssnData ? GetString(ssnData, "ssn") : "";
        var data = new DocumentWalletData
        {
            DocumentNumber = FirstNonEmpty(passport, license, ssn),
            FullName = JoinNonEmpty(
                identity is { } first ? GetString(first, "firstName") : "",
                identity is { } middle ? GetString(middle, "middleName") : "",
                identity is { } last ? GetString(last, "lastName") : ""),
            DocumentTypeString = !string.IsNullOrWhiteSpace(passport) ? "PASSPORT" :
                !string.IsNullOrWhiteSpace(license) ? "DRIVER_LICENSE" :
                !string.IsNullOrWhiteSpace(ssn) ? "SOCIAL_SECURITY" : "ID_CARD",
            Nationality = identity is { } country ? GetString(country, "country") : "",
            AdditionalInfo = BuildIdentityDetails(identity)
        };
        return CreateSecureItem(
            metadata,
            sourceModelId,
            VaultItemType.Document,
            ResolveTitle(metadata.Title, "Bitwarden identity"),
            notes,
            WalletItemDataCodec.EncodeDocument(data),
            "[]");
    }

    private static SecureItem CreateSecureItem(
        SourceMetadata metadata,
        long sourceModelId,
        VaultItemType itemType,
        string title,
        string notes,
        string itemData,
        string imagePaths) =>
        new()
        {
            Id = sourceModelId,
            ItemType = itemType,
            Title = title,
            Notes = notes,
            IsFavorite = metadata.IsFavorite,
            CreatedAt = metadata.CreatedAt,
            UpdatedAt = metadata.UpdatedAt,
            ItemData = itemData,
            ImagePaths = imagePaths,
            IsDeleted = metadata.DeletedAt is not null,
            DeletedAt = metadata.DeletedAt
        };

    private string AppendSecondaryMetadata(JsonElement item, string notes)
    {
        var lines = new List<string>();
        var fields = GetArray(item, "fields", _limits.MaximumFieldsPerItem);
        if (fields is { } values)
        {
            foreach (var field in values.EnumerateArray())
            {
                if (field.ValueKind != JsonValueKind.Object)
                {
                    throw InvalidExport();
                }

                var name = GetString(field, "name").Trim();
                var value = GetString(field, "value");
                if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(value))
                {
                    lines.Add($"{ResolveTitle(name, "Bitwarden field")}: {value}");
                }
            }
        }

        var attachments = GetArray(item, "attachments", _limits.MaximumFieldsPerItem);
        if (attachments is { } attachmentValues)
        {
            foreach (var attachment in attachmentValues.EnumerateArray())
            {
                if (attachment.ValueKind != JsonValueKind.Object)
                {
                    throw InvalidExport();
                }

                var fileName = GetString(attachment, "fileName").Trim();
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    lines.Add($"Bitwarden attachment: {fileName}");
                }
            }
        }

        return lines.Count == 0
            ? notes
            : string.IsNullOrWhiteSpace(notes)
                ? string.Join(Environment.NewLine, lines)
                : $"{notes}{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
    }

    private static string BuildIdentityDetails(JsonElement? identity)
    {
        if (identity is null)
        {
            return "";
        }

        var fields = new (string Label, string Name)[]
        {
            ("Title", "title"),
            ("Address", "address1"),
            ("Address 2", "address2"),
            ("City", "city"),
            ("State", "state"),
            ("Postal code", "postalCode"),
            ("Country", "country"),
            ("Email", "email"),
            ("Phone", "phone"),
            ("SSN", "ssn"),
            ("Passport", "passportNumber"),
            ("License", "licenseNumber")
        };
        var builder = new StringBuilder();
        foreach (var field in fields)
        {
            var value = GetString(identity.Value, field.Name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(field.Label).Append(": ").Append(value);
            }
        }

        return builder.ToString();
    }

    private static string ResolveTitle(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static string JoinNonEmpty(params string[] values) =>
        string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()));
}
