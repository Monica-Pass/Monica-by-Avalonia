using Monica.App.Services;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static readonly TimeSpan SettingsSaveDebounce = TimeSpan.FromMilliseconds(150);

    private void ApplySettings(DesktopAppSettings settings)
    {
        _settingsSensitiveCacheCleared = false;
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
            WindowCaptureProtectionEnabled = settings.WindowCaptureProtectionEnabled;
            RecycleBinRetentionDays = settings.RecycleBinRetentionDays;
            ApplySecurityRecoverySettings(settings.SecurityRecovery);
            MinimizeToTray = settings.MinimizeToTray && CanUseTrayIntegration;
            QuickSearchEnabled = settings.QuickSearchEnabled && CanUseGlobalHotkeyIntegration;
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
        lock (_settingsSaveSync)
        {
            if (_settingsSaveSuspended)
            {
                return;
            }

            _hasPendingSettingsSave = true;
            if (_isSavingSettings)
            {
                return;
            }

            StartSettingsSaveWorkerUnsafe();
        }
    }

    private void StartSettingsSaveWorkerUnsafe()
    {
        _isSavingSettings = true;
        _settingsSaveCancellation = new CancellationTokenSource();
        _settingsSaveTask = SaveSettingsAsync(_settingsSaveCancellation);
    }

    private async Task SaveSettingsAsync(CancellationTokenSource saveCancellation)
    {
        var cancellationToken = saveCancellation.Token;
        var statusBeforeSave = StatusMessage;
        try
        {
            while (true)
            {
                await Task.Delay(SettingsSaveDebounce, cancellationToken);
                lock (_settingsSaveSync)
                {
                    if (_settingsSaveSuspended || !_hasPendingSettingsSave)
                    {
                        return;
                    }

                    _hasPendingSettingsSave = false;
                }

                // Once file persistence begins, let it finish atomically before a
                // Vault lock clears sensitive settings from the in-memory cache.
                await _settingsService.SaveAsync();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error("Settings save failed", ex);
            if (string.Equals(StatusMessage, statusBeforeSave, StringComparison.Ordinal))
            {
                StatusMessage = _localization.Get("SettingsSaveFailed");
            }
        }
        finally
        {
            lock (_settingsSaveSync)
            {
                if (ReferenceEquals(_settingsSaveCancellation, saveCancellation))
                {
                    _settingsSaveCancellation = null;
                    if (!_settingsSaveSuspended && _hasPendingSettingsSave)
                    {
                        StartSettingsSaveWorkerUnsafe();
                    }
                    else
                    {
                        _settingsSaveTask = Task.CompletedTask;
                        _isSavingSettings = false;
                        _hasPendingSettingsSave = false;
                    }
                }
            }

            saveCancellation.Dispose();
        }
    }

    private async Task SuspendSettingsSaveAsync()
    {
        Task saveTask;
        lock (_settingsSaveSync)
        {
            _settingsSaveSuspended = true;
            _hasPendingSettingsSave = false;
            _settingsSaveCancellation?.Cancel();
            saveTask = _settingsSaveTask;
        }

        await saveTask;
    }

    private void ResumeSettingsSave()
    {
        lock (_settingsSaveSync)
        {
            _settingsSaveSuspended = false;
        }
    }
}
