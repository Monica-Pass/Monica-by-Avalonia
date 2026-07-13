using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class PasswordDetailGroup : ObservableObject
{
    public PasswordDetailGroup(string title, bool isExpanded, IReadOnlyList<PasswordDetailField> fields)
    {
        Title = title;
        _isExpanded = isExpanded;
        Fields = fields;
    }

    public string Title { get; }
    public IReadOnlyList<PasswordDetailField> Fields { get; }
    public IReadOnlyList<PasswordDetailField> VisibleFields => IsExpanded ? Fields : [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleFields))]
    private bool _isExpanded;
}

public sealed partial class PasswordDetailField(
    string label,
    string displayValue,
    string copyValue,
    bool canCopy = true,
    bool isSensitive = false) : ObservableObject
{
    private const string HiddenSensitiveValue = "************";

    public string Label { get; } = label;
    public string DisplayValue { get; } = displayValue;
    public string CopyValue { get; } = copyValue;
    public bool CanCopy { get; } = canCopy;
    public bool IsSensitive { get; } = isSensitive;
    public bool CanToggleVisibility => IsSensitive && CanCopy && !string.IsNullOrWhiteSpace(DisplayValue);
    public string DisplayText => CanToggleVisibility && !IsVisible ? HiddenSensitiveValue : DisplayValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    private bool _isVisible;
}

public sealed class PasswordAttachmentItem(ILocalizationService localization, Attachment attachment)
{
    public Attachment Attachment { get; } = attachment;
    public string FileName => Attachment.FileName;
    public string DisplayValue => BuildAttachmentDisplayValue(localization, Attachment);
    public string StoragePath => Attachment.StoragePath;
    public bool CanCopy => !string.IsNullOrWhiteSpace(StoragePath) && !IsMdbxAttachment(StoragePath);

    private static string BuildAttachmentDisplayValue(ILocalizationService localization, Attachment attachment)
    {
        var values = new[]
        {
            FormatAttachmentSize(attachment.SizeBytes),
            attachment.ContentType,
            IsMdbxAttachment(attachment.StoragePath) ? "" : attachment.StoragePath
        }.Where(value => !string.IsNullOrWhiteSpace(value));
        return string.Join(" - ", values);
    }

    private static bool IsMdbxAttachment(string storagePath) =>
        storagePath.StartsWith("mdbx:", StringComparison.OrdinalIgnoreCase);

    private static string FormatAttachmentSize(long sizeBytes)
    {
        if (sizeBytes <= 0)
        {
            return "";
        }

        string[] units = ["B", "KB", "MB", "GB"];
        var size = (double)sizeBytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{sizeBytes} {units[unit]}"
            : $"{size:0.#} {units[unit]}";
    }
}

public sealed partial class PasswordHistoryItemViewModel : ObservableObject
{
    public PasswordHistoryItemViewModel(ILocalizationService localization, PasswordHistoryDisplayItem source, bool isLatest)
    {
        Entry = source.Entry;
        Password = source.DisplayPassword;
        CanCopy = source.CanCopy;
        IsLatest = isLatest;
        LastUsedText = localization.Format("PasswordHistoryLastUsedFormat", source.Entry.LastUsedAt.ToString("g", localization.Culture));
    }

    public PasswordHistoryEntry Entry { get; }
    public string Password { get; }
    public bool CanCopy { get; }
    public bool IsLatest { get; }
    public string LastUsedText { get; }
    public string DisplayPassword => IsVisible ? Password : new string('*', Math.Clamp(Password.Length, 8, 24));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayPassword))]
    private bool _isVisible;
}

public sealed partial class PasswordDetailViewModel : ObservableObject
{
    private readonly IClipboardService _clipboardService;
    private readonly Func<PasswordEntry, Task>? _addAttachment;
    private readonly Func<Attachment, Task<bool>>? _deleteAttachment;
    private readonly Func<PasswordHistoryEntry, Task<bool>>? _deletePasswordHistory;
    private readonly Func<long, Task<bool>>? _clearPasswordHistory;

