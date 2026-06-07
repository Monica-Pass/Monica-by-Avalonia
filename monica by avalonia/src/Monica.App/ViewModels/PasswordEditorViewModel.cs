using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.App.ViewModels;

public sealed record PasswordCategoryChoice(long? Id, string Name);
public sealed record PasswordLoginTypeChoice(PasswordLoginType Value, string Label);
public sealed record BoundNoteChoice(long? Id, string Title);
public sealed record CustomIconTypeChoice(string Value, string Label);

public sealed partial class PasswordEditorViewModel : ObservableObject
{
    private readonly IPasswordGeneratorService _passwordGenerator;

    public PasswordEditorViewModel(
        ILocalizationService localization,
        IPasswordGeneratorService passwordGenerator,
        PasswordEntry? source,
        IEnumerable<Category> categories,
        string plainPassword,
        IEnumerable<string>? siblingPasswords = null,
        IEnumerable<SecureItem>? notes = null,
        IEnumerable<CustomField>? customFields = null)
    {
        L = localization;
        _passwordGenerator = passwordGenerator;
        Source = source;
        IsNew = source is null;

        CategoryOptions.Add(new PasswordCategoryChoice(null, localization.Get("NoFolder")));
        foreach (var category in categories.OrderBy(item => item.SortOrder).ThenBy(item => item.Name))
        {
            CategoryOptions.Add(new PasswordCategoryChoice(category.Id, category.Name));
        }

        var selectedCategoryId = source?.CategoryId;
        SelectedCategory = CategoryOptions.FirstOrDefault(item => item.Id == selectedCategoryId) ?? CategoryOptions[0];

        BoundNoteOptions.Add(new BoundNoteChoice(null, localization.Get("NoBoundNote")));
        foreach (var note in (notes ?? []).OrderByDescending(item => item.UpdatedAt).ThenBy(item => item.Title))
        {
            BoundNoteOptions.Add(new BoundNoteChoice(note.Id, string.IsNullOrWhiteSpace(note.Title) ? localization.Get("Untitled") : note.Title));
        }

        SelectedBoundNote = BoundNoteOptions.FirstOrDefault(item => item.Id == source?.BoundNoteId) ?? BoundNoteOptions[0];

        Title = source?.Title ?? "";
        WebsiteLines = string.Join(Environment.NewLine, ParseWebsiteRows(source?.Website ?? ""));
        Username = source?.Username ?? "";
        PasswordLines = string.Join(
            Environment.NewLine,
            NormalizePasswordRows(siblingPasswords ?? [plainPassword]));
        Notes = source?.Notes ?? "";
        AuthenticatorKey = source?.AuthenticatorKey ?? "";
        AppPackageName = source?.AppPackageName ?? "";
        AppName = source?.AppName ?? "";
        Email = source?.Email ?? "";
        Phone = source?.Phone ?? "";
        AddressLine = source?.AddressLine ?? "";
        City = source?.City ?? "";
        State = source?.State ?? "";
        ZipCode = source?.ZipCode ?? "";
        Country = source?.Country ?? "";
        CreditCardNumber = source?.CreditCardNumber ?? "";
        CreditCardHolder = source?.CreditCardHolder ?? "";
        CreditCardExpiry = source?.CreditCardExpiry ?? "";
        CreditCardCvv = source?.CreditCardCvv ?? "";
        PasskeyBindings = source?.PasskeyBindings ?? "";
        SshKeyData = source?.SshKeyData ?? "";
        SsoProvider = source?.SsoProvider ?? "";
        WifiMetadata = source?.WifiMetadata ?? "";
        IsFavorite = source?.IsFavorite ?? false;

        LoginTypeOptions.Add(new PasswordLoginTypeChoice(PasswordLoginType.Password, localization.Get("LoginTypePassword")));
        LoginTypeOptions.Add(new PasswordLoginTypeChoice(PasswordLoginType.Sso, localization.Get("LoginTypeSso")));
        LoginTypeOptions.Add(new PasswordLoginTypeChoice(PasswordLoginType.Wifi, localization.Get("LoginTypeWifi")));
        LoginTypeOptions.Add(new PasswordLoginTypeChoice(PasswordLoginType.SshKey, localization.Get("LoginTypeSshKey")));
        SelectedLoginType = LoginTypeOptions.FirstOrDefault(item => item.Value == source?.LoginType) ?? LoginTypeOptions[0];
        CustomIconTypeOptions.Add(new CustomIconTypeChoice("NONE", localization.Get("CustomIconUseDefault")));
        CustomIconTypeOptions.Add(new CustomIconTypeChoice("SIMPLE_ICON", localization.Get("CustomIconSimple")));
        CustomIconTypeOptions.Add(new CustomIconTypeChoice("UPLOADED", localization.Get("CustomIconUploaded")));
        var iconType = NormalizeCustomIconType(source?.CustomIconType);
        CustomIconValue = source?.CustomIconValue ?? "";
        SelectedCustomIconType = CustomIconTypeOptions.FirstOrDefault(item => item.Value == iconType) ?? CustomIconTypeOptions[0];
        CustomFieldsText = EncodeCustomFields(customFields ?? []);
    }

