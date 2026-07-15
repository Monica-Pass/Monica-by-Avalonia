using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class WalletFieldDisplayItem : ObservableObject
{
    public WalletFieldDisplayItem(string label, string value, bool isSensitive = false)
    {
        Label = label;
        Value = value;
        IsSensitive = isSensitive;
        MaskedValue = isSensitive ? MaskValue(value) : value;
    }

    public string Label { get; }
    public string Value { get; private set; }
    public bool IsSensitive { get; }
    public string MaskedValue { get; private set; }
    public string DisplayValue => IsSensitive && !IsRevealed ? MaskedValue : Value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayValue))]
    private bool _isRevealed;

    [RelayCommand]
    private void ToggleVisibility()
    {
        if (IsSensitive)
        {
            IsRevealed = !IsRevealed;
        }
    }

    internal void ClearSensitiveState()
    {
        Value = "";
        MaskedValue = "";
        IsRevealed = false;
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(MaskedValue));
        OnPropertyChanged(nameof(DisplayValue));
    }

    private static string MaskValue(string value)
    {
        var compact = new string(value.Where(char.IsLetterOrDigit).ToArray());
        if (compact.Length <= 4)
        {
            return new string('•', compact.Length);
        }

        return compact.All(char.IsDigit)
            ? MaskNumericValue(compact)
            : MaskDocumentValue(compact);
    }

    private static string MaskNumericValue(string value)
    {
        var masked = new string('•', value.Length - 4) + value[^4..];
        return string.Join(" ", masked.Chunk(4).Select(chunk => new string(chunk)));
    }

    private static string MaskDocumentValue(string value)
    {
        if (value.Length <= 7)
        {
            return $"{new string('•', value.Length - 2)}{value[^2..]}";
        }

        var prefixLength = Math.Min(3, value.Length - 4);
        var hiddenLength = value.Length - prefixLength - 4;
        return $"{value[..prefixLength]}{new string('•', hiddenLength)}{value[^4..]}";
    }
}

public sealed partial class WalletItemDetailsViewModel : ObservableObject, IDisposable
{
    public WalletItemDetailsViewModel(ILocalizationService localization, SecureItem item)
    {
        L = localization;
        Item = item;
        Title = item.Title;
        Notes = item.Notes;
        KindText = item.ItemType == VaultItemType.BankCard ? localization.Get("BankCard") : localization.Get("Document");
        Fields = item.ItemType == VaultItemType.BankCard
            ? BuildBankCardDetails(localization, item)
            : BuildDocumentDetails(localization, item);
        HasImages = ImagePaths.Count > 0;
        FrontImagePath = ImagePaths.Count > 0 ? ImagePaths[0] : "";
        BackImagePath = ImagePaths.Count > 1 ? ImagePaths[1] : "";
    }

    public ILocalizationService L { get; }
    public SecureItem Item { get; private set; }
    public string Title { get; private set; }
    public string KindText { get; private set; }
    public string PrimaryText { get; private set; } = "";
    public string SecondaryText { get; private set; } = "";
    public string ExpiryText { get; private set; } = "";
    public string Notes { get; private set; }
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    public IReadOnlyList<WalletFieldDisplayItem> Fields { get; private set; }
    public IReadOnlyList<string> ImagePaths { get; private set; } = [];
    public bool HasImages { get; private set; }
    public string FrontImagePath { get; private set; }
    public string BackImagePath { get; private set; }
    public bool IsSensitiveStateCleared { get; private set; }

    public void ClearSensitiveState()
    {
        if (IsSensitiveStateCleared)
        {
            return;
        }

        foreach (var field in Fields)
        {
            field.ClearSensitiveState();
        }

        Item = new SecureItem();
        Title = "";
        KindText = "";
        PrimaryText = "";
        SecondaryText = "";
        ExpiryText = "";
        Notes = "";
        Fields = [];
        ImagePaths = [];
        HasImages = false;
        FrontImagePath = "";
        BackImagePath = "";
        IsSensitiveStateCleared = true;
        OnPropertyChanged(nameof(Item));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(KindText));
        OnPropertyChanged(nameof(PrimaryText));
        OnPropertyChanged(nameof(SecondaryText));
        OnPropertyChanged(nameof(ExpiryText));
        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(HasNotes));
        OnPropertyChanged(nameof(Fields));
        OnPropertyChanged(nameof(ImagePaths));
        OnPropertyChanged(nameof(HasImages));
        OnPropertyChanged(nameof(FrontImagePath));
        OnPropertyChanged(nameof(BackImagePath));
        OnPropertyChanged(nameof(IsSensitiveStateCleared));
    }

    public void Dispose() => ClearSensitiveState();

    private IReadOnlyList<WalletFieldDisplayItem> BuildBankCardDetails(
        ILocalizationService localization,
        SecureItem item)
    {
        var data = WalletItemDataCodec.DecodeBankCard(item);
        PrimaryText = MaskBankCard(data.CardNumber);
        SecondaryText = string.IsNullOrWhiteSpace(data.CardholderName) ? data.BankName : data.CardholderName;
        ExpiryText = $"{data.ExpiryMonth}/{data.ExpiryYear}".Trim('/');
        ImagePaths = FilterDisplayImagePaths(data.ImagePaths);
        return RemoveEmptyFields(
        [
            new(localization.Get("CardNumber"), FormatCardNumber(data.CardNumber), true),
            new(localization.Get("CardholderName"), data.CardholderName),
            new(localization.Get("Expiry"), ExpiryText),
            new(localization.CreditCardCvv, data.Cvv, true),
            new(localization.Get("BankName"), data.BankName),
            new(localization.Get("BillingAddress"), data.BillingAddress)
        ]);
    }

    private IReadOnlyList<WalletFieldDisplayItem> BuildDocumentDetails(
        ILocalizationService localization,
        SecureItem item)
    {
        var data = WalletItemDataCodec.DecodeDocument(item);
        PrimaryText = MaskDocumentNumber(data.DocumentNumber);
        SecondaryText = string.IsNullOrWhiteSpace(data.FullName) ? data.IssuedBy : data.FullName;
        ExpiryText = data.ExpiryDate;
        ImagePaths = FilterDisplayImagePaths(data.ImagePaths);
        return RemoveEmptyFields(
        [
            new(localization.Get("DocumentNumber"), data.DocumentNumber, true),
            new(localization.Get("FullName"), data.FullName),
            new(localization.Get("IssuedDate"), data.IssuedDate),
            new(localization.Get("ExpiryDate"), data.ExpiryDate),
            new(localization.Get("IssuedBy"), data.IssuedBy),
            new(localization.Get("Nationality"), data.Nationality),
            new(localization.Get("AdditionalInfo"), data.AdditionalInfo)
        ]);
    }

    private static IReadOnlyList<WalletFieldDisplayItem> RemoveEmptyFields(
        IEnumerable<WalletFieldDisplayItem> fields) =>
        fields.Where(field => !string.IsNullOrWhiteSpace(field.Value)).ToArray();

    private static IReadOnlyList<string> FilterDisplayImagePaths(IEnumerable<string> imagePaths) =>
        imagePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(path => !path.StartsWith("mdbx:", StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private static string MaskBankCard(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? $"**** **** **** {digits[^4..]}" : value;
    }

    private static string FormatCardNumber(string value)
    {
        var compact = new string(value.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrWhiteSpace(compact)
            ? value
            : string.Join(" ", compact.Chunk(4).Select(chunk => new string(chunk)));
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
