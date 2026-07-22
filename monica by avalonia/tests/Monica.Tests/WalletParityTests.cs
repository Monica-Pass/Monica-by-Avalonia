using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.ImportExport;
using Monica.Core.Models;
using Monica.Data.Mdbx;

namespace Monica.Tests;

public sealed class WalletParityTests
{
    [Fact]
    public void Android_mdbx_codec_preserves_barcode_and_extended_wallet_types()
    {
        var barcode = new PasswordEntry
        {
            Id = 7,
            LoginType = PasswordLoginType.Barcode,
            Password = "line-one\nline-two"
        };
        var barcodeJson = AndroidMdbxPayloadCodec.EncodePassword(barcode, []);
        var decodedBarcode = AndroidMdbxPayloadCodec.DecodePassword(barcodeJson, "Recovery barcode");
        Assert.NotNull(decodedBarcode);
        Assert.Equal(PasswordLoginType.Barcode, decodedBarcode!.Entry.LoginType);
        Assert.Equal("line-one\nline-two", decodedBarcode.Entry.Password);

        var address = new SecureItem
        {
            Id = 8,
            ItemType = VaultItemType.BillingAddress,
            ItemData = WalletItemDataCodec.EncodeBillingAddress(new BillingAddressWalletData
            {
                StreetAddress = "12 Computing Lane"
            })
        };
        var payment = new SecureItem
        {
            Id = 9,
            ItemType = VaultItemType.PaymentAccount,
            ItemData = WalletItemDataCodec.EncodePaymentAccount(new PaymentAccountWalletData
            {
                Provider = "Monica Pay"
            })
        };

        var decodedAddress = AndroidMdbxPayloadCodec.DecodeSecureItem(
            AndroidMdbxPayloadCodec.EncodeSecureItem(address),
            "Home",
            "billing-address");
        var decodedPayment = AndroidMdbxPayloadCodec.DecodeSecureItem(
            AndroidMdbxPayloadCodec.EncodeSecureItem(payment),
            "Monica Pay",
            "payment-account");

        Assert.Equal(VaultItemType.BillingAddress, decodedAddress!.Item.ItemType);
        Assert.Equal(VaultItemType.PaymentAccount, decodedPayment!.Item.ItemType);
    }

    [Fact]
    public void Billing_address_codec_round_trips_android_fields()
    {
        var source = new BillingAddressWalletData
        {
            FullName = "Ada Lovelace",
            Company = "Analytical Engine",
            StreetAddress = "12 Computing Lane",
            Apartment = "Suite 4",
            City = "London",
            StateProvince = "Greater London",
            PostalCode = "SW1A 1AA",
            Country = "United Kingdom",
            Phone = "+44 20 0000 0000",
            Email = "ada@example.test",
            IsDefault = true,
            ImagePaths = ["front.png"]
        };
        var item = new SecureItem
        {
            ItemType = VaultItemType.BillingAddress,
            ItemData = WalletItemDataCodec.EncodeBillingAddress(source),
            ImagePaths = WalletItemDataCodec.EncodeImagePaths(source.ImagePaths)
        };

        var decoded = WalletItemDataCodec.DecodeBillingAddress(item);

        Assert.Equal(source.FullName, decoded.FullName);
        Assert.Equal(source.StreetAddress, decoded.StreetAddress);
        Assert.Equal(source.PostalCode, decoded.PostalCode);
        Assert.Equal(source.Email, decoded.Email);
        Assert.True(decoded.IsDefault);
        Assert.Equal(["front.png"], decoded.ImagePaths);
    }

    [Fact]
    public void Payment_account_codec_round_trips_android_fields()
    {
        var source = new PaymentAccountWalletData
        {
            PaymentType = "BANK_ACCOUNT",
            Provider = "Monica Bank",
            AccountName = "Daily account",
            AccountHolderName = "Ada Lovelace",
            AccountId = "account-12345678",
            RoutingNumber = "110000000",
            Iban = "GB82WEST12345698765432",
            SwiftBic = "MONIGB2L",
            Currency = "GBP",
            IsDefault = true,
            ImagePaths = ["statement.png"]
        };
        var item = new SecureItem
        {
            ItemType = VaultItemType.PaymentAccount,
            ItemData = WalletItemDataCodec.EncodePaymentAccount(source),
            ImagePaths = WalletItemDataCodec.EncodeImagePaths(source.ImagePaths)
        };

        var decoded = WalletItemDataCodec.DecodePaymentAccount(item);

        Assert.Equal(source.PaymentType, decoded.PaymentType);
        Assert.Equal(source.Provider, decoded.Provider);
        Assert.Equal(source.AccountId, decoded.AccountId);
        Assert.Equal(source.Iban, decoded.Iban);
        Assert.Equal(source.Currency, decoded.Currency);
        Assert.True(decoded.IsDefault);
        Assert.Equal(["statement.png"], decoded.ImagePaths);
    }