    public PasswordDetailViewModel(
        ILocalizationService localization,
        IClipboardService clipboardService,
        ICryptoService cryptoService,
        ITotpService totpService,
        PasswordEntry entry,
        IReadOnlyList<PasswordEntry> siblings,
        Category? category,
        SecureItem? boundNote,
        IReadOnlyList<Attachment> attachments,
        IReadOnlyList<CustomField> customFields,
        IReadOnlyList<PasswordHistoryDisplayItem>? passwordHistory = null,
        Func<PasswordEntry, Task>? addAttachment = null,
        Func<Attachment, Task<bool>>? deleteAttachment = null,
        Func<PasswordHistoryEntry, Task<bool>>? deletePasswordHistory = null,
        Func<long, Task<bool>>? clearPasswordHistory = null)
    {
        L = localization;
        _clipboardService = clipboardService;
        _addAttachment = addAttachment;
        _deleteAttachment = deleteAttachment;
        _deletePasswordHistory = deletePasswordHistory;
        _clearPasswordHistory = clearPasswordHistory;
        Entry = entry;
        DialogTitle = localization.Get("PasswordDetails");
        Title = entry.Title;
        Subtitle = string.Join(" - ", new[] { entry.Username, entry.Website }.Where(value => !string.IsNullOrWhiteSpace(value)));
        Initial = entry.AvatarText;
        PasswordHistoryDescription = localization.Get("PasswordHistoryDescription");

        foreach (var group in BuildGroups(localization, cryptoService, totpService, entry, NormalizeSiblings(entry, siblings), category, boundNote, attachments, customFields))
        {
            Groups.Add(group);
        }

        foreach (var attachment in attachments
            .OrderByDescending(attachment => attachment.CreatedAt)
            .ThenByDescending(attachment => attachment.Id)
            .Select(attachment => new PasswordAttachmentItem(localization, attachment)))
        {
            Attachments.Add(attachment);
        }

        SetPasswordHistory(passwordHistory ?? []);
    }

