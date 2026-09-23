using Monica.Core.Models;
using Monica.Core.Passkeys;

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
public sealed class PasskeyService(IPasskeyStore store) : IPasskeyService
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
}
