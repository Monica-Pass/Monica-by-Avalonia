using System.Security.Cryptography;
using System.Text;
using Monica.Core.Bitwarden;

namespace Monica.Platform.Bitwarden;

public sealed class BitwardenAuthenticationService(
    IBitwardenHttpClientFactory httpClientFactory,
    TimeProvider? timeProvider = null) : IBitwardenAuthenticationService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<BitwardenKdfParameters> PreloginAsync(
        string email,
        BitwardenEndpointSet endpoints,
        BitwardenTlsOptions tls,
        string? clientCertificatePassword = null,
        CancellationToken cancellationToken = default)
    {
        using var client = httpClientFactory.Create(tls, clientCertificatePassword);
        return await new BitwardenIdentityClient(client, endpoints)
            .PreloginAsync(email, cancellationToken);
    }

    public async Task<BitwardenAuthenticationResult> AuthenticateAsync(
        BitwardenAuthenticationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        var endpoints = BitwardenEndpointPolicy.Validate(request.Endpoints);
        var loginEmail = request.Email.Trim();
        var email = BitwardenKdfPolicy.CanonicalizeEmail(request.Email);
        using var client = httpClientFactory.Create(request.Tls, request.ClientCertificatePassword);
        var identity = new BitwardenIdentityClient(client, endpoints);
        var kdf = await identity.PreloginAsync(email, cancellationToken);
        var masterKey = BitwardenKeyDerivation.DeriveMasterKey(request.MasterPassword, email, kdf);
        BitwardenSymmetricKey? stretchedKey = null;
        try
        {
            var passwordHash = BitwardenKeyDerivation.DeriveMasterPasswordHash(masterKey, request.MasterPassword);
            stretchedKey = BitwardenKeyDerivation.StretchMasterKey(masterKey);
            var reply = await identity.LoginAsync(new BitwardenIdentityClient.TokenRequest(
                loginEmail,
                passwordHash,
                request.DeviceIdentifier.Trim(),
                request.DeviceName.Trim(),
                request.CaptchaResponse,
                request.TwoFactorToken,
                request.TwoFactorProvider,
                request.RememberTwoFactor,
                request.NewDeviceOtp), cancellationToken);
            if (!reply.Succeeded)
            {
                return ClassifyFailure(reply);
            }

            var payload = reply.Payload;
            if (string.IsNullOrWhiteSpace(payload.RefreshToken) || string.IsNullOrWhiteSpace(payload.Key))
            {
                throw new BitwardenProtocolException("Bitwarden login response omitted required session material.");
            }

            using var vaultKey = BitwardenCipherStringCrypto.DecryptSymmetricKey(payload.Key, stretchedKey);
            var accessTokenBytes = Encoding.UTF8.GetBytes(payload.AccessToken!);
            var refreshTokenBytes = Encoding.UTF8.GetBytes(payload.RefreshToken);
            var vaultEncryptionKey = vaultKey.CopyEncryptionKey();
            var vaultMacKey = vaultKey.CopyMacKey();
            var certificatePasswordBytes = string.IsNullOrEmpty(request.ClientCertificatePassword)
                ? []
                : Encoding.UTF8.GetBytes(request.ClientCertificatePassword);
            try
            {
                var secrets = new BitwardenAccountSecrets(
                    accessTokenBytes,
                    refreshTokenBytes,
                    masterKey,
                    vaultEncryptionKey,
                    vaultMacKey,
                    certificatePasswordBytes);
                var now = _timeProvider.GetUtcNow();
                var account = new BitwardenAccount
                {
                    Email = email,
                    AccountKey = BitwardenAccountIdentity.CreateAccountKey(email, endpoints),
                    Endpoints = endpoints,
                    Kdf = kdf,
                    Tls = request.Tls,
                    AccessTokenExpiresAt = now.AddSeconds(ValidateExpiry(payload.ExpiresIn)),
                    IsConnected = true,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                return new BitwardenAuthenticationResult(true, account, secrets, BitwardenLoginChallengeKind.None);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(accessTokenBytes);
                CryptographicOperations.ZeroMemory(refreshTokenBytes);
                CryptographicOperations.ZeroMemory(vaultEncryptionKey);
                CryptographicOperations.ZeroMemory(vaultMacKey);
                CryptographicOperations.ZeroMemory(certificatePasswordBytes);
            }
        }
        finally
        {
            stretchedKey?.Dispose();
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    public async Task<(BitwardenAccount Account, BitwardenAccountSecrets Secrets)> RefreshAsync(
        BitwardenAccount account,
        BitwardenAccountSecrets currentSecrets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(currentSecrets);
        var refreshTokenBytes = currentSecrets.CopyRefreshToken();
        var certificatePasswordBytes = currentSecrets.CopyClientCertificatePassword();
        try
        {
            var refreshToken = Encoding.UTF8.GetString(refreshTokenBytes);
            var certificatePassword = certificatePasswordBytes is null
                ? null
                : Encoding.UTF8.GetString(certificatePasswordBytes);
            using var client = httpClientFactory.Create(account.Tls, certificatePassword);
            var reply = await new BitwardenIdentityClient(client, account.Endpoints)
                .RefreshAsync(refreshToken, cancellationToken);
            if (!reply.Succeeded)
            {
                throw new HttpRequestException(
                    reply.Message ?? "Bitwarden token refresh failed.",
                    null,
                    reply.StatusCode);
            }

            var accessToken = Encoding.UTF8.GetBytes(reply.Payload.AccessToken!);
            var replacementRefreshToken = string.IsNullOrWhiteSpace(reply.Payload.RefreshToken)
                ? refreshTokenBytes.ToArray()
                : Encoding.UTF8.GetBytes(reply.Payload.RefreshToken);
            var masterKey = currentSecrets.CopyMasterKey();
            var encryptionKey = currentSecrets.CopyEncryptionKey();
            var macKey = currentSecrets.CopyMacKey();
            try
            {
                var secrets = new BitwardenAccountSecrets(
                    accessToken,
                    replacementRefreshToken,
                    masterKey,
                    encryptionKey,
                    macKey,
                    certificatePasswordBytes ?? []);
                var now = _timeProvider.GetUtcNow();
                return (account with
                {
                    AccessTokenExpiresAt = now.AddSeconds(ValidateExpiry(reply.Payload.ExpiresIn)),
                    UpdatedAt = now
                }, secrets);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(accessToken);
                CryptographicOperations.ZeroMemory(replacementRefreshToken);
                CryptographicOperations.ZeroMemory(masterKey);
                CryptographicOperations.ZeroMemory(encryptionKey);
                CryptographicOperations.ZeroMemory(macKey);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(refreshTokenBytes);
            if (certificatePasswordBytes is not null)
            {
                CryptographicOperations.ZeroMemory(certificatePasswordBytes);
            }
        }
    }

    private static BitwardenAuthenticationResult ClassifyFailure(BitwardenIdentityClient.TokenReply reply)
    {
        var payload = reply.Payload;
        var combined = $"{payload.Error} {payload.ErrorDescription} {payload.ErrorModel?.Message}";
        if (!string.IsNullOrWhiteSpace(payload.CaptchaSiteKey) ||
            combined.Contains("captcha", StringComparison.OrdinalIgnoreCase))
        {
            return new(false, null, null, BitwardenLoginChallengeKind.Captcha, reply.Message, payload.CaptchaSiteKey);
        }

        if (combined.Contains("new device verification required", StringComparison.OrdinalIgnoreCase))
        {
            return new(false, null, null, BitwardenLoginChallengeKind.NewDeviceVerification, reply.Message);
        }

        var factors = ParseFactors(payload);
        if (factors.Count > 0)
        {
            return new(false, null, null, BitwardenLoginChallengeKind.TwoFactor, reply.Message, Factors: factors);
        }

        if (string.Equals(payload.Error, "invalid_grant", StringComparison.OrdinalIgnoreCase) &&
            combined.Contains("invalid_username_or_password", StringComparison.OrdinalIgnoreCase))
        {
            return new(false, null, null, BitwardenLoginChallengeKind.InvalidCredentials, reply.Message);
        }

        return new(false, null, null, BitwardenLoginChallengeKind.Rejected, reply.Message);
    }

    private static IReadOnlyList<BitwardenLoginFactor> ParseFactors(BitwardenIdentityClient.TokenDto payload)
    {
        var providers = new SortedSet<int>();
        if (payload.TwoFactorProviders2 is not null)
        {
            foreach (var key in payload.TwoFactorProviders2.Keys)
            {
                if (int.TryParse(key, out var provider))
                {
                    providers.Add(provider);
                }
            }
        }

        if (payload.TwoFactorProviders is not null)
        {
            foreach (var value in payload.TwoFactorProviders)
            {
                if (int.TryParse(value, out var provider))
                {
                    providers.Add(provider);
                }
            }
        }

        return providers.Select(provider => new BitwardenLoginFactor(provider, ProviderName(provider))).ToArray();
    }

    private static string ProviderName(int provider) => provider switch
    {
        0 => "Authenticator app",
        1 => "Email",
        2 => "Duo",
        3 => "YubiKey",
        4 => "Duo for organizations",
        5 => "WebAuthn",
        _ => $"Provider {provider}"
    };

    private static void ValidateRequest(BitwardenAuthenticationRequest request)
    {
        BitwardenKdfPolicy.ValidatePassword(request.MasterPassword);
        _ = BitwardenKdfPolicy.CanonicalizeEmail(request.Email);
        if (string.IsNullOrWhiteSpace(request.DeviceIdentifier) || request.DeviceIdentifier.Length > 1024 ||
            string.IsNullOrWhiteSpace(request.DeviceName) || request.DeviceName.Length > 256)
        {
            throw new BitwardenProtocolException("Bitwarden device identity is invalid.");
        }

        if (request.TwoFactorToken is not null && request.TwoFactorProvider is null)
        {
            throw new BitwardenProtocolException("Bitwarden two-factor provider is required with a token.");
        }
    }

    private static int ValidateExpiry(int expiresIn)
    {
        if (expiresIn is < 1 or > 7 * 24 * 60 * 60)
        {
            throw new BitwardenProtocolException("Bitwarden token expiry is outside the supported range.");
        }

        return expiresIn;
    }
}
