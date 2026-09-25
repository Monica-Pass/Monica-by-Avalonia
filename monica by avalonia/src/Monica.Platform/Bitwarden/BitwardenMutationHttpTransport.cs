using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Monica.Core.Bitwarden;

namespace Monica.Platform.Bitwarden;

public sealed class BitwardenMutationTransportFactory(
    IBitwardenHttpClientFactory httpClientFactory) : IBitwardenMutationTransportFactory
{
    public IBitwardenOwnedMutationTransport Create(
        BitwardenAccount account,
        BitwardenAccountSecrets secrets) =>
        new BitwardenMutationHttpTransport(httpClientFactory, account, secrets);
}

internal sealed class BitwardenMutationHttpTransport : IBitwardenOwnedMutationTransport
{
    private const int MaximumMutationBytes = 2 * 1024 * 1024;
    private readonly BitwardenAccount _account;
    private readonly HttpClient _client;
    private byte[]? _accessToken;

    public BitwardenMutationHttpTransport(
        IBitwardenHttpClientFactory httpClientFactory,
        BitwardenAccount account,
        BitwardenAccountSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _account = account ?? throw new ArgumentNullException(nameof(account));
        ArgumentNullException.ThrowIfNull(secrets);
        _accessToken = secrets.CopyAccessToken();
        var certificatePasswordBytes = secrets.CopyClientCertificatePassword();
        try
        {
            _client = httpClientFactory.Create(
                account.Tls,
                certificatePasswordBytes is null ? null : Encoding.UTF8.GetString(certificatePasswordBytes));
        }
        finally
        {
            if (certificatePasswordBytes is not null)
            {
                CryptographicOperations.ZeroMemory(certificatePasswordBytes);
            }
        }
    }

    public async Task<BitwardenMutationResponse> SendAsync(
        BitwardenMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.VaultId != _account.Id || _accessToken is null)
        {
            throw new BitwardenProtocolException("Bitwarden mutation transport does not match the active vault.");
        }

        ValidatePayload(request);
        if (request.OperationType is BitwardenMutationOperationType.Update
                or BitwardenMutationOperationType.Delete
                or BitwardenMutationOperationType.SoftDelete
                or BitwardenMutationOperationType.Restore)
        {
            var preflight = await ReadRevisionAsync(request.CipherId, cancellationToken);
            if (!preflight.Succeeded)
            {
                return preflight.Response!;
            }

            if (!string.Equals(preflight.Revision, request.ExpectedRemoteRevision, StringComparison.Ordinal))
            {
                return new BitwardenMutationResponse(
                    false,
                    request.CipherId,
                    preflight.Revision,
                    (int)HttpStatusCode.Conflict,
                    "The remote Bitwarden item changed before this mutation could be applied.");
            }
        }

        var (method, uri) = request.OperationType switch
        {
            BitwardenMutationOperationType.Create =>
                (HttpMethod.Post, new Uri(_account.Endpoints.Api, "ciphers")),
            BitwardenMutationOperationType.Update =>
                (HttpMethod.Put, CipherUri(request.CipherId)),
            BitwardenMutationOperationType.Delete =>
                (HttpMethod.Delete, CipherUri(request.CipherId)),
            // The official clients move a cipher to the trash with this route and reserve DELETE for
            // erasing it, which is the pair Monica's own recoverable trash and permanent purge map onto.
            BitwardenMutationOperationType.SoftDelete =>
                (HttpMethod.Put, TrashUri(request.CipherId)),
            // A restore is decided by the route as well, and unlike the deletions the server answers it
            // with the whole revived cipher, so its revision has to be read back (see ReadsCipherResponse).
            BitwardenMutationOperationType.Restore =>
                (HttpMethod.Put, RestoreUri(request.CipherId)),
            _ => throw new BitwardenProtocolException("Unsupported Bitwarden mutation operation.")
        };
        using var message = CreateRequest(method, uri, request.IdempotencyKey);
        if (!WritesNoBody(request.OperationType))
        {
            message.Content = new StringContent(request.PayloadJson, Encoding.UTF8, "application/json");
        }

