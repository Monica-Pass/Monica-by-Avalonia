using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Monica.Core.Bitwarden;
using Monica.Platform.Bitwarden;

namespace Monica.Tests;

public sealed class BitwardenNetworkAuthenticationTests
{
    private static readonly BitwardenEndpointSet Endpoints = new(
        new Uri("https://vault.example.test/"),
        new Uri("https://identity.example.test/"),
        new Uri("https://api.example.test/"));

    [Fact]
    public async Task AuthenticateAsync_UsesDesktopPasswordGrantAndUnwrapsVaultKey()
    {
        var encryptionKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var macKey = Enumerable.Range(33, 32).Select(value => (byte)value).ToArray();
        var protectedKey = CreateProtectedVaultKey("secret-password", encryptionKey, macKey);
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/accounts/prelogin" => Json(HttpStatusCode.OK, new
            {
                Kdf = 0,
                KdfIterations = 100_000,
                KdfMemory = (int?)null,
                KdfParallelism = (int?)null
            }),
            "/connect/token" => Json(HttpStatusCode.OK, new
            {
                access_token = "access-value",
                refresh_token = "refresh-value",
                expires_in = 3600,
                Key = protectedKey
            }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var service = CreateService(handler);

        using var result = await service.AuthenticateAsync(new BitwardenAuthenticationRequest(
            "Test@Example.com",
            "secret-password",
            Endpoints,
            new BitwardenTlsOptions(),
            "device-123",
            "Windows desktop"));

        Assert.True(result.Succeeded);
        Assert.Equal("test@example.com", result.Account!.Email);
        Assert.Equal(encryptionKey, result.Secrets!.CopyEncryptionKey());
        Assert.Equal(macKey, result.Secrets.CopyMacKey());
        Assert.Equal("access-value", Encoding.UTF8.GetString(result.Secrets.CopyAccessToken()));
        var tokenRequest = Assert.Single(handler.Requests, request => request.Path == "/connect/token");
        Assert.Equal("VGVzdEBFeGFtcGxlLmNvbQ", tokenRequest.Headers["Auth-Email"]);
        Assert.Equal("desktop", tokenRequest.Form["client_id"]);
        Assert.Equal("api offline_access", tokenRequest.Form["scope"]);
        Assert.Equal("8", tokenRequest.Form["deviceType"]);
        Assert.Equal("no-store", tokenRequest.Headers["Cache-Control"]);
        Assert.DoesNotContain("secret-password", tokenRequest.Form.Values);
    }

    [Fact]
    public async Task AuthenticateAsync_ReturnsTwoFactorChallengeWithoutSessionSecrets()
    {
        var handler = ChallengeHandler(new
        {
            error = "invalid_grant",
            error_description = "Two factor required.",
            TwoFactorProviders2 = new Dictionary<string, object>
            {
                ["1"] = new { },
                ["0"] = new { }
            }
        });
        var service = CreateService(handler);

        using var result = await service.AuthenticateAsync(CreateRequest());

        Assert.False(result.Succeeded);
        Assert.Null(result.Secrets);
        Assert.Equal(BitwardenLoginChallengeKind.TwoFactor, result.Challenge);
        Assert.Equal([0, 1], result.Factors!.Select(factor => factor.Provider));
    }

    [Fact]
    public async Task AuthenticateAsync_RecognizesCaptchaAndNewDeviceChallenges()
    {
        var captchaService = CreateService(ChallengeHandler(new
        {
            error = "invalid_grant",
            HCaptcha_SiteKey = "site-key"
        }));
        using var captcha = await captchaService.AuthenticateAsync(CreateRequest());
        Assert.Equal(BitwardenLoginChallengeKind.Captcha, captcha.Challenge);
        Assert.Equal("site-key", captcha.CaptchaSiteKey);

        var deviceService = CreateService(ChallengeHandler(new
        {
            error = "invalid_grant",
            ErrorModel = new { Message = "New device verification required." }
        }));
        using var device = await deviceService.AuthenticateAsync(CreateRequest());
        Assert.Equal(BitwardenLoginChallengeKind.NewDeviceVerification, device.Challenge);
    }

