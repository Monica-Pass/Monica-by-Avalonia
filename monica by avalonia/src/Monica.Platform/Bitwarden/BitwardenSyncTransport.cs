using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Monica.Core.Bitwarden;

namespace Monica.Platform.Bitwarden;

public sealed class BitwardenSyncTransport(
    IBitwardenHttpClientFactory httpClientFactory,
    TimeProvider? timeProvider = null) : IBitwardenSyncTransport
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<BitwardenRemoteSyncResult> DownloadAsync(
        BitwardenAccount account,
        BitwardenAccountSecrets secrets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(secrets);
        var accessToken = secrets.CopyAccessToken();
        var certificatePasswordBytes = secrets.CopyClientCertificatePassword();
        try
        {
            var certificatePassword = certificatePasswordBytes is null
                ? null
                : Encoding.UTF8.GetString(certificatePasswordBytes);
            using var client = httpClientFactory.Create(account.Tls, certificatePassword);
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(account.Endpoints.Api, "sync?excludeDomains=true"));
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                Encoding.UTF8.GetString(accessToken));
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var payload = await BitwardenHttpContent.ReadLimitedAsync(
                response,
                BitwardenHttpLimits.MaximumResponseBytes,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Bitwarden sync failed with HTTP {(int)response.StatusCode}.",
                    null,
                    response.StatusCode);
            }

            var dto = BitwardenHttpContent.Deserialize<VaultSyncDto>(payload);
            using var vaultKey = secrets.CreateVaultKey();
            return new BitwardenCipherDecoder(vaultKey).Decode(dto, _timeProvider.GetUtcNow());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(accessToken);
            if (certificatePasswordBytes is not null)
            {
                CryptographicOperations.ZeroMemory(certificatePasswordBytes);
            }
        }
    }
}
