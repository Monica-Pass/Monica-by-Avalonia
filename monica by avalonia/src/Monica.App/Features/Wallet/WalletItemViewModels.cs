using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed record WalletChoice(string Value, string Label);

public sealed partial class WalletItemEditorViewModel : ObservableObject
{
    private readonly SecureItem? _source;
    private readonly bool _canEditCategory;
    private IReadOnlyList<string> _hiddenMdbxImagePaths = [];

    public WalletItemEditorViewModel(
        ILocalizationService localization,
        SecureItem? source,
        VaultItemType? newItemType = null,
        IEnumerable<Category>? categories = null)
    {
        L = localization;
        _source = source;
        _canEditCategory = categories is not null;
        ItemType = source?.ItemType ?? newItemType ?? VaultItemType.Document;

        WalletTypeOptions.Add(new(VaultItemType.Document, localization.Get("Document")));
        WalletTypeOptions.Add(new(VaultItemType.BankCard, localization.Get("BankCard")));
        WalletTypeOptions.Add(new(VaultItemType.BillingAddress, localization.Get("BillingAddress")));
        WalletTypeOptions.Add(new(VaultItemType.PaymentAccount, localization.Get("PaymentAccount")));
        DocumentTypeOptions.Add(new("ID_CARD", localization.Get("DocumentTypeIdCard")));
        DocumentTypeOptions.Add(new("PASSPORT", localization.Get("DocumentTypePassport")));
        DocumentTypeOptions.Add(new("DRIVER_LICENSE", localization.Get("DocumentTypeDriverLicense")));
        DocumentTypeOptions.Add(new("SOCIAL_SECURITY", localization.Get("DocumentTypeSocialSecurity")));
        DocumentTypeOptions.Add(new("OTHER", localization.Get("DocumentTypeOther")));
        CardTypeOptions.Add(new("DEBIT", localization.Get("CardTypeDebit")));
        CardTypeOptions.Add(new("CREDIT", localization.Get("CardTypeCredit")));
        CardTypeOptions.Add(new("PREPAID", localization.Get("CardTypePrepaid")));
        BuildExtendedWalletOptions(localization);
        foreach (var category in PasswordCategoryChoice.BuildOptions(categories ?? [], localization.Get("NoFolder")))
        {
            CategoryOptions.Add(category);
        }

        SelectedCategory = CategoryOptions.FirstOrDefault(option => option.Id == source?.CategoryId) ?? CategoryOptions[0];

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
        if (source.ItemType is VaultItemType.BillingAddress or VaultItemType.PaymentAccount)
        {
            LoadExtendedWalletSource(source);
            SelectedCardType = CardTypeOptions[0];
            SelectedDocumentType = DocumentTypeOptions[0];
            return;
        }

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
            _hiddenMdbxImagePaths = GetMdbxImagePaths(data.ImagePaths);
            ImagePathsText = string.Join(Environment.NewLine, FilterEditableImagePaths(data.ImagePaths));
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
            _hiddenMdbxImagePaths = GetMdbxImagePaths(data.ImagePaths);
            ImagePathsText = string.Join(Environment.NewLine, FilterEditableImagePaths(data.ImagePaths));
            SelectedDocumentType = DocumentTypeOptions.FirstOrDefault(item => item.Value == data.DocumentTypeString) ?? DocumentTypeOptions[0];
            SelectedCardType = CardTypeOptions[0];
        }
    }

    public ILocalizationService L { get; }
    public ObservableCollection<WalletTypeChoice> WalletTypeOptions { get; } = [];
    public ObservableCollection<WalletChoice> DocumentTypeOptions { get; } = [];
    public ObservableCollection<WalletChoice> CardTypeOptions { get; } = [];
    public ObservableCollection<PasswordCategoryChoice> CategoryOptions { get; } = [];
    public string DialogTitle => _source is null ? L.Get("AddWalletItem") : L.Get("EditWalletItem");
    public bool IsDocument => SelectedWalletType.Value == VaultItemType.Document;
    public bool IsBankCard => SelectedWalletType.Value == VaultItemType.BankCard;
    public bool IsBillingAddress => SelectedWalletType.Value == VaultItemType.BillingAddress;
    public bool IsPaymentAccount => SelectedWalletType.Value == VaultItemType.PaymentAccount;
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
    private PasswordCategoryChoice? _selectedCategory;

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
        OnPropertyChanged(nameof(IsBillingAddress));
        OnPropertyChanged(nameof(IsPaymentAccount));
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

        if (IsBillingAddress && string.IsNullOrWhiteSpace(StreetAddress))
        {
            ValidationMessage = L.Get("StreetAddressRequired");
            return false;
        }

        if (IsPaymentAccount && string.IsNullOrWhiteSpace(PaymentProvider))
        {
            ValidationMessage = L.Get("PaymentProviderRequired");
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
        if (_canEditCategory)
        {
            item.CategoryId = SelectedCategory?.Id;
        }

        item.IsDeleted = false;
        item.DeletedAt = null;
        item.SyncStatus = item.BitwardenVaultId is null ? SyncStatus.None : SyncStatus.Pending;

        if (ItemType is VaultItemType.BillingAddress or VaultItemType.PaymentAccount)
        {
            ApplyExtendedWalletData(item);
        }
        else if (ItemType == VaultItemType.BankCard)
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

}

public sealed record WalletTypeChoice(VaultItemType Value, string Label);
