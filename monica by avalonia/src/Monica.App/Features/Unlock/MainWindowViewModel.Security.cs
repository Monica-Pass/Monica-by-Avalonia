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

    public bool EnableWindowCaptureProtection() =>
        _windowPrivacyService.EnableCaptureProtection();

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

    private void ClearSensitiveSessionState()
    {
        CancelSensitiveBackgroundWork();
        ClearVaultCollections();
        ClearEditorAndTransferBuffers();
        CancelNoteImagePreviewRefresh();
        ClearCredentialForms();
        ClearSensitiveCaches();
    }

    private void CancelSensitiveBackgroundWork()
    {
        _oneDriveSignInCancellation?.Cancel();
        SuspendSecurityAnalysis();
        _passwordSearchDebounceCts?.Cancel();
        _passwordSearchDebounceCts?.Dispose();
        _passwordSearchDebounceCts = null;
        _selectedPasswordDetailsCts?.Cancel();
        _selectedPasswordDetailsCts?.Dispose();
        _selectedPasswordDetailsCts = null;
        CancelNoteImagePreviewRefresh();
    }

    private void ClearVaultCollections()
    {
        ClearItems(Passwords);
        ClearItems(ArchivedPasswords);
        ClearItems(DeletedPasswords);
        ClearItems(PasswordFolderFilters);
        ClearItems(NoteItems);
        ClearItems(TotpItems);
        ClearItems(TotpFilterChoices);
        ClearItems(WalletItems);
        ClearItems(Categories);
        ClearItems(OpenNoteTabs);
        ReplaceNoteImagePreviews([]);
        ClearGeneratedPasswordHistorySecrets();
        ClearItems(TimelineEntries);
        ClearItems(SecuritySummaryItems);
        ClearItems(SecurityIssueItems);
        ClearItems(WebDavBackupHistory);
        ClearItems(MdbxDatabases);
        ClearItems(MdbxDatabaseItems);
        ClearItems(VaultSources);
    }

    private void ClearEditorAndTransferBuffers()
    {
        SelectedPassword = null;
        SelectedPasswordDetails = null;
        SelectedPasswordFolderFilter = null;
        SelectedArchivedPassword = null;
        SelectedDeletedPassword = null;
        SelectedNote = null;
        SelectedNoteTab = null;
        SelectedTotpItem = null;
        SelectedTotpDetails = null;
        SelectedWalletItem = null;
        SelectedWalletDetails = null;
        NoteTitle = "";
        NoteContent = "";
        NoteTagsText = "";
        NewFolderName = "";
        SetPasswordSearchImmediately("");
        TotpSearchText = "";
        SelectedTotpFilterKey = TotpFilterAll;
        TotpNarrowShowsList = true;
        WalletSearchText = "";
        WalletNarrowShowsList = true;
        GeneratedPassword = "";
        ClearTransferBuffers();
    }

    private void ClearTransferBuffers()
    {
        ClearSensitiveImportBuffers();
        ClearSensitiveExportPreviews();
        ExportWalletCsvPreview = "";
        ExportTimelinePreview = "";
    }

    private void ClearCredentialForms()
    {
        MasterPassword = "";
        ConfirmMasterPassword = "";
        IsMasterPasswordVisible = false;
        IsConfirmMasterPasswordVisible = false;
        HasUnlockError = false;
        CurrentMasterPassword = "";
        NewMasterPassword = "";
        ConfirmNewMasterPassword = "";
        SecurityQuestion1Answer = "";
        SecurityQuestion2Answer = "";
        SecurityRecoveryAnswer1 = "";
        SecurityRecoveryAnswer2 = "";
        RecoveryNewMasterPassword = "";
        RecoveryConfirmNewMasterPassword = "";
        DangerZoneConfirmationText = "";
        ClearCachedRemoteCredentials();
    }

    private void ClearCachedRemoteCredentials()
    {
        var wasApplyingSettings = _isApplyingSettings;
        _isApplyingSettings = true;
        try
        {
            WebDavUsername = "";
            WebDavPassword = "";
            WebDavBackupEncryptionPassword = "";
        }
        finally
        {
            _isApplyingSettings = wasApplyingSettings;
        }
    }

    private void ClearSensitiveCaches()
    {
        ReleaseRepositoryVaultItemSnapshots();
        _passwordCustomFields = new Dictionary<long, IReadOnlyList<CustomField>>();
        _passwordCustomFieldSearchMatches = new HashSet<long>();
        _passwordCustomFieldSearchQuery = "";
        _passwordAttachmentOwnerIds = new HashSet<long>();
        _passwordAttachmentSearchMatches = new HashSet<long>();
        _passwordAttachmentSearchQuery = "";
        _passwordQuickAccessRecords = new Dictionary<long, PasswordQuickAccessRecord>();
        ClearSensitiveProjectionCaches();
        _compromisedPasswordResults = new Dictionary<long, CompromisedPasswordResult>();
        _hasCompromisedPasswordCheckResults = false;
        _exportPreviewAuthorizationExpiresAt = null;
        SelectedSecurityIssue = null;
        VaultLoadStageText = "";
        _vaultLoadVersion++;
        IsLoadingVault = false;
    }

    private void ClearSensitiveProjectionCaches()
    {
        ClearRebuildableProjectionCaches();
        _collapsedPasswordFolderKeys.Clear();
        _expandedPasswordStackKeys.Clear();
    }
}
