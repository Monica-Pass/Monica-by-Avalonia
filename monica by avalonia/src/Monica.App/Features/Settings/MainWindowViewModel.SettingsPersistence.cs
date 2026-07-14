using Monica.App.Services;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static readonly TimeSpan SettingsSaveDebounce = TimeSpan.FromMilliseconds(150);

    private void ApplySettings(DesktopAppSettings settings)
    {
        _isApplyingSettings = true;
        try
        {
            _localization.SetLanguage(settings.Language);
            SettingsLanguage = settings.Language;
            SettingsTheme = settings.Theme;
            StartupSection = settings.StartupSection;
            AutoLockEnabled = settings.AutoLockEnabled;
            AutoLockMinutes = settings.AutoLockMinutes;
            ClearClipboardEnabled = settings.ClearClipboardEnabled;
            ClipboardClearSeconds = settings.ClipboardClearSeconds;
            RequirePasswordBeforeExport = settings.RequirePasswordBeforeExport;
            ApplySecurityRecoverySettings(settings.SecurityRecovery);
            MinimizeToTray = settings.MinimizeToTray && CanUseTrayIntegration;
            QuickSearchEnabled = settings.QuickSearchEnabled;
            QuickSearchHotkey = settings.QuickSearchHotkey;
            BrowserIntegrationEnabled = settings.BrowserIntegrationEnabled && CanUseBrowserBridgeIntegration;
            BrowserIntegrationPort = settings.BrowserIntegrationPort;
            CompactPasswordList = settings.CompactPasswordList;
            SelectedPasswordSort = settings.PasswordSortOrder;
            WebDavEnabled = settings.WebDavEnabled;
            WebDavServerUrl = settings.WebDavServerUrl;
            WebDavUsername = settings.WebDavUsername;
            WebDavPassword = settings.WebDavPassword;
            WebDavRemotePath = settings.WebDavRemotePath;
            WebDavSyncOnStartup = settings.WebDavSyncOnStartup;
            WebDavSyncAfterChanges = settings.WebDavSyncAfterChanges;
            WebDavBackupIncludePasswords = settings.WebDavBackupIncludePasswords;
            WebDavBackupIncludeTotp = settings.WebDavBackupIncludeTotp;
            WebDavBackupIncludeNotes = settings.WebDavBackupIncludeNotes;
            WebDavBackupIncludeCards = settings.WebDavBackupIncludeCards;
            WebDavBackupIncludeDocuments = settings.WebDavBackupIncludeDocuments;
            WebDavBackupIncludeImages = settings.WebDavBackupIncludeImages;
            WebDavBackupIncludeCategories = settings.WebDavBackupIncludeCategories;
            WebDavBackupEncryptionEnabled = settings.WebDavBackupEncryptionEnabled;
            WebDavBackupEncryptionPassword = settings.WebDavBackupEncryptionPassword;
            SyncConflictStrategy = settings.SyncConflictStrategy;
            OneDriveEnabled = settings.OneDriveEnabled;
            MdbxLocalCacheEnabled = settings.MdbxLocalCacheEnabled;
            ApplyTheme(settings.Theme);
            ApplyClipboardPolicy();
            RefreshLocalizedProperties();
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private void ApplyClipboardPolicy()
    {
        var lifetime = ClearClipboardEnabled
            ? TimeSpan.FromSeconds(ClipboardClearSeconds)
            : (TimeSpan?)null;
        _clipboardService.ConfigureSensitiveClear(lifetime);
    }

    private void ApplySecurityRecoverySettings(SecurityRecoverySettings settings)
    {
        var wasApplyingSettings = _isApplyingSettings;
        _isApplyingSettings = true;
        try
        {
            SecurityRecoveryEnabled = settings.IsEnabled;
            SecurityQuestion1Id = settings.Question1Id;
            SecurityQuestion1CustomText = settings.Question1Id == SecurityQuestionService.CustomQuestionId ? settings.Question1Text : "";
            SecurityQuestion1Answer = "";
            SecurityQuestion2Id = settings.Question2Id;
            SecurityQuestion2CustomText = settings.Question2Id == SecurityQuestionService.CustomQuestionId ? settings.Question2Text : "";
            SecurityQuestion2Answer = "";
            OnPropertyChanged(nameof(SecurityRecoveryStatusText));
            OnPropertyChanged(nameof(SecurityRecoveryQuestion1PromptText));
            OnPropertyChanged(nameof(SecurityRecoveryQuestion2PromptText));
            OnPropertyChanged(nameof(CanResetMasterPasswordWithSecurityQuestions));
            OnPropertyChanged(nameof(CanRunResetMasterPassword));
            OnPropertyChanged(nameof(IsSecurityQuestion1Custom));
            OnPropertyChanged(nameof(IsSecurityQuestion2Custom));
        }
        finally
        {
            _isApplyingSettings = wasApplyingSettings;
        }
    }

    private void UpdateSettings(Action<DesktopAppSettings> update)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        update(_settingsService.Current);
        QueueSaveSettings();
    }

    private void QueueSaveSettings()
    {
        var shouldStartSave = false;
        lock (_settingsSaveSync)
        {
            _hasPendingSettingsSave = true;
            if (!_isSavingSettings)
            {
                _isSavingSettings = true;
                shouldStartSave = true;
            }
        }

        if (shouldStartSave)
        {
            _ = SaveSettingsAsync();
        }
    }

    private async Task SaveSettingsAsync()
    {
        var statusBeforeSave = StatusMessage;
        try
        {
            while (true)
            {
                await Task.Delay(SettingsSaveDebounce);
                lock (_settingsSaveSync)
                {
                    if (!_hasPendingSettingsSave)
                    {
                        _isSavingSettings = false;
                        return;
                    }

                    _hasPendingSettingsSave = false;
                }

                await _settingsService.SaveAsync();
            }
        }
        catch (Exception ex)
        {
            lock (_settingsSaveSync)
            {
                _isSavingSettings = false;
                _hasPendingSettingsSave = false;
            }

            AppDiagnostics.Error("Settings save failed", ex);
            if (string.Equals(StatusMessage, statusBeforeSave, StringComparison.Ordinal))
            {
                StatusMessage = _localization.Format("SettingsSaveFailedFormat", ex.Message);
            }
        }
    }
}
