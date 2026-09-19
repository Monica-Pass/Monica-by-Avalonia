using Monica.Core.Services;
using Monica.Data.Repositories;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _isUnlockedShellHibernated;
    private bool _isHibernatedByWindow;
    private bool _isHibernatedByLock;
    private int _memorySheddingSequence;

    private bool IsShellHibernated => _isHibernatedByWindow || _isHibernatedByLock;

    internal void SetShellHibernatedByWindow(bool isHibernated) =>
        ApplyHibernationReason(ref _isHibernatedByWindow, isHibernated);

    internal void SetShellHibernatedByLock(bool isHibernated)
    {
        if (!ApplyHibernationReason(ref _isHibernatedByLock, isHibernated))
        {
            return;
        }

        if (isHibernated)
        {
            SchedulePostLockMemoryCompaction();
        }
    }

    private bool ApplyHibernationReason(ref bool reasonFlag, bool value)
    {
        if (reasonFlag == value)
        {
            return false;
        }

        reasonFlag = value;
        SetUnlockedShellHibernated(IsShellHibernated);
        return true;
    }

    private void SetUnlockedShellHibernated(bool isHibernated)
    {
        if (_isUnlockedShellHibernated == isHibernated)
        {
            return;
        }

        _isUnlockedShellHibernated = isHibernated;
        OnPropertyChanged(nameof(UnlockedShellContent));
        if (isHibernated)
        {
            ReleaseRebuildableBackgroundCaches();
        }
        else
        {
            RestoreRebuildableBackgroundCaches();
        }
    }

    // Only a full blocking compaction returns the freed segments; safe to block because no vault data is on screen.
    private void SchedulePostLockMemoryCompaction()
    {
        var sequence = Interlocked.Increment(ref _memorySheddingSequence);
        _ = ShedLockedMemoryAsync(sequence);
    }

    private async Task ShedLockedMemoryAsync(int sequence)
    {
        await Task.Delay(TimeSpan.FromSeconds(1));
        if (sequence != Volatile.Read(ref _memorySheddingSequence) || IsUnlocked)
        {
            return;
        }

        GC.Collect(
            GC.MaxGeneration,
            GCCollectionMode.Aggressive,
            blocking: true,
            compacting: true);
    }

    private void ReleaseRebuildableBackgroundCaches()
    {
        ReleaseSensitiveBackgroundDetails();
        ReleaseTransientBackgroundSecrets();
        SuspendPasswordSearchProjectionUpdates();
        SuspendSecurityAnalysis();
        ReleaseRepositoryVaultItemSnapshots();
        (_pwnedPasswordService as ITransientPwnedPasswordCache)?.ClearCachedRanges();
        CancelNoteImagePreviewRefresh();
        Interlocked.Increment(ref _noteImagePreviewVersion);
        ReplaceNoteImagePreviews([]);
        ClearRebuildableProjectionCaches();
    }

    private void RestoreRebuildableBackgroundCaches()
    {
        RestoreActiveWorkspaceState();
        if (IsUnlocked && string.Equals(SelectedSection, "Generator", StringComparison.OrdinalIgnoreCase))
        {
            EnsureGeneratedPassword();
        }

        if (IsUnlocked && string.Equals(SelectedSection, "Notes", StringComparison.OrdinalIgnoreCase))
        {
            QueueNoteImagePreviewRefresh(NoteContent);
        }

        RefreshSecurityAnalysisIfNeeded();
    }

    private void ReleaseSensitiveBackgroundDetails()
    {
        Interlocked.Increment(ref _selectedPasswordDetailsVersion);
        CancelSelectedPasswordDetailsRefresh();
        IsLoadingSelectedPasswordDetails = false;
        SelectedPasswordDetailsError = null;
        var passwordDetails = SelectedPasswordDetails;
        SelectedPasswordDetails = null;
        passwordDetails?.Dispose();

        SelectedTotpDetails = null;
        SelectedWalletDetails = null;
    }

    private void ReleaseTransientBackgroundSecrets()
    {
        ClearTransientSettingsSecurityInputs();
        ClearTransferBuffers();
        GeneratedPassword = "";
        ClearGeneratedPasswordHistorySecrets();
    }

    private void RestoreActiveWorkspaceState()
    {
        if (_isUnlockedShellHibernated || !IsUnlocked)
        {
            return;
        }

        RestorePasswordSearchQueryIfActive();
        RestoreBackgroundDetailState();
    }

    private void RestoreBackgroundDetailState()
    {
        switch (SelectedSection)
        {
            case "Passwords" when SelectedPassword is not null:
                QueueSelectedPasswordDetailsRefresh(SelectedPassword);
                break;
            case "Totp" when
                SelectedTotpItem is not null &&
                SelectedTotpDetails?.Item.Id != SelectedTotpItem.Id:
                RefreshTotpDisplay(SelectedTotpItem);
                SelectedTotpDetails = new TotpItemDetailsViewModel(_localization, SelectedTotpItem);
                break;
            case "Cards" when
                SelectedWalletItem is not null &&
                SelectedWalletDetails?.Item.Id != SelectedWalletItem.Id:
                SelectedWalletDetails = new WalletItemDetailsViewModel(_localization, SelectedWalletItem);
                break;
        }
    }

    private void ClearRebuildableProjectionCaches()
    {
        _filteredPasswords = [];
        _filteredPasswordRows = [];
        _filteredPasswordsDirty = true;
        _filteredPasswordRowsDirty = true;
        _filteredTotpItems = [];
        _filteredTotpItemsDirty = true;
        _filteredWalletItems = [];
        _filteredWalletItemsDirty = true;
        _filteredNoteItems = [];
        _favoriteNoteItems = [];
        _noteTreeGroups = [];
        _favoriteNoteCount = 0;
        _noteTreeProjectionDirty = true;
        ClearSensitiveNoteEditorProjectionCaches();
        ClearItems(SecuritySummaryItems);
        ClearItems(SecurityIssueItems);
        ClearItems(FilteredSecurityIssueItems);
        SelectedSecurityIssue = null;
        _isSecurityAnalysisDirty = true;
    }

    private void ReleaseRepositoryVaultItemSnapshots() =>
        (_repository as ITransientVaultReadCache)?.ReleaseVaultItemSnapshots();
}
