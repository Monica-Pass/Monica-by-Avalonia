namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    partial void OnSettingsLanguageChanged(string value)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        _localization.SetLanguage(value);
        _settingsService.Current.Language = value;
        QueueSaveSettings();
    }

    partial void OnSettingsThemeChanged(string value)
    {
        ApplyTheme(value);
        UpdateSettings(settings => settings.Theme = value);
    }

    partial void OnStartupSectionChanged(string value) => UpdateSettings(settings => settings.StartupSection = value);
    partial void OnAutoLockEnabledChanged(bool value) => UpdateSettings(settings => settings.AutoLockEnabled = value);
    partial void OnAutoLockMinutesChanged(int value) => UpdateSettings(settings => settings.AutoLockMinutes = value);
    partial void OnClearClipboardEnabledChanged(bool value) => UpdateSettings(settings => settings.ClearClipboardEnabled = value);
    partial void OnClipboardClearSecondsChanged(int value) => UpdateSettings(settings => settings.ClipboardClearSeconds = value);
    partial void OnRequirePasswordBeforeExportChanged(bool value) => UpdateSettings(settings => settings.RequirePasswordBeforeExport = value);

    partial void OnSecurityRecoveryEnabledChanged(bool value)
    {
        UpdateSettings(settings => settings.SecurityRecovery.IsEnabled = value);
        OnPropertyChanged(nameof(SecurityRecoveryStatusText));
        OnPropertyChanged(nameof(CanResetMasterPasswordWithSecurityQuestions));
        OnPropertyChanged(nameof(CanRunResetMasterPassword));
    }

    partial void OnIsResettingMasterPasswordChanged(bool value) => OnPropertyChanged(nameof(CanRunResetMasterPassword));

    partial void OnSecurityQuestion1IdChanged(int value)
    {
        if (!IsSecurityQuestion1Custom)
        {
            SecurityQuestion1CustomText = "";
        }

        OnPropertyChanged(nameof(IsSecurityQuestion1Custom));
    }

    partial void OnSecurityQuestion2IdChanged(int value)
    {
        if (!IsSecurityQuestion2Custom)
        {
            SecurityQuestion2CustomText = "";
        }

        OnPropertyChanged(nameof(IsSecurityQuestion2Custom));
    }
}