    public ILocalizationService L { get; }
    public PasswordEntry? Source { get; }
    public bool IsNew { get; }
    public ObservableCollection<PasswordCategoryChoice> CategoryOptions { get; } = [];
    public ObservableCollection<PasswordLoginTypeChoice> LoginTypeOptions { get; } = [];
    public ObservableCollection<BoundNoteChoice> BoundNoteOptions { get; } = [];
    public ObservableCollection<CustomIconTypeChoice> CustomIconTypeOptions { get; } = [];
    public string DialogTitle => IsNew ? L.Get("NewPassword") : L.Get("EditPassword");
    public string Website
    {
        get => WebsiteLines;
        set
        {
            WebsiteLines = value;
            OnPropertyChanged();
        }
    }

    public string Password
    {
        get => PasswordLines;
        set
        {
            PasswordLines = value;
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _websiteLines = "";

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _passwordLines = "";

    [ObservableProperty]
    private bool _isPasswordVisible;

    [ObservableProperty]
    private int _generatorLength = 24;

    [ObservableProperty]
    private bool _generatorIncludeUppercase = true;

    [ObservableProperty]
    private bool _generatorIncludeLowercase = true;

    [ObservableProperty]
    private bool _generatorIncludeNumbers = true;

    [ObservableProperty]
    private bool _generatorIncludeSymbols = true;

    [ObservableProperty]
    private string _notes = "";

    [ObservableProperty]
    private string _authenticatorKey = "";

    [ObservableProperty]
    private string _appPackageName = "";

    [ObservableProperty]
    private string _appName = "";

    [ObservableProperty]
    private string _email = "";

    [ObservableProperty]
    private string _phone = "";

    [ObservableProperty]
    private string _addressLine = "";

    [ObservableProperty]
    private string _city = "";

    [ObservableProperty]
    private string _state = "";

    [ObservableProperty]
    private string _zipCode = "";

    [ObservableProperty]
    private string _country = "";

    [ObservableProperty]
    private string _creditCardNumber = "";

    [ObservableProperty]
    private string _creditCardHolder = "";

    [ObservableProperty]
    private string _creditCardExpiry = "";

    [ObservableProperty]
    private string _creditCardCvv = "";

    [ObservableProperty]
    private string _passkeyBindings = "";

    [ObservableProperty]
    private string _sshKeyData = "";

    [ObservableProperty]
    private string _ssoProvider = "";

    [ObservableProperty]
    private string _wifiMetadata = "";

    [ObservableProperty]
    private string _customFieldsText = "";

    [ObservableProperty]
    private string _customIconValue = "";

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private PasswordCategoryChoice? _selectedCategory;

    [ObservableProperty]
    private PasswordLoginTypeChoice? _selectedLoginType;

    [ObservableProperty]
    private BoundNoteChoice? _selectedBoundNote;

    [ObservableProperty]
    private CustomIconTypeChoice? _selectedCustomIconType;

    [ObservableProperty]
    private string _validationMessage = "";

    public char PasswordMaskChar => IsPasswordVisible ? '\0' : '*';
    public string TogglePasswordVisibilityLabel => IsPasswordVisible ? L.Get("HidePassword") : L.Get("ShowPassword");
    public string PasswordEditorStrengthText
    {
        get
        {
            var rows = GetPasswordRows();
            if (rows.Count == 0)
            {
                return L.Get("GeneratorNoPassword");
            }

            var strength = _passwordGenerator.Analyze(rows[0]);
            return L.Format(
                "GeneratedPasswordStrengthFormat",
                PasswordStrengthLocalization.Label(L, strength.Label),
                strength.Score,
                PasswordStrengthLocalization.Warnings(L, strength.Warnings));
        }
    }

    public int PasswordEditorStrengthValue
    {
        get
        {
            var rows = GetPasswordRows();
            return rows.Count == 0 ? 0 : _passwordGenerator.Analyze(rows[0]).Score * 20;
        }
    }

    public string PasswordRowCountText => L.Format("PasswordRowCountFormat", GetPasswordRows().Count);
    public string GeneratorLengthText => L.Format("GeneratorLengthFormat", GeneratorLength);
    public bool IsCustomIconValueEnabled => SelectedCustomIconType?.Value is "SIMPLE_ICON" or "UPLOADED";
    public string CustomIconValueWatermark => SelectedCustomIconType?.Value == "UPLOADED"
        ? L.Get("CustomIconUploadedHint")
        : L.Get("CustomIconSimpleHint");

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            ValidationMessage = L.Get("PasswordTitleRequired");
            return false;
        }

        if (GetPasswordRows().Count == 0 && SelectedLoginType?.Value != PasswordLoginType.Sso)
        {
            ValidationMessage = L.Get("PasswordValueRequired");
            return false;
        }

        ValidationMessage = "";
        return true;
    }

