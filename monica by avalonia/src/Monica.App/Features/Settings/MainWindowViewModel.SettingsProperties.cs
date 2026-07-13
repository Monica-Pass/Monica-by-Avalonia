using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.App.Services;
using Monica.Core.Services;
using Monica.Data.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public const string GitHubRepositoryUrl = "https://github.com/JoyinJoester/Monica";

    private readonly IMasterPasswordMaintenanceService _masterPasswordMaintenanceService;
    private readonly IAppSettingsService _settingsService;
    private readonly SecurityQuestionService _securityQuestionService = new();
    private bool _isApplyingSettings;
    private readonly object _settingsSaveSync = new();
    private bool _isSavingSettings;
    private bool _hasPendingSettingsSave;

    public ObservableCollection<SettingsChoice> LanguageOptions { get; } = [];
    public ObservableCollection<SettingsChoice> ThemeOptions { get; } = [];
    public ObservableCollection<SettingsChoice> StartupSectionOptions { get; } = [];
    public ObservableCollection<SettingsChoice> AutoLockMinuteOptions { get; } = [];
    public ObservableCollection<SettingsChoice> ClipboardSecondOptions { get; } = [];
    public ObservableCollection<SettingsChoice> SecurityQuestionOptions { get; } = [];

    public string AboutTitle => _localization.Get("About");
    public string AboutDescription => _localization.Get("AboutDescription");
    public string AppVersionLabel => _localization.Get("AppVersion");
    public string GitHubRepositoryLabel => _localization.Get("GitHubRepository");
    public string OpenRepositoryText => _localization.Get("OpenRepository");
    public string RepositoryUrlText => GitHubRepositoryUrl;
    public string AppVersionText => GetAppVersionText();
    public string DangerZoneTitle => _localization.Get("DangerZone");
    public string DangerZoneDescription => _localization.Get("DangerZoneDescription");
    public string ClearVaultDataTitle => _localization.Get("ClearVaultData");
    public string ClearVaultDataDescription => _localization.Get("ClearVaultDataDescription");
    public string ClearPasswordsOnlyText => _localization.Get("ClearPasswordsOnly");
    public string ClearSecureItemsOnlyText => _localization.Get("ClearSecureItemsOnly");
    public string ClearAllVaultDataText => _localization.Get("ClearAllVaultData");
    public string ClearVaultConfirmationInstructionText =>
        _localization.Format("ClearVaultConfirmationInstructionFormat", _localization.Get("ClearVaultConfirmationPhrase"));
    public string ChangeMasterPasswordTitle => _localization.Get("ChangeMasterPassword");
    public string ChangeMasterPasswordDescription => _localization.Get("ChangeMasterPasswordDescription");
    public string CurrentMasterPasswordText => _localization.Get("CurrentMasterPassword");
    public string NewMasterPasswordText => _localization.Get("NewMasterPassword");
    public string ConfirmNewMasterPasswordText => _localization.Get("ConfirmNewMasterPassword");
    public string ChangeMasterPasswordActionText => _localization.Get("ChangeMasterPasswordAction");
    public string SecurityRecoveryTitle => _localization.Get("SecurityRecovery");
    public string SecurityRecoveryDescription => _localization.Get("SecurityRecoveryDescription");
    public string SecurityRecoveryStatusText => _settingsService.Current.SecurityRecovery.HasCompleteSetup
        ? _localization.Get("SecurityQuestionsConfigured")
        : _localization.Get("SecurityQuestionsNotConfigured");
    public string SecurityRecoveryEnabledText => _localization.Get("SecurityRecoveryEnabled");
    public string SecurityQuestion1Text => _localization.Get("SecurityQuestion1");
    public string SecurityQuestion2Text => _localization.Get("SecurityQuestion2");
    public string SecurityQuestionAnswerText => _localization.Get("SecurityQuestionAnswer");
    public string CustomSecurityQuestionText => _localization.Get("CustomSecurityQuestion");
    public string SaveSecurityQuestionsText => _localization.Get("SaveSecurityQuestions");
    public string ResetMasterPasswordTitle => _localization.Get("ResetMasterPassword");
    public string ResetMasterPasswordDescription => _localization.Get("ResetMasterPasswordDescription");
    public string ResetMasterPasswordActionText => _localization.Get("ResetMasterPasswordAction");
    public string SecurityRecoveryQuestion1PromptText => _settingsService.Current.SecurityRecovery.Question1Text;
    public string SecurityRecoveryQuestion2PromptText => _settingsService.Current.SecurityRecovery.Question2Text;
    public bool IsSecurityQuestion1Custom => SecurityQuestion1Id == SecurityQuestionService.CustomQuestionId;
    public bool IsSecurityQuestion2Custom => SecurityQuestion2Id == SecurityQuestionService.CustomQuestionId;
    public bool CanResetMasterPasswordWithSecurityQuestions => _settingsService.Current.SecurityRecovery.HasCompleteSetup;
    public bool CanRunResetMasterPassword => CanResetMasterPasswordWithSecurityQuestions && !IsResettingMasterPassword;

    public bool IsSettingsGeneralSelected => IsWorkspacePageSelected(SelectedSettingsPage, "General");
    public bool IsSettingsSecuritySelected => IsWorkspacePageSelected(SelectedSettingsPage, "Security");
    public bool IsSettingsSecurityRecoverySelected => IsWorkspacePageSelected(SelectedSettingsPage, "SecurityRecovery");
    public bool IsSettingsDataSelected => IsWorkspacePageSelected(SelectedSettingsPage, "Data");
    public bool IsSettingsDesktopSelected => IsWorkspacePageSelected(SelectedSettingsPage, "Desktop");
    public bool IsSettingsIntegrationsSelected => IsWorkspacePageSelected(SelectedSettingsPage, "Integrations");
    public bool IsSettingsAboutSelected => IsWorkspacePageSelected(SelectedSettingsPage, "About");
    public bool IsSettingsDangerSelected => IsWorkspacePageSelected(SelectedSettingsPage, "Danger");

    [ObservableProperty]
    private string _currentMasterPassword = "";

    [ObservableProperty]
    private string _newMasterPassword = "";

    [ObservableProperty]
    private string _confirmNewMasterPassword = "";

    [ObservableProperty]
    private bool _isChangingMasterPassword;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsGeneralSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsSecuritySelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsSecurityRecoverySelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsDataSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsDesktopSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsIntegrationsSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsAboutSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsDangerSelected))]
    private string _selectedSettingsPage = "General";

    [ObservableProperty]
    private string _settingsLanguage = "system";

    [ObservableProperty]
    private string _settingsTheme = "system";

    [ObservableProperty]
    private string _startupSection = "Passwords";

    [ObservableProperty]
    private bool _autoLockEnabled = true;

    [ObservableProperty]
    private int _autoLockMinutes = 5;

    [ObservableProperty]
    private bool _clearClipboardEnabled = true;

    [ObservableProperty]
    private int _clipboardClearSeconds = 30;

    [ObservableProperty]
    private bool _requirePasswordBeforeExport = true;

    [ObservableProperty]
    private bool _securityRecoveryEnabled;

    [ObservableProperty]
    private int _securityQuestion1Id = 11;

    [ObservableProperty]
    private string _securityQuestion1CustomText = "";

    [ObservableProperty]
    private string _securityQuestion1Answer = "";

    [ObservableProperty]
    private int _securityQuestion2Id = 1;

    [ObservableProperty]
    private string _securityQuestion2CustomText = "";

    [ObservableProperty]
    private string _securityQuestion2Answer = "";

    [ObservableProperty]
    private string _securityRecoveryAnswer1 = "";

    [ObservableProperty]
    private string _securityRecoveryAnswer2 = "";

    [ObservableProperty]
    private string _recoveryNewMasterPassword = "";

    [ObservableProperty]
    private string _recoveryConfirmNewMasterPassword = "";

    [ObservableProperty]
    private bool _isResettingMasterPassword;

    [ObservableProperty]
    private string _dangerZoneConfirmationText = "";
}
