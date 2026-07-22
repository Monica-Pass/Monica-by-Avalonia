using System.Text.Json;
using System.Text.Json.Serialization;

namespace Monica.Core.Models;

public sealed class DocumentWalletData
{
    [JsonPropertyName("documentNumber")]
    public string DocumentNumber { get; set; } = "";

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = "";

    [JsonPropertyName("issuedDate")]
    public string IssuedDate { get; set; } = "";

    [JsonPropertyName("expiryDate")]
    public string ExpiryDate { get; set; } = "";

    [JsonPropertyName("issuedBy")]
    public string IssuedBy { get; set; } = "";

    [JsonPropertyName("nationality")]
    public string Nationality { get; set; } = "";

    [JsonPropertyName("documentType")]
    public string DocumentTypeString { get; set; } = "ID_CARD";

    [JsonPropertyName("imagePaths")]
    public List<string> ImagePaths { get; set; } = [];

    [JsonPropertyName("additionalInfo")]
    public string AdditionalInfo { get; set; } = "";
}

public sealed class BankCardWalletData
{
    [JsonPropertyName("cardNumber")]
    public string CardNumber { get; set; } = "";

    [JsonPropertyName("cardholderName")]
    public string CardholderName { get; set; } = "";

    [JsonPropertyName("expiryMonth")]
    public string ExpiryMonth { get; set; } = "";

    [JsonPropertyName("expiryYear")]
    public string ExpiryYear { get; set; } = "";

    [JsonPropertyName("cvv")]
    public string Cvv { get; set; } = "";

    [JsonPropertyName("bankName")]
    public string BankName { get; set; } = "";

    [JsonPropertyName("cardType")]
    public string CardTypeString { get; set; } = "DEBIT";

    [JsonPropertyName("billingAddress")]
    public string BillingAddress { get; set; } = "";

    [JsonPropertyName("imagePaths")]
    public List<string> ImagePaths { get; set; } = [];

    [JsonPropertyName("brand")]
    public string Brand { get; set; } = "";

    [JsonPropertyName("nickname")]
    public string Nickname { get; set; } = "";
}

public static partial class WalletItemDataCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static DocumentWalletData DecodeDocument(SecureItem item)
    {
        var data = Decode<DocumentWalletData>(item.ItemData) ?? new DocumentWalletData();
        MergeImagePaths(data.ImagePaths, item.ImagePaths);
        return data;
    }

    public static BankCardWalletData DecodeBankCard(SecureItem item)
    {
        var data = Decode<BankCardWalletData>(item.ItemData) ?? new BankCardWalletData();
        MergeImagePaths(data.ImagePaths, item.ImagePaths);
        return data;
    }

    public static string EncodeDocument(DocumentWalletData data) =>
        JsonSerializer.Serialize(data, JsonOptions);

    public static string EncodeBankCard(BankCardWalletData data) =>
        JsonSerializer.Serialize(data, JsonOptions);

    public static string EncodeImagePaths(IEnumerable<string> imagePaths) =>
        JsonSerializer.Serialize(NormalizeImagePaths(imagePaths), JsonOptions);

    public static IReadOnlyList<string> DecodeImagePaths(string imagePaths)
    {
        if (string.IsNullOrWhiteSpace(imagePaths))
        {
            return [];
        }

        try
        {
            return NormalizeImagePaths(JsonSerializer.Deserialize<string[]>(imagePaths, JsonOptions) ?? []);
        }
        catch (JsonException)
        {
            return NormalizeImagePaths(imagePaths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
    }

    private static T? Decode<T>(string itemData)
    {
        if (string.IsNullOrWhiteSpace(itemData))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(itemData, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static void MergeImagePaths(List<string> target, string fallbackImagePaths)
    {
        if (target.Count == 0)
        {
            target.AddRange(DecodeImagePaths(fallbackImagePaths));
        }
    }

    private static List<string> NormalizeImagePaths(IEnumerable<string> values) =>
        values
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
