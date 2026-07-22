using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class WalletItemDetailsViewModel
{
    private IReadOnlyList<WalletFieldDisplayItem> BuildBillingAddressDetails(
        ILocalizationService localization,
        SecureItem item)
    {
        var data = WalletItemDataCodec.DecodeBillingAddress(item);
        PrimaryText = data.FullName;
        SecondaryText = string.Join(", ", new[] { data.City, data.Country }.Where(value => !string.IsNullOrWhiteSpace(value)));
        ImagePaths = FilterDisplayImagePaths(data.ImagePaths);
        return RemoveEmptyFields(
        [
            new(localization.Get("FullName"), data.FullName),
            new(localization.Get("Company"), data.Company),
            new(localization.Get("StreetAddress"), data.StreetAddress),
            new(localization.Get("Apartment"), data.Apartment),
            new(localization.Get("City"), data.City),
            new(localization.Get("StateProvince"), data.StateProvince),
            new(localization.Get("PostalCode"), data.PostalCode),
            new(localization.Get("Country"), data.Country),
            new(localization.Get("Phone"), data.Phone),
            new(localization.Get("Email"), data.Email)
        ]);
    }

    private IReadOnlyList<WalletFieldDisplayItem> BuildPaymentAccountDetails(
        ILocalizationService localization,
        SecureItem item)
    {
        var data = WalletItemDataCodec.DecodePaymentAccount(item);
        PrimaryText = data.Provider;
        SecondaryText = data.AccountName;
        ImagePaths = FilterDisplayImagePaths(data.ImagePaths);
        return RemoveEmptyFields(
        [
            new(localization.Get("PaymentProvider"), data.Provider),
            new(localization.Get("AccountName"), data.AccountName),
            new(localization.Get("AccountHolderName"), data.AccountHolderName),
            new(localization.Get("Email"), data.Email),
            new(localization.Get("Phone"), data.Phone),
            new(localization.Get("Username"), data.Username),
            new(localization.Get("AccountId"), data.AccountId, true),
            new(localization.Get("MaskedAccountNumber"), data.MaskedAccountNumber, true),
            new(localization.Get("LinkedCardLast4"), data.LinkedCardLast4, true),
            new(localization.Get("RoutingNumber"), data.RoutingNumber, true),
            new(localization.Get("Iban"), data.Iban, true),
            new(localization.Get("SwiftBic"), data.SwiftBic, true),
            new(localization.Get("BillingAddress"), data.BillingAddress),
            new(localization.Get("Website"), data.Website),
            new(localization.Get("Currency"), data.Currency)
        ]);
    }
}
