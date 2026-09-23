using System.Security.Cryptography;
using System.Text;

namespace Monica.Core.Passkeys;

/// <summary>
/// The relying-party half of the engine: it checks a response the way a standards-compliant server
/// would, without trusting anything the authenticator side chose to believe about itself.
///
/// Failure reasons are short machine-readable tokens and never contain key material.
/// </summary>
public static class PasskeyVerifier
{
    public static bool TryVerifyRegistration(
        PasskeyRegistration registration,
        byte[] expectedChallenge,
        string expectedRpId,
        out string? failure,
        string? expectedOrigin = null)
    {
        failure = null;
        if (!TryVerifyClientData(
                registration.ClientData,
                PasskeyClientData.CreateType,
                expectedChallenge,
                expectedOrigin ?? ResolveOrigin(expectedRpId),
                out failure))
        {
            return false;
        }

        if (!TryReadNoneAttestationObject(registration.AttestationObject, out var embeddedAuthData))
        {
            failure = "attestation-object";
            return false;
        }

        if (!embeddedAuthData.AsSpan().SequenceEqual(registration.AuthenticatorData))
        {
            failure = "attestation-auth-data-mismatch";
            return false;
        }

        if (!PasskeyAuthenticatorDataCodec.TryParse(registration.AuthenticatorData, out var authData) ||
            authData is null)
        {
            failure = "auth-data-malformed";
            return false;
        }

        if (!authData.Flags.HasFlag(PasskeyAuthenticatorFlags.AttestedCredentialData))
        {
            failure = "missing-attested-credential-data";
            return false;
        }

        if (!TryVerifyAuthDataHeader(authData, expectedRpId, out failure))
        {
            return false;
        }

        if (authData.CredentialId is not { Length: > 0 } credentialId ||
            !credentialId.AsSpan().SequenceEqual(registration.CredentialId))
        {
            failure = "credential-id-mismatch";
            return false;
        }

        if (authData.PublicKey is null ||
            authData.PublicKey.KeyType != KeyTypeFor(registration.KeyMaterial.Algorithm) ||
            !authData.PublicKey.MatchesSubjectPublicKeyInfo(registration.KeyMaterial.PublicKeySpkiBase64))
        {
            failure = "public-key-mismatch";
            return false;
        }

        return true;
    }

    /// <summary>
    /// The sign count is read but never compared: Monica's keys are 0-counter by design, and enforcing
    /// monotonicity against such an authenticator would reject every legitimate re-authentication.
    /// </summary>
    public static bool TryVerifyAssertion(
        PasskeyAssertion assertion,
        int algorithm,
        string publicKeySpkiBase64,
        byte[] expectedChallenge,
        string expectedRpId,
        out string? failure,
        string? expectedOrigin = null)
    {
        failure = null;
        if (!TryVerifyClientData(
                assertion.ClientData,
                PasskeyClientData.AssertionType,
                expectedChallenge,
                expectedOrigin ?? ResolveOrigin(expectedRpId),
                out failure))
        {
            return false;
        }

        if (!PasskeyAuthenticatorDataCodec.TryParse(assertion.AuthenticatorData, out var authData) ||
            authData is null)
        {
            failure = "auth-data-malformed";
            return false;
        }

        if (authData.Flags.HasFlag(PasskeyAuthenticatorFlags.AttestedCredentialData))
        {
            failure = "unexpected-attested-credential-data";
            return false;
        }

        if (!TryVerifyAuthDataHeader(authData, expectedRpId, out failure))
        {
            return false;
        }

        var clientDataHash = SHA256.HashData(assertion.ClientData.Json);
        var signedPayload = new byte[assertion.AuthenticatorData.Length + clientDataHash.Length];
        assertion.AuthenticatorData.CopyTo(signedPayload, 0);
        clientDataHash.CopyTo(signedPayload, assertion.AuthenticatorData.Length);

        if (!PasskeyKeyMaterialGenerator.Verify(
                algorithm,
                publicKeySpkiBase64,
                signedPayload,
                assertion.Signature))
        {
            failure = "signature";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Decodes a {"fmt":"none","attStmt":{},"authData":&lt;bytes&gt;} map. Anything that is not literally
    /// "none" attestation is rejected: this build has no trust anchor to evaluate packed or fido-u2f
    /// statements against, and accepting them unread would pretend otherwise.
    /// </summary>
    public static bool TryReadNoneAttestationObject(byte[] attestationObject, out byte[]? authenticatorData)
    {
        authenticatorData = null;
        try
        {
            var reader = new CborReader(attestationObject);
            var entries = reader.ReadStartMap();
            string? format = null;
            var attestationStatementEntries = -1;
            byte[]? authData = null;

            for (var index = 0; index < entries; index++)
            {
                var label = reader.ReadValue().Text;
                switch (label)
                {
                    case "fmt":
                        format = reader.ReadValue().Text;
                        break;
                    case "attStmt":
                        attestationStatementEntries = reader.ReadStartMap();
                        for (var statement = 0; statement < attestationStatementEntries; statement++)
                        {
                            reader.Skip();
                            reader.Skip();
                        }

                        break;
                    case "authData":
                        authData = reader.ReadValue().Bytes;
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            if (reader.Remaining != 0 ||
                format != "none" ||
                attestationStatementEntries != 0 ||
                authData is not { Length: > 0 })
            {
                return false;
            }

            authenticatorData = authData;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TryVerifyClientData(
        PasskeyClientData clientData,
        string expectedType,
        byte[] expectedChallenge,
        string expectedOrigin,
        out string? failure)
    {
        if (clientData.Type != expectedType)
        {
            failure = "client-data-type";
            return false;
        }

        if (!PasskeyBase64Url.TryDecode(clientData.Challenge, out var challenge) ||
            challenge is null ||
            !challenge.AsSpan().SequenceEqual(expectedChallenge))
        {
            failure = "challenge";
            return false;
        }

        if (!string.Equals(clientData.Origin, expectedOrigin, StringComparison.Ordinal))
        {
            failure = "origin";
            return false;
        }

        failure = null;
        return true;
    }

    private static bool TryVerifyAuthDataHeader(
        PasskeyAuthenticatorData authData,
        string expectedRpId,
        out string? failure)
    {
        if (!authData.UserPresent)
        {
            failure = "user-not-present";
            return false;
        }

        if (!authData.UserVerified)
        {
            failure = "user-not-verified";
            return false;
        }

        if (!authData.RpIdHash.AsSpan().SequenceEqual(PasskeyRpId.Hash(expectedRpId)))
        {
            failure = "rp-id-hash";
            return false;
        }

        failure = null;
        return true;
    }

    private static int KeyTypeFor(int algorithm) =>
        algorithm == PasskeyAlgorithm.Es256 ? PasskeyAlgorithm.Ec2KeyType : PasskeyAlgorithm.RsaKeyType;

    private static string ResolveOrigin(string rpId) => $"https://{rpId}";
}
