using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Monica.Core.Bitwarden;
using Monica.Data.Bitwarden;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private int _bitwardenStuckErasuresRead;

    /// <summary>
    /// The cipher identities the most recent synchronization held back from being resurrected. It is the
    /// only live record of "the server still holds a copy you deleted here" this device owns: the purge
    /// queue keeps the erase, but nothing maps a suppressed cipher to a visible row without this set being
    /// carried across the reads that follow. Same lifetime rule as the refusal list - one account's last
    /// attempt, replaced wholesale by the next round that names it, never read back from storage.
    /// </summary>
    private IReadOnlySet<string> _bitwardenSuppressedCipherIds = new HashSet<string>(StringComparer.Ordinal);
    private long? _bitwardenSuppressedVaultId;

    public ObservableCollection<BitwardenStuckEraseDisplayItem> BitwardenStuckErasures { get; } = [];

    public bool HasBitwardenStuckErasures => BitwardenStuckErasures.Count > 0;

    public bool CanResolveBitwardenStuckErasures =>
        _bitwardenStuckEraseService is not null && !IsBitwardenBusy;

    private void ApplyBitwardenSuppressedResurrections(long vaultId, IReadOnlyList<string> cipherIds)
    {
        _bitwardenSuppressedCipherIds = cipherIds.Count == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : cipherIds.ToHashSet(StringComparer.Ordinal);
        _bitwardenSuppressedVaultId = cipherIds.Count == 0 ? null : vaultId;
    }

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

        // The suppressed set belongs to whichever account produced it; showing it against another account
        // would offer to undo a delete the row's vault never made. An account with nothing suppressed this
        // round still lists its genuinely stuck rows, with an empty suppressed set.
        var suppressedForAccount = _bitwardenSuppressedVaultId == account.Id
            ? _bitwardenSuppressedCipherIds
            : (IReadOnlySet<string>)new HashSet<string>(StringComparer.Ordinal);

        List<BitwardenStuckEraseDisplayItem> rows;
        try
        {
            var erasures = await _bitwardenStuckEraseService.GetSuppressedAsync(
                account.Id,
                suppressedForAccount,
                _vaultSessionService.SessionCancellationToken);
            rows = erasures
                .Select(erase => new BitwardenStuckEraseDisplayItem(
                    erase.OperationId,
                    erase.CipherId,
                    _localization.Format("BitwardenStuckEraseCipherFormat", erase.CipherId),
                    _localization.Get(GetBitwardenStuckEraseReasonKey(erase)),
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
            // The user just chose to bring the server's copy back, so this device's live suppression of that
            // exact cipher is spent. Dropping it lets the next pull actually re-add the entry instead of the
            // decision looking like it had no effect. The other suppressed rows stay until their own choice.
            if (_bitwardenSuppressedVaultId == account.Id)
            {
                var remaining = _bitwardenSuppressedCipherIds
                    .Where(cipherId => !string.Equals(cipherId, erase.CipherId, StringComparison.Ordinal))
                    .ToHashSet(StringComparer.Ordinal);
                _bitwardenSuppressedCipherIds = remaining;
                _bitwardenSuppressedVaultId = remaining.Count == 0 ? null : account.Id;
            }
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

    private static string GetBitwardenStuckEraseReasonKey(BitwardenStuckErase erase)
    {
        // Suppression is the reason the row is on screen at all this round: the server kept its copy and
        // this device refused to grow it back. That outranks the raw queue status - a stuck erase and a
        // still-retrying one tell the same story the moment their booking held back a resurrection.
        if (erase.SuppressedThisRound)
        {
            return "BitwardenStuckEraseReasonSuppressed";
        }

        return erase.Status == BitwardenMutationStatus.Conflict
            ? "BitwardenStuckEraseReasonConflict"
            : "BitwardenStuckEraseReasonFailed";
    }
}

/// <summary>
/// A row of the stuck-erase list. It names a cipher the server still holds and this device threw away, and
/// nothing about its content: the erase carried none, and the entry is gone from the vault. The raw
/// <paramref name="CipherId"/> stays on the row so abandoning it can retire exactly that identity from the
/// live suppression set; the formatted <paramref name="CipherText"/> is what is rendered.
/// </summary>
public sealed record BitwardenStuckEraseDisplayItem(
    long OperationId,
    string CipherId,
    string CipherText,
    string ReasonText,
    string LastAttemptText);
