using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed record WalletChoice(string Value, string Label);
public sealed record WalletFieldDisplayItem(string Label, string Value, bool IsSensitive = false);

public sealed partial class WalletItemEditorViewModel : ObservableObject
{
    private readonly SecureItem? _source;

    public WalletItemEditorViewModel(ILocalizationService localization, SecureItem? source, VaultItemType? newItemType = null)
    {
        L = localization;
        _source = source;
        ItemType = source?.ItemType ?? newItemType ?? VaultItemType.Document;

        WalletTypeOptions.Add(new(VaultItemType.Document, localization.Get("Document")));
        WalletTypeOptions.Add(new(VaultItemType.BankCard, localization.Get("BankCard")));
        DocumentTypeOptions.Add(new("ID_CARD", localization.Get("DocumentTypeIdCard")));
        DocumentTypeOptions.Add(new("PASSPORT", localization.Get("DocumentTypePassport")));
        DocumentTypeOptions.Add(new("DRIVER_LICENSE", localization.Get("DocumentTypeDriverLicense")));
        DocumentTypeOptions.Add(new("SOCIAL_SECURITY", localization.Get("DocumentTypeSocialSecurity")));
        DocumentTypeOptions.Add(new("OTHER", localization.Get("DocumentTypeOther")));
        CardTypeOptions.Add(new("DEBIT", localization.Get("CardTypeDebit")));
        CardTypeOptions.Add(new("CREDIT", localization.Get("CardTypeCredit")));
        CardTypeOptions.Add(new("PREPAID", localization.Get("CardTypePrepaid")));

        SelectedWalletType = WalletTypeOptions.First(item => item.Value == ItemType);
        if (source is null)
        {
            SelectedDocumentType = DocumentTypeOptions[0];
            SelectedCardType = CardTypeOptions[0];
            return;
        }

        Title = source.Title;
        Notes = source.Notes;
        IsFavorite = source.IsFavorite;
        if (source.ItemType == VaultItemType.BankCard)
        {
            var data = WalletItemDataCodec.DecodeBankCard(source);
            CardNumber = data.CardNumber;
            CardholderName = data.CardholderName;
            ExpiryMonth = data.ExpiryMonth;
            ExpiryYear = data.ExpiryYear;
            Cvv = data.Cvv;
            BankName = data.BankName;
            Brand = data.Brand;
            BillingAddress = data.BillingAddress;
            ImagePathsText = string.Join(Environment.NewLine, data.ImagePaths);
            SelectedCardType = CardTypeOptions.FirstOrDefault(item => item.Value == data.CardTypeString) ?? CardTypeOptions[0];
            SelectedDocumentType = DocumentTypeOptions[0];
        }
        else
        {
            var data = WalletItemDataCodec.DecodeDocument(source);
            DocumentNumber = data.DocumentNumber;
            FullName = data.FullName;
            IssuedDate = data.IssuedDate;
            ExpiryDate = data.ExpiryDate;
            IssuedBy = data.IssuedBy;
            Nationality = data.Nationality;
            AdditionalInfo = data.AdditionalInfo;
            ImagePathsText = string.Join(Environment.NewLine, data.ImagePaths);
            SelectedDocumentType = DocumentTypeOptions.FirstOrDefault(item => item.Value == data.DocumentTypeString) ?? DocumentTypeOptions[0];
            SelectedCardType = CardTypeOptions[0];
        }
    }

    public ILocalizationService L { get; }
    public ObservableCollection<WalletTypeChoice> WalletTypeOptions { get; } = [];
    public ObservableCollection<WalletChoice> DocumentTypeOptions { get; } = [];
    public ObservableCollection<WalletChoice> CardTypeOptions { get; } = [];
    public string DialogTitle => _source is null ? L.Get("AddWalletItem") : L.Get("EditWalletItem");
    public bool IsDocument => SelectedWalletType.Value == VaultItemType.Document;
    public bool IsBankCard => SelectedWalletType.Value == VaultItemType.BankCard;
    public VaultItemType ItemType { get; private set; }

    [ObservableProperty]
    private WalletTypeChoice _selectedWalletType = new(VaultItemType.Document, "");

