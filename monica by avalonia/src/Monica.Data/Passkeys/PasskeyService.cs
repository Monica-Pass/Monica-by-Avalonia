using Monica.Core.Models;
using Monica.Core.Passkeys;
using System.Security.Cryptography;

namespace Monica.Data.Passkeys;

public sealed record PasskeyCreateRequest(
    string RpId,
    byte[] Challenge,
    byte[] UserHandle,
    string UserName,
    string? RpName = null,
    string? UserDisplayName = null,
    int Algorithm = PasskeyAlgorithm.Es256,
    string? Origin = null,
    bool IsDiscoverable = true,
    bool IsUserVerificationRequired = true,
    long? CategoryId = null,
    long? BoundPasswordId = null);

public interface IPasskeyService
{
    Task<PasskeyEntry> CreateAsync(PasskeyCreateRequest request, CancellationToken cancellationToken = default);

    Task<PlatformPasskeyCreationResult> CreatePlatformRegistrationAsync(
        PasskeyCreateRequest request, string platformMode,
        nint parentWindowHandle = 0, CancellationToken cancellationToken = default);

    Task<PasskeyAssertion?> AssertAsync(
        string credentialId,
        byte[] challenge,
        string? rpId = null,
        string? origin = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The only passkey write and read path: it verifies a fresh registration with the relying-party checks
/// before persisting it, and it re-verifies every assertion it produces, so a response that would be
/// rejected by a standards-compliant server never leaves Monica. Private key material is resolved for
/// the single signature call and never travels back out through the entry.
/// </summary>
public sealed record PlatformPasskeyCreationResult(PasskeyEntry Entry, NativePasskeyRegistration Registration);

public sealed class PasskeyService(
    IPasskeyStore store,
    INativePasskeyAuthenticator? nativeAuthenticator = null) : IPasskeyService
{
    public async Task<PasskeyEntry> CreateAsync(
        PasskeyCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var rpId = PasskeyRpId.Normalize(request.RpId)
            ?? throw new ArgumentException("A passkey needs a relying-party id.", nameof(request));

        var registration = PasskeyAuthenticator.Register(
            rpId,
            request.Challenge,
            request.Algorithm,
            request.Origin);

        if (!PasskeyVerifier.TryVerifyRegistration(
                registration,
                request.Challenge,
                rpId,
                out var failure,
                request.Origin))
        {
            throw new InvalidOperationException($"Refused to store a passkey that fails verification: {failure}.");
        }

        var now = DateTimeOffset.UtcNow;
        var entry = new PasskeyEntry
        {
            CredentialId = registration.CredentialIdBase64Url,
            RpId = rpId,
            RpName = request.RpName?.Trim() ?? rpId,
            UserId = PasskeyBase64Url.Encode(request.UserHandle),
            UserName = request.UserName.Trim(),
            UserDisplayName = string.IsNullOrWhiteSpace(request.UserDisplayName)
                ? request.UserName.Trim()
                : request.UserDisplayName.Trim(),
            PublicKeyAlgorithm = registration.KeyMaterial.Algorithm,
            PublicKey = registration.KeyMaterial.PublicKeySpkiBase64,
            PrivateKeyAlias = string.Empty,
            CreatedAt = now,
            LastUsedAt = now,
            Aaguid = PasskeyAuthenticator.MonicaAaguidText,
            IsDiscoverable = request.IsDiscoverable,
            IsUserVerificationRequired = request.IsUserVerificationRequired,
            CategoryId = request.CategoryId,
            BoundPasswordId = request.BoundPasswordId,
            PasskeyMode = PasskeyModes.BitwardenCompatible
        };

        entry.Id = await store.SaveAsync(
            entry,
            registration.KeyMaterial.PrivateKeyPkcs8Base64,
            cancellationToken);
        return entry;
    }

    public async Task<PasskeyAssertion?> AssertAsync(
        string credentialId,
        byte[] challenge,
        string? rpId = null,
        string? origin = null,
        CancellationToken cancellationToken = default)
    {
        var entry = await store.FindAsync(credentialId, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("No passkey matches that credential id.");

        var requestedRpId = PasskeyRpId.Normalize(rpId ?? entry.RpId)
            ?? throw new ArgumentException("A passkey assertion needs a relying-party id.", nameof(rpId));
        if (!PasskeyRpId.IsEquivalent(requestedRpId, entry.RpId))
        {
            throw new InvalidOperationException(
                $"This passkey belongs to '{entry.RpId}' and cannot sign for '{requestedRpId}'.");
        }

        if (entry.PasskeyMode == PasskeyModes.WindowsHello)
        {
            if (nativeAuthenticator is null || !nativeAuthenticator.IsAvailable)
            {
                throw new PlatformNotSupportedException("This passkey is stored in Windows Hello, which is unavailable on this platform.");
            }

            var nativeAssertion = await nativeAuthenticator.GetAssertionAsync(
                new NativePasskeyAssertionRequest(
                    requestedRpId,
                    challenge,
                    DecodeNativeCredentialId(entry.CredentialId),
                    origin,
                    entry.IsUserVerificationRequired),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!NativePasskeyValidation.VerifyClientData(nativeAssertion.ClientDataJson,
                    PasskeyClientData.AssertionType, challenge, origin ?? $"https://{requestedRpId}") ||
                !PasskeyClientData.TryParse(nativeAssertion.ClientDataJson, out var nativeClientData) || nativeClientData is null ||
                !CryptographicOperations.FixedTimeEquals(nativeAssertion.CredentialId, DecodeNativeCredentialId(entry.CredentialId)))
            {
                throw new InvalidOperationException("Windows Hello returned invalid client data.");
            }

            var nativePasskeyAssertion = new PasskeyAssertion(
                nativeAssertion.AuthenticatorData,
                nativeClientData,
                nativeAssertion.Signature,
                PasskeyCredentialId.Normalize(entry.CredentialId) ?? entry.CredentialId,
                PasskeyBase64Url.Encode(nativeAssertion.UserHandle));
            if (!PasskeyAuthenticatorDataCodec.TryParse(nativeAssertion.AuthenticatorData, out var authData) || authData is null ||
                !authData.UserPresent || (entry.IsUserVerificationRequired && !authData.UserVerified) ||
                !CryptographicOperations.FixedTimeEquals(authData.RpIdHash, PasskeyRpId.Hash(requestedRpId)) ||
                !NativePasskeyValidation.VerifySignature(entry.PublicKeyAlgorithm, entry.PublicKey,
                    [.. nativeAssertion.AuthenticatorData, .. nativeClientData.ClientDataHash], nativeAssertion.Signature))
            {
                throw new InvalidOperationException("Refused to release an invalid Windows Hello assertion.");
            }

            await store.MarkUsedAsync(entry.Id, cancellationToken);
            return nativePasskeyAssertion;
        }

        var privateKey = await store.ResolvePrivateKeyAsync(entry, cancellationToken)
            ?? throw new InvalidOperationException("This passkey's private key is not available.");

        var assertion = PasskeyAuthenticator.Assert(entry, privateKey, challenge, requestedRpId, origin);
        if (!PasskeyVerifier.TryVerifyAssertion(
                assertion,
                entry.PublicKeyAlgorithm,
                entry.PublicKey,
                challenge,
                requestedRpId,
                out var failure,
                origin))
        {
            throw new InvalidOperationException($"Refused to release a passkey assertion: {failure}.");
        }

        await store.MarkUsedAsync(entry.Id, cancellationToken);
        return assertion;
    }

    /// <summary>
    /// Registers a credential in the platform authenticator. The private key never enters Monica's
    /// database; only the public WebAuthn material and the platform credential id are persisted.
    /// </summary>
    public async Task<PasskeyEntry> CreateWithPlatformAuthenticatorAsync(
        PasskeyCreateRequest request,
        string platformMode,
        CancellationToken cancellationToken = default) =>
        (await CreatePlatformRegistrationAsync(request, platformMode, cancellationToken: cancellationToken)).Entry;

    public async Task<PlatformPasskeyCreationResult> CreatePlatformRegistrationAsync(
        PasskeyCreateRequest request,
        string platformMode,
        nint parentWindowHandle = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (platformMode != PasskeyModes.WindowsHello)
            throw new NotSupportedException("Only the implemented Windows platform mode can be registered here.");
        if (nativeAuthenticator is null || !nativeAuthenticator.IsAvailable)
        {
            throw new PlatformNotSupportedException("The selected platform passkey authenticator is unavailable.");
        }

        var rpId = NativePasskeyValidation.ValidateRpAndOrigin(request.RpId, request.Origin, request.Challenge);
        var nativeRequest = new NativePasskeyCreateRequest(
                rpId,
                request.RpName?.Trim() ?? rpId,
                request.UserHandle,
                request.UserName.Trim(),
                string.IsNullOrWhiteSpace(request.UserDisplayName) ? request.UserName.Trim() : request.UserDisplayName.Trim(),
                request.Challenge,
                request.Algorithm,
                request.IsDiscoverable,
                request.IsUserVerificationRequired,
                request.Origin,
                parentWindowHandle);
        NativePasskeyValidation.ValidateCreate(nativeRequest);
        var native = await nativeAuthenticator.CreateAsync(nativeRequest, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (!PasskeyAuthenticatorDataCodec.TryParse(native.AuthenticatorData, out var authData) ||
            authData?.CredentialId is not { Length: > 0 } credentialId ||
            authData.PublicKey is null ||
            !credentialId.AsSpan().SequenceEqual(native.CredentialId) || authData.PublicKey.Algorithm != request.Algorithm)
        {
            throw new InvalidOperationException("The platform authenticator returned malformed credential data.");
        }

        if (!NativePasskeyValidation.VerifyClientData(native.ClientDataJson, PasskeyClientData.CreateType,
                request.Challenge, request.Origin ?? $"https://{rpId}") ||
            !PasskeyClientData.TryParse(native.ClientDataJson, out var clientData) || clientData is null)
        {
            throw new InvalidOperationException("The platform authenticator returned invalid client data.");
        }

        var entry = new PasskeyEntry
        {
            CredentialId = PasskeyBase64Url.Encode(native.CredentialId),
            RpId = rpId,
            RpName = request.RpName?.Trim() ?? rpId,
            UserId = PasskeyBase64Url.Encode(request.UserHandle),
            UserName = request.UserName.Trim(),
            UserDisplayName = string.IsNullOrWhiteSpace(request.UserDisplayName) ? request.UserName.Trim() : request.UserDisplayName.Trim(),
            PublicKeyAlgorithm = authData.PublicKey.Algorithm,
            PublicKey = authData.PublicKey.ToSubjectPublicKeyInfoBase64(),
            PrivateKeyAlias = string.Empty,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUsedAt = DateTimeOffset.UtcNow,
            Aaguid = ExtractAaguid(native.AuthenticatorData),
            IsDiscoverable = request.IsDiscoverable,
            IsUserVerificationRequired = request.IsUserVerificationRequired,
            Transports = native.Transport,
            BoundPasswordId = request.BoundPasswordId,
            CategoryId = request.CategoryId,
            PasskeyMode = platformMode
        };

        // The platform API has already verified the user and produced the attestation. Validate the
        // client data, RP hash, UV/UP flags and public-key binding before persisting anything.
        if (!string.Equals(clientData.Type, PasskeyClientData.CreateType, StringComparison.Ordinal) ||
            !PasskeyBase64Url.TryDecode(clientData.Challenge, out var returnedChallenge) ||
            returnedChallenge is null || !returnedChallenge.AsSpan().SequenceEqual(request.Challenge) ||
            !authData.UserPresent || (request.IsUserVerificationRequired && !authData.UserVerified) ||
            !authData.RpIdHash.AsSpan().SequenceEqual(PasskeyRpId.Hash(rpId)))
        {
            throw new InvalidOperationException("The platform authenticator returned data that failed WebAuthn checks.");
        }

        entry.Id = await store.SaveAsync(entry, privateKeyPkcs8Base64: null, cancellationToken);
        return new PlatformPasskeyCreationResult(entry, native);
    }

    private static string ExtractAaguid(byte[] authenticatorData)
    {
        if (authenticatorData.Length < 37 + PasskeyAuthenticatorDataCodec.AaguidLength ||
            !((PasskeyAuthenticatorFlags)authenticatorData[32]).HasFlag(PasskeyAuthenticatorFlags.AttestedCredentialData))
        {
            return string.Empty;
        }

        var bytes = authenticatorData.AsSpan(37, PasskeyAuthenticatorDataCodec.AaguidLength).ToArray();
        return PasskeyCredentialId.ToUuidText(bytes);
    }

    private static byte[] DecodeNativeCredentialId(string credentialId) =>
        PasskeyBase64Url.TryDecode(PasskeyCredentialId.ToWebAuthnId(credentialId), out var id) && id.Length > 0
            ? id
            : throw new InvalidOperationException("The native credential id is invalid.");
}
