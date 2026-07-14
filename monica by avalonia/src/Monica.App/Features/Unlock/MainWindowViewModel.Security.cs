using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _isWindowActive = true;

    [ObservableProperty]
    private bool _isPrivacyScreenVisible;

    public string LockVaultText => _localization.Get("LockVault");

    public bool EnableWindowCaptureProtection() =>
        _windowPrivacyService.EnableCaptureProtection();

    public void RecordUserActivity() => _vaultSessionService.RecordActivity();

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
        _vaultSessionService.MarkLocked();
        _cryptoService.Lock();
        IsUnlocked = false;
        ClearSensitiveSessionState();
        await ClearSettingsSensitiveCacheAsync();
        await ClearOwnedClipboardAsync();
        StatusMessage = _localization.Get("VaultLocked");
        IsPrivacyScreenVisible = !_isWindowActive;
    }

    partial void OnIsUnlockedChanged(bool value)
    {
        if (value)
        {
            _vaultSessionService.MarkUnlocked();
            IsPrivacyScreenVisible = false;
            return;
        }

        _vaultSessionService.MarkLocked();
    }

    private bool ShouldAutoLock() =>
        IsUnlocked &&
        AutoLockEnabled &&
        _vaultSessionService.IsExpired(TimeSpan.FromMinutes(AutoLockMinutes));

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
        try
        {
            await _settingsService.ClearSensitiveCacheAsync();
        }
        catch (Exception exception)
        {
            AppDiagnostics.Error("Settings secret cache clearing failed while locking", exception);
        }
    }

    private void ClearSensitiveSessionState()
    {
        CancelSensitiveBackgroundWork();
        ClearVaultCollections();
        ClearEditorAndTransferBuffers();
        ClearCredentialForms();
        ClearSensitiveCaches();
    }

    private void CancelSensitiveBackgroundWork()
    {
        _passwordSearchDebounceCts?.Cancel();
        _passwordSearchDebounceCts?.Dispose();
        _passwordSearchDebounceCts = null;
        _selectedPasswordDetailsCts?.Cancel();
        _selectedPasswordDetailsCts?.Dispose();
        _selectedPasswordDetailsCts = null;
    }

    private void ClearVaultCollections()
    {
        Passwords.Clear();
        ArchivedPasswords.Clear();
        DeletedPasswords.Clear();
        NoteItems.Clear();
        TotpItems.Clear();
        WalletItems.Clear();
        Categories.Clear();
        OpenNoteTabs.Clear();
        NoteImagePreviewItems.Clear();
        GeneratedPasswordHistory.Clear();
        TimelineEntries.Clear();
        SecuritySummaryItems.Clear();
        SecurityIssueItems.Clear();
        WebDavBackupHistory.Clear();
        MdbxDatabases.Clear();
        MdbxDatabaseItems.Clear();
        VaultSources.Clear();
    }

    private void ClearEditorAndTransferBuffers()
    {
        SelectedPassword = null;
        SelectedPasswordDetails = null;
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
        PasswordSearchText = "";
        SearchText = "";
        TotpSearchText = "";
        TotpNarrowShowsList = true;
        WalletSearchText = "";
        WalletNarrowShowsList = true;
        GeneratedPassword = "";
        ClearTransferBuffers();
    }

    private void ClearTransferBuffers()
    {
        ImportJsonText = "";
        ImportCsvText = "";
        ImportNoteCsvText = "";
        ImportAegisJsonText = "";
        ImportTotpCsvText = "";
        ExportPreview = "";
        ExportCsvPreview = "";
        ExportNoteCsvPreview = "";
        ExportTotpCsvPreview = "";
        ExportAegisPreview = "";
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
        _passwordCustomFields = new Dictionary<long, IReadOnlyList<CustomField>>();
        _passwordAttachments = new Dictionary<long, IReadOnlyList<Attachment>>();
        _passwordQuickAccessRecords = new Dictionary<long, PasswordQuickAccessRecord>();
        _compromisedPasswordResults = new Dictionary<long, CompromisedPasswordResult>();
        _hasCompromisedPasswordCheckResults = false;
        _exportPreviewAuthorizationExpiresAt = null;
        SelectedSecurityIssue = null;
        VaultLoadStageText = "";
        _vaultLoadVersion++;
        IsLoadingVault = false;
    }
}