    [ObservableProperty]
    private WalletChoice _selectedDocumentType = new("ID_CARD", "");

    [ObservableProperty]
    private WalletChoice _selectedCardType = new("DEBIT", "");

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _notes = "";

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private string _documentNumber = "";

    [ObservableProperty]
    private string _fullName = "";

    [ObservableProperty]
    private string _issuedDate = "";

    [ObservableProperty]
    private string _expiryDate = "";

    [ObservableProperty]
    private string _issuedBy = "";

    [ObservableProperty]
    private string _nationality = "";

    [ObservableProperty]
    private string _additionalInfo = "";

    [ObservableProperty]
    private string _cardNumber = "";

    [ObservableProperty]
    private string _cardholderName = "";

    [ObservableProperty]
    private string _expiryMonth = "";

    [ObservableProperty]
    private string _expiryYear = "";

    [ObservableProperty]
    private string _cvv = "";

    [ObservableProperty]
    private string _bankName = "";

    [ObservableProperty]
    private string _brand = "";

    [ObservableProperty]
    private string _billingAddress = "";

    [ObservableProperty]
    private string _imagePathsText = "";

    [ObservableProperty]
    private string _validationMessage = "";

    partial void OnSelectedWalletTypeChanged(WalletTypeChoice value)
    {
        ItemType = value.Value;
        OnPropertyChanged(nameof(IsDocument));
        OnPropertyChanged(nameof(IsBankCard));
    }

    public bool Validate()
    {
        if (IsDocument && string.IsNullOrWhiteSpace(DocumentNumber))
        {
            ValidationMessage = L.Get("DocumentNumberRequired");
            return false;
        }

        if (IsBankCard && string.IsNullOrWhiteSpace(CardNumber))
        {
            ValidationMessage = L.Get("CardNumberRequired");
            return false;
        }

        ValidationMessage = "";
        return true;
    }

    public SecureItem ApplyTo(SecureItem? target = null)
    {
        var item = target ?? _source ?? new SecureItem
        {
            CreatedAt = DateTimeOffset.UtcNow
        };
        item.ItemType = ItemType;
        item.Title = ResolveTitle();
        item.Notes = Notes.Trim();
        item.IsFavorite = IsFavorite;
        item.IsDeleted = false;
        item.DeletedAt = null;
        item.SyncStatus = item.BitwardenVaultId is null ? SyncStatus.None : SyncStatus.Pending;

        if (ItemType == VaultItemType.BankCard)
        {
            var data = new BankCardWalletData
            {
                CardNumber = CardNumber.Trim(),
                CardholderName = CardholderName.Trim(),
                ExpiryMonth = ExpiryMonth.Trim(),
                ExpiryYear = ExpiryYear.Trim(),
                Cvv = Cvv.Trim(),
                BankName = BankName.Trim(),
                CardTypeString = SelectedCardType.Value,
                BillingAddress = BillingAddress.Trim(),
                Brand = Brand.Trim(),
                ImagePaths = GetImagePaths().ToList()
            };
            item.ItemData = WalletItemDataCodec.EncodeBankCard(data);
            item.ImagePaths = WalletItemDataCodec.EncodeImagePaths(data.ImagePaths);
        }
        else
        {
            var data = new DocumentWalletData
            {
                DocumentNumber = DocumentNumber.Trim(),
                FullName = FullName.Trim(),
                IssuedDate = IssuedDate.Trim(),
                ExpiryDate = ExpiryDate.Trim(),
                IssuedBy = IssuedBy.Trim(),
                Nationality = Nationality.Trim(),
                DocumentTypeString = SelectedDocumentType.Value,
                AdditionalInfo = AdditionalInfo.Trim(),
                ImagePaths = GetImagePaths().ToList()
            };
            item.ItemData = WalletItemDataCodec.EncodeDocument(data);
            item.ImagePaths = WalletItemDataCodec.EncodeImagePaths(data.ImagePaths);
        }

        return item;
    }

