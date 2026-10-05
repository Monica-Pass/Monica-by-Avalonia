using CommunityToolkit.Mvvm.Input;
using Monica.Core.Bitwarden;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task LoadBitwardenAccountsAsync()
    {
        if (!IsUnlocked || IsMdbxRestoreInProgress)
        {
            return;
        }

        if (_bitwardenAccountStore is null ||
            Interlocked.CompareExchange(ref _bitwardenAccountsLoadActive, 1, 0) != 0)
        {
            return;
        }

        var loadVersion = ++_bitwardenAccountsLoadVersion;
        var sessionCancellationToken = _vaultSessionService.SessionCancellationToken;
        IsLoadingBitwardenAccounts = true;
        var selectedId = SelectedBitwardenAccount?.Id;
        var operationError = BitwardenOperationError;
        try
        {
            var accounts = await _bitwardenAccountStore.GetAllAsync(
                sessionCancellationToken);
            if (!IsCurrentLoad())
            {
                return;
            }

            var displayItems = await Task.WhenAll(accounts.Select(CreateBitwardenAccountDisplayItemAsync));
            if (!IsCurrentLoad())
            {
                return;
            }
            BitwardenAccounts.Clear();
            foreach (var item in displayItems)
            {
                BitwardenAccounts.Add(item);
            }

            SelectedBitwardenAccount = selectedId is { } id
                ? BitwardenAccounts.FirstOrDefault(item => item.Id == id)
                : BitwardenAccounts.FirstOrDefault();
            BitwardenOperationError = operationError;
            IsBitwardenConnectionEditorVisible = BitwardenAccounts.Count == 0;
        }
        catch (OperationCanceledException) when (sessionCancellationToken.IsCancellationRequested)
        {
            // Locking already cleared this session's presentation state.
        }
        catch (Exception exception)
        {
            if (IsCurrentLoad())
            {
                AppDiagnostics.Error("Bitwarden account list could not be loaded", exception);
                BitwardenOperationError = _localization.Get("BitwardenLoadAccountsFailed");
            }
        }
        finally
        {
            if (loadVersion == _bitwardenAccountsLoadVersion)
            {
                IsLoadingBitwardenAccounts = false;
                Interlocked.Exchange(ref _bitwardenAccountsLoadActive, 0);
                RaiseBitwardenState();
            }
        }

        bool IsCurrentLoad() =>
            loadVersion == _bitwardenAccountsLoadVersion && IsUnlocked &&
            !sessionCancellationToken.IsCancellationRequested && !IsMdbxRestoreInProgress;
    }

    [RelayCommand]
    private void ShowBitwardenConnectionEditor(BitwardenAccountDisplayItem? account)
    {
        ClearBitwardenAuthenticationFields(preserveIdentity: account is not null);
        BitwardenOperationError = "";
        if (account is not null)
        {
            BitwardenEmail = account.Email;
            BitwardenServerUrl = account.Account.Endpoints.WebVault.AbsoluteUri;
            SelectedBitwardenAccount = account;
        }

        IsBitwardenConnectionEditorVisible = true;
    }

    [RelayCommand]
    private void CancelBitwardenConnection()
    {
        ClearBitwardenAuthenticationFields(preserveIdentity: false);
        BitwardenOperationError = "";
        IsBitwardenConnectionEditorVisible = !HasBitwardenAccounts;
    }

    [RelayCommand]
    private async Task SyncBitwardenAccountAsync(BitwardenAccountDisplayItem? account)
    {
        account ??= SelectedBitwardenAccount;
        if (account is null || !account.IsConnected || _bitwardenSyncCoordinator is null ||
            !TryBeginBitwardenOnlineOperation())
        {
            return;
        }

        SelectedBitwardenAccount = account;
        var cancellationToken = _bitwardenSyncOperationCancellation!.Token;
        try
        {
            var result = await _bitwardenSyncCoordinator.SyncAsync(
                account.Id,
                BitwardenSyncTrigger.Manual,
                cancellationToken);
            if (!await ApplyBitwardenPullToVaultAsync(result))
            {
                SetStatusNotice("BitwardenSyncedFormat", account.DisplayName);
            }

            if (result.Merge.SuppressedResurrections > 0)
            {
                // The sync reports success, but one thing it deliberately did not do is grow back the cipher
                // the user already deleted here. That choice needs to be visible, not absorbed into "1 added
                // and everything is fine" - the entry the server still holds has not come back, and never
                // will until the user takes the decision the list now offers.
                await LoadBitwardenStuckErasuresAsync();
            }

            if (result.Unsyncable.Count > 0)
            {
                // Last word on the status line, because it is the part the user still has to act on. The
                // pull in this same round has already written the server's copy back over the refused row
                // (measured: merge[Updated=1]), so "已同步" would be wrong twice over - the edit did not
                // leave the device, and the row on screen is no longer the one the user typed.
                SetStatusFailure("BitwardenUnsyncableChangesFormat", result.Unsyncable.Count);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            BitwardenOperationError = _localization.Get("BitwardenOperationCanceled");
        }
        catch (Exception exception)
        {
            AppDiagnostics.Error("Manual Bitwarden synchronization failed", exception);
            BitwardenOperationError = _localization.Get("BitwardenSyncFailed");
        }
        finally
        {
            EndBitwardenOnlineOperation();
            await LoadBitwardenAccountsAsync();
        }
    }

    [RelayCommand]
    private async Task DisconnectBitwardenAccountAsync(BitwardenAccountDisplayItem? account)
    {
        account ??= SelectedBitwardenAccount;
        if (account is null || _bitwardenAccountStore is null || !account.IsConnected)
        {
            return;
        }

        var confirmed = await _confirmationDialogService.ConfirmAsync(
            _localization.Get("BitwardenDisconnectTitle"),
            _localization.Format("BitwardenDisconnectMessageFormat", account.DisplayName),
            _localization.Get("BitwardenDisconnect"),
            _localization.Cancel);
        if (!confirmed || !TryBeginBitwardenOnlineOperation())
        {
            return;
        }

        var cancellationToken = _bitwardenSyncOperationCancellation!.Token;
        try
        {
            await _bitwardenAccountStore.DisconnectAsync(account.Id, cancellationToken);
            if (_bitwardenSessionManager?.HasSession(account.Id) == true)
            {
                _bitwardenSessionManager.Clear();
            }

            ClearBitwardenAuthenticationFields(preserveIdentity: false);
            SetStatusNotice("BitwardenDisconnectedFormat", account.DisplayName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            BitwardenOperationError = _localization.Get("BitwardenOperationCanceled");
        }
        catch (Exception exception)
        {
            AppDiagnostics.Error("Bitwarden account disconnect failed", exception);
            BitwardenOperationError = _localization.Get("BitwardenDisconnectFailed");
        }
        finally
        {
            EndBitwardenOnlineOperation();
            await LoadBitwardenAccountsAsync();
        }
    }

}
