using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.App.Services;
using Monica.Core.Bitwarden;
using Monica.Data.Bitwarden;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly IBitwardenAccountStore? _bitwardenAccountStore;
    private readonly IBitwardenAuthenticationService? _bitwardenAuthenticationService;
    private readonly IBitwardenSyncCoordinator? _bitwardenSyncCoordinator;
    private readonly IBitwardenSessionManager? _bitwardenSessionManager;
    private readonly IBitwardenPendingOperationStore? _bitwardenPendingOperationStore;
    private readonly IBitwardenConflictBackupStore? _bitwardenConflictBackupStore;
    private readonly IBitwardenDeviceIdentityProvider? _bitwardenDeviceIdentityProvider;
    private int _bitwardenSyncOperationActive;
    private int _bitwardenAccountsLoadActive;
    private CancellationTokenSource? _bitwardenSyncOperationCancellation;

    public ObservableCollection<BitwardenAccountDisplayItem> BitwardenAccounts { get; } = [];
    public ObservableCollection<BitwardenLoginFactor> BitwardenLoginFactors { get; } = [];

    public bool IsBitwardenIntegrationAvailable =>
        _bitwardenAccountStore is not null &&
        _bitwardenAuthenticationService is not null &&
        _bitwardenSyncCoordinator is not null &&
        _bitwardenSessionManager is not null &&
        _bitwardenDeviceIdentityProvider is not null;

    public bool HasBitwardenAccounts => BitwardenAccounts.Count > 0;
    public bool HasSelectedBitwardenAccount => SelectedBitwardenAccount is not null;
    public bool HasBitwardenOperationError => !string.IsNullOrWhiteSpace(BitwardenOperationError);
    public bool IsBitwardenOperationBusy => IsBitwardenBusy || IsBitwardenSyncActive || IsLoadingBitwardenAccounts;
    public bool IsBitwardenChallengeVisible => BitwardenLoginChallenge != BitwardenLoginChallengeKind.None;
    public bool IsBitwardenTwoFactorChallenge => BitwardenLoginChallenge == BitwardenLoginChallengeKind.TwoFactor;
    public bool IsBitwardenCaptchaChallenge => BitwardenLoginChallenge == BitwardenLoginChallengeKind.Captcha;
    public bool IsBitwardenNewDeviceChallenge => BitwardenLoginChallenge == BitwardenLoginChallengeKind.NewDeviceVerification;
    public bool HasBitwardenCaptchaSiteKey => !string.IsNullOrWhiteSpace(BitwardenCaptchaSiteKey);
    public bool CanSyncSelectedBitwardenAccount =>
        IsUnlocked &&
        !IsBitwardenOperationBusy &&
        SelectedBitwardenAccount?.IsConnected == true;
    public bool CanDisconnectSelectedBitwardenAccount =>
        !IsBitwardenOperationBusy &&
        SelectedBitwardenAccount?.IsConnected == true;
    public bool CanAuthenticateBitwarden
    {
        get
        {
            if (!IsBitwardenIntegrationAvailable || !IsUnlocked || IsBitwardenOperationBusy ||
                string.IsNullOrWhiteSpace(BitwardenEmail) ||
                string.IsNullOrEmpty(BitwardenMasterPassword) ||
                !Uri.TryCreate(BitwardenServerUrl?.Trim(), UriKind.Absolute, out var server) ||
                !server.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return BitwardenLoginChallenge switch
            {
                BitwardenLoginChallengeKind.TwoFactor =>
                    SelectedBitwardenLoginFactor is not null &&
                    !string.IsNullOrWhiteSpace(BitwardenTwoFactorToken),
                BitwardenLoginChallengeKind.Captcha =>
                    !string.IsNullOrWhiteSpace(BitwardenCaptchaResponse),
                BitwardenLoginChallengeKind.NewDeviceVerification =>
                    !string.IsNullOrWhiteSpace(BitwardenNewDeviceOtp),
                _ => true
            };
        }
    }

    public string BitwardenAccountsSummaryText
    {
        get
        {
            if (!IsBitwardenIntegrationAvailable)
            {
                return _localization.Get("BitwardenUnavailable");
            }

            if (IsLoadingBitwardenAccounts)
            {
                return _localization.Get("BitwardenLoadingAccounts");
            }

            return HasBitwardenAccounts
                ? _localization.Format("BitwardenAccountCountFormat", BitwardenAccounts.Count)
                : _localization.Get("BitwardenNoAccounts");
        }
    }

    public string BitwardenLoginActionText => BitwardenLoginChallenge == BitwardenLoginChallengeKind.None
        ? _localization.Get("BitwardenConnect")
        : _localization.Get("Continue");

    public string BitwardenChallengeTitle => BitwardenLoginChallenge switch
    {
        BitwardenLoginChallengeKind.TwoFactor => _localization.Get("BitwardenTwoFactorTitle"),
        BitwardenLoginChallengeKind.Captcha => _localization.Get("BitwardenCaptchaTitle"),
        BitwardenLoginChallengeKind.NewDeviceVerification => _localization.Get("BitwardenNewDeviceTitle"),
        BitwardenLoginChallengeKind.InvalidCredentials => _localization.Get("BitwardenInvalidCredentialsTitle"),
        BitwardenLoginChallengeKind.Rejected => _localization.Get("BitwardenRejectedTitle"),
        _ => ""
    };

    public string BitwardenChallengeDescription => !string.IsNullOrWhiteSpace(BitwardenChallengeMessage)
        ? BitwardenChallengeMessage
        : BitwardenLoginChallenge switch
        {
            BitwardenLoginChallengeKind.TwoFactor => _localization.Get("BitwardenTwoFactorDescription"),
            BitwardenLoginChallengeKind.Captcha => _localization.Get("BitwardenCaptchaDescription"),
            BitwardenLoginChallengeKind.NewDeviceVerification => _localization.Get("BitwardenNewDeviceDescription"),
            BitwardenLoginChallengeKind.InvalidCredentials => _localization.Get("BitwardenInvalidCredentialsDescription"),
            BitwardenLoginChallengeKind.Rejected => _localization.Get("BitwardenRejectedDescription"),
            _ => ""
        };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAuthenticateBitwarden))]
    private string _bitwardenServerUrl = "https://vault.bitwarden.com/";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAuthenticateBitwarden))]
    private string _bitwardenEmail = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAuthenticateBitwarden))]
    private string _bitwardenMasterPassword = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAuthenticateBitwarden))]
    private string _bitwardenTwoFactorToken = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAuthenticateBitwarden))]
    private BitwardenLoginFactor? _selectedBitwardenLoginFactor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAuthenticateBitwarden))]
    private string _bitwardenCaptchaResponse = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAuthenticateBitwarden))]
    private string _bitwardenNewDeviceOtp = "";

    [ObservableProperty]
    private bool _bitwardenRememberTwoFactor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBitwardenOperationBusy))]
    [NotifyPropertyChangedFor(nameof(CanAuthenticateBitwarden))]
    [NotifyPropertyChangedFor(nameof(CanSyncSelectedBitwardenAccount))]
    [NotifyPropertyChangedFor(nameof(CanDisconnectSelectedBitwardenAccount))]
    private bool _isBitwardenBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBitwardenOperationBusy))]
    [NotifyPropertyChangedFor(nameof(BitwardenAccountsSummaryText))]
    [NotifyPropertyChangedFor(nameof(CanAuthenticateBitwarden))]
    [NotifyPropertyChangedFor(nameof(CanSyncSelectedBitwardenAccount))]
    [NotifyPropertyChangedFor(nameof(CanDisconnectSelectedBitwardenAccount))]
    private bool _isLoadingBitwardenAccounts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBitwardenOperationBusy))]
    [NotifyPropertyChangedFor(nameof(CanAuthenticateBitwarden))]
    [NotifyPropertyChangedFor(nameof(CanSyncSelectedBitwardenAccount))]
    [NotifyPropertyChangedFor(nameof(CanDisconnectSelectedBitwardenAccount))]
    private bool _isBitwardenSyncActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBitwardenChallengeVisible))]
    [NotifyPropertyChangedFor(nameof(IsBitwardenTwoFactorChallenge))]
    [NotifyPropertyChangedFor(nameof(IsBitwardenCaptchaChallenge))]
    [NotifyPropertyChangedFor(nameof(IsBitwardenNewDeviceChallenge))]
    [NotifyPropertyChangedFor(nameof(BitwardenLoginActionText))]
    [NotifyPropertyChangedFor(nameof(BitwardenChallengeTitle))]
    [NotifyPropertyChangedFor(nameof(BitwardenChallengeDescription))]
    [NotifyPropertyChangedFor(nameof(CanAuthenticateBitwarden))]
    private BitwardenLoginChallengeKind _bitwardenLoginChallenge;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BitwardenChallengeDescription))]
    private string _bitwardenChallengeMessage = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBitwardenCaptchaSiteKey))]
    private string _bitwardenCaptchaSiteKey = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBitwardenOperationError))]
    private string _bitwardenOperationError = "";

    [ObservableProperty]
    private string _bitwardenSyncStageText = "";

    [ObservableProperty]
    private bool _isBitwardenConnectionEditorVisible = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedBitwardenAccount))]
    [NotifyPropertyChangedFor(nameof(CanSyncSelectedBitwardenAccount))]
    [NotifyPropertyChangedFor(nameof(CanDisconnectSelectedBitwardenAccount))]
    private BitwardenAccountDisplayItem? _selectedBitwardenAccount;
}