    public PasswordEntry BuildEntry(string storedPassword)
    {
        return BuildEntryFrom(Source, storedPassword);
    }

    public PasswordEntry BuildEntryFrom(PasswordEntry? source, string storedPassword)
    {
        var entry = source is null ? new PasswordEntry() : Clone(source);
        entry.Title = Title.Trim();
        entry.Website = EncodeWebsites();
        entry.Username = Username.Trim();
        entry.Password = storedPassword;
        entry.Notes = Notes.Trim();
        entry.AuthenticatorKey = AuthenticatorKey.Trim();
        entry.AppPackageName = AppPackageName.Trim();
        entry.AppName = AppName.Trim();
        entry.Email = Email.Trim();
        entry.Phone = Phone.Trim();
        entry.AddressLine = AddressLine.Trim();
        entry.City = City.Trim();
        entry.State = State.Trim();
        entry.ZipCode = ZipCode.Trim();
        entry.Country = Country.Trim();
        entry.CreditCardNumber = CreditCardNumber.Trim();
        entry.CreditCardHolder = CreditCardHolder.Trim();
        entry.CreditCardExpiry = CreditCardExpiry.Trim();
        entry.CreditCardCvv = CreditCardCvv.Trim();
        entry.PasskeyBindings = PasskeyBindings.Trim();
        entry.SshKeyData = SshKeyData.Trim();
        entry.SsoProvider = SsoProvider.Trim();
        entry.WifiMetadata = WifiMetadata.Trim();
        entry.LoginType = SelectedLoginType?.Value ?? PasswordLoginType.Password;
        entry.CategoryId = SelectedCategory?.Id;
        entry.BoundNoteId = SelectedBoundNote?.Id;
        var customIconType = NormalizeCustomIconType(SelectedCustomIconType?.Value);
        var customIconValue = CustomIconValue.Trim();
        if (customIconType == "NONE" || string.IsNullOrWhiteSpace(customIconValue))
        {
            customIconType = "NONE";
            customIconValue = "";
        }

        entry.CustomIconType = customIconType;
        entry.CustomIconValue = customIconType == "NONE" ? null : customIconValue;
        entry.CustomIconUpdatedAt = ShouldUpdateCustomIconTimestamp(source, entry)
            ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            : source?.CustomIconUpdatedAt ?? 0;
        entry.IsFavorite = IsFavorite;
        return entry;
    }

