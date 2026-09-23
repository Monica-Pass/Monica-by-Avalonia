using Monica.Core.Models;

namespace Monica.Core.Passkeys;

public sealed record PasskeyRegistration(
    byte[] CredentialId,
    PasskeyKeyMaterial KeyMaterial,
    byte[] AuthenticatorData,
    byte[] AttestationObject,
    PasskeyClientData ClientData)
{
    public string CredentialIdBase64Url => PasskeyBase64Url.Encode(CredentialId);
}

public sealed record PasskeyAssertion(
    byte[] AuthenticatorData,
    PasskeyClientData ClientData,
    byte[] Signature,
    string CredentialId,
    string UserHandle)
{
    public string CredentialIdWebAuthn => PasskeyCredentialId.ToWebAuthnId(CredentialId) ?? CredentialId;
}

/// <summary>
/// Monica's own WebAuthn authenticator. It is a roaming software authenticator: the private key lives
/// in the vault next to the credential, so registrations work on every machine the vault is opened on
/// without any platform passkey provider being involved.
/// </summary>
public static class PasskeyAuthenticator
{
    /// <summary>
    /// Keep this stable forever: relying parties brand the authenticator from the AAGUID, while keys
    /// registered under an earlier value stay valid because nothing in the assertion depends on it.
    /// </summary>
    public static byte[] MonicaAaguid { get; } =
    [
        0x6d, 0x6f, 0x6e, 0x69, 0x63, 0x61, 0x4d, 0x33,
        0xa0, 0x01, 0x70, 0x61, 0x73, 0x73, 0x6b, 0x79
    ];

    public const string MonicaAaguidText = "6d6f6e69-6361-4d33-a001-706173736b79";

    public static PasskeyRegistration Register(
        string rpId,
        byte[] challenge,
        int algorithm,
        string? origin = null,
        byte[]? credentialId = null)
    {
        var keyMaterial = PasskeyKeyMaterialGenerator.Generate(algorithm);
        var id = credentialId ?? PasskeyCredentialId.NewRandom();
        var authenticatorData = PasskeyAuthenticatorDataCodec.BuildCreation(
            rpId,
            MonicaAaguid,
            id,
            keyMaterial.CosePublicKey,
            signCount: 0);

        return new PasskeyRegistration(
            id,
            keyMaterial,
            authenticatorData,
            BuildNoneAttestationObject(authenticatorData),
            PasskeyClientData.Build(PasskeyClientData.CreateType, challenge, ResolveOrigin(rpId, origin)));
    }

    /// <summary>
    /// UP and UV are both asserted because a signature is only reachable behind the vault unlock, and
    /// BE/BS are set since the key material is exported and re-imported with the vault rather than being
    /// hardware-bound. The counter is pinned at 0 on purpose: WebAuthn allows an authenticator to omit
    /// it, and Monica restores keys from whole-vault backups without a live two-way sync, so a monotonic
    /// counter diverges across machines and the relying party rejects the credential once it goes
    /// backwards. That divergence is the "passkey worked for a while and then stopped" failure.
    /// </summary>
    public static PasskeyAssertion Assert(
        PasskeyEntry credential,
        string privateKeyPkcs8Base64,
        byte[] challenge,
        string? rpId = null,
        string? origin = null)
    {
        var resolvedRpId = rpId ?? credential.RpId;
        var authenticatorData = PasskeyAuthenticatorDataCodec.BuildAssertion(resolvedRpId, signCount: 0);
        var clientData = PasskeyClientData.Build(
            PasskeyClientData.AssertionType,
            challenge,
            ResolveOrigin(resolvedRpId, origin));
        var signature = PasskeyKeyMaterialGenerator.Sign(
            credential.PublicKeyAlgorithm,
            privateKeyPkcs8Base64,
            [.. authenticatorData, .. clientData.ClientDataHash]);

        return new PasskeyAssertion(
            authenticatorData,
            clientData,
            signature,
            credential.CredentialId,
            credential.UserId);
    }

    /// <summary>
    /// "none" attestation: a roaming software key has no manufacturer trust anchor to speak for it, and
    /// inventing one would only look like a forged packed attestation to a relying party.
    /// </summary>
    public static byte[] BuildNoneAttestationObject(byte[] authenticatorData) =>
        new CborBuilder()
            .WriteStartMap(3)
            .WriteTextString("fmt")
            .WriteTextString("none")
            .WriteTextString("attStmt")
            .WriteStartMap(0)
            .WriteTextString("authData")
            .WriteByteString(authenticatorData)
            .Build();

    /// <summary>
    /// UP and UV are both asserted because a signature is only reachable behind the vault unlock, and
    /// BE/BS are set since the key material is exported and re-imported with the vault rather than being
    /// hardware-bound. The counter is pinned at 0 on purpose: WebAuthn allows an authenticator to omit
    /// it, and Monica restores keys from whole-vault backups without a live two-way sync, so a monotonic
    /// counter diverges across machines and the relying party rejects the credential once it goes
    /// backwards. That divergence is the "passkey worked for a while and then stopped" failure.
    /// </summary>
    private static string ResolveOrigin(string rpId, string? origin) =>
        string.IsNullOrWhiteSpace(origin) ? $"https://{rpId}" : origin.Trim();
}
