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

    /// <summary>
    /// Asks whether this row can travel without encrypting anything. The vault key is only used to turn
    /// plaintext into cipher strings, never to decide, so the projection gate below answers the question on
    /// its own - which is what lets the library count publishable rows before an upload is even chosen.
    /// </summary>
    public static bool CanEncode(SecureItem item)
    {
        if (item is null)
        {
            return false;
        }

        try
        {
            PlanSecureItem(item);
            return true;
        }
        catch (BitwardenProtocolException)
        {
            return false;
        }
    }

    public static string BuildSecureItemCipher(SecureItem item, BitwardenSymmetricKey key)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(key);
        var json = JsonSerializer.Serialize(Emit(PlanSecureItem(item), key), PayloadOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaximumPayloadUtf8Bytes)
        {
            throw new BitwardenProtocolException("Bitwarden cipher payload exceeds the supported size.");
        }

        return json;
    }

    private static SecureItemPlan PlanSecureItem(SecureItem item) => item.ItemType switch
    {
        VaultItemType.Note => PlanNote(item),
        VaultItemType.BankCard => PlanCard(item),
        VaultItemType.Document => PlanIdentity(item),
        _ => throw new BitwardenProtocolException(
            "Monica can only write back Bitwarden login, note, card and identity ciphers.")
    };

    private static SecureItemPlan PlanNote(SecureItem item)
    {
        var title = RequireSecureTitle(item);
        var content = NoteContentCodec.DecodeFromItem(item).Content;
        // Reading a note back is not a field-for-field copy: the decoder runs the save payload over the
        // title and the text again, so that is the only state a written note can return to.
        var saved = NoteContentCodec.BuildSavePayload(title, content, "", isMarkdown: false);
        EnsureRoundTrips(item, VaultItemType.Note, saved.Title, saved.NotesCache, saved.ItemData, saved.ImagePaths);
        return new SecureItemPlan(2, saved.Title, FolderOf(item), item.IsFavorite, saved.NotesCache, null, null);
    }

    private static SecureItemPlan PlanCard(SecureItem item)
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
        return new SecureItemPlan(
            3,
            title,
            FolderOf(item),
            item.IsFavorite,
            item.Notes,
            new CardPlan(
                source.CardholderName,
                source.Brand,
                source.CardNumber,
                source.ExpiryMonth,
                source.ExpiryYear,
                source.Cvv),
            null);
    }

    private static SecureItemPlan PlanIdentity(SecureItem item)
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
        return new SecureItemPlan(
            4,
            title,
            FolderOf(item),
            item.IsFavorite,
            item.Notes,
            null,
            new IdentityPlan(
                Detail(details, "Title"),
                source.FullName,
                Detail(details, "Address"),
                Detail(details, "Address2"),
                Detail(details, "Address3"),
                Detail(details, "City"),
                Detail(details, "State"),
                Detail(details, "Postal code"),
                country,
                Detail(details, "Company"),
                Detail(details, "Email"),
                Detail(details, "Phone"),
                Detail(details, "Username"),
                number,
                license,
                ssn));
    }

    private static CipherRequestDto Emit(SecureItemPlan plan, BitwardenSymmetricKey key)
    {
        var request = new CipherRequestDto
        {
            FolderId = plan.FolderId,
            Type = plan.CipherType,
            Name = BitwardenCipherStringCrypto.EncryptString(plan.Title, key),
            Notes = EncryptOptional(plan.Notes, key),
            Favorite = plan.IsFavorite
        };
        return plan.CipherType switch
        {
            2 => request with { SecureNote = new SecureNoteRequestDto { Type = 0 } },
            3 => request with
            {
                Card = new CardRequestDto
                {
                    CardholderName = EncryptOptional(plan.Card!.CardholderName, key),
                    Brand = EncryptOptional(plan.Card.Brand, key),
                    Number = EncryptOptional(plan.Card.Number, key),
                    ExpMonth = EncryptOptional(plan.Card.ExpMonth, key),
                    ExpYear = EncryptOptional(plan.Card.ExpYear, key),
                    Code = EncryptOptional(plan.Card.Code, key)
                }
            },
            _ => request with
            {
                Identity = new IdentityRequestDto
                {
                    Title = EncryptOptional(plan.Identity!.Title, key),
                    FirstName = EncryptOptional(plan.Identity.FirstName, key),
                    Address1 = EncryptOptional(plan.Identity.Address1, key),
                    Address2 = EncryptOptional(plan.Identity.Address2, key),
                    Address3 = EncryptOptional(plan.Identity.Address3, key),
                    City = EncryptOptional(plan.Identity.City, key),
                    State = EncryptOptional(plan.Identity.State, key),
                    PostalCode = EncryptOptional(plan.Identity.PostalCode, key),
                    Country = EncryptOptional(plan.Identity.Country, key),
                    Company = EncryptOptional(plan.Identity.Company, key),
                    Email = EncryptOptional(plan.Identity.Email, key),
                    Phone = EncryptOptional(plan.Identity.Phone, key),
                    Username = EncryptOptional(plan.Identity.Username, key),
                    PassportNumber = EncryptOptional(plan.Identity.PassportNumber, key),
                    LicenseNumber = EncryptOptional(plan.Identity.LicenseNumber, key),
                    Ssn = EncryptOptional(plan.Identity.Ssn, key)
                }
            }
        };
    }

    private static string? FolderOf(SecureItem item) =>
        string.IsNullOrWhiteSpace(item.BitwardenFolderId) ? null : item.BitwardenFolderId;

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

/// <summary>
/// What a safe write looks like, stated in plaintext. The projection gate decides inside the plan, so
/// everything after it - including whether a row is worth offering for upload at all - reads from here.
/// </summary>
internal sealed record SecureItemPlan(
    int CipherType,
    string Title,
    string? FolderId,
    bool IsFavorite,
    string Notes,
    CardPlan? Card,
    IdentityPlan? Identity);

internal sealed record CardPlan(
    string CardholderName,
    string Brand,
    string Number,
    string ExpMonth,
    string ExpYear,
    string Code);

internal sealed record IdentityPlan(
    string Title,
    string FirstName,
    string Address1,
    string Address2,
    string Address3,
    string City,
    string State,
    string PostalCode,
    string Country,
    string Company,
    string Email,
    string Phone,
    string Username,
    string PassportNumber,
    string LicenseNumber,
    string Ssn);

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
