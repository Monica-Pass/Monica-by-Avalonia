using System.Globalization;
using CsvHelper;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.Core.ImportExport;

public sealed partial class ImportExportService
{
    private static readonly string[] PasswordCsvHeaders =
    [
        "title", "website", "username", "password", "notes", "authenticatorKey",
        "appName", "appPackageName", "email", "phone", "loginType", "ssoProvider",
        "passkeyBindings", "wifiMetadata", "sshKeyData"
    ];

    private static readonly string[] SecureItemCsvHeaders =
    [
        "ID", "Type", "Title", "Data", "Notes", "IsFavorite", "ImagePaths", "CreatedAt", "UpdatedAt"
    ];

    public string ExportPasswordCsv(IEnumerable<PasswordEntry> passwords)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        using var csv = new CsvWriter(writer, CreateCsvConfiguration());
        foreach (var header in PasswordCsvHeaders)
        {
            csv.WriteField(header);
        }

        csv.NextRecord();
        foreach (var password in passwords)
        {
            csv.WriteField(password.Title);
            csv.WriteField(password.Website);
            csv.WriteField(password.Username);
            csv.WriteField(password.Password);
            csv.WriteField(password.Notes);
            csv.WriteField(password.AuthenticatorKey);
            csv.WriteField(password.AppName);
            csv.WriteField(password.AppPackageName);
            csv.WriteField(password.Email);
            csv.WriteField(password.Phone);
            csv.WriteField(password.LoginType.ToString());
            csv.WriteField(password.SsoProvider);
            csv.WriteField(password.PasskeyBindings);
            csv.WriteField(password.WifiMetadata);
            csv.WriteField(password.SshKeyData);
            csv.NextRecord();
        }

        return writer.ToString();
    }

    public string ExportTotpCsv(IEnumerable<SecureItem> secureItems)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        using var csv = new CsvWriter(writer, CreateCsvConfiguration());
        WriteSecureItemHeaders(csv);
        foreach (var item in secureItems.Where(item => item.ItemType == VaultItemType.Totp))
        {
            var data = TotpDataResolver.ParseStoredItemData(item.ItemData, item.Title, item.Notes);
            if (data is null || string.IsNullOrWhiteSpace(data.Secret))
            {
                continue;
            }

            WriteSecureItemRow(csv, item, "TOTP", TotpDataResolver.ToItemData(data), "");
        }

        return writer.ToString();
    }

    public string ExportNoteCsv(IEnumerable<SecureItem> secureItems)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        using var csv = new CsvWriter(writer, CreateCsvConfiguration());
        WriteSecureItemHeaders(csv);
        foreach (var item in secureItems.Where(item => item.ItemType == VaultItemType.Note))
        {
            var decoded = NoteContentCodec.DecodeFromItem(item);
            var payload = NoteContentCodec.BuildSavePayload(
                item.Title,
                decoded.Content,
                string.Join(",", decoded.Tags),
                decoded.IsMarkdown,
                FilterPortableImagePaths(NoteContentCodec.DecodeImagePaths(item.ImagePaths)));
            WriteSecureItemRow(csv, item, "NOTE", payload.ItemData, payload.ImagePaths, payload.Title, payload.NotesCache);
        }

        return writer.ToString();
    }

    public string ExportWalletCsv(IEnumerable<SecureItem> secureItems)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        using var csv = new CsvWriter(writer, CreateCsvConfiguration());
        WriteSecureItemHeaders(csv);
        foreach (var item in secureItems.Where(item => item.ItemType is VaultItemType.BankCard or VaultItemType.Document or
                     VaultItemType.BillingAddress or VaultItemType.PaymentAccount))
        {
            var (type, data, imagePaths) = CreateWalletCsvPayload(item);
            WriteSecureItemRow(csv, item, type, data, imagePaths);
        }

        return writer.ToString();
    }

    private static void WriteSecureItemHeaders(CsvWriter csv)
    {
        foreach (var header in SecureItemCsvHeaders)
        {
            csv.WriteField(header);
        }

        csv.NextRecord();
    }

    private static void WriteSecureItemRow(
        CsvWriter csv,
        SecureItem item,
        string type,
        string data,
        string imagePaths,
        string? title = null,
        string? notes = null)
    {
        csv.WriteField(item.Id);
        csv.WriteField(type);
        csv.WriteField(title ?? item.Title);
        csv.WriteField(data);
        csv.WriteField(notes ?? item.Notes);
        csv.WriteField(item.IsFavorite.ToString());
        csv.WriteField(imagePaths);
        csv.WriteField(item.CreatedAt.ToUnixTimeMilliseconds());
        csv.WriteField(item.UpdatedAt.ToUnixTimeMilliseconds());
        csv.NextRecord();
    }

    private static (string Type, string Data, string ImagePaths) CreateWalletCsvPayload(SecureItem item)
    {
        if (item.ItemType == VaultItemType.BankCard)
        {
            var data = WalletItemDataCodec.DecodeBankCard(item);
            var imagePaths = WalletItemDataCodec.EncodeImagePaths(FilterPortableImagePaths(data.ImagePaths));
            data.ImagePaths.Clear();
            return ("BANK_CARD", WalletItemDataCodec.EncodeBankCard(data), imagePaths);
        }

        if (item.ItemType == VaultItemType.BillingAddress)
        {
            var address = WalletItemDataCodec.DecodeBillingAddress(item);
            var imagePaths = WalletItemDataCodec.EncodeImagePaths(FilterPortableImagePaths(address.ImagePaths));
            address.ImagePaths.Clear();
            return ("BILLING_ADDRESS", WalletItemDataCodec.EncodeBillingAddress(address), imagePaths);
        }

        if (item.ItemType == VaultItemType.PaymentAccount)
        {
            var payment = WalletItemDataCodec.DecodePaymentAccount(item);
            var imagePaths = WalletItemDataCodec.EncodeImagePaths(FilterPortableImagePaths(payment.ImagePaths));
            payment.ImagePaths.Clear();
            return ("PAYMENT_ACCOUNT", WalletItemDataCodec.EncodePaymentAccount(payment), imagePaths);
        }

        var document = WalletItemDataCodec.DecodeDocument(item);
        var documentImagePaths = WalletItemDataCodec.EncodeImagePaths(FilterPortableImagePaths(document.ImagePaths));
        document.ImagePaths.Clear();
        return ("DOCUMENT", WalletItemDataCodec.EncodeDocument(document), documentImagePaths);
    }

    private static IReadOnlyList<string> FilterPortableImagePaths(IEnumerable<string> imagePaths) =>
        imagePaths
            .Where(path => !path.StartsWith("mdbx:", StringComparison.OrdinalIgnoreCase))
            .ToList();
}
