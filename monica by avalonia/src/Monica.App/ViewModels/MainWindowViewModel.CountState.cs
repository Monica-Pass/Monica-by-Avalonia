namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void RaiseAllCountState()
    {
        RaisePasswordCountState();
        RaiseNoteCountState();
        RaiseTotpCountState();
        RaiseWalletCountState();
        RaiseTimelineCountState();
        RaiseSecurityCountState();
        RaiseVaultSourceCountState();
    }

    private void RaisePasswordCountState()
    {
        RefreshArchiveCountState();
        RefreshRecycleBinCountState();
        OnPropertyChanged(nameof(PasswordCountText));
        OnPropertyChanged(nameof(ArchiveEmptyStateText));
        OnPropertyChanged(nameof(RecycleBinEmptyStateText));
    }

    private void RaiseNoteCountState()
    {
        OnPropertyChanged(nameof(NoteCountText));
        RaiseNoteTreeState();
    }

    private void RaiseTotpCountState(bool reconcileSelection = true)
    {
        OnPropertyChanged(nameof(TotpCountText));
        OnPropertyChanged(nameof(HasTotpItems));
        RaiseTotpFilterState(reconcileSelection);
        RaiseTotpSelectionState();
    }

    private void RaiseWalletCountState(bool reconcileSelection = true)
    {
        OnPropertyChanged(nameof(WalletCountText));
        OnPropertyChanged(nameof(HasWalletItems));
        RaiseWalletFilterState(reconcileSelection);
        RaiseWalletSelectionState();
    }

    private void RaiseTimelineCountState()
    {
        OnPropertyChanged(nameof(TimelineEmptyStateText));
        OnPropertyChanged(nameof(TimelineCountText));
        OnPropertyChanged(nameof(HasTimelineEntries));
    }

    private void RaiseSecurityCountState()
    {
        OnPropertyChanged(nameof(SecurityIssueCountText));
        OnPropertyChanged(nameof(HasSecurityIssues));
    }

    private void RaiseVaultSourceCountState()
    {
        OnPropertyChanged(nameof(LocalDatabaseSummaryText));
        OnPropertyChanged(nameof(MdbxDatabaseCountText));
        RaiseMdbxVaultState();
        OnPropertyChanged(nameof(VaultSourceCountText));
    }
}