    public IReadOnlyList<PasswordEntry> BuildEntries(IReadOnlyList<string> storedPasswords)
    {
        if (storedPasswords.Count == 0)
        {
            return [BuildEntry("")];
        }

        var entries = storedPasswords.Select(BuildEntry).ToArray();
        if (entries.Length > 1)
        {
            var replicaGroupId = entries
                .Select(entry => entry.ReplicaGroupId)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? $"editor-{Guid.NewGuid():N}";
            foreach (var entry in entries)
            {
                entry.ReplicaGroupId = replicaGroupId;
            }
        }

        return entries;
    }

    public IReadOnlyList<string> GetPasswordRows()
    {
        return NormalizePasswordRows(SplitRows(PasswordLines));
    }

    public IReadOnlyList<CustomField> GetCustomFields()
    {
        return SplitRows(CustomFieldsText)
            .Select((row, index) => ParseCustomField(row, index))
            .Where(field => field is not null)
            .Select(field => field!)
            .ToArray();
    }

    [RelayCommand]
    private void GeneratePassword()
    {
        var rows = GetPasswordRows().ToList();
        if (rows.Count == 0)
        {
            rows.Add(GenerateEditorPassword());
        }
        else
        {
            rows[0] = GenerateEditorPassword();
        }

        PasswordLines = string.Join(Environment.NewLine, rows);
    }

    [RelayCommand]
    private void AddGeneratedPasswordRow()
    {
        var rows = GetPasswordRows().ToList();
        rows.Add(GenerateEditorPassword());
        PasswordLines = string.Join(Environment.NewLine, rows);
    }

    [RelayCommand]
    private void TogglePasswordVisibility()
    {
        IsPasswordVisible = !IsPasswordVisible;
    }

    partial void OnPasswordLinesChanged(string value) => RaisePasswordEditorState();

    partial void OnSelectedCustomIconTypeChanged(CustomIconTypeChoice? value)
    {
        if (value?.Value == "NONE")
        {
            CustomIconValue = "";
        }

        OnPropertyChanged(nameof(IsCustomIconValueEnabled));
        OnPropertyChanged(nameof(CustomIconValueWatermark));
    }

