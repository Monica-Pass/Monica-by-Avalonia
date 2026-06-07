using System.Globalization;
using Monica.App.Services;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.App.ViewModels;

public sealed class TotpItemDetailsViewModel
{
    public TotpItemDetailsViewModel(ILocalizationService localization, SecureItem item)
    {
        Item = item;
        Title = string.IsNullOrWhiteSpace(item.Title) ? localization.Get("Untitled") : item.Title;
        Notes = item.Notes;
        IsFavorite = item.IsFavorite;
        IsBoundToPassword = item.BoundPasswordId is not null;
        var data = TotpDataResolver.ParseStoredItemData(item.ItemData, item.Title, item.Notes);
        Issuer = data?.Issuer ?? "";
        Account = data?.AccountName ?? item.Notes;
        OtpType = data?.OtpType ?? "TOTP";
        PeriodText = data is null ? "" : $"{data.Period}s";
        DigitsText = data is null ? "" : data.Digits.ToString(CultureInfo.InvariantCulture);
        Algorithm = data?.Algorithm ?? "";
        CreatedAtText = item.CreatedAt.ToLocalTime().ToString("g", localization.Culture);
        UpdatedAtText = item.UpdatedAt.ToLocalTime().ToString("g", localization.Culture);

        Fields =
        [
            new(localization.Issuer, Issuer),
            new(localization.Account, Account),
            new("OTP", OtpType),
            new("Period", PeriodText),
            new("Digits", DigitsText),
            new("Algorithm", Algorithm),
            new(localization.CreatedAt, CreatedAtText),
            new(localization.UpdatedAt, UpdatedAtText)
        ];
        Fields = Fields.Where(field => !string.IsNullOrWhiteSpace(field.Value)).ToArray();
    }

    public SecureItem Item { get; }
    public string Title { get; }
    public string Notes { get; }
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    public bool IsFavorite { get; }
    public bool IsBoundToPassword { get; }
    public string Issuer { get; }
    public string Account { get; }
    public string OtpType { get; }
    public string PeriodText { get; }
    public string DigitsText { get; }
    public string Algorithm { get; }
    public string CreatedAtText { get; }
    public string UpdatedAtText { get; }
    public IReadOnlyList<WalletFieldDisplayItem> Fields { get; private set; }
}