    public ILocalizationService L { get; }
    public PasswordEntry Entry { get; }
    public string DialogTitle { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string Initial { get; }
    public string CopyLabel => L.Get("Copy");
    public string DeleteLabel => L.Get("Delete");
    public string PasswordHistoryTitle => L.Get("PasswordHistory");
    public string PasswordHistoryDescription { get; }
    public string LatestLabel => L.Get("PasswordHistoryLatest");
    public string ClearPasswordHistoryLabel => L.Get("ClearPasswordHistory");
    public bool HasPasswordHistory => PasswordHistory.Count > 0;
    public bool HasAttachments => Attachments.Count > 0;
    public ObservableCollection<PasswordDetailGroup> Groups { get; } = [];
    public ObservableCollection<PasswordAttachmentItem> Attachments { get; } = [];
    public ObservableCollection<PasswordHistoryItemViewModel> PasswordHistory { get; } = [];

    public void SetPasswordHistory(IReadOnlyList<PasswordHistoryDisplayItem> passwordHistory)
    {
        PasswordHistory.Clear();
        foreach (var item in passwordHistory.Select((item, index) => new PasswordHistoryItemViewModel(L, item, index == 0)))
        {
            PasswordHistory.Add(item);
        }

        OnPropertyChanged(nameof(HasPasswordHistory));
    }

    [ObservableProperty]
    private string _statusText = "";

    [RelayCommand]
    private async Task CopyFieldAsync(PasswordDetailField? field)
    {
        if (field is null || !field.CanCopy || string.IsNullOrWhiteSpace(field.CopyValue))
        {
            return;
        }

        await _clipboardService.SetSensitiveTextAsync(field.CopyValue);
        StatusText = L.Format("CopiedFieldFormat", field.Label);
    }

    [RelayCommand]
    private void ToggleFieldVisibility(PasswordDetailField? field)
    {
        if (field is null || !field.CanToggleVisibility)
        {
            return;
        }

        field.IsVisible = !field.IsVisible;
    }

    [RelayCommand]
    private async Task AddAttachmentAsync()
    {
        if (_addAttachment is null)
        {
            return;
        }

        await _addAttachment(Entry);
        StatusText = L.Get("AttachmentAddedRefreshDetails");
    }

    [RelayCommand]
    private async Task CopyAttachmentPathAsync(PasswordAttachmentItem? item)
    {
        if (item is null || !item.CanCopy)
        {
            return;
        }

        await _clipboardService.SetSensitiveTextAsync(item.StoragePath);
        StatusText = L.Format("CopiedFieldFormat", item.FileName);
    }

    [RelayCommand]
    private async Task DeleteAttachmentAsync(PasswordAttachmentItem? item)
    {
        if (item is null || _deleteAttachment is null)
        {
            return;
        }

        if (!await _deleteAttachment(item.Attachment))
        {
            return;
        }

        Attachments.Remove(item);
        OnPropertyChanged(nameof(HasAttachments));
        StatusText = L.Format("DeletedAttachmentFormat", item.FileName);
    }

    [RelayCommand]
    private void ToggleHistoryPassword(PasswordHistoryItemViewModel? item)
    {
        if (item is null || !item.CanCopy)
        {
            return;
        }

        item.IsVisible = !item.IsVisible;
    }

    [RelayCommand]
    private async Task CopyHistoryPasswordAsync(PasswordHistoryItemViewModel? item)
    {
        if (item is null || !item.CanCopy || string.IsNullOrWhiteSpace(item.Password))
        {
            return;
        }

        await _clipboardService.SetSensitiveTextAsync(item.Password);
        StatusText = L.Get("CopiedPasswordHistory");
    }

    [RelayCommand]
    private async Task DeleteHistoryPasswordAsync(PasswordHistoryItemViewModel? item)
    {
        if (item is null || _deletePasswordHistory is null)
        {
            return;
        }

        if (!await _deletePasswordHistory(item.Entry))
        {
            return;
        }

        PasswordHistory.Remove(item);
        OnPropertyChanged(nameof(HasPasswordHistory));
        StatusText = L.Get("DeletedPasswordHistoryEntry");
    }

    [RelayCommand]
    private async Task ClearPasswordHistoryAsync()
    {
        if (_clearPasswordHistory is null || PasswordHistory.Count == 0)
        {
            return;
        }

        if (!await _clearPasswordHistory(Entry.Id))
        {
            return;
        }

        PasswordHistory.Clear();
        OnPropertyChanged(nameof(HasPasswordHistory));
        StatusText = L.Get("ClearedPasswordHistory");
    }

    private static IReadOnlyList<PasswordDetailGroup> BuildGroups(
        ILocalizationService localization,
        ICryptoService cryptoService,
        ITotpService totpService,
        PasswordEntry entry,
        IReadOnlyList<PasswordEntry> siblings,
        Category? category,
        SecureItem? boundNote,
        IReadOnlyList<Attachment> attachments,
        IReadOnlyList<CustomField> customFields)
    {
        var groups = new List<PasswordDetailGroup>();

        AddGroup(groups, localization.Get("General"), true,
            Field(localization.Get("PasswordTitle"), entry.Title),
            Field(localization.Get("Username"), entry.Username),
            Field(localization.Get("Website"), entry.Website),
            Field(localization.Get("Category"), category?.Name ?? ""),
            Field(localization.Get("BoundNote"), boundNote?.Title ?? ""));

        var passwordFields = new List<PasswordDetailField>();
        for (var index = 0; index < siblings.Count; index++)
        {
            var password = TryUnprotectPassword(siblings[index].Password, cryptoService);
            var label = siblings.Count == 1
                ? localization.Get("Password")
                : $"{localization.Get("Password")} {index + 1}";
            passwordFields.Add(Field(
                label,
                password.DisplayValue,
                password.CopyValue,
                password.CanCopy,
                isSensitive: true));
        }

        AddGroup(groups, localization.Get("Passwords"), true, passwordFields.ToArray());

        var totpData = TotpDataResolver.FromAuthenticatorKey(entry.AuthenticatorKey, entry.Title, entry.Username);
        if (totpData is not null)
        {
            var code = totpService.GenerateCode(totpData.Secret, totpData.Period, totpData.Digits, totpData.OtpType, totpData.Counter);
            AddGroup(groups, localization.Get("SecurityVerification"), true,
                Field(localization.Get("TotpCode"), code),
                Field(localization.Get("RemainingTime"), localization.Format("SecondFormat", totpService.GetRemainingSeconds(totpData.Period)), canCopy: false),
                Field(localization.Get("Issuer"), totpData.Issuer),
                Field(localization.Get("Account"), totpData.AccountName),
                Field(localization.Get("TotpSecret"), totpData.Secret, isSensitive: true),
                Field(localization.Get("AuthenticatorKey"), entry.AuthenticatorKey, isSensitive: true));
        }

        AddGroup(groups, localization.Get("AppBinding"), false,
            Field(localization.Get("AppName"), entry.AppName),
            Field(localization.Get("AppPackageName"), entry.AppPackageName));

        AddGroup(groups, localization.Get("PersonalInfo"), false,
            Field(localization.Get("Email"), entry.Email),
            Field(localization.Get("Phone"), entry.Phone),
            Field(localization.Get("AddressLine"), entry.AddressLine),
            Field(localization.Get("City"), entry.City),
            Field(localization.Get("State"), entry.State),
            Field(localization.Get("ZipCode"), entry.ZipCode),
            Field(localization.Get("Country"), entry.Country));

        AddGroup(groups, localization.Get("CardInfo"), false,
            Field(localization.Get("CreditCardNumber"), entry.CreditCardNumber, isSensitive: true),
            Field(localization.Get("CreditCardHolder"), entry.CreditCardHolder),
            Field(localization.Get("CreditCardExpiry"), entry.CreditCardExpiry),
            Field(localization.Get("CreditCardCvv"), entry.CreditCardCvv, isSensitive: true));

        AddGroup(groups, localization.Get("AdvancedLogin"), false,
            Field(localization.Get("LoginType"), LocalizeLoginType(localization, entry.LoginType)),
            Field(localization.Get("SsoProvider"), entry.SsoProvider),
            Field(localization.Get("PasskeyBindings"), entry.PasskeyBindings),
            Field(localization.Get("WifiMetadata"), entry.WifiMetadata),
            Field(localization.Get("SshKeyData"), entry.SshKeyData));

        AddGroup(groups, localization.Get("CustomIcon"), false,
            Field(localization.Get("CustomIconType"), LocalizeCustomIconType(localization, entry.CustomIconType), canCopy: false),
            Field(localization.Get("CustomIconValue"), entry.CustomIconValue ?? ""),
            Field(localization.Get("UpdatedAt"), entry.CustomIconUpdatedAt == 0
                ? ""
                : DateTimeOffset.FromUnixTimeMilliseconds(entry.CustomIconUpdatedAt).ToString("g", localization.Culture), canCopy: false));

        AddGroup(groups, localization.Get("Notes"), false,
            Field(localization.Get("Notes"), entry.Notes),
            Field(localization.Get("BoundNote"), boundNote is null ? "" : NoteContentCodec.ToPlainPreview(
                NoteContentCodec.DecodeFromItem(boundNote).Content,
                NoteContentCodec.DecodeFromItem(boundNote).IsMarkdown)));

        AddGroup(groups, localization.Get("CustomFields"), false,
            customFields
                .OrderBy(field => field.SortOrder)
                .ThenBy(field => field.Id)
                .Select(field => Field(field.Title, field.Value, isSensitive: field.IsProtected))
                .ToArray());

        AddGroup(groups, localization.Get("SourceMetadata"), false,
            Field(localization.Get("BitwardenVault"), entry.BitwardenVaultId?.ToString() ?? ""),
            Field(localization.Get("BitwardenCipher"), entry.BitwardenCipherId ?? ""),
            Field(localization.Get("KeePassDatabase"), entry.KeepassDatabaseId?.ToString() ?? ""),
            Field(localization.Get("KeePassGroup"), entry.KeepassGroupPath ?? ""),
            Field(localization.Get("MdbxDatabase"), entry.MdbxDatabaseId?.ToString() ?? ""),
            Field(localization.Get("MdbxFolder"), entry.MdbxFolderId ?? ""),
            Field(localization.Get("CreatedAt"), entry.CreatedAt.ToString("g", localization.Culture), canCopy: false),
            Field(localization.Get("UpdatedAt"), entry.UpdatedAt.ToString("g", localization.Culture), canCopy: false));

        return groups;
    }

    private static IReadOnlyList<PasswordEntry> NormalizeSiblings(PasswordEntry entry, IReadOnlyList<PasswordEntry> siblings)
    {
        return siblings.Count == 0
            ? [entry]
            : siblings;
    }

    private static void AddGroup(List<PasswordDetailGroup> groups, string title, bool isExpanded, params PasswordDetailField[] fields)
    {
        var visibleFields = fields
            .Where(field => !string.IsNullOrWhiteSpace(field.DisplayValue))
            .ToArray();
        if (visibleFields.Length > 0)
        {
            groups.Add(new PasswordDetailGroup(title, isExpanded, visibleFields));
        }
    }

    private static PasswordDetailField Field(
        string label,
        string value,
        string? copyValue = null,
        bool canCopy = true,
        bool isSensitive = false)
    {
        var normalizedValue = value.Trim();
        return new PasswordDetailField(
            label,
            normalizedValue,
            copyValue ?? normalizedValue,
            canCopy && normalizedValue.Length > 0,
            isSensitive);
    }

    private static (string DisplayValue, string CopyValue, bool CanCopy) TryUnprotectPassword(string storedPassword, ICryptoService cryptoService)
    {
        if (string.IsNullOrWhiteSpace(storedPassword))
        {
            return ("", "", false);
        }

        if (!cryptoService.IsUnlocked)
        {
            return ("********", "", false);
        }

        try
        {
            var plainText = cryptoService.DecryptString(storedPassword);
            return (plainText, plainText, true);
        }
        catch
        {
            return (storedPassword, storedPassword, true);
        }
    }

    private static string LocalizeLoginType(ILocalizationService localization, PasswordLoginType loginType)
    {
        return loginType switch
        {
            PasswordLoginType.Sso => localization.Get("LoginTypeSso"),
            PasswordLoginType.Wifi => localization.Get("LoginTypeWifi"),
            PasswordLoginType.SshKey => localization.Get("LoginTypeSshKey"),
            _ => localization.Get("LoginTypePassword")
        };
    }

    private static string LocalizeCustomIconType(ILocalizationService localization, string customIconType)
    {
        return customIconType.ToUpperInvariant() switch
        {
            "SIMPLE_ICON" => localization.Get("CustomIconSimple"),
            "UPLOADED" => localization.Get("CustomIconUploaded"),
            _ => localization.Get("CustomIconUseDefault")
        };
    }
}
