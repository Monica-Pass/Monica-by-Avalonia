using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private int _bitwardenConflictsRead;

    public ObservableCollection<BitwardenConflictDisplayItem> BitwardenConflicts { get; } = [];

    public bool HasBitwardenConflicts => BitwardenConflicts.Count > 0;

    public bool CanResolveBitwardenConflicts => _bitwardenConflictRestoreService is not null && !IsBitwardenBusy;

    private async Task LoadBitwardenConflictsAsync()
    {
        // The read is never awaited by its caller, so several can be in flight - the view attaching, an
        // account switch, a finished sync each ask for one. Every read carries the number of the newest
        // request, and only the newest may write the list: otherwise the rows of the account just left
        // land under the account now selected, where restoring one would be a lookup the new vault loses.
        var requestId = Interlocked.Increment(ref _bitwardenConflictsRead);
        var account = SelectedBitwardenAccount;
        if (account is null || _bitwardenConflictRestoreService is null || !IsUnlocked)
        {
            BitwardenConflicts.Clear();
            RaiseBitwardenState();
            return;
        }

        List<BitwardenConflictDisplayItem> rows;
        try
        {
            var summaries = await _bitwardenConflictRestoreService.GetSummariesAsync(
                account.Id,
                _vaultSessionService.SessionCancellationToken);
            rows = summaries
                .Select(summary => new BitwardenConflictDisplayItem(
                    summary.BackupId,
                    summary.Title,
                    _localization.Get(summary.IsPassword
                        ? "BitwardenConflictKindLogin"
                        : "BitwardenConflictKindNote"),
                    _localization.Format(
                        "BitwardenConflictSavedFormat",
                        summary.CreatedAt.ToLocalTime().ToString("g", _localization.Culture))))
                .ToList();
        }
        catch (OperationCanceledException) when (!IsUnlocked)
        {
            return;
        }
        catch (Exception exception)
        {
            AppDiagnostics.Error($"Bitwarden conflicts could not be loaded for account {account.Id}", exception);
            if (requestId == Volatile.Read(ref _bitwardenConflictsRead))
            {
                BitwardenConflicts.Clear();
            }

            return;
        }

        if (requestId != Volatile.Read(ref _bitwardenConflictsRead))
        {
            return;
        }

        BitwardenConflicts.Clear();
        foreach (var row in rows)
        {
            BitwardenConflicts.Add(row);
        }

        RaiseBitwardenState();
    }

    [RelayCommand]
    private Task RestoreBitwardenConflictAsync(BitwardenConflictDisplayItem? conflict) =>
        ResolveBitwardenConflictAsync(conflict, restore: true);

    [RelayCommand]
    private Task DiscardBitwardenConflictAsync(BitwardenConflictDisplayItem? conflict) =>
        ResolveBitwardenConflictAsync(conflict, restore: false);

    private async Task ResolveBitwardenConflictAsync(BitwardenConflictDisplayItem? conflict, bool restore)
    {
        var account = SelectedBitwardenAccount;
        if (account is null || conflict is null || _bitwardenConflictRestoreService is null ||
            !TryBeginBitwardenOnlineOperation())
        {
            return;
        }

        try
        {
            if (restore)
            {
                await _bitwardenConflictRestoreService.RestoreAsync(
                    account.Id,
                    conflict.BackupId,
                    _bitwardenSyncOperationCancellation!.Token);

                // The restore writes through the repository, so until the vault is read again the row on
                // screen still carries the title the pull overwrote it with, and "restored" would only be
                // true after another lock and unlock.
                await ReloadVaultKeepingSelectionAsync();
            }
            else
            {
                await _bitwardenConflictRestoreService.DiscardAsync(
                    account.Id,
                    conflict.BackupId,
                    _bitwardenSyncOperationCancellation!.Token);
            }

            SetStatusNotice(restore ? "BitwardenConflictRestored" : "BitwardenConflictDiscarded");
        }
        catch (OperationCanceledException) when (!IsUnlocked)
        {
            BitwardenOperationError = _localization.Get("BitwardenRequiresUnlockedVault");
        }
        catch (Exception exception)
        {
            AppDiagnostics.Error("A Bitwarden conflict could not be resolved", exception);
            BitwardenOperationError = _localization.Get(restore
                ? "BitwardenConflictRestoreFailed"
                : "BitwardenConflictDiscardFailed");
        }
        finally
        {
            EndBitwardenOnlineOperation();
        }

        // The account card carries the conflict count, and a restore re-opens an upload the next
        // synchronization owes, so both the card and this list have to be re-read.
        await LoadBitwardenAccountsAsync();
        await LoadBitwardenConflictsAsync();
    }
}

/// <summary>
/// A row of the conflict list. Only the title is shown: the stored payload holds plaintext field
/// values, and a list of overwritten entries has no reason to render one.
/// </summary>
public sealed record BitwardenConflictDisplayItem(
    long BackupId,
    string Title,
    string KindText,
    string SavedText);
