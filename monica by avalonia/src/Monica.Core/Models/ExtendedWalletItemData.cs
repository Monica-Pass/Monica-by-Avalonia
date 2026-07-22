using System.Text.Json;
using System.Text.Json.Serialization;

namespace Monica.Core.Models;

public sealed class BillingAddressWalletData
{
    [JsonPropertyName("fullName")] public string FullName { get; set; } = "";
    [JsonPropertyName("company")] public string Company { get; set; } = "";
    [JsonPropertyName("streetAddress")] public string StreetAddress { get; set; } = "";
    [JsonPropertyName("apartment")] public string Apartment { get; set; } = "";
    [JsonPropertyName("city")] public string City { get; set; } = "";
    [JsonPropertyName("stateProvince")] public string StateProvince { get; set; } = "";
    [JsonPropertyName("postalCode")] public string PostalCode { get; set; } = "";
    [JsonPropertyName("country")] public string Country { get; set; } = "";
    [JsonPropertyName("phone")] public string Phone { get; set; } = "";
    [JsonPropertyName("email")] public string Email { get; set; } = "";
    [JsonPropertyName("isDefault")] public bool IsDefault { get; set; }
    [JsonPropertyName("imagePaths")] public List<string> ImagePaths { get; set; } = [];
}

public sealed class PaymentAccountWalletData
{
    [JsonPropertyName("paymentType")] public string PaymentType { get; set; } = "DIGITAL_WALLET";
    [JsonPropertyName("provider")] public string Provider { get; set; } = "";
    [JsonPropertyName("accountName")] public string AccountName { get; set; } = "";
    [JsonPropertyName("accountHolderName")] public string AccountHolderName { get; set; } = "";
    [JsonPropertyName("email")] public string Email { get; set; } = "";
    [JsonPropertyName("phone")] public string Phone { get; set; } = "";
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("accountId")] public string AccountId { get; set; } = "";
    [JsonPropertyName("maskedAccountNumber")] public string MaskedAccountNumber { get; set; } = "";
    [JsonPropertyName("linkedCardLast4")] public string LinkedCardLast4 { get; set; } = "";
    [JsonPropertyName("routingNumber")] public string RoutingNumber { get; set; } = "";
    [JsonPropertyName("iban")] public string Iban { get; set; } = "";
    [JsonPropertyName("swiftBic")] public string SwiftBic { get; set; } = "";
    [JsonPropertyName("billingAddress")] public string BillingAddress { get; set; } = "";
    [JsonPropertyName("website")] public string Website { get; set; } = "";
    [JsonPropertyName("currency")] public string Currency { get; set; } = "";
    [JsonPropertyName("isDefault")] public bool IsDefault { get; set; }
    [JsonPropertyName("imagePaths")] public List<string> ImagePaths { get; set; } = [];
}

public static partial class WalletItemDataCodec
{
    public static BillingAddressWalletData DecodeBillingAddress(SecureItem item)
    {
        var data = Deserialize<BillingAddressWalletData>(item.ItemData) ?? new BillingAddressWalletData();
        MergeExtendedImagePaths(data.ImagePaths, item.ImagePaths);
        return data;
    }

    public static PaymentAccountWalletData DecodePaymentAccount(SecureItem item)
    {
        var data = Deserialize<PaymentAccountWalletData>(item.ItemData) ?? new PaymentAccountWalletData();
        MergeExtendedImagePaths(data.ImagePaths, item.ImagePaths);
        return data;
    }

    public static string EncodeBillingAddress(BillingAddressWalletData data) => JsonSerializer.Serialize(data, JsonOptions);
    public static string EncodePaymentAccount(PaymentAccountWalletData data) => JsonSerializer.Serialize(data, JsonOptions);

    private static T? Deserialize<T>(string itemData)
    {
        if (string.IsNullOrWhiteSpace(itemData)) return default;
        try { return JsonSerializer.Deserialize<T>(itemData, JsonOptions); }
        catch (JsonException) { return default; }
    }

    private static void MergeExtendedImagePaths(List<string> target, string fallback)
    {
        if (target.Count == 0) target.AddRange(DecodeImagePaths(fallback));
    }
}