    [Fact]
    public void Wallet_editor_validates_and_applies_extended_types()
    {
        var localization = new LocalizationService();
        var addressEditor = new WalletItemEditorViewModel(localization, null, VaultItemType.BillingAddress);
        Assert.False(addressEditor.Validate());

        addressEditor.AddressFullName = "Ada Lovelace";
        addressEditor.StreetAddress = "12 Computing Lane";
        addressEditor.AddressCity = "London";
        Assert.True(addressEditor.Validate());
        var address = addressEditor.ApplyTo();
        Assert.Equal(VaultItemType.BillingAddress, address.ItemType);
        Assert.Equal("Ada Lovelace", address.Title);
        Assert.Equal("12 Computing Lane", WalletItemDataCodec.DecodeBillingAddress(address).StreetAddress);

        var paymentEditor = new WalletItemEditorViewModel(localization, null, VaultItemType.PaymentAccount);
        Assert.False(paymentEditor.Validate());

        paymentEditor.PaymentProvider = "Monica Pay";
        paymentEditor.PaymentAccountId = "account-12345678";
        paymentEditor.SelectedPaymentType = paymentEditor.PaymentTypeOptions.Single(option => option.Value == "PAYMENT_APP");
        Assert.True(paymentEditor.Validate());
        var payment = paymentEditor.ApplyTo();
        var paymentData = WalletItemDataCodec.DecodePaymentAccount(payment);
        Assert.Equal(VaultItemType.PaymentAccount, payment.ItemType);
        Assert.Equal("Monica Pay", payment.Title);
        Assert.Equal("PAYMENT_APP", paymentData.PaymentType);
        Assert.Equal("account-12345678", paymentData.AccountId);
    }

    [Fact]
    public void Payment_account_details_mask_financial_identifiers_by_default()
    {
        var localization = new LocalizationService();
        var details = new WalletItemDetailsViewModel(localization, new SecureItem
        {
            ItemType = VaultItemType.PaymentAccount,
            Title = "Monica Pay",
            ItemData = WalletItemDataCodec.EncodePaymentAccount(new PaymentAccountWalletData
            {
                Provider = "Monica Pay",
                AccountId = "account-12345678",
                Iban = "GB82WEST12345698765432"
            })
        });

        var accountId = details.Fields.Single(field => field.Label == localization.Get("AccountId"));
        var iban = details.Fields.Single(field => field.Label == localization.Get("Iban"));
        Assert.True(accountId.IsSensitive);
        Assert.NotEqual(accountId.Value, accountId.DisplayValue);
        Assert.True(iban.IsSensitive);
        Assert.NotEqual(iban.Value, iban.DisplayValue);
    }

    [Fact]
    public void Wallet_csv_and_json_preserve_extended_types_without_internal_image_paths()
    {
        var address = new SecureItem
        {
            Id = 21,
            ItemType = VaultItemType.BillingAddress,
            Title = "Home",
            ItemData = WalletItemDataCodec.EncodeBillingAddress(new BillingAddressWalletData
            {
                StreetAddress = "12 Computing Lane",
                ImagePaths = ["address.png", "mdbx:address-image"]
            }),
            ImagePaths = WalletItemDataCodec.EncodeImagePaths(["address.png", "mdbx:address-image"])
        };
        var payment = new SecureItem
        {
            Id = 22,
            ItemType = VaultItemType.PaymentAccount,
            Title = "Monica Pay",
            ItemData = WalletItemDataCodec.EncodePaymentAccount(new PaymentAccountWalletData
            {
                Provider = "Monica Pay",
                ImagePaths = ["payment.png", "mdbx:payment-image"]
            }),
            ImagePaths = WalletItemDataCodec.EncodeImagePaths(["payment.png", "mdbx:payment-image"])
        };
        var service = new ImportExportService();

        var csv = service.ExportWalletCsv([address, payment]);
        var json = service.ExportJson([], [address, payment]);
        var imported = service.ImportJson(json).SecureItems;

        Assert.Contains("BILLING_ADDRESS", csv, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_ACCOUNT", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("mdbx:", csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mdbx:", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(imported, item => item.ItemType == VaultItemType.BillingAddress);
        Assert.Contains(imported, item => item.ItemType == VaultItemType.PaymentAccount);
    }
}
