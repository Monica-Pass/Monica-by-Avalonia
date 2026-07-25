using Monica.Core.Bitwarden;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool TryBeginBitwardenOnlineOperation()
    {
        if (Interlocked.CompareExchange(ref _bitwardenSyncOperationActive, 1, 0) != 0)
        {
            BitwardenOperationError = _localization.Get("BitwardenOperationInProgress");
            return false;
        }

        if (!IsUnlocked)
        {
            Interlocked.Exchange(ref _bitwardenSyncOperationActive, 0);
            BitwardenOperationError = _localization.Get("BitwardenRequiresUnlockedVault");
            return false;
        }

        _bitwardenSyncOperationCancellation?.Dispose();
        _bitwardenSyncOperationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _vaultSessionService.SessionCancellationToken);
        IsBitwardenBusy = true;
        BitwardenOperationError = "";
        return true;
    }

    private void EndBitwardenOnlineOperation()
    {
        IsBitwardenBusy = false;
        Interlocked.Exchange(ref _bitwardenSyncOperationActive, 0);
        _bitwardenSyncOperationCancellation?.Dispose();
        _bitwardenSyncOperationCancellation = null;
        RaiseBitwardenState();
    }

    private void CancelBitwardenOperationAndClearSecrets()
    {
        _bitwardenSyncOperationCancellation?.Cancel();
        ClearBitwardenAuthenticationFields(preserveIdentity: false);
        BitwardenAccounts.Clear();
        SelectedBitwardenAccount = null;
        IsBitwardenConnectionEditorVisible = true;
        IsBitwardenSyncActive = false;
        BitwardenSyncStageText = "";
        RaiseBitwardenState();
    }

    private void ClearBitwardenAuthenticationFields(bool preserveIdentity)
    {
        BitwardenMasterPassword = "";
        BitwardenTwoFactorToken = "";
        BitwardenCaptchaResponse = "";
        BitwardenNewDeviceOtp = "";
        BitwardenRememberTwoFactor = false;
        SelectedBitwardenLoginFactor = null;
        BitwardenLoginFactors.Clear();
        BitwardenLoginChallenge = BitwardenLoginChallengeKind.None;
        BitwardenChallengeMessage = "";
        BitwardenCaptchaSiteKey = "";
        if (!preserveIdentity)
        {
            BitwardenEmail = "";
            BitwardenServerUrl = "https://vault.bitwarden.com/";
        }
    }

    private void RaiseBitwardenState()
    {
        OnPropertyChanged(nameof(IsBitwardenIntegrationAvailable));
        OnPropertyChanged(nameof(HasBitwardenAccounts));
        OnPropertyChanged(nameof(BitwardenAccountsSummaryText));
        OnPropertyChanged(nameof(HasSelectedBitwardenAccount));
        OnPropertyChanged(nameof(IsBitwardenOperationBusy));
        OnPropertyChanged(nameof(CanAuthenticateBitwarden));
        OnPropertyChanged(nameof(CanSyncSelectedBitwardenAccount));
        OnPropertyChanged(nameof(CanDisconnectSelectedBitwardenAccount));
        OnPropertyChanged(nameof(BitwardenLoginActionText));
        OnPropertyChanged(nameof(BitwardenChallengeTitle));
        OnPropertyChanged(nameof(BitwardenChallengeDescription));
        RefreshSyncHealthItems();
    }

    partial void OnSelectedBitwardenAccountChanged(
        BitwardenAccountDisplayItem? oldValue,
        BitwardenAccountDisplayItem? newValue)
    {
        if (oldValue?.Id != newValue?.Id)
        {
            BitwardenOperationError = "";
        }

        if (newValue is not null)
        {
            ApplyBitwardenSyncState(_bitwardenSyncCoordinator?.GetState(newValue.Id));
        }
        else
        {
            IsBitwardenSyncActive = false;
            BitwardenSyncStageText = "";
        }

        RaiseBitwardenState();
    }

    private void OnBitwardenSyncStateChanged(object? sender, BitwardenSyncState state) =>
        _viewModelDispatcher.Post(() =>
        {
            if (SelectedBitwardenAccount?.Id == state.AccountId)
            {
                ApplyBitwardenSyncState(state);
            }

            if (state.Phase is BitwardenSyncPhase.Completed or BitwardenSyncPhase.Failed)
            {
                _ = LoadBitwardenAccountsAsync();
            }
        });

    private void ApplyBitwardenSyncState(BitwardenSyncState? state)
    {
        if (state is null || state.Phase == BitwardenSyncPhase.Idle)
        {
            IsBitwardenSyncActive = false;
            BitwardenSyncStageText = "";
            return;
        }

        IsBitwardenSyncActive = state.Phase is
            BitwardenSyncPhase.Preparing or
            BitwardenSyncPhase.RefreshingToken or
            BitwardenSyncPhase.Uploading or
            BitwardenSyncPhase.Downloading or
            BitwardenSyncPhase.Applying;
        BitwardenSyncStageText = GetBitwardenSyncPhaseText(state.Phase);
        if (state.Phase == BitwardenSyncPhase.Failed)
        {
            BitwardenOperationError = _localization.Get("BitwardenSyncFailed");
        }
        else if (state.Phase == BitwardenSyncPhase.Locked)
        {
            BitwardenOperationError = _localization.Get("BitwardenRequiresUnlockedVault");
        }
    }

    private string GetBitwardenSyncPhaseText(BitwardenSyncPhase phase) => phase switch
    {
        BitwardenSyncPhase.Preparing => _localization.Get("BitwardenSyncPreparing"),
        BitwardenSyncPhase.RefreshingToken => _localization.Get("BitwardenSyncRefreshingToken"),
        BitwardenSyncPhase.Uploading => _localization.Get("BitwardenSyncUploading"),
        BitwardenSyncPhase.Downloading => _localization.Get("BitwardenSyncDownloading"),
        BitwardenSyncPhase.Applying => _localization.Get("BitwardenSyncApplying"),
        BitwardenSyncPhase.Completed => _localization.Get("BitwardenSyncCompleted"),
        BitwardenSyncPhase.Failed => _localization.Get("BitwardenSyncFailed"),
        BitwardenSyncPhase.Locked => _localization.Get("Locked"),
        _ => ""
    };

    private static BitwardenEndpointSet CreateBitwardenEndpoints(string serverUrl)
    {
        var validated = BitwardenEndpointPolicy.ValidateBaseAddress(serverUrl.Trim(), nameof(serverUrl));
        if (validated.Host.Equals("vault.bitwarden.com", StringComparison.OrdinalIgnoreCase) &&
            validated.AbsolutePath == "/")
        {
            return BitwardenEndpointSet.UnitedStates;
        }

        if (validated.Host.Equals("vault.bitwarden.eu", StringComparison.OrdinalIgnoreCase) &&
            validated.AbsolutePath == "/")
        {
            return BitwardenEndpointSet.Europe;
        }

        return BitwardenEndpointPolicy.CreateSelfHosted(validated.AbsoluteUri);
    }
}
