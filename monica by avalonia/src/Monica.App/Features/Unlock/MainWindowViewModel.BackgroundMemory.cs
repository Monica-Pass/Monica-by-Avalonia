namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _isUnlockedShellHibernated;

    internal void SetUnlockedShellHibernated(bool isHibernated)
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

    private void ReleaseRebuildableBackgroundCaches()
    {
        CancelNoteImagePreviewRefresh();
        Interlocked.Increment(ref _noteImagePreviewVersion);
        ReplaceNoteImagePreviews([]);
        ClearRebuildableProjectionCaches();
    }

    private void RestoreRebuildableBackgroundCaches()
    {
        if (IsUnlocked && string.Equals(SelectedSection, "Notes", StringComparison.OrdinalIgnoreCase))
        {
            QueueNoteImagePreviewRefresh(NoteContent);
        }

        RefreshSecurityAnalysisIfNeeded();
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
}
