namespace Monica.Core.Passkeys;

/// <summary>Input for a platform authenticator ceremony.</summary>
public sealed record NativePasskeyCreateRequest(
    string RpId,
    string RpName,
    byte[] UserId,
    string UserName,
    string UserDisplayName,
    byte[] Challenge,
    int Algorithm = PasskeyAlgorithm.Es256,
    bool Discoverable = true,
    bool RequireUserVerification = true,
    string? Origin = null,
    nint ParentWindowHandle = 0);

public sealed record NativePasskeyAssertionRequest(
    string RpId,
    byte[] Challenge,
    byte[]? CredentialId = null,
    string? Origin = null,
    bool RequireUserVerification = true,
    nint ParentWindowHandle = 0);

/// <summary>Raw WebAuthn ceremony output returned by Windows Hello or another platform authenticator.</summary>
public sealed record NativePasskeyRegistration(
    byte[] CredentialId,
    byte[] AuthenticatorData,
    byte[] AttestationObject,
    byte[] ClientDataJson,
    string Transport,
    bool IsDiscoverable,
    bool IsUserVerified);

public sealed record NativePasskeyAssertion(
    byte[] CredentialId,
    byte[] AuthenticatorData,
    byte[] Signature,
    byte[] UserHandle,
    byte[] ClientDataJson,
    string Transport,
    bool IsUserVerified);

/// <summary>
/// Platform passkey boundary. Implementations must keep private keys in the platform authenticator;
/// the application receives only public WebAuthn material and assertion bytes.
/// </summary>
public interface INativePasskeyAuthenticator
{
    bool IsAvailable { get; }
    bool IsUserVerifyingPlatformAuthenticatorAvailable { get; }

    Task<NativePasskeyRegistration> CreateAsync(
        NativePasskeyCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<NativePasskeyAssertion> GetAssertionAsync(
        NativePasskeyAssertionRequest request,
        CancellationToken cancellationToken = default);
}