    private string ResolveTitle()
    {
        if (!string.IsNullOrWhiteSpace(Title))
        {
            return Title.Trim();
        }

        return ItemType == VaultItemType.BankCard
            ? string.IsNullOrWhiteSpace(BankName) ? L.Get("BankCard") : BankName.Trim()
            : SelectedDocumentType.Label;
    }

    private IReadOnlyList<string> GetImagePaths() =>
        ImagePathsText.Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed record WalletTypeChoice(VaultItemType Value, string Label);

public sealed partial class WalletItemDetailsViewModel : ObservableObject
{
    public WalletItemDetailsViewModel(ILocalizationService localization, SecureItem item)
    {
        L = localization;
        Item = item;
        Title = item.Title;
        Notes = item.Notes;
        KindText = item.ItemType == VaultItemType.BankCard ? localization.Get("BankCard") : localization.Get("Document");
        if (item.ItemType == VaultItemType.BankCard)
        {
            var data = WalletItemDataCodec.DecodeBankCard(item);
            PrimaryText = MaskBankCard(data.CardNumber);
            SecondaryText = string.IsNullOrWhiteSpace(data.CardholderName) ? data.BankName : data.CardholderName;
            ExpiryText = $"{data.ExpiryMonth}/{data.ExpiryYear}".Trim('/');
            ImagePaths = data.ImagePaths;
            Fields =
            [
                new(localization.Get("CardNumber"), FormatCardNumber(data.CardNumber), true),
                new(localization.Get("CardholderName"), data.CardholderName),
                new(localization.Get("Expiry"), ExpiryText),
                new("CVV", data.Cvv, true),
                new(localization.Get("BankName"), data.BankName),
                new(localization.Get("BillingAddress"), data.BillingAddress)
            ];
        }
        else
        {
            var data = WalletItemDataCodec.DecodeDocument(item);
            PrimaryText = MaskDocumentNumber(data.DocumentNumber);
            SecondaryText = string.IsNullOrWhiteSpace(data.FullName) ? data.IssuedBy : data.FullName;
            ExpiryText = data.ExpiryDate;
            ImagePaths = data.ImagePaths;
            Fields =
            [
                new(localization.Get("DocumentNumber"), data.DocumentNumber, true),
                new(localization.Get("FullName"), data.FullName),
                new(localization.Get("IssuedDate"), data.IssuedDate),
                new(localization.Get("ExpiryDate"), data.ExpiryDate),
                new(localization.Get("IssuedBy"), data.IssuedBy),
                new(localization.Get("Nationality"), data.Nationality),
                new(localization.Get("AdditionalInfo"), data.AdditionalInfo)
            ];
        }

        Fields = Fields.Where(item => !string.IsNullOrWhiteSpace(item.Value)).ToArray();
        HasImages = ImagePaths.Count > 0;
        FrontImagePath = ImagePaths.Count > 0 ? ImagePaths[0] : "";
        BackImagePath = ImagePaths.Count > 1 ? ImagePaths[1] : "";
    }

    public ILocalizationService L { get; }
    public SecureItem Item { get; }
    public string Title { get; }
    public string KindText { get; }
    public string PrimaryText { get; }
    public string SecondaryText { get; }
    public string ExpiryText { get; }
    public string Notes { get; }
    public IReadOnlyList<WalletFieldDisplayItem> Fields { get; private set; } = [];
    public IReadOnlyList<string> ImagePaths { get; }
    public bool HasImages { get; }
    public string FrontImagePath { get; }
    public string BackImagePath { get; }

    private static string MaskBankCard(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? $"**** **** **** {digits[^4..]}" : value;
    }

    private static string FormatCardNumber(string value)
    {
        var compact = new string(value.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(compact))
        {
            return value;
        }

        return string.Join(" ", compact.Chunk(4).Select(chunk => new string(chunk)));
    }

    private static string MaskDocumentNumber(string value)
    {
        if (value.Length <= 6)
        {
            return value;
        }

        var start = value[..Math.Min(3, value.Length)];
        var end = value[^Math.Min(4, value.Length)..];
        return $"{start}{new string('*', Math.Max(3, value.Length - start.Length - end.Length))}{end}";
    }
}
