using CommunityToolkit.Mvvm.Input;
using Monica.Core.Bitwarden;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task LoadBitwardenAccountsAsync()
    {
        if (!IsUnlocked)
        {
            return;
        }

        if (_bitwardenAccountStore is null ||
            Interlocked.CompareExchange(ref _bitwardenAccountsLoadActive, 1, 0) != 0)
        {
            return;
        }

        IsLoadingBitwardenAccounts = true;
        var selectedId = SelectedBitwardenAccount?.Id;
        var operationError = BitwardenOperationError;
        try
        {
            var accounts = await _bitwardenAccountStore.GetAllAsync(
                _vaultSessionService.SessionCancellationToken);
            var displayItems = await Task.WhenAll(accounts.Select(CreateBitwardenAccountDisplayItemAsync));
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
        catch (OperationCanceledException) when (!IsUnlocked)
        {
            BitwardenOperationError = _localization.Get("BitwardenRequiresUnlockedVault");
        }
        catch (Exception exception)
        {
            AppDiagnostics.Error("Bitwarden account list could not be loaded", exception);
            BitwardenOperationError = _localization.Get("BitwardenLoadAccountsFailed");
        }
        finally
        {
            IsLoadingBitwardenAccounts = false;
            Interlocked.Exchange(ref _bitwardenAccountsLoadActive, 0);
            RaiseBitwardenState();
        }
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
            await _bitwardenSyncCoordinator.SyncAsync(
                account.Id,
                BitwardenSyncTrigger.Manual,
                cancellationToken);
            StatusMessage = _localization.Format("BitwardenSyncedFormat", account.DisplayName);
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
            StatusMessage = _localization.Format("BitwardenDisconnectedFormat", account.DisplayName);
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