    [Fact]
    public async Task RefreshAsync_PreservesRotatingMaterialWhenServerOnlyReturnsAccessToken()
    {
        var handler = new RecordingHandler(request => Json(HttpStatusCode.OK, new
        {
            access_token = "replacement-access",
            expires_in = 7200
        }));
        var service = CreateService(handler);
        var account = CreateAccount();
        using var current = new BitwardenAccountSecrets(
            Encoding.UTF8.GetBytes("old-access"),
            Encoding.UTF8.GetBytes("old-refresh"),
            new byte[32],
            Enumerable.Repeat((byte)7, 32).ToArray(),
            Enumerable.Repeat((byte)9, 32).ToArray());

        var refreshed = await service.RefreshAsync(account, current);
        using var secrets = refreshed.Secrets;

        Assert.Equal("replacement-access", Encoding.UTF8.GetString(secrets.CopyAccessToken()));
        Assert.Equal("old-refresh", Encoding.UTF8.GetString(secrets.CopyRefreshToken()));
        var request = Assert.Single(handler.Requests);
        Assert.Equal("refresh_token", request.Form["grant_type"]);
        Assert.Equal("old-refresh", request.Form["refresh_token"]);
        Assert.True(refreshed.Account.AccessTokenExpiresAt > account.AccessTokenExpiresAt);
    }

    [Fact]
    public async Task PreloginAsync_RejectsServerKdfOutsideResourceLimits()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, new
        {
            Kdf = 0,
            KdfIterations = int.MaxValue
        }));

        await Assert.ThrowsAsync<BitwardenProtocolException>(() =>
            CreateService(handler).PreloginAsync(
                "test@example.com",
                Endpoints,
                new BitwardenTlsOptions()));
    }

    private static BitwardenAuthenticationRequest CreateRequest() => new(
        "test@example.com",
        "secret-password",
        Endpoints,
        new BitwardenTlsOptions(),
        "device-123",
        "Windows desktop");

    private static BitwardenAccount CreateAccount()
    {
        var now = DateTimeOffset.UtcNow;
        return new BitwardenAccount
        {
            Id = 1,
            Email = "test@example.com",
            AccountKey = BitwardenAccountIdentity.CreateAccountKey("test@example.com", Endpoints),
            Endpoints = Endpoints,
            Kdf = new BitwardenKdfParameters(BitwardenKdfAlgorithm.Pbkdf2Sha256, 100_000),
            AccessTokenExpiresAt = now,
            IsConnected = true,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static BitwardenAuthenticationService CreateService(RecordingHandler handler) =>
        new(new FakeHttpClientFactory(handler));

    private static RecordingHandler ChallengeHandler(object challenge) =>
        new(request => request.RequestUri!.AbsolutePath == "/accounts/prelogin"
            ? Json(HttpStatusCode.OK, new { Kdf = 0, KdfIterations = 100_000 })
            : Json(HttpStatusCode.BadRequest, challenge));

    private static string CreateProtectedVaultKey(
        string password,
        byte[] encryptionKey,
        byte[] macKey)
    {
        var masterKey = BitwardenKeyDerivation.DeriveMasterKey(
            password,
            "test@example.com",
            new BitwardenKdfParameters(BitwardenKdfAlgorithm.Pbkdf2Sha256, 100_000));
        using var stretched = BitwardenKeyDerivation.StretchMasterKey(masterKey);
        var combined = encryptionKey.Concat(macKey).ToArray();
        try
        {
            return BitwardenCipherStringCrypto.Encrypt(combined, stretched);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
            CryptographicOperations.ZeroMemory(combined);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, object payload) => new(statusCode)
    {
        Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
    };

    private sealed class FakeHttpClientFactory(RecordingHandler handler) : IBitwardenHttpClientFactory
    {
        public HttpClient Create(BitwardenTlsOptions tls, string? clientCertificatePassword = null) =>
            new(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var headers = request.Headers
                .Select(pair => new KeyValuePair<string, IEnumerable<string>>(pair.Key, pair.Value));
            if (request.Content is not null)
            {
                headers = headers.Concat(request.Content.Headers.Select(
                    pair => new KeyValuePair<string, IEnumerable<string>>(pair.Key, pair.Value)));
            }

            Requests.Add(new CapturedRequest(
                request.RequestUri!.AbsolutePath,
                headers
                    .ToDictionary(pair => pair.Key, pair => string.Join(",", pair.Value), StringComparer.OrdinalIgnoreCase),
                ParseForm(body)));
            return responder(request);
        }

        private static Dictionary<string, string> ParseForm(string value) =>
            value.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => pair.Split('=', 2))
                .ToDictionary(
                    pair => Uri.UnescapeDataString(pair[0].Replace('+', ' ')),
                    pair => Uri.UnescapeDataString(pair.ElementAtOrDefault(1)?.Replace('+', ' ') ?? string.Empty));
    }

    private sealed record CapturedRequest(
        string Path,
        IReadOnlyDictionary<string, string> Headers,
        IReadOnlyDictionary<string, string> Form);
}
