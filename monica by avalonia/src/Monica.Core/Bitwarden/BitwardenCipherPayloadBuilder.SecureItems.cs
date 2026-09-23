using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Monica.Core.Models;

namespace Monica.Core.Bitwarden;

/// <summary>
/// The outbound shape of Bitwarden's three non-login ciphers, written as the exact inverse of
/// <c>BitwardenCipherDecoder</c>'s note/card/identity readers in Monica.Platform. The rule that makes a
/// write safe is measured rather than assumed: every plan rebuilds the local row the decoder would
/// produce from the payload about to be sent and compares its fingerprint with the row that came in, so a
/// note with tags, a card with a billing address or an identity with an issue date - content Bitwarden's
/// shape has no field for - refuses instead of being rewritten by the next pull. The projection mirrors
/// the handful of decoder lines it stands in for, and the round-trip test feeds these payloads through
/// the real decoder to prove the mirror holds.
/// </summary>
public static partial class BitwardenCipherPayloadBuilder
{
    private static readonly string[] IdentityDetailLabels =
    [
        "Title",
        "Address",
        "Address2",
        "Address3",
        "City",
        "State",
        "Postal code",
        "Country",
        "Company",
        "Email",
        "Phone",
        "Username"
    ];

    public static string BuildSecureItemCipher(SecureItem item, BitwardenSymmetricKey key)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(key);
        var request = item.ItemType switch
        {
            VaultItemType.Note => PlanNote(item, key),
            VaultItemType.BankCard => PlanCard(item, key),
            VaultItemType.Document => PlanIdentity(item, key),
            _ => throw new BitwardenProtocolException(
                "Monica can only write back Bitwarden login, note, card and identity ciphers.")
        };

        var json = JsonSerializer.Serialize(request, PayloadOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaximumPayloadUtf8Bytes)
        {
            throw new BitwardenProtocolException("Bitwarden cipher payload exceeds the supported size.");
        }

