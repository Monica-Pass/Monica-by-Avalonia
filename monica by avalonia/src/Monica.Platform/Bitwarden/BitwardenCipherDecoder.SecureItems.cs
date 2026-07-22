using System.Text;
using Monica.Core.Bitwarden;
using Monica.Core.Models;

namespace Monica.Platform.Bitwarden;

internal sealed partial class BitwardenCipherDecoder
{
    private BitwardenDecodedCipher DecodeSecureNote(
        VaultCipherDto cipher,
        string id,
        string revision,
        DateTimeOffset updatedAt,
        BitwardenSymmetricKey key)
    {
        var notes = AppendFields(DecryptOptional(cipher.Notes, key), cipher.Fields, key);
        var title = ResolveTitle(DecryptRequired(cipher.Name, key, "cipher name"), "Bitwarden note");
        var payload = NoteContentCodec.BuildSavePayload(title, notes, "", isMarkdown: false);
        var item = CreateSecureItemBase(
            cipher,
            VaultItemType.Note,
            payload.Title,
            payload.NotesCache,
            payload.ItemData,
            updatedAt);
        item.ImagePaths = payload.ImagePaths;
        return BuildSecureResult(cipher, id, revision, updatedAt, item);
    }

    private BitwardenDecodedCipher DecodeCard(
        VaultCipherDto cipher,
        string id,
        string revision,
        DateTimeOffset updatedAt,
        BitwardenSymmetricKey key)
    {
        var card = cipher.Card ?? new VaultCardDto();
        var brand = DecryptOptional(card.Brand, key);
        var data = new BankCardWalletData
        {
            CardholderName = DecryptOptional(card.CardholderName, key),
            Brand = brand,
            CardNumber = DecryptOptional(card.Number, key),
            ExpiryMonth = DecryptOptional(card.ExpMonth, key),
            ExpiryYear = DecryptOptional(card.ExpYear, key),
            Cvv = DecryptOptional(card.Code, key),
            BankName = brand,
            CardTypeString = "CREDIT"
        };
        var item = CreateSecureItemBase(
            cipher,
            VaultItemType.BankCard,
            ResolveTitle(DecryptRequired(cipher.Name, key, "cipher name"), "Bitwarden card"),
            AppendFields(DecryptOptional(cipher.Notes, key), cipher.Fields, key),
            WalletItemDataCodec.EncodeBankCard(data),
            updatedAt);
        return BuildSecureResult(cipher, id, revision, updatedAt, item);
    }

    private BitwardenDecodedCipher DecodeIdentity(
        VaultCipherDto cipher,
        string id,
        string revision,
        DateTimeOffset updatedAt,
        BitwardenSymmetricKey key)
    {
        var identity = cipher.Identity ?? new VaultIdentityDto();
        var passport = DecryptOptional(identity.PassportNumber, key);
        var license = DecryptOptional(identity.LicenseNumber, key);
        var ssn = DecryptOptional(identity.Ssn, key);
        var data = new DocumentWalletData
        {
            DocumentNumber = FirstNonEmpty(passport, license, ssn),
            FullName = JoinNonEmpty(
                DecryptOptional(identity.FirstName, key),
                DecryptOptional(identity.MiddleName, key),
                DecryptOptional(identity.LastName, key)),
            DocumentTypeString = !string.IsNullOrWhiteSpace(passport) ? "PASSPORT" :
                !string.IsNullOrWhiteSpace(license) ? "DRIVER_LICENSE" :
                !string.IsNullOrWhiteSpace(ssn) ? "SOCIAL_SECURITY" : "ID_CARD",
            Nationality = DecryptOptional(identity.Country, key),
            AdditionalInfo = BuildIdentityDetails(identity, key)
        };
        var item = CreateSecureItemBase(
            cipher,
            VaultItemType.Document,
            ResolveTitle(DecryptRequired(cipher.Name, key, "cipher name"), "Bitwarden identity"),
            AppendFields(DecryptOptional(cipher.Notes, key), cipher.Fields, key),
            WalletItemDataCodec.EncodeDocument(data),
            updatedAt);
        return BuildSecureResult(cipher, id, revision, updatedAt, item);
    }

    private SecureItem CreateSecureItemBase(
        VaultCipherDto cipher,
        VaultItemType itemType,
        string title,
        string notes,
        string itemData,
        DateTimeOffset updatedAt) => new()
        {
            ItemType = itemType,
            Title = title,
            Notes = notes,
            ItemData = itemData,
            ImagePaths = "[]",
            IsFavorite = cipher.Favorite,
            BitwardenFolderId = EmptyToNull(cipher.FolderId),
            CreatedAt = ParseOptionalDate(cipher.CreationDate) ?? updatedAt,
            UpdatedAt = updatedAt
        };

    private static BitwardenDecodedCipher BuildSecureResult(
        VaultCipherDto source,
        string id,
        string revision,
        DateTimeOffset updatedAt,
        SecureItem item)
    {
        var metadata = new BitwardenRemoteCipherMetadata(
            id,
            item.BitwardenFolderId,
            revision,
            source.Type,
            false,
            BitwardenPayloadFingerprint.ForSecureItem(item),
            updatedAt);
        return new BitwardenDecodedCipher(metadata, null, item, [], []);
    }

    private string AppendFields(
        string notes,
        IReadOnlyList<VaultFieldDto>? source,
        BitwardenSymmetricKey key)
    {
        var fields = DecodeFields(source, key);
        if (fields.Count == 0)
        {
            return notes;
        }

        var suffix = string.Join(Environment.NewLine, fields.Select(field => $"{field.Title}: {field.Value}"));
        return string.IsNullOrWhiteSpace(notes) ? suffix : $"{notes}{Environment.NewLine}{Environment.NewLine}{suffix}";
    }

    private static string BuildIdentityDetails(VaultIdentityDto identity, BitwardenSymmetricKey key)
    {
        var fields = new (string Label, string Value)[]
        {
            ("Title", DecryptOptional(identity.Title, key)),
            ("Address", DecryptOptional(identity.Address1, key)),
            ("Address 2", DecryptOptional(identity.Address2, key)),
            ("Address 3", DecryptOptional(identity.Address3, key)),
            ("City", DecryptOptional(identity.City, key)),
            ("State", DecryptOptional(identity.State, key)),
            ("Postal code", DecryptOptional(identity.PostalCode, key)),
            ("Country", DecryptOptional(identity.Country, key)),
            ("Company", DecryptOptional(identity.Company, key)),
            ("Email", DecryptOptional(identity.Email, key)),
            ("Phone", DecryptOptional(identity.Phone, key)),
            ("Username", DecryptOptional(identity.Username, key))
        };
        var builder = new StringBuilder();
        foreach (var field in fields.Where(field => !string.IsNullOrWhiteSpace(field.Value)))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(field.Label).Append(": ").Append(field.Value);
        }

        return builder.ToString();
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static string JoinNonEmpty(params string[] values) =>
        string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()));
}
