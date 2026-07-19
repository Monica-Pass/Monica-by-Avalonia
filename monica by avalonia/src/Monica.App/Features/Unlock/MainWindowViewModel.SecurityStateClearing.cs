using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
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
        ClearItems(DeletedSecureItems);
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
        HasPendingLegacyBusinessData = false;
        _pendingLegacyBusinessDataSignature = "";
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
