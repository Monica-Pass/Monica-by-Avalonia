namespace Monica.Core.Bitwarden;

public enum BitwardenLoginChallengeKind
{
    None = 0,
    TwoFactor,
    Captcha,
    NewDeviceVerification,
    InvalidCredentials,
    Rejected
}

public sealed record BitwardenLoginFactor(int Provider, string Name);

public sealed record BitwardenAuthenticationRequest(
    string Email,
    string MasterPassword,
    BitwardenEndpointSet Endpoints,
    BitwardenTlsOptions Tls,
    string DeviceIdentifier,
    string DeviceName,
    string? CaptchaResponse = null,
    string? TwoFactorToken = null,
    int? TwoFactorProvider = null,
    bool RememberTwoFactor = false,
    string? NewDeviceOtp = null,
    string? ClientCertificatePassword = null);

public sealed record BitwardenAuthenticationResult(
    bool Succeeded,
    BitwardenAccount? Account,
    BitwardenAccountSecrets? Secrets,
    BitwardenLoginChallengeKind Challenge,
    string? Message = null,
    string? CaptchaSiteKey = null,
    IReadOnlyList<BitwardenLoginFactor>? Factors = null) : IDisposable
{
    public void Dispose() => Secrets?.Dispose();
}

public interface IBitwardenAuthenticationService
{
    Task<BitwardenKdfParameters> PreloginAsync(
        string email,
        BitwardenEndpointSet endpoints,
        BitwardenTlsOptions tls,
        string? clientCertificatePassword = null,
        CancellationToken cancellationToken = default);

    Task<BitwardenAuthenticationResult> AuthenticateAsync(
        BitwardenAuthenticationRequest request,
        CancellationToken cancellationToken = default);

    Task<(BitwardenAccount Account, BitwardenAccountSecrets Secrets)> RefreshAsync(
        BitwardenAccount account,
        BitwardenAccountSecrets currentSecrets,
        CancellationToken cancellationToken = default);
}
