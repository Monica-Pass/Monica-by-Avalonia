using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Monica.Core.Bitwarden;

namespace Monica.Platform.Bitwarden;

internal sealed class BitwardenIdentityClient(HttpClient httpClient, BitwardenEndpointSet endpoints)
{
    private const string ClientVersion = "2025.9.1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly BitwardenEndpointSet _endpoints = BitwardenEndpointPolicy.Validate(endpoints);

    public async Task<BitwardenKdfParameters> PreloginAsync(
        string email,
        CancellationToken cancellationToken)
    {
        var canonicalEmail = BitwardenKdfPolicy.CanonicalizeEmail(email);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(_endpoints.Identity, "accounts/prelogin"))
        {
            Content = JsonContent.Create(new { email = canonicalEmail }, options: JsonOptions)
        };
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var payload = await ReadLimitedAsync(response, BitwardenHttpLimits.MaximumIdentityResponseBytes, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateHttpException("Bitwarden prelogin failed", response.StatusCode, payload);
        }

        var dto = Deserialize<PreloginDto>(payload);
        var parameters = new BitwardenKdfParameters(
            (BitwardenKdfAlgorithm)dto.Kdf,
            dto.KdfIterations,
            dto.KdfMemory,
            dto.KdfParallelism);
        BitwardenKdfPolicy.Validate(parameters);
        return parameters;
    }

    public async Task<TokenReply> LoginAsync(
        TokenRequest input,
        CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = input.Email,
            ["password"] = input.PasswordHash,
            ["scope"] = "api offline_access",
            ["client_id"] = "desktop",
            ["deviceIdentifier"] = input.DeviceIdentifier,
            ["deviceType"] = "8",
            ["deviceName"] = input.DeviceName
        };
        AddOptional(form, "captchaResponse", input.CaptchaResponse);
        AddOptional(form, "twoFactorToken", input.TwoFactorToken);
        if (input.TwoFactorProvider is { } provider)
        {
            form["twoFactorProvider"] = provider.ToString(System.Globalization.CultureInfo.InvariantCulture);
            form["twoFactorRemember"] = input.RememberTwoFactor ? "1" : "0";
        }

        AddOptional(form, "newDeviceOtp", input.NewDeviceOtp);
        using var request = CreateTokenRequest(form);
        request.Headers.TryAddWithoutValidation("Auth-Email", ToBase64Url(input.Email));
        request.Headers.TryAddWithoutValidation("device-type", "8");
        request.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
        return await SendTokenAsync(request, cancellationToken);
    }

    public async Task<TokenReply> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new BitwardenProtocolException("Bitwarden refresh token is required.");
        }

        using var request = CreateTokenRequest(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = "desktop"
        });
        return await SendTokenAsync(request, cancellationToken);
    }

    private HttpRequestMessage CreateTokenRequest(IReadOnlyDictionary<string, string> form)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_endpoints.Identity, "connect/token"))
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.TryAddWithoutValidation("Bitwarden-Client-Name", "desktop");
        request.Headers.TryAddWithoutValidation("Bitwarden-Client-Version", ClientVersion);
        return request;
    }

    private async Task<TokenReply> SendTokenAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var payload = await ReadLimitedAsync(response, BitwardenHttpLimits.MaximumIdentityResponseBytes, cancellationToken);
        var dto = payload.Length == 0 ? new TokenDto() : Deserialize<TokenDto>(payload);
        if (response.IsSuccessStatusCode)
        {
            if (string.IsNullOrWhiteSpace(dto.AccessToken))
            {
                throw new BitwardenProtocolException("Bitwarden token response omitted the access token.");
            }

            return TokenReply.Success(dto);
        }

        return TokenReply.Failure(response.StatusCode, dto, SanitizeMessage(dto, response.StatusCode));
    }

    private static async Task<byte[]> ReadLimitedAsync(
        HttpResponseMessage response,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > maximumBytes)
        {
            throw new BitwardenProtocolException("Bitwarden response exceeds the supported size.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maximumBytes)
            {
                throw new BitwardenProtocolException("Bitwarden response exceeds the supported size.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static T Deserialize<T>(byte[] payload)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(payload, JsonOptions) ??
                   throw new BitwardenProtocolException("Bitwarden returned an empty JSON document.");
        }
        catch (JsonException exception)
        {
            throw new BitwardenProtocolException("Bitwarden returned malformed JSON.", exception);
        }
    }

    private static HttpRequestException CreateHttpException(
        string message,
        HttpStatusCode statusCode,
        byte[] payload) =>
        new($"{message}: {(int)statusCode} {ReadSafeError(payload)}", null, statusCode);

    private static string ReadSafeError(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return "The server returned no details.";
        }

        try
        {
            var dto = Deserialize<TokenDto>(payload);
            return SanitizeMessage(dto, null);
        }
        catch (BitwardenProtocolException)
        {
            return "The server returned an unreadable error response.";
        }
    }

    private static string SanitizeMessage(TokenDto dto, HttpStatusCode? statusCode)
    {
        var candidate = dto.ErrorModel?.Message ?? dto.ErrorDescription ?? dto.Error ??
                        (statusCode is null ? "Bitwarden rejected the request." : $"HTTP {(int)statusCode.Value}");
        var sanitized = new string(candidate.Where(ch => !char.IsControl(ch) || ch is '\r' or '\n' or '\t').ToArray());
        return sanitized.Length <= BitwardenHttpLimits.MaximumErrorMessageLength
            ? sanitized
            : sanitized[..BitwardenHttpLimits.MaximumErrorMessageLength];
    }

    private static void AddOptional(IDictionary<string, string> form, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            form[key] = value.Trim();
        }
    }

    private static string ToBase64Url(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal sealed record TokenRequest(
        string Email,
        string PasswordHash,
        string DeviceIdentifier,
        string DeviceName,
        string? CaptchaResponse,
        string? TwoFactorToken,
        int? TwoFactorProvider,
        bool RememberTwoFactor,
        string? NewDeviceOtp);

    internal sealed record TokenReply(
        bool Succeeded,
        HttpStatusCode StatusCode,
        TokenDto Payload,
        string? Message)
    {
        public static TokenReply Success(TokenDto payload) => new(true, HttpStatusCode.OK, payload, null);
        public static TokenReply Failure(HttpStatusCode statusCode, TokenDto payload, string message) =>
            new(false, statusCode, payload, message);
    }

    internal sealed record PreloginDto
    {
        public int Kdf { get; init; }
        public int KdfIterations { get; init; }
        public int? KdfMemory { get; init; }
        public int? KdfParallelism { get; init; }
    }

    internal sealed record TokenDto
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; } = 3600;
        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }
        public string? Key { get; init; }
        public string? Error { get; init; }
        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; init; }
        public TokenErrorModel? ErrorModel { get; init; }
        [JsonPropertyName("HCaptcha_SiteKey")]
        public string? CaptchaSiteKey { get; init; }
        public Dictionary<string, JsonElement>? TwoFactorProviders2 { get; init; }
        public List<string>? TwoFactorProviders { get; init; }
    }

    internal sealed record TokenErrorModel
    {
        public string? Message { get; init; }
    }
}
