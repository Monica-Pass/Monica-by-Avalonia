using CommunityToolkit.Mvvm.ComponentModel;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public enum PasswordEditorValidationTarget
{
    None,
    Title,
    Password,
    ApiKeyUrl,
    WifiSsid,
    WifiMetadata
}

public sealed partial class PasswordEditorViewModel
{
    [ObservableProperty]
    private string _validationMessage = "";

    public PasswordEditorValidationTarget ValidationTarget { get; private set; }
    public bool HasTitleValidationError => ValidationTarget == PasswordEditorValidationTarget.Title;
    public bool HasPasswordValidationError => ValidationTarget == PasswordEditorValidationTarget.Password;
    public bool HasApiKeyUrlValidationError => ValidationTarget == PasswordEditorValidationTarget.ApiKeyUrl;
    public string TitleValidationMessage => HasTitleValidationError ? L.Get("PasswordTitleRequired") : "";
    public string PasswordValidationMessage => HasPasswordValidationError ? L.Get("PasswordValueRequired") : "";
    public string ApiKeyUrlValidationMessage => HasApiKeyUrlValidationError ? L.Get("ApiKeyUrlInvalid") : "";
    public bool IsSensitiveStateCleared { get; private set; }

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            SetValidation(PasswordEditorValidationTarget.Title, L.Get("PasswordTitleRequired"));
            return false;
        }

        if (IsWifi && string.IsNullOrWhiteSpace(WifiSsid))
        {
            SetValidation(PasswordEditorValidationTarget.WifiSsid, L.Get("WifiSsidRequired"));
            return false;
        }

        if (IsWifi && !WifiNetworkData.TryRead(WifiMetadata, Title, out _))
        {
            SetValidation(PasswordEditorValidationTarget.WifiMetadata, L.Get("WifiMetadataInvalid"));
            return false;
        }

        if (GetPasswordRows().Count == 0 && SelectedLoginType?.Value != PasswordLoginType.Sso && WifiRequiresPassword)
        {
            SetValidation(PasswordEditorValidationTarget.Password, L.Get("PasswordValueRequired"));
            return false;
        }

        if (SelectedLoginType?.Value == PasswordLoginType.ApiKey && !ApiKeyEntryFields.IsValidOptionalUrl(ApiKeyUrl))
        {
            SetValidation(PasswordEditorValidationTarget.ApiKeyUrl, L.Get("ApiKeyUrlInvalid"));
            return false;
        }

        ClearValidation();
        return true;
    }

    public void ClearSensitiveState()
    {
        if (IsSensitiveStateCleared)
        {
            return;
        }

        Source = null;
        Title = "";
        WebsiteLines = "";
        Username = "";
        PasswordLines = "";
        Notes = "";
        AuthenticatorKey = "";
        AppPackageName = "";
        AppName = "";
        Email = "";
        Phone = "";
        AddressLine = "";
        City = "";
        State = "";
        ZipCode = "";
        Country = "";
        CreditCardNumber = "";
        CreditCardHolder = "";
        CreditCardExpiry = "";
        CreditCardCvv = "";
        PasskeyBindings = "";
        SshKeyData = "";
        SsoProvider = "";
        WifiMetadata = "";
        ApiKeyUrl = "";
        CustomFieldsText = "";
        CustomIconValue = "";
        IsPasswordVisible = false;
        SelectedCategory = null;
        SelectedLoginType = null;
        SelectedBoundNote = null;
        SelectedCustomIconType = null;
        ClearWifiState();
        CategoryOptions.Clear();
        LoginTypeOptions.Clear();
        BoundNoteOptions.Clear();
        CustomIconTypeOptions.Clear();
        ClearValidation();
        IsSensitiveStateCleared = true;
        ImportWifiQrImageCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsSensitiveStateCleared));
    }

    partial void OnTitleChanged(string value)
    {
        if (HasTitleValidationError && !string.IsNullOrWhiteSpace(value))
        {
            ClearValidation();
        }
    }

    partial void OnApiKeyUrlChanged(string value)
    {
        if (HasApiKeyUrlValidationError && ApiKeyEntryFields.IsValidOptionalUrl(value))
        {
            ClearValidation();
        }
    }

    partial void OnSelectedLoginTypeChanged(PasswordLoginTypeChoice? value)
    {
        if (value?.Value != PasswordLoginType.ApiKey && HasApiKeyUrlValidationError)
        {
            ClearValidation();
        }

        ClearCorrectedPasswordValidation();
        OnPropertyChanged(nameof(IsBarcode));
        OnPropertyChanged(nameof(IsApiKey));
        OnPropertyChanged(nameof(IsWifi));
        OnPropertyChanged(nameof(WifiIsEnterprise));
        OnPropertyChanged(nameof(WifiRequiresPassword));
        RaisePasswordEditorState();
        OnPropertyChanged(nameof(PasswordFieldLabel));
    }

    private void ClearCorrectedPasswordValidation()
    {
        if (HasPasswordValidationError &&
            (GetPasswordRows().Count > 0 || SelectedLoginType?.Value == PasswordLoginType.Sso || !WifiRequiresPassword))
        {
            ClearValidation();
        }
    }

    private void ClearValidation() => SetValidation(PasswordEditorValidationTarget.None, "");

    private void SetValidation(PasswordEditorValidationTarget target, string message)
    {
        ValidationMessage = message;
        if (ValidationTarget == target)
        {
            return;
        }

        ValidationTarget = target;
        OnPropertyChanged(nameof(ValidationTarget));
        OnPropertyChanged(nameof(HasTitleValidationError));
        OnPropertyChanged(nameof(HasPasswordValidationError));
        OnPropertyChanged(nameof(HasApiKeyUrlValidationError));
        OnPropertyChanged(nameof(HasWifiValidationError));
        OnPropertyChanged(nameof(TitleValidationMessage));
        OnPropertyChanged(nameof(PasswordValidationMessage));
        OnPropertyChanged(nameof(ApiKeyUrlValidationMessage));
    }
}
