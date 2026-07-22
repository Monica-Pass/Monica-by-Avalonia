using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class WalletItemEditorViewModel
{
    public ObservableCollection<WalletChoice> PaymentTypeOptions { get; } = [];

    [ObservableProperty] private string _addressFullName = "";
    [ObservableProperty] private string _addressCompany = "";
    [ObservableProperty] private string _streetAddress = "";
    [ObservableProperty] private string _addressApartment = "";
    [ObservableProperty] private string _addressCity = "";
    [ObservableProperty] private string _addressStateProvince = "";
    [ObservableProperty] private string _addressPostalCode = "";
    [ObservableProperty] private string _addressCountry = "";
    [ObservableProperty] private string _addressPhone = "";
    [ObservableProperty] private string _addressEmail = "";
    [ObservableProperty] private bool _addressIsDefault;

    [ObservableProperty] private WalletChoice _selectedPaymentType = new("DIGITAL_WALLET", "");
    [ObservableProperty] private string _paymentProvider = "";
    [ObservableProperty] private string _paymentAccountName = "";
    [ObservableProperty] private string _paymentAccountHolderName = "";
    [ObservableProperty] private string _paymentEmail = "";
    [ObservableProperty] private string _paymentPhone = "";
    [ObservableProperty] private string _paymentUsername = "";
    [ObservableProperty] private string _paymentAccountId = "";
    [ObservableProperty] private string _paymentMaskedAccountNumber = "";
    [ObservableProperty] private string _paymentLinkedCardLast4 = "";
    [ObservableProperty] private string _paymentRoutingNumber = "";
    [ObservableProperty] private string _paymentIban = "";
    [ObservableProperty] private string _paymentSwiftBic = "";
    [ObservableProperty] private string _paymentBillingAddress = "";
    [ObservableProperty] private string _paymentWebsite = "";
    [ObservableProperty] private string _paymentCurrency = "";
    [ObservableProperty] private bool _paymentIsDefault;

    private void BuildExtendedWalletOptions(ILocalizationService localization)
    {
        PaymentTypeOptions.Add(new("DIGITAL_WALLET", localization.Get("PaymentTypeDigitalWallet")));
        PaymentTypeOptions.Add(new("BANK_ACCOUNT", localization.Get("PaymentTypeBankAccount")));
        PaymentTypeOptions.Add(new("PAYMENT_APP", localization.Get("PaymentTypePaymentApp")));
        PaymentTypeOptions.Add(new("BUY_NOW_PAY_LATER", localization.Get("PaymentTypeBuyNowPayLater")));
        PaymentTypeOptions.Add(new("CRYPTO_WALLET", localization.Get("PaymentTypeCryptoWallet")));
        PaymentTypeOptions.Add(new("OTHER", localization.Get("DocumentTypeOther")));
        SelectedPaymentType = PaymentTypeOptions[0];
    }

    private void LoadExtendedWalletSource(SecureItem source)
    {
        if (source.ItemType == VaultItemType.BillingAddress)
        {
            var data = WalletItemDataCodec.DecodeBillingAddress(source);
            AddressFullName = data.FullName;
            AddressCompany = data.Company;
            StreetAddress = data.StreetAddress;
            AddressApartment = data.Apartment;
            AddressCity = data.City;
            AddressStateProvince = data.StateProvince;
            AddressPostalCode = data.PostalCode;
            AddressCountry = data.Country;
            AddressPhone = data.Phone;
            AddressEmail = data.Email;
            AddressIsDefault = data.IsDefault;
            LoadExtendedImagePaths(data.ImagePaths);
            return;
        }

        var payment = WalletItemDataCodec.DecodePaymentAccount(source);
        SelectedPaymentType = PaymentTypeOptions.FirstOrDefault(item => item.Value == payment.PaymentType) ?? PaymentTypeOptions[0];
        PaymentProvider = payment.Provider;
        PaymentAccountName = payment.AccountName;
        PaymentAccountHolderName = payment.AccountHolderName;
        PaymentEmail = payment.Email;
        PaymentPhone = payment.Phone;
        PaymentUsername = payment.Username;
        PaymentAccountId = payment.AccountId;
        PaymentMaskedAccountNumber = payment.MaskedAccountNumber;
        PaymentLinkedCardLast4 = payment.LinkedCardLast4;
        PaymentRoutingNumber = payment.RoutingNumber;
        PaymentIban = payment.Iban;
        PaymentSwiftBic = payment.SwiftBic;
        PaymentBillingAddress = payment.BillingAddress;
        PaymentWebsite = payment.Website;
        PaymentCurrency = payment.Currency;
        PaymentIsDefault = payment.IsDefault;
        LoadExtendedImagePaths(payment.ImagePaths);
    }

    private void LoadExtendedImagePaths(IEnumerable<string> paths)
    {
        _hiddenMdbxImagePaths = GetMdbxImagePaths(paths);
        ImagePathsText = string.Join(Environment.NewLine, FilterEditableImagePaths(paths));
    }

    private void ApplyExtendedWalletData(SecureItem item)
    {
        if (ItemType == VaultItemType.BillingAddress)
        {
            var data = new BillingAddressWalletData
            {
                FullName = AddressFullName.Trim(),
                Company = AddressCompany.Trim(),
                StreetAddress = StreetAddress.Trim(),
                Apartment = AddressApartment.Trim(),
                City = AddressCity.Trim(),
                StateProvince = AddressStateProvince.Trim(),
                PostalCode = AddressPostalCode.Trim(),
                Country = AddressCountry.Trim(),
                Phone = AddressPhone.Trim(),
                Email = AddressEmail.Trim(),
                IsDefault = AddressIsDefault,
                ImagePaths = GetImagePaths().ToList()
            };
            item.ItemData = WalletItemDataCodec.EncodeBillingAddress(data);
            item.ImagePaths = WalletItemDataCodec.EncodeImagePaths(data.ImagePaths);
            return;
        }

        var payment = new PaymentAccountWalletData
        {
            PaymentType = SelectedPaymentType.Value,
            Provider = PaymentProvider.Trim(),
            AccountName = PaymentAccountName.Trim(),
            AccountHolderName = PaymentAccountHolderName.Trim(),
            Email = PaymentEmail.Trim(),
            Phone = PaymentPhone.Trim(),
            Username = PaymentUsername.Trim(),
            AccountId = PaymentAccountId.Trim(),
            MaskedAccountNumber = PaymentMaskedAccountNumber.Trim(),
            LinkedCardLast4 = PaymentLinkedCardLast4.Trim(),
            RoutingNumber = PaymentRoutingNumber.Trim(),
            Iban = PaymentIban.Trim(),
            SwiftBic = PaymentSwiftBic.Trim(),
            BillingAddress = PaymentBillingAddress.Trim(),
            Website = PaymentWebsite.Trim(),
            Currency = PaymentCurrency.Trim(),
            IsDefault = PaymentIsDefault,
            ImagePaths = GetImagePaths().ToList()
        };
        item.ItemData = WalletItemDataCodec.EncodePaymentAccount(payment);
        item.ImagePaths = WalletItemDataCodec.EncodeImagePaths(payment.ImagePaths);
    }
}
