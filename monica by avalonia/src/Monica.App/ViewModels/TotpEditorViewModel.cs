using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.App.Services;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.App.ViewModels;

public sealed record TotpChoice(string Value, string Label);

public sealed partial class TotpEditorViewModel : ObservableObject
{
    private readonly SecureItem? _source;

    public TotpEditorViewModel(ILocalizationService localization, SecureItem? source)
    {
        L = localization;
        _source = source;

        OtpTypeOptions.Add(new("TOTP", localization.Get("TotpTypeTotp")));
        OtpTypeOptions.Add(new("HOTP", localization.Get("TotpTypeHotp")));
        OtpTypeOptions.Add(new("STEAM", localization.Get("TotpTypeSteam")));
        AlgorithmOptions.Add(new("SHA1", "SHA1"));
        AlgorithmOptions.Add(new("SHA256", "SHA256"));
        AlgorithmOptions.Add(new("SHA512", "SHA512"));

        Title = source?.Title ?? "";
        Notes = source?.Notes ?? "";
        IsFavorite = source?.IsFavorite ?? false;

        var data = source is null
            ? new TotpData("")
            : TotpDataResolver.ParseStoredItemData(source.ItemData, source.Title, source.Notes) ?? new TotpData("");

        Secret = data.Secret;
        Issuer = data.Issuer;
        AccountName = data.AccountName;
        Period = data.Period <= 0 ? 30 : data.Period;
        Digits = data.Digits <= 0 ? 6 : data.Digits;
        Counter = Math.Max(0, data.Counter);
        SelectedOtpType = OtpTypeOptions.FirstOrDefault(item => item.Value == data.OtpType) ?? OtpTypeOptions[0];
        SelectedAlgorithm = AlgorithmOptions.FirstOrDefault(item => item.Value == data.Algorithm) ?? AlgorithmOptions[0];
    }

    public ILocalizationService L { get; }
    public ObservableCollection<TotpChoice> OtpTypeOptions { get; } = [];
    public ObservableCollection<TotpChoice> AlgorithmOptions { get; } = [];
    public string DialogTitle => _source is null ? L.Get("AddAuthenticator") : L.Get("EditAuthenticator");
    public bool IsTimeBased => SelectedOtpType.Value != "HOTP";
    public bool IsCounterBased => SelectedOtpType.Value == "HOTP";
    public bool UsesDigits => SelectedOtpType.Value != "STEAM";

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _secret = "";

    [ObservableProperty]
    private string _issuer = "";

    [ObservableProperty]
    private string _accountName = "";

    [ObservableProperty]
    private string _notes = "";

    [ObservableProperty]
    private TotpChoice _selectedOtpType = new("TOTP", "TOTP");

    [ObservableProperty]
    private TotpChoice _selectedAlgorithm = new("SHA1", "SHA1");

    [ObservableProperty]
    private int _period = 30;

    [ObservableProperty]
    private int _digits = 6;

    [ObservableProperty]
    private long _counter;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private string _validationMessage = "";

    partial void OnSelectedOtpTypeChanged(TotpChoice value)
    {
        if (value.Value == "STEAM")
        {
            Digits = 5;
            Period = 30;
        }
        else if (Digits == 5)
        {
            Digits = 6;
        }

        OnPropertyChanged(nameof(IsTimeBased));
        OnPropertyChanged(nameof(IsCounterBased));
        OnPropertyChanged(nameof(UsesDigits));
    }

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            ValidationMessage = L.Get("AuthenticatorTitleRequired");
            return false;
        }

        var data = BuildData();
        if (string.IsNullOrWhiteSpace(data.Secret))
        {
            ValidationMessage = L.Get("TotpSecretRequired");
            return false;
        }

        ValidationMessage = "";
        return true;
    }

    public SecureItem ApplyTo(SecureItem? target = null)
    {
        var item = target ?? _source ?? new SecureItem
        {
            ItemType = VaultItemType.Totp,
            CreatedAt = DateTimeOffset.UtcNow
        };

        item.ItemType = VaultItemType.Totp;
        item.Title = Title.Trim();
        item.Notes = Notes.Trim();
        item.IsFavorite = IsFavorite;
        item.ItemData = TotpDataResolver.ToItemData(BuildData());
        item.IsDeleted = false;
        item.DeletedAt = null;
        item.SyncStatus = item.BitwardenVaultId is null ? SyncStatus.None : SyncStatus.Pending;
        return item;
    }

    public string ToAuthenticatorKey()
    {
        var data = BuildData();
        var labelText = string.IsNullOrWhiteSpace(data.AccountName)
            ? Title.Trim()
            : string.IsNullOrWhiteSpace(data.Issuer)
                ? data.AccountName
                : $"{data.Issuer}:{data.AccountName}";
        var label = Uri.EscapeDataString(labelText);
        var query = $"secret={Uri.EscapeDataString(data.Secret)}";
        if (!string.IsNullOrWhiteSpace(data.Issuer))
        {
            query += $"&issuer={Uri.EscapeDataString(data.Issuer)}";
        }

        if (data.Period != 30 && data.OtpType != "HOTP")
        {
            query += $"&period={data.Period}";
        }

        if (data.Digits != 6 && data.OtpType != "STEAM")
        {
            query += $"&digits={data.Digits}";
        }

        if (!string.Equals(data.Algorithm, "SHA1", StringComparison.OrdinalIgnoreCase))
        {
            query += $"&algorithm={Uri.EscapeDataString(data.Algorithm)}";
        }

        if (data.OtpType == "STEAM")
        {
            query += "&encoder=steam";
        }

        if (data.OtpType == "HOTP")
        {
            query += $"&counter={data.Counter}";
        }

        return $"otpauth://{data.OtpType.ToLowerInvariant()}/{label}?{query}";
    }

    private TotpData BuildData() => TotpDataResolver.Normalize(new TotpData(
        Secret,
        Issuer,
        AccountName,
        Math.Clamp(Period, 10, 120),
        Math.Clamp(Digits, 4, 10),
        SelectedAlgorithm.Value,
        SelectedOtpType.Value,
        Math.Max(0, Counter)));
}
