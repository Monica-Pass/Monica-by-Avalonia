using System.Security.Cryptography;
using System.Text;
using Dapper;
using Monica.Core.Bitwarden;

namespace Monica.Data.Bitwarden;

public sealed partial class BitwardenAccountStore
{
    private BitwardenAccount MapAccount(BitwardenAccountRow row)
    {
        var endpoints = BitwardenEndpointPolicy.Validate(new BitwardenEndpointSet(
            new Uri(row.ServerUrl),
            new Uri(row.IdentityUrl),
            new Uri(row.ApiUrl),
            string.IsNullOrWhiteSpace(row.EventsUrl) ? null : new Uri(row.EventsUrl)));
        return new BitwardenAccount
        {
            Id = row.Id,
            Email = _protector.UnprotectString(row.Email),
            UserId = _protector.UnprotectNullableString(row.UserId),
            DisplayName = _protector.UnprotectNullableString(row.DisplayName),
            AccountKey = row.AccountKey,
            Endpoints = endpoints,
            Kdf = new BitwardenKdfParameters(
                (BitwardenKdfAlgorithm)row.KdfType,
                row.KdfIterations,
                row.KdfMemory,
                row.KdfParallelism),
            Tls = new BitwardenTlsOptions(
                ParseTlsMode(row.TlsMode),
                _protector.UnprotectNullableString(row.CustomCaCertificatePath),
                _protector.UnprotectNullableString(row.ClientCertificatePath)),
            AccessTokenExpiresAt = FromUnixMilliseconds(row.AccessTokenExpiresAt),
            LastSyncAt = FromUnixMilliseconds(row.LastSyncAt),
            LastFullSyncAt = FromUnixMilliseconds(row.LastFullSyncAt),
            RevisionDate = row.RevisionDate,
            LastSyncStatus = row.LastSyncStatus,
            LastSyncError = _protector.UnprotectNullableString(row.LastSyncError),
            IsDefault = row.IsDefault,
            IsConnected = row.IsConnected,
            SyncEnabled = row.SyncEnabled,
            CreatedAt = FromUnixMilliseconds(row.CreatedAt) ?? DateTimeOffset.UnixEpoch,
            UpdatedAt = FromUnixMilliseconds(row.UpdatedAt) ?? DateTimeOffset.UnixEpoch
        };
    }

    private BitwardenAccountSecrets Unprotect(BitwardenSecretRow row)
    {
        if (row.EncryptedAccessToken is null || row.EncryptedRefreshToken is null ||
            row.EncryptedMasterKey is null || row.EncryptedEncKey is null || row.EncryptedMacKey is null)
        {
            throw new CryptographicException("Connected Bitwarden account has incomplete protected secrets.");
        }

        var accessToken = _protector.UnprotectBytes(row.EncryptedAccessToken);
        var refreshToken = _protector.UnprotectBytes(row.EncryptedRefreshToken);
        var masterKey = _protector.UnprotectBytes(row.EncryptedMasterKey);
        var encryptionKey = _protector.UnprotectBytes(row.EncryptedEncKey);
        var macKey = _protector.UnprotectBytes(row.EncryptedMacKey);
        var certificatePassword = _protector.UnprotectNullableBytes(row.EncryptedClientCertificatePassword);
        try
        {
            return new BitwardenAccountSecrets(
                accessToken,
                refreshToken,
                masterKey,
                encryptionKey,
                macKey,
                certificatePassword ?? []);
        }
        finally
        {
            Zero(accessToken);
            Zero(refreshToken);
            Zero(masterKey);
            Zero(encryptionKey);
            Zero(macKey);
            Zero(certificatePassword);
        }
    }

    private static BitwardenAccount ValidateAndNormalize(BitwardenAccount account)
    {
        var endpoints = BitwardenEndpointPolicy.Validate(account.Endpoints);
        BitwardenKdfPolicy.Validate(account.Kdf);
        ValidateTls(account.Tls);
        var canonicalEmail = BitwardenKdfPolicy.CanonicalizeEmail(account.Email);
        var syncStatus = string.IsNullOrWhiteSpace(account.LastSyncStatus) ? "never" : account.LastSyncStatus;
        ValidateOptionalText(account.UserId, 1024, nameof(account.UserId));
        ValidateOptionalText(account.DisplayName, 4096, nameof(account.DisplayName));
        ValidateOptionalText(account.RevisionDate, 1024, nameof(account.RevisionDate));
        ValidateOptionalText(syncStatus, 128, nameof(account.LastSyncStatus));
        ValidateOptionalText(account.LastSyncError, 16 * 1024, nameof(account.LastSyncError));
        var expectedAccountKey = BitwardenAccountIdentity.CreateAccountKey(canonicalEmail, endpoints);
        if (!string.Equals(account.AccountKey, expectedAccountKey, StringComparison.Ordinal))
        {
            throw new BitwardenProtocolException("Bitwarden account identity does not match its email and server.");
        }

        return account with
        {
            Email = canonicalEmail,
            AccountKey = expectedAccountKey,
            Endpoints = endpoints,
            LastSyncStatus = syncStatus,
            IsConnected = true
        };
    }

