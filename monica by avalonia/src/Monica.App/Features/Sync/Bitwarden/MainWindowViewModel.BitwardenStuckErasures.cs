using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Monica.Core.Bitwarden;
using Monica.Data.Bitwarden;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private int _bitwardenStuckErasuresRead;

    public ObservableCollection<BitwardenStuckEraseDisplayItem> BitwardenStuckErasures { get; } = [];

    public bool HasBitwardenStuckErasures => BitwardenStuckErasures.Count > 0;

    public bool CanResolveBitwardenStuckErasures =>
        _bitwardenStuckEraseService is not null && !IsBitwardenBusy;

    private async Task LoadBitwardenStuckErasuresAsync()
    {
        // Same rule as the conflict list: nobody awaits this read, so an account switch and a finished
        // synchronization can both have one in flight, and only the newest may write the list.
        var requestId = Interlocked.Increment(ref _bitwardenStuckErasuresRead);
        var account = SelectedBitwardenAccount;
        if (account is null || _bitwardenStuckEraseService is null || !IsUnlocked)
        {
            BitwardenStuckErasures.Clear();
            RaiseBitwardenState();
            return;
        }

        List<BitwardenStuckEraseDisplayItem> rows;
        try
        {
            var erasures = await _bitwardenStuckEraseService.GetStuckAsync(
                account.Id,
                _vaultSessionService.SessionCancellationToken);
            rows = erasures
                .Select(erase => new BitwardenStuckEraseDisplayItem(
                    erase.OperationId,
                    _localization.Format("BitwardenStuckEraseCipherFormat", erase.CipherId),
                    _localization.Get(erase.Status == BitwardenMutationStatus.Conflict
                        ? "BitwardenStuckEraseReasonConflict"
                        : "BitwardenStuckEraseReasonFailed"),
                    _localization.Format(
                        "BitwardenStuckEraseLastAttemptFormat",
                        erase.LastAttemptAt.ToLocalTime().ToString("g", _localization.Culture))))
                .ToList();
        }
        catch (OperationCanceledException) when (!IsUnlocked)
        {
            return;
        }
        catch (Exception exception)
        {
            AppDiagnostics.Error(
                $"Bitwarden stuck erasures could not be loaded for account {account.Id}",
                exception);
            if (requestId == Volatile.Read(ref _bitwardenStuckErasuresRead))
            {
                BitwardenStuckErasures.Clear();
            }

            return;
        }

        if (requestId != Volatile.Read(ref _bitwardenStuckErasuresRead))
        {
            return;
        }

        BitwardenStuckErasures.Clear();
        foreach (var row in rows)
        {
            BitwardenStuckErasures.Add(row);
        }

        RaiseBitwardenState();
    }

    [RelayCommand]
    private async Task AbandonBitwardenStuckEraseAsync(BitwardenStuckEraseDisplayItem? erase)
    {
        var account = SelectedBitwardenAccount;
        if (account is null || erase is null || _bitwardenStuckEraseService is null ||
            !TryBeginBitwardenOnlineOperation())
        {
            return;
        }

        try
        {
            await _bitwardenStuckEraseService.AbandonAsync(
                account.Id,
                erase.OperationId,
                _bitwardenSyncOperationCancellation!.Token);
            // Deliberately not followed by a synchronization: this decision is local, and running a full
            // sync would push whatever else the queue holds at the same moment the user pressed a button
            // about one entry. The notice says what has to happen for the entry to come back.
            SetStatusNotice("BitwardenStuckEraseAbandoned");
        }
        catch (OperationCanceledException) when (!IsUnlocked)
        {
            BitwardenOperationError = _localization.Get("BitwardenRequiresUnlockedVault");
        }
        catch (Exception exception)
        {
            AppDiagnostics.Error("A stuck Bitwarden erase could not be abandoned", exception);
            BitwardenOperationError = _localization.Get("BitwardenStuckEraseAbandonFailed");
        }
        finally
        {
            EndBitwardenOnlineOperation();
        }

        await LoadBitwardenStuckErasuresAsync();
    }
}

/// <summary>
/// A row of the stuck-erase list. It names a cipher the server still holds and this device threw away, and
/// nothing about its content: the erase carried none, and the entry is gone from the vault.
/// </summary>
public sealed record BitwardenStuckEraseDisplayItem(
    long OperationId,
    string CipherText,
    string ReasonText,
    string LastAttemptText);
