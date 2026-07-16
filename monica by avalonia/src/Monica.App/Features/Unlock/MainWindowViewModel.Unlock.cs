using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Core.Services;
using Monica.Data;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly IVaultUnlockCoordinator _vaultUnlockCoordinator;
    private LegacyVaultDetection _legacyVaultDetection = LegacyVaultDetection.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecoverableStatusMessage))]
    private bool _isUnlocked;

    public MainWindowViewModel? UnlockedShellContent =>
        IsUnlocked && !_isUnlockedShellHibernated ? this : null;

    [ObservableProperty]
    private bool _isVaultInitialized;

    [ObservableProperty]
    private string _masterPassword = "";

    [ObservableProperty]
    private string _confirmMasterPassword = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoginButtonText))]
    private bool _isUnlocking;

    [ObservableProperty]
    private bool _hasUnlockError;

    [ObservableProperty]
    private bool _isMasterPasswordVisible;

    [ObservableProperty]
    private bool _isConfirmMasterPasswordVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecoverableStatusMessage))]
    private bool _hasPendingLegacyBusinessData;

    public string LoginTitle => IsVaultAccessInitializing
        ? _localization.Get("PreparingVaultAccess")
        : IsVaultInitialized
            ? _localization.UnlockMonica
            : _localization.CreateMonicaVault;

    public string LoginDescription => IsVaultAccessInitializing
        ? _localization.Get("PreparingVaultAccessDescription")
        : IsVaultInitialized
            ? _localization.UnlockDescription
            : _localization.CreateVaultDescription;

    public string LoginButtonText => IsVaultAccessInitializing
        ? _localization.Get("PreparingVaultAccess")
        : IsUnlocking
            ? _localization.Get(IsVaultInitialized ? "UnlockingVault" : "CreatingVault")
            : IsVaultInitialized
                ? _localization.Unlock
                : _localization.CreateVault;

    public char MasterPasswordMaskChar => IsMasterPasswordVisible ? '\0' : '*';

    public char ConfirmMasterPasswordMaskChar => IsConfirmMasterPasswordVisible ? '\0' : '*';

    public string ToggleMasterPasswordVisibilityLabel =>
        IsMasterPasswordVisible ? _localization.HidePassword : _localization.ShowPassword;

    public string ToggleConfirmMasterPasswordVisibilityLabel =>
        IsConfirmMasterPasswordVisible ? _localization.HidePassword : _localization.ShowPassword;

    public string MasterPasswordPrivacyNotice => _localization.Get("MasterPasswordPrivacyNotice");

    public bool ShowCreateVaultPasswordGuidance =>
        IsVaultAccessReady &&
        !IsVaultInitialized &&
        !_legacyVaultDetection.RequiresImport;

    public bool IsCreateVaultPasswordLengthValid =>
        !string.IsNullOrWhiteSpace(MasterPassword) &&
        VaultMasterPasswordPolicy.MeetsMinimumLength(MasterPassword);

    public bool IsCreateVaultPasswordConfirmationValid =>
        !string.IsNullOrEmpty(ConfirmMasterPassword) &&
        string.Equals(MasterPassword, ConfirmMasterPassword, StringComparison.Ordinal);

    public bool IsCreateVaultPasswordInputValid =>
        IsCreateVaultPasswordLengthValid &&
        IsCreateVaultPasswordConfirmationValid;

    public string CreateVaultPasswordLengthStatusText =>
        _localization.Format(
            IsCreateVaultPasswordLengthValid
                ? "CreateVaultPasswordLengthRequirementMetFormat"
                : "CreateVaultPasswordLengthRequirementFormat",
            VaultMasterPasswordPolicy.MinimumLength);

    public string CreateVaultPasswordConfirmationStatusText =>
        string.IsNullOrEmpty(ConfirmMasterPassword)
            ? _localization.Get("MasterPasswordConfirmationRequired")
            : IsCreateVaultPasswordConfirmationValid
                ? _localization.Get("MasterPasswordConfirmationMatches")
                : _localization.Get("ConfirmationMismatch");

    public bool HasUnlockStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public bool HasLegacyVaultImportPrompt => _legacyVaultDetection.RequiresImport;

    public string LegacyVaultImportPromptText => _legacyVaultDetection.RequiresImport
        ? _localization.Format("LegacyVaultImportPromptFormat", _legacyVaultDetection.DatabasePath)
        : "";

    [RelayCommand(CanExecute = nameof(CanUnlockVault))]
    private async Task UnlockAsync()
    {
        if (IsUnlocking)
        {
            return;
        }

        IsUnlocking = true;
        HasUnlockError = false;
        try
        {
            HasPendingLegacyBusinessData = false;
            AppDiagnostics.Info(
                $"Unlock requested. initialized={IsVaultInitialized}, " +
                $"legacyImportRequired={_legacyVaultDetection.RequiresImport}");
            var result = await AppDiagnostics.MeasureAsync(
                "Unlock credential verification",
                () => _vaultUnlockCoordinator.UnlockOrCreateAsync(
                    MasterPassword,
                    ConfirmMasterPassword,
                    _legacyVaultDetection));
            AppDiagnostics.Info($"Unlock result={result.Status}");

            switch (result.Status)
            {
                case VaultUnlockStatus.MissingPassword:
                case VaultUnlockStatus.LegacyImportRequired:
                case VaultUnlockStatus.PasswordTooShort:
                case VaultUnlockStatus.ConfirmationMismatch:
                    IsVaultInitialized = result.IsVaultInitialized;
                    SetUnlockError(_localization.Get(result.MessageKey));
                    return;
                case VaultUnlockStatus.WrongPassword:
                    IsVaultInitialized = result.IsVaultInitialized;
                    _cryptoService.Lock();
                    IsUnlocked = false;
                    MasterPassword = "";
                    ConfirmMasterPassword = "";
                    IsMasterPasswordVisible = false;
                    IsConfirmMasterPasswordVisible = false;
                    SetUnlockError(_localization.Get(result.MessageKey));
                    return;
                case VaultUnlockStatus.Failed:
                    IsVaultInitialized = result.IsVaultInitialized || DefaultVaultDatabaseExists();
                    _cryptoService.Lock();
                    IsUnlocked = false;
                    SetUnlockError(_localization.Format(result.MessageKey, result.Error?.Message ?? ""));
                    return;
                case VaultUnlockStatus.CreatedAndUnlocked:
                case VaultUnlockStatus.Unlocked:
                    IsVaultInitialized = result.IsVaultInitialized;
                    await ReloadSettingsAfterUnlockAsync();
                    IsUnlocked = true;
                    HasPendingLegacyBusinessData = result.LegacyBusinessDataPending;
                    MasterPassword = "";
                    ConfirmMasterPassword = "";
                    IsMasterPasswordVisible = false;
                    IsConfirmMasterPasswordVisible = false;
                    StatusMessage = _localization.Format(
                        "VaultUnlockedLoadingFormat",
                        _localization.Get(result.MessageKey));
                    _ = LoadAfterUnlockAsync();
                    return;
                default:
                    _cryptoService.Lock();
                    IsUnlocked = false;
                    SetUnlockError(_localization.Format("UnlockFailedFormat", result.Status.ToString()));
                    return;
            }
        }
        catch (Exception exception)
        {
            _cryptoService.Lock();
            IsUnlocked = false;
            AppDiagnostics.Error("Unlock workflow failed", exception);
            SetUnlockError(_localization.Format("UnlockFailedFormat", exception.Message));
        }
        finally
        {
            IsUnlocking = false;
        }
    }

    private async Task ReloadSettingsAfterUnlockAsync()
    {
        await _settingsService.LoadAsync();
        ApplySettings(_settingsService.Current);
        ResumeSettingsSave();
    }

    private bool CanUnlockVault() =>
        IsVaultAccessReady &&
        !IsUnlocking &&
        !_legacyVaultDetection.RequiresImport &&
        !string.IsNullOrEmpty(MasterPassword) &&
        (IsVaultInitialized || IsCreateVaultPasswordInputValid);

    [RelayCommand]
    private void ToggleMasterPasswordVisibility()
    {
        IsMasterPasswordVisible = !IsMasterPasswordVisible;
    }

    [RelayCommand]
    private void ToggleConfirmMasterPasswordVisibility()
    {
        IsConfirmMasterPasswordVisible = !IsConfirmMasterPasswordVisible;
    }

    private void SetUnlockError(string message)
    {
        HasUnlockError = true;
        StatusMessage = message;
    }

    private void ClearUnlockErrorForCorrectedInput()
    {
        if (!HasUnlockError)
        {
            return;
        }

        HasUnlockError = false;
        StatusMessage = "";
    }

    private static string SanitizeMasterPassword(string value) =>
        new(value.Where(character => !char.IsControl(character)).ToArray());

    private void HandleMasterPasswordInputChanged(string value, bool isConfirmation)
    {
        var sanitized = SanitizeMasterPassword(value);
        if (!string.Equals(value, sanitized, StringComparison.Ordinal))
        {
            if (isConfirmation)
            {
                ConfirmMasterPassword = sanitized;
            }
            else
            {
                MasterPassword = sanitized;
            }

            SetUnlockError(_localization.Get("UnsupportedMasterPasswordCharactersRemoved"));
            UnlockCommand.NotifyCanExecuteChanged();
            return;
        }

        ClearUnlockErrorForCorrectedInput();
        RaiseCreateVaultPasswordGuidanceState();
        UnlockCommand.NotifyCanExecuteChanged();
    }

    private void RaiseCreateVaultPasswordGuidanceState()
    {
        OnPropertyChanged(nameof(ShowCreateVaultPasswordGuidance));
        OnPropertyChanged(nameof(IsCreateVaultPasswordLengthValid));
        OnPropertyChanged(nameof(IsCreateVaultPasswordConfirmationValid));
        OnPropertyChanged(nameof(IsCreateVaultPasswordInputValid));
        OnPropertyChanged(nameof(CreateVaultPasswordLengthStatusText));
        OnPropertyChanged(nameof(CreateVaultPasswordConfirmationStatusText));
    }

    partial void OnMasterPasswordChanged(string value) =>
        HandleMasterPasswordInputChanged(value, isConfirmation: false);

    partial void OnConfirmMasterPasswordChanged(string value) =>
        HandleMasterPasswordInputChanged(value, isConfirmation: true);

    partial void OnIsUnlockingChanged(bool value) => UnlockCommand.NotifyCanExecuteChanged();

    partial void OnIsMasterPasswordVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(MasterPasswordMaskChar));
        OnPropertyChanged(nameof(ToggleMasterPasswordVisibilityLabel));
    }

    partial void OnIsConfirmMasterPasswordVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(ConfirmMasterPasswordMaskChar));
        OnPropertyChanged(nameof(ToggleConfirmMasterPasswordVisibilityLabel));
    }

    partial void OnIsVaultInitializedChanged(bool value)
    {
        OnPropertyChanged(nameof(LoginTitle));
        OnPropertyChanged(nameof(LoginDescription));
        OnPropertyChanged(nameof(LoginButtonText));
        RaiseCreateVaultPasswordGuidanceState();
        UnlockCommand.NotifyCanExecuteChanged();
    }

    private void RaiseLegacyVaultImportPrompt()
    {
        OnPropertyChanged(nameof(HasLegacyVaultImportPrompt));
        OnPropertyChanged(nameof(LegacyVaultImportPromptText));
        RaiseCreateVaultPasswordGuidanceState();
        UnlockCommand.NotifyCanExecuteChanged();
    }

    private static bool DefaultVaultDatabaseExists()
    {
        try
        {
            return File.Exists(MonicaAppDataPaths.GetDatabasePath());
        }
        catch
        {
            return false;
        }
    }
}
