using CommunityToolkit.Mvvm.Input;
using Monica.Core.Bitwarden;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task AuthenticateBitwardenAsync()
    {
        if (!CanAuthenticateBitwarden ||
            _bitwardenAuthenticationService is null ||
            _bitwardenAccountStore is null ||
            _bitwardenSessionManager is null ||
            _bitwardenSyncCoordinator is null ||
            _bitwardenDeviceIdentityProvider is null ||
            !TryBeginBitwardenOnlineOperation())
        {
            return;
        }

        var cancellationToken = _bitwardenSyncOperationCancellation!.Token;
        long? connectedAccountId = null;
        try
        {
            var endpoints = CreateBitwardenEndpoints(BitwardenServerUrl);
            using var result = await _bitwardenAuthenticationService.AuthenticateAsync(
                new BitwardenAuthenticationRequest(
                    BitwardenEmail.Trim(),
                    BitwardenMasterPassword,
                    endpoints,
                    new BitwardenTlsOptions(),
                    _bitwardenDeviceIdentityProvider.DeviceIdentifier,
                    _bitwardenDeviceIdentityProvider.DeviceName,
                    CaptchaResponse: NullIfWhiteSpace(BitwardenCaptchaResponse),
                    TwoFactorToken: NullIfWhiteSpace(BitwardenTwoFactorToken),
                    TwoFactorProvider: SelectedBitwardenLoginFactor?.Provider,
                    RememberTwoFactor: BitwardenRememberTwoFactor,
                    NewDeviceOtp: NullIfWhiteSpace(BitwardenNewDeviceOtp)),
                cancellationToken);

            if (!result.Succeeded || result.Account is null || result.Secrets is null)
            {
                ApplyBitwardenAuthenticationChallenge(result);
                return;
            }

            var savedAccount = await _bitwardenAccountStore.SaveConnectedAsync(
                result.Account,
                result.Secrets,
                cancellationToken);
            _bitwardenSessionManager.Open(
                savedAccount.Id,
                result.Secrets,
                savedAccount.AccessTokenExpiresAt);
            connectedAccountId = savedAccount.Id;
            ClearBitwardenAuthenticationFields(preserveIdentity: true);
            IsBitwardenConnectionEditorVisible = false;
            StatusMessage = _localization.Format("BitwardenConnectedFormat", savedAccount.Email);

            try
            {
                await _bitwardenSyncCoordinator.SyncAsync(
                    savedAccount.Id,
                    BitwardenSyncTrigger.Manual,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception syncException)
            {
                AppDiagnostics.Error("Initial Bitwarden synchronization failed", syncException);
                BitwardenOperationError = _localization.Get("BitwardenConnectedSyncFailed");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            BitwardenOperationError = _localization.Get("BitwardenOperationCanceled");
        }
        catch (BitwardenProtocolException exception)
        {
            AppDiagnostics.Error("Bitwarden endpoint or protocol validation failed", exception);
            BitwardenOperationError = _localization.Get("BitwardenSecureConnectionRequired");
        }
        catch (Exception exception)
        {
            AppDiagnostics.Error("Bitwarden authentication failed", exception);
            BitwardenOperationError = _localization.Get("BitwardenAuthenticationFailed");
        }
        finally
        {
            EndBitwardenOnlineOperation();
            await LoadBitwardenAccountsAsync();
            if (connectedAccountId is { } id)
            {
                SelectedBitwardenAccount = BitwardenAccounts.FirstOrDefault(item => item.Id == id);
            }
        }
    }

    private void ApplyBitwardenAuthenticationChallenge(BitwardenAuthenticationResult result)
    {
        BitwardenLoginChallenge = result.Challenge;
        BitwardenChallengeMessage = result.Message?.Trim() ?? "";
        BitwardenCaptchaSiteKey = result.CaptchaSiteKey?.Trim() ?? "";
        BitwardenLoginFactors.Clear();
        foreach (var factor in result.Factors ?? [])
        {
            BitwardenLoginFactors.Add(factor);
        }

        SelectedBitwardenLoginFactor = BitwardenLoginFactors.FirstOrDefault();
        BitwardenTwoFactorToken = "";
        BitwardenCaptchaResponse = "";
        BitwardenNewDeviceOtp = "";
        if (result.Challenge is BitwardenLoginChallengeKind.InvalidCredentials or BitwardenLoginChallengeKind.Rejected)
        {
            BitwardenMasterPassword = "";
        }

        BitwardenOperationError = result.Challenge is BitwardenLoginChallengeKind.InvalidCredentials or BitwardenLoginChallengeKind.Rejected
            ? BitwardenChallengeDescription
            : "";
        RaiseBitwardenState();
    }

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