    partial void OnIsPasswordVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(PasswordMaskChar));
        OnPropertyChanged(nameof(TogglePasswordVisibilityLabel));
    }

    partial void OnGeneratorLengthChanged(int value)
    {
        GeneratorLength = Math.Clamp(value, 8, 128);
        OnPropertyChanged(nameof(GeneratorLengthText));
    }

    private string GenerateEditorPassword()
    {
        return _passwordGenerator.GeneratePassword(
            GeneratorLength,
            GeneratorIncludeUppercase,
            GeneratorIncludeLowercase,
            GeneratorIncludeNumbers,
            GeneratorIncludeSymbols);
    }

    private void RaisePasswordEditorState()
    {
        OnPropertyChanged(nameof(PasswordEditorStrengthText));
        OnPropertyChanged(nameof(PasswordEditorStrengthValue));
        OnPropertyChanged(nameof(PasswordRowCountText));
    }

    private string EncodeWebsites()
    {
        return string.Join(", ", ParseWebsiteRows(WebsiteLines));
    }

    private static IReadOnlyList<string> ParseWebsiteRows(string value)
    {
        return SplitRows(value.Replace('，', ','))
            .SelectMany(row => row.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Select(row => row.Trim())
            .Where(row => row.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> NormalizePasswordRows(IEnumerable<string> values)
    {
        return values
            .Select(row => row.Trim())
            .Where(row => row.Length > 0)
            .ToArray();
    }

    private static string NormalizeCustomIconType(string? value)
    {
        return value?.Trim().ToUpperInvariant() switch
        {
            "SIMPLE_ICON" => "SIMPLE_ICON",
            "UPLOADED" => "UPLOADED",
            _ => "NONE"
        };
    }

    private static bool ShouldUpdateCustomIconTimestamp(PasswordEntry? source, PasswordEntry entry)
    {
        return !string.Equals(source?.CustomIconType ?? "NONE", entry.CustomIconType, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(source?.CustomIconValue ?? "", entry.CustomIconValue ?? "", StringComparison.Ordinal);
    }

    private static IEnumerable<string> SplitRows(string value)
    {
        return value.Split(["\r\n", "\n", "\r"], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static string EncodeCustomFields(IEnumerable<CustomField> fields)
    {
        return string.Join(
            Environment.NewLine,
            fields
                .OrderBy(field => field.SortOrder)
                .ThenBy(field => field.Id)
                .Where(field => !string.IsNullOrWhiteSpace(field.Title) && !string.IsNullOrWhiteSpace(field.Value))
                .Select(field => $"{(field.IsProtected ? "!" : "")}{field.Title.Trim()}={field.Value.Trim()}"));
    }

    private static CustomField? ParseCustomField(string row, int sortOrder)
    {
        var separator = row.IndexOf('=');
        if (separator < 0)
        {
            separator = row.IndexOf(':');
        }

        if (separator <= 0)
        {
            return null;
        }

        var rawTitle = row[..separator].Trim();
        var isProtected = rawTitle.StartsWith('!');
        var title = isProtected ? rawTitle[1..].Trim() : rawTitle;
        var value = row[(separator + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return new CustomField
        {
            Title = title,
            Value = value,
            IsProtected = isProtected,
            SortOrder = sortOrder
        };
    }

    private static PasswordEntry Clone(PasswordEntry source)
    {
        return new PasswordEntry
        {
            Id = source.Id,
            Title = source.Title,
            Website = source.Website,
            Username = source.Username,
            Password = source.Password,
            Notes = source.Notes,
            IsFavorite = source.IsFavorite,
            SortOrder = source.SortOrder,
            IsGroupCover = source.IsGroupCover,
            AppPackageName = source.AppPackageName,
            AppName = source.AppName,
            Email = source.Email,
            Phone = source.Phone,
            AddressLine = source.AddressLine,
            City = source.City,
            State = source.State,
            ZipCode = source.ZipCode,
            Country = source.Country,
            CreditCardNumber = source.CreditCardNumber,
            CreditCardHolder = source.CreditCardHolder,
            CreditCardExpiry = source.CreditCardExpiry,
            CreditCardCvv = source.CreditCardCvv,
            CategoryId = source.CategoryId,
            BoundNoteId = source.BoundNoteId,
            KeepassDatabaseId = source.KeepassDatabaseId,
            KeepassGroupPath = source.KeepassGroupPath,
            KeepassEntryUuid = source.KeepassEntryUuid,
            KeepassGroupUuid = source.KeepassGroupUuid,
            MdbxDatabaseId = source.MdbxDatabaseId,
            MdbxFolderId = source.MdbxFolderId,
            AuthenticatorKey = source.AuthenticatorKey,
            PasskeyBindings = source.PasskeyBindings,
            SshKeyData = source.SshKeyData,
            LoginType = source.LoginType,
            SsoProvider = source.SsoProvider,
            SsoRefEntryId = source.SsoRefEntryId,
            WifiMetadata = source.WifiMetadata,
            CustomIconType = source.CustomIconType,
            CustomIconValue = source.CustomIconValue,
            CustomIconUpdatedAt = source.CustomIconUpdatedAt,
            IsDeleted = source.IsDeleted,
            DeletedAt = source.DeletedAt,
            IsArchived = source.IsArchived,
            ArchivedAt = source.ArchivedAt,
            ReplicaGroupId = source.ReplicaGroupId,
            BitwardenVaultId = source.BitwardenVaultId,
            BitwardenCipherId = source.BitwardenCipherId,
            BitwardenFolderId = source.BitwardenFolderId,
            BitwardenRevisionDate = source.BitwardenRevisionDate,
            BitwardenCipherType = source.BitwardenCipherType,
            BitwardenLocalModified = source.BitwardenLocalModified,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt
        };
    }
}
