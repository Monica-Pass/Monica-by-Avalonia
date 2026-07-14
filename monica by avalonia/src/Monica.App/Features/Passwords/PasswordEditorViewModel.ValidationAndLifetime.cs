using CommunityToolkit.Mvvm.ComponentModel;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public enum PasswordEditorValidationTarget
{
    None,
    Title,
    Password
}

public sealed partial class PasswordEditorViewModel
{
    [ObservableProperty]
    private string _validationMessage = "";

    public PasswordEditorValidationTarget ValidationTarget { get; private set; }
    public bool HasTitleValidationError => ValidationTarget == PasswordEditorValidationTarget.Title;
    public bool HasPasswordValidationError => ValidationTarget == PasswordEditorValidationTarget.Password;
    public string TitleValidationMessage => HasTitleValidationError ? L.Get("PasswordTitleRequired") : "";
    public string PasswordValidationMessage => HasPasswordValidationError ? L.Get("PasswordValueRequired") : "";
    public bool IsSensitiveStateCleared { get; private set; }

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            SetValidation(PasswordEditorValidationTarget.Title, L.Get("PasswordTitleRequired"));
            return false;
        }

        if (GetPasswordRows().Count == 0 && SelectedLoginType?.Value != PasswordLoginType.Sso)
        {
            SetValidation(PasswordEditorValidationTarget.Password, L.Get("PasswordValueRequired"));
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
        CustomFieldsText = "";
        CustomIconValue = "";
        IsPasswordVisible = false;
        SelectedCategory = null;
        SelectedLoginType = null;
        SelectedBoundNote = null;
        SelectedCustomIconType = null;
        CategoryOptions.Clear();
        LoginTypeOptions.Clear();
        BoundNoteOptions.Clear();
        CustomIconTypeOptions.Clear();
        ClearValidation();
        IsSensitiveStateCleared = true;
        OnPropertyChanged(nameof(IsSensitiveStateCleared));
    }

    partial void OnTitleChanged(string value)
    {
        if (HasTitleValidationError && !string.IsNullOrWhiteSpace(value))
        {
            ClearValidation();
        }
    }

    partial void OnSelectedLoginTypeChanged(PasswordLoginTypeChoice? value) => ClearCorrectedPasswordValidation();

    private void ClearCorrectedPasswordValidation()
    {
        if (HasPasswordValidationError &&
            (GetPasswordRows().Count > 0 || SelectedLoginType?.Value == PasswordLoginType.Sso))
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
        OnPropertyChanged(nameof(TitleValidationMessage));
        OnPropertyChanged(nameof(PasswordValidationMessage));
    }
}