    private static void ValidateTls(BitwardenTlsOptions tls)
    {
        ArgumentNullException.ThrowIfNull(tls);
        ValidateProtectedPath(tls.CustomCaCertificatePath, nameof(tls.CustomCaCertificatePath));
        ValidateProtectedPath(tls.ClientCertificatePath, nameof(tls.ClientCertificatePath));
        switch (tls.Mode)
        {
            case BitwardenTlsMode.SystemTrust when
                tls.CustomCaCertificatePath is null && tls.ClientCertificatePath is null:
                return;
            case BitwardenTlsMode.SystemAndCustomCertificate when
                !string.IsNullOrWhiteSpace(tls.CustomCaCertificatePath) && tls.ClientCertificatePath is null:
                return;
            case BitwardenTlsMode.MutualTls when !string.IsNullOrWhiteSpace(tls.ClientCertificatePath):
                return;
            default:
                throw new BitwardenProtocolException("Bitwarden TLS settings are incomplete or inconsistent.");
        }
    }

    private static void ValidateProtectedPath(string? value, string parameterName)
    {
        ValidateOptionalText(value, 4096, parameterName);
        if (value?.Contains('\0') == true)
        {
            throw new BitwardenProtocolException($"Bitwarden {parameterName} contains a null character.");
        }
    }

    private static void ValidateOptionalText(string? value, int maximumUtf8Bytes, string parameterName)
    {
        if (value is not null && Encoding.UTF8.GetByteCount(value) > maximumUtf8Bytes)
        {
            throw new BitwardenProtocolException($"Bitwarden {parameterName} exceeds the supported length.");
        }
    }

    private static object CreateSaveParameters(
        BitwardenAccount account,
        ProtectedAccountValues protectedValues,
        DateTimeOffset now) =>
        new
        {
            account.AccountKey,
            Email = protectedValues.Email,
            CanonicalEmail = BitwardenAccountIdentity.CreateEmailLookupHash(account.Email),
            UserId = protectedValues.UserId,
            DisplayName = protectedValues.DisplayName,
            ServerUrl = account.Endpoints.WebVault.AbsoluteUri,
            IdentityUrl = account.Endpoints.Identity.AbsoluteUri,
            ApiUrl = account.Endpoints.Api.AbsoluteUri,
            EventsUrl = account.Endpoints.Notifications?.AbsoluteUri,
            EncryptedAccessToken = protectedValues.AccessToken,
            EncryptedRefreshToken = protectedValues.RefreshToken,
            AccessTokenExpiresAt = ToUnixMilliseconds(account.AccessTokenExpiresAt),
            EncryptedMasterKey = protectedValues.MasterKey,
            EncryptedEncKey = protectedValues.EncryptionKey,
            EncryptedMacKey = protectedValues.MacKey,
            KdfType = (int)account.Kdf.Algorithm,
            KdfIterations = account.Kdf.Iterations,
            KdfMemory = account.Kdf.MemoryMb,
            KdfParallelism = account.Kdf.Parallelism,
            LastSyncAt = ToUnixMilliseconds(account.LastSyncAt),
            LastFullSyncAt = ToUnixMilliseconds(account.LastFullSyncAt),
            account.RevisionDate,
            account.LastSyncStatus,
            LastSyncError = protectedValues.LastSyncError,
            TlsMode = FormatTlsMode(account.Tls.Mode),
            CustomCaCertificatePath = protectedValues.CustomCaCertificatePath,
            ClientCertificatePath = protectedValues.ClientCertificatePath,
            EncryptedClientCertificatePassword = protectedValues.ClientCertificatePassword,
            account.IsDefault,
            account.SyncEnabled,
            Now = now.ToUnixTimeMilliseconds()
        };

    private static long? ToUnixMilliseconds(DateTimeOffset? value) => value?.ToUnixTimeMilliseconds();
    private static DateTimeOffset? FromUnixMilliseconds(long? value) =>
        value is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(value.Value);

    private static string FormatTlsMode(BitwardenTlsMode mode) => mode switch
    {
        BitwardenTlsMode.SystemTrust => "system",
        BitwardenTlsMode.SystemAndCustomCertificate => "system-custom-ca",
        BitwardenTlsMode.MutualTls => "mutual-tls",
        _ => throw new BitwardenProtocolException($"Unsupported Bitwarden TLS mode: {(int)mode}.")
    };

    private static BitwardenTlsMode ParseTlsMode(string value) => value switch
    {
        "system" => BitwardenTlsMode.SystemTrust,
        "system-custom-ca" => BitwardenTlsMode.SystemAndCustomCertificate,
        "mutual-tls" => BitwardenTlsMode.MutualTls,
        _ => throw new CryptographicException($"Stored Bitwarden TLS mode is invalid: {value}.")
    };
}
