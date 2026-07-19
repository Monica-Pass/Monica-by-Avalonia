using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _isWindowActive = true;

    internal event EventHandler? AutoLockScheduleChanged;

    [ObservableProperty]
    private bool _isPrivacyScreenVisible;

    public string LockVaultText => _localization.Get("LockVault");

    public bool ApplyWindowCapturePolicy() =>
        _windowPrivacyService.SetCaptureProtection(WindowCaptureProtectionEnabled);

    public void RecordUserActivity()
    {
        _vaultSessionService.RecordActivity();
        NotifyAutoLockScheduleChanged();
    }

    internal bool TryGetAutoLockDelay(out TimeSpan delay)
    {
        delay = TimeSpan.Zero;
        if (!IsUnlocked || !AutoLockEnabled)
        {
            return false;
        }

        var timeout = GetAutoLockTimeout();
        var remaining = _vaultSessionService.GetRemainingInactivity(timeout);
        if (remaining is null)
        {
            return false;
        }

        delay = remaining.Value > TimeSpan.Zero
            ? remaining.Value
            : TimeSpan.FromMilliseconds(1);
        return true;
    }

    public async Task HandleWindowActivatedAsync()
    {
        _isWindowActive = true;
        if (ShouldAutoLock())
        {
            await LockAsync();
        }
        else
        {
            RecordUserActivity();
        }

        IsPrivacyScreenVisible = false;
    }

    public void HandleWindowDeactivated()
    {
        _isWindowActive = false;
        IsPrivacyScreenVisible = IsUnlocked;
    }

    public async Task CheckAutoLockAsync()
    {
        if (ShouldAutoLock())
        {
            await LockAsync();
        }
    }

    [RelayCommand]
    private async Task LockAsync()
    {
        var settingsSaveCompletion = SuspendSettingsSaveAsync();
        _vaultSessionService.MarkLocked();
        _cryptoService.Lock();
        IsUnlocked = false;
        ClearSensitiveSessionState();
        await settingsSaveCompletion;
        await ClearSettingsSensitiveCacheAsync();
        await ClearOwnedClipboardAsync();
        StatusMessage = _localization.Get("VaultLocked");
        IsPrivacyScreenVisible = !_isWindowActive;
    }

    partial void OnIsUnlockedChanged(bool value)
    {
        OnPropertyChanged(nameof(UnlockedShellContent));
        RaiseSecurityMaintenanceState();
        if (value)
        {
            _vaultSessionService.MarkUnlocked();
            IsPrivacyScreenVisible = false;
            NotifyAutoLockScheduleChanged();
            return;
        }

        CancelSensitiveBackgroundWork();
        _vaultSessionService.MarkLocked();
        NotifyAutoLockScheduleChanged();
    }

    private void NotifyAutoLockScheduleChanged() =>
        AutoLockScheduleChanged?.Invoke(this, EventArgs.Empty);

    private bool ShouldAutoLock() =>
        IsUnlocked &&
        AutoLockEnabled &&
        _vaultSessionService.IsExpired(GetAutoLockTimeout());

    private TimeSpan GetAutoLockTimeout() =>
        TimeSpan.FromMinutes(Math.Max(1, AutoLockMinutes));

    private async Task ClearOwnedClipboardAsync()
    {
        try
        {
            await _clipboardService.ClearOwnedContentAsync();
        }
        catch (Exception exception)
        {
            AppDiagnostics.Error("Clipboard clearing failed while locking", exception);
        }
    }

    private async Task ClearSettingsSensitiveCacheAsync()
    {
        await _settingsSensitiveCacheClearGate.WaitAsync();
        try
        {
            if (_settingsSensitiveCacheCleared)
            {
                return;
            }

            await _settingsService.ClearSensitiveCacheAsync();
            _settingsSensitiveCacheCleared = true;
        }
        catch (Exception exception)
        {
            AppDiagnostics.Error("Settings secret cache clearing failed while locking", exception);
        }
        finally
        {
            _settingsSensitiveCacheClearGate.Release();
        }
    }

}
