using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Monica.Core.Bitwarden;

namespace Monica.Platform.Bitwarden;

public interface IBitwardenHttpClientFactory
{
    HttpClient Create(
        BitwardenTlsOptions tls,
        string? clientCertificatePassword = null);
}

public sealed class BitwardenHttpClientFactory : IBitwardenHttpClientFactory
{
    public HttpClient Create(
        BitwardenTlsOptions tls,
        string? clientCertificatePassword = null)
    {
        ArgumentNullException.ThrowIfNull(tls);
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.Brotli |
                                     DecompressionMethods.Deflate |
                                     DecompressionMethods.GZip,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxResponseHeadersLength = 64,
            SslOptions = BuildSslOptions(tls, clientCertificatePassword)
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(60),
            MaxResponseContentBufferSize = BitwardenHttpLimits.MaximumResponseBytes
        };
    }

    private static SslClientAuthenticationOptions BuildSslOptions(
        BitwardenTlsOptions tls,
        string? clientCertificatePassword)
    {
        var options = new SslClientAuthenticationOptions();
        X509Certificate2? customRoot = null;
        if (!string.IsNullOrWhiteSpace(tls.CustomCaCertificatePath))
        {
            customRoot = X509CertificateLoader.LoadCertificateFromFile(tls.CustomCaCertificatePath);
            options.RemoteCertificateValidationCallback = (_, certificate, chain, errors) =>
                ValidateWithAdditionalRoot(certificate, chain, errors, customRoot);
        }

        if (!string.IsNullOrWhiteSpace(tls.ClientCertificatePath))
        {
            options.ClientCertificates =
            [
                X509CertificateLoader.LoadPkcs12FromFile(
                    tls.ClientCertificatePath,
                    clientCertificatePassword,
                    X509KeyStorageFlags.EphemeralKeySet)
            ];
        }

        return options;
    }

    private static bool ValidateWithAdditionalRoot(
        X509Certificate? certificate,
        X509Chain? systemChain,
        SslPolicyErrors errors,
        X509Certificate2 customRoot)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        if (certificate is null ||
            errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch) ||
            errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
        {
            return false;
        }

        using var leaf = certificate is X509Certificate2 certificate2
            ? X509CertificateLoader.LoadCertificate(certificate2.RawData)
            : X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
        using var customChain = new X509Chain();
        customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        customChain.ChainPolicy.CustomTrustStore.Add(customRoot);
        customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        customChain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        customChain.ChainPolicy.VerificationTime = DateTime.UtcNow;
        if (systemChain is not null)
        {
            foreach (var element in systemChain.ChainElements.Cast<X509ChainElement>().Skip(1))
            {
                customChain.ChainPolicy.ExtraStore.Add(element.Certificate);
            }
        }

        return customChain.Build(leaf);
    }
}

internal static class BitwardenHttpLimits
{
    public const int MaximumResponseBytes = 32 * 1024 * 1024;
    public const int MaximumIdentityResponseBytes = 256 * 1024;
    public const int MaximumErrorMessageLength = 4096;
}
