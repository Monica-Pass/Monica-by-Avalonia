using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.App.Services;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.App.ViewModels;

public sealed record PasswordLoginTypeChoice(PasswordLoginType Value, string Label);
public sealed record BoundNoteChoice(long? Id, string Title);
public sealed record CustomIconTypeChoice(string Value, string Label);

public sealed partial class PasswordEditorViewModel : ObservableObject, IDisposable
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

        foreach (var category in PasswordCategoryChoice.BuildOptions(categories, localization.Get("NoFolder")))
        {
            CategoryOptions.Add(category);
        }

        SelectedCategory = CategoryOptions.FirstOrDefault(item => item.Id == source?.CategoryId) ?? CategoryOptions[0];
        BoundNoteOptions.Add(new BoundNoteChoice(null, localization.Get("NoBoundNote")));
        foreach (var note in (notes ?? []).OrderByDescending(item => item.UpdatedAt).ThenBy(item => item.Title))
        {
            BoundNoteOptions.Add(new BoundNoteChoice(
                note.Id,
                string.IsNullOrWhiteSpace(note.Title) ? localization.Get("Untitled") : note.Title));
        }

        SelectedBoundNote = BoundNoteOptions.FirstOrDefault(item => item.Id == source?.BoundNoteId) ?? BoundNoteOptions[0];
        Title = source?.Title ?? "";
        WebsiteLines = string.Join(Environment.NewLine, ParseWebsiteRows(source?.Website ?? ""));
        Username = source?.Username ?? "";
        PasswordLines = string.Join(Environment.NewLine, NormalizePasswordRows(siblingPasswords ?? [plainPassword]));
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
    public PasswordEntry? Source { get; private set; }
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

    public void Dispose() => ClearSensitiveState();
}