        using var response = await _client.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        return await ReadMutationResponseAsync(request, response, cancellationToken);
    }

    public void Dispose()
    {
        _client.Dispose();
        if (_accessToken is not null)
        {
            CryptographicOperations.ZeroMemory(_accessToken);
            _accessToken = null;
        }
    }

    private async Task<(bool Succeeded, string? Revision, BitwardenMutationResponse? Response)> ReadRevisionAsync(
        string cipherId,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, CipherUri(cipherId), null);
        using var response = await _client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var payload = await BitwardenHttpContent.ReadLimitedAsync(response, MaximumMutationBytes, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (false, null, Failure(response));
        }

        var dto = BitwardenHttpContent.Deserialize<MutationCipherResponseDto>(payload);
        if (string.IsNullOrWhiteSpace(dto.RevisionDate))
        {
            throw new BitwardenProtocolException("Bitwarden cipher response omitted its revision.");
        }

        return (true, dto.RevisionDate, null);
    }

    private async Task<BitwardenMutationResponse> ReadMutationResponseAsync(
        BitwardenMutationRequest request,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await BitwardenHttpContent.ReadLimitedAsync(response, MaximumMutationBytes, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Failure(response);
        }

        if (!ReadsCipherResponse(request.OperationType))
        {
            return new BitwardenMutationResponse(true, request.CipherId, request.ExpectedRemoteRevision);
        }

        var dto = BitwardenHttpContent.Deserialize<MutationCipherResponseDto>(payload);
        if (string.IsNullOrWhiteSpace(dto.Id) || string.IsNullOrWhiteSpace(dto.RevisionDate))
        {
            throw new BitwardenProtocolException("Bitwarden mutation response omitted its identity or revision.");
        }

        return new BitwardenMutationResponse(true, dto.Id, dto.RevisionDate);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string? idempotencyKey)
    {
        var accessToken = _accessToken ?? throw new ObjectDisposedException(nameof(BitwardenMutationHttpTransport));
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Encoding.UTF8.GetString(accessToken));
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        return request;
    }

    private static BitwardenMutationResponse Failure(HttpResponseMessage response) => new(
        false,
        null,
        null,
        (int)response.StatusCode,
        $"Bitwarden mutation failed with HTTP {(int)response.StatusCode}.",
        ReadRetryAfter(response));

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        if (retry?.Delta is { } delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        return retry?.Date is { } date
            ? date - DateTimeOffset.UtcNow is { } delay && delay > TimeSpan.Zero ? delay : TimeSpan.Zero
            : null;
    }

    private static void ValidatePayload(BitwardenMutationRequest request)
    {
        if (Encoding.UTF8.GetByteCount(request.PayloadJson) > MaximumMutationBytes)
        {
            throw new BitwardenProtocolException("Bitwarden mutation payload exceeds the supported size.");
        }

        if (!WritesNoBody(request.OperationType))
        {
            try
            {
                using var document = JsonDocument.Parse(request.PayloadJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new BitwardenProtocolException("Bitwarden mutation payload must be a JSON object.");
                }
            }
            catch (JsonException exception)
            {
                throw new BitwardenProtocolException("Bitwarden mutation payload is malformed.", exception);
            }
        }
    }

    private Uri CipherUri(string cipherId)
    {
        if (string.IsNullOrWhiteSpace(cipherId) || cipherId.Length > 256)
        {
            throw new BitwardenProtocolException("Bitwarden cipher identity is invalid.");
        }

        return new Uri(_account.Endpoints.Api, $"ciphers/{Uri.EscapeDataString(cipherId)}");
    }

    private Uri TrashUri(string cipherId) =>
        new($"{CipherUri(cipherId).AbsoluteUri}/delete", UriKind.Absolute);

    private Uri RestoreUri(string cipherId) =>
        new($"{CipherUri(cipherId).AbsoluteUri}/restore", UriKind.Absolute);

    /// <summary>
    /// These routes decide the change on their own, so the queued payload is not sent. A restore belongs
    /// here even though it revives the cipher: sending the local copy back over a trashed one would look
    /// like an edit the server has no reason to accept.
    /// </summary>
    private static bool WritesNoBody(BitwardenMutationOperationType operation) =>
        operation is BitwardenMutationOperationType.Delete
            or BitwardenMutationOperationType.SoftDelete
            or BitwardenMutationOperationType.Restore;

    /// <summary>
    /// Whether the answer names the cipher and its revision. Both deletions leave the cipher where the
    /// server put it and answer with nothing to read, while a restore answers with the revived cipher,
    /// whose revision is what tells the next pull that the local copy and the remote one are the same
    /// state - without it the pull reads a content difference at an older revision and backs the entry up.
    /// </summary>
    private static bool ReadsCipherResponse(BitwardenMutationOperationType operation) =>
        operation is BitwardenMutationOperationType.Create
            or BitwardenMutationOperationType.Update
            or BitwardenMutationOperationType.Restore;
}