        return json;
    }

    private static CipherRequestDto PlanNote(SecureItem item, BitwardenSymmetricKey key)
    {
        var title = RequireSecureTitle(item);
        var content = NoteContentCodec.DecodeFromItem(item).Content;
        // Reading a note back is not a field-for-field copy: the decoder runs the save payload over the
        // title and the text again, so that is the only state a written note can return to.
        var saved = NoteContentCodec.BuildSavePayload(title, content, "", isMarkdown: false);
        EnsureRoundTrips(item, VaultItemType.Note, saved.Title, saved.NotesCache, saved.ItemData, saved.ImagePaths);
        return SecureItemRequest(item, 2, title, saved.NotesCache, key)
            with
        { SecureNote = new SecureNoteRequestDto { Type = 0 } };
    }

    private static CipherRequestDto PlanCard(SecureItem item, BitwardenSymmetricKey key)
    {
        var title = RequireSecureTitle(item);
        var source = WalletItemDataCodec.DecodeBankCard(item);
        // The decoder fills seven fields from the remote card, echoes the brand into the bank name and
        // hard-codes the rest, so anything the editor put elsewhere cannot come back.
        var decoded = new BankCardWalletData
        {
            CardholderName = source.CardholderName,
            Brand = source.Brand,
            CardNumber = source.CardNumber,
            ExpiryMonth = source.ExpiryMonth,
            ExpiryYear = source.ExpiryYear,
            Cvv = source.Cvv,
            BankName = source.Brand,
            CardTypeString = "CREDIT"
        };
        EnsureRoundTrips(
            item,
            VaultItemType.BankCard,
            title,
            item.Notes,
            WalletItemDataCodec.EncodeBankCard(decoded),
            "[]");
        return SecureItemRequest(item, 3, title, item.Notes, key) with
        {
            Card = new CardRequestDto
            {
                CardholderName = EncryptOptional(source.CardholderName, key),
                Brand = EncryptOptional(source.Brand, key),
                Number = EncryptOptional(source.CardNumber, key),
                ExpMonth = EncryptOptional(source.ExpiryMonth, key),
                ExpYear = EncryptOptional(source.ExpiryYear, key),
                Code = EncryptOptional(source.Cvv, key)
            }
        };
    }

    private static CipherRequestDto PlanIdentity(SecureItem item, BitwardenSymmetricKey key)
    {
        var title = RequireSecureTitle(item);
        var source = WalletItemDataCodec.DecodeDocument(item);
        var details = ParseIdentityDetails(source.AdditionalInfo)
            ?? throw new BitwardenProtocolException(
                "This identity holds details Monica cannot map back onto Bitwarden's fields.");
        // Bitwarden reads the country once and uses it for both the nationality and the details block, so
        // a local row that split the two has no faithful write.
        var country = source.Nationality;
        if (details.TryGetValue("Country", out var detailed) &&
            !string.Equals(detailed, country, StringComparison.Ordinal))
        {
            throw new BitwardenProtocolException(
                "This identity holds details Monica cannot map back onto Bitwarden's fields.");
        }

        details["Country"] = country;
        var number = source.DocumentTypeString switch
        {
            "PASSPORT" => source.DocumentNumber,
            "DRIVER_LICENSE" => "",
            "SOCIAL_SECURITY" => "",
            _ => ""
        };
        var license = source.DocumentTypeString == "DRIVER_LICENSE" ? source.DocumentNumber : "";
        var ssn = source.DocumentTypeString == "SOCIAL_SECURITY" ? source.DocumentNumber : "";
        var decoded = new DocumentWalletData
        {
            DocumentNumber = FirstNonEmpty(number, license, ssn),
            FullName = JoinNonEmpty(source.FullName),
            DocumentTypeString = !string.IsNullOrWhiteSpace(number) ? "PASSPORT" :
                !string.IsNullOrWhiteSpace(license) ? "DRIVER_LICENSE" :
                !string.IsNullOrWhiteSpace(ssn) ? "SOCIAL_SECURITY" : "ID_CARD",
            Nationality = country,
            AdditionalInfo = BuildIdentityDetails(details)
        };
        EnsureRoundTrips(
            item,
            VaultItemType.Document,
            title,
            item.Notes,
            WalletItemDataCodec.EncodeDocument(decoded),
            "[]");
        return SecureItemRequest(item, 4, title, item.Notes, key) with
        {
            Identity = new IdentityRequestDto
            {
                Title = EncryptOptional(Detail(details, "Title"), key),
                FirstName = EncryptOptional(source.FullName, key),
                Address1 = EncryptOptional(Detail(details, "Address"), key),
                Address2 = EncryptOptional(Detail(details, "Address2"), key),
                Address3 = EncryptOptional(Detail(details, "Address3"), key),
                City = EncryptOptional(Detail(details, "City"), key),
                State = EncryptOptional(Detail(details, "State"), key),
                PostalCode = EncryptOptional(Detail(details, "Postal code"), key),
                Country = EncryptOptional(country, key),
                Company = EncryptOptional(Detail(details, "Company"), key),
                Email = EncryptOptional(Detail(details, "Email"), key),
                Phone = EncryptOptional(Detail(details, "Phone"), key),
                Username = EncryptOptional(Detail(details, "Username"), key),
                PassportNumber = EncryptOptional(number, key),
                LicenseNumber = EncryptOptional(license, key),
                Ssn = EncryptOptional(ssn, key)
            }
        };
    }

    private static string RequireSecureTitle(SecureItem item)
    {
        if (item.IsDeleted)
        {
            throw new BitwardenProtocolException(
                "Monica deletes Bitwarden ciphers through the trash endpoint, not the update payload.");
        }

        var title = item.Title.Trim();
        if (title.Length == 0)
        {
            throw new BitwardenProtocolException("A Bitwarden cipher requires a title.");
        }

        return title;
    }

    private static CipherRequestDto SecureItemRequest(
        SecureItem item,
        int cipherType,
        string title,
        string notes,
        BitwardenSymmetricKey key) => new()
        {
            FolderId = string.IsNullOrWhiteSpace(item.BitwardenFolderId) ? null : item.BitwardenFolderId,
            Type = cipherType,
            Name = BitwardenCipherStringCrypto.EncryptString(title, key),
            Notes = EncryptOptional(notes, key),
            Favorite = item.IsFavorite
        };

    private static void EnsureRoundTrips(
        SecureItem item,
        VaultItemType itemType,
        string title,
        string notes,
        string itemData,
        string imagePaths)
    {
        var projected = new SecureItem
        {
            ItemType = itemType,
            Title = title,
            Notes = notes,
            ItemData = itemData,
            ImagePaths = imagePaths,
            IsFavorite = item.IsFavorite,
            BitwardenFolderId = string.IsNullOrWhiteSpace(item.BitwardenFolderId) ? null : item.BitwardenFolderId,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt
        };
        if (!string.Equals(
                BitwardenPayloadFingerprint.ForSecureItem(item),
                BitwardenPayloadFingerprint.ForSecureItem(projected),
                StringComparison.Ordinal))
        {
            throw new BitwardenProtocolException(
                "This entry holds content Bitwarden's shape for it cannot carry.");
        }
    }

    private static Dictionary<string, string>? ParseIdentityDetails(string additionalInfo)
    {
        var details = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(additionalInfo))
        {
            return details;
        }

        foreach (var line in additionalInfo.Split(Environment.NewLine, StringSplitOptions.None))
        {
            var separator = line.IndexOf(": ", StringComparison.Ordinal);
            if (separator <= 0 ||
                !IdentityDetailLabels.Contains(line[..separator], StringComparer.Ordinal) ||
                !details.TryAdd(line[..separator], line[(separator + 2)..]))
            {
                return null;
            }
        }

        return details;
    }

    private static string BuildIdentityDetails(IReadOnlyDictionary<string, string> details)
    {
        var builder = new StringBuilder();
        foreach (var label in IdentityDetailLabels)
        {
            var value = Detail(details, label);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(label).Append(": ").Append(value);
        }

        return builder.ToString();
    }

    private static string Detail(IReadOnlyDictionary<string, string> details, string label) =>
        details.TryGetValue(label, out var value) ? value : "";

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static string JoinNonEmpty(params string[] values) =>
        string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()));
}

internal sealed record SecureNoteRequestDto
{
    public int Type { get; init; }
}

internal sealed record CardRequestDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CardholderName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Brand { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Number { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpMonth { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpYear { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; init; }
}

internal sealed record IdentityRequestDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FirstName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MiddleName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Address1 { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Address2 { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Address3 { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? City { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? State { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PostalCode { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Country { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Company { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Email { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Phone { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Ssn { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Username { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PassportNumber { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LicenseNumber { get; init; }
}
