using System.Security.Cryptography;
using System.Text;
using Monica.Core.Passkeys;

namespace Monica.Tests;

/// <summary>
/// Protocol evidence for Monica's software authenticator, using independent .NET signature checks,
/// published W3C vectors and relying-party validation without a platform passkey provider.
/// </summary>
public sealed class PasskeyEngineTests
{
    private const string RpId = "example.com";
    private const string Origin = "https://example.com";

    [Theory]
    [InlineData(PasskeyAlgorithm.Es256)]
    [InlineData(PasskeyAlgorithm.Rs256)]
    public void RegistrationAndAssertionRoundTrip(int algorithm)
    {
        var challenge = RandomNumberGenerator.GetBytes(32);
        var registration = PasskeyAuthenticator.Register(RpId, challenge, algorithm, Origin);

        Assert.True(PasskeyVerifier.TryVerifyRegistration(
            registration,
            challenge,
            RpId,
            out var registrationFailure,
            Origin));
        Assert.Null(registrationFailure);

        var entry = new Monica.Core.Models.PasskeyEntry
        {
            CredentialId = registration.CredentialIdBase64Url,
            RpId = RpId,
            UserId = "dXNlcg",
            PublicKeyAlgorithm = algorithm,
            PublicKey = registration.KeyMaterial.PublicKeySpkiBase64
        };
        var secondChallenge = RandomNumberGenerator.GetBytes(32);
        var assertion = PasskeyAuthenticator.Assert(
            entry,
            registration.KeyMaterial.PrivateKeyPkcs8Base64,
            secondChallenge,
            rpId: null,
            Origin);

        Assert.True(PasskeyVerifier.TryVerifyAssertion(
            assertion,
            algorithm,
            entry.PublicKey,
            secondChallenge,
            RpId,
            out var assertionFailure,
            Origin));
        Assert.Null(assertionFailure);
        Assert.Equal(registration.CredentialIdBase64Url, assertion.CredentialIdWebAuthn);
    }

    [Fact]
    public void Es256AssertionsVerifyWithAnIndependentWebAuthnDerVerifier()
    {
        var (registration, assertion, _) = CreateAssertion(PasskeyAlgorithm.Es256);
        // Compute the relying-party payload from the response bytes, then verify with .NET
        // directly. Monica's signer and verifier must not validate a shared format mistake.
        var signedPayload = assertion.AuthenticatorData.Concat(SHA256.HashData(assertion.ClientData.Json)).ToArray();
        using var relyingPartyKey = ECDsa.Create();
        relyingPartyKey.ImportSubjectPublicKeyInfo(Convert.FromBase64String(registration.KeyMaterial.PublicKeySpkiBase64), out _);

        Assert.True(relyingPartyKey.VerifyData(signedPayload, assertion.Signature,
            HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        Assert.False(relyingPartyKey.VerifyData(signedPayload, assertion.Signature,
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        signedPayload[0] ^= 1;
        Assert.False(relyingPartyKey.VerifyData(signedPayload, assertion.Signature,
            HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
    }

    [Fact]
    public void AValidRawEs256SignatureIsRejectedByTheWebAuthnVerifier()
    {
        var (registration, assertion, _) = CreateAssertion(PasskeyAlgorithm.Es256);
        var signedPayload = assertion.AuthenticatorData.Concat(SHA256.HashData(assertion.ClientData.Json)).ToArray();
        using var independentSigner = ECDsa.Create();
        independentSigner.ImportPkcs8PrivateKey(Convert.FromBase64String(registration.KeyMaterial.PrivateKeyPkcs8Base64), out _);
        var rawSignature = independentSigner.SignData(signedPayload, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        Assert.True(independentSigner.VerifyData(signedPayload, rawSignature,
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        Assert.False(PasskeyVerifier.TryVerifyAssertion(assertion with { Signature = rawSignature },
            PasskeyAlgorithm.Es256, registration.KeyMaterial.PublicKeySpkiBase64, ChallengeOf(assertion),
            RpId, out var failure, Origin));
        Assert.Equal("signature", failure);
    }

    [Fact]
    public void IndependentDerSignaturesAreAcceptedButTamperedOrMalformedResponsesAreRefused()
    {
        using var independentSigner = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = Convert.ToBase64String(independentSigner.ExportSubjectPublicKeyInfo());
        var payload = Encoding.UTF8.GetBytes("independent WebAuthn assertion payload");
        var signature = independentSigner.SignData(payload, HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);

        Assert.True(PasskeyKeyMaterialGenerator.Verify(PasskeyAlgorithm.Es256, publicKey, payload, signature));
        Assert.False(PasskeyKeyMaterialGenerator.Verify(PasskeyAlgorithm.Es256, publicKey, payload, signature[..^1]));
        Assert.False(PasskeyKeyMaterialGenerator.Verify(PasskeyAlgorithm.Es256, publicKey, payload, [.. signature, 0]));
        var tamperedSignature = signature.ToArray();
        tamperedSignature[^1] ^= 1;
        Assert.False(PasskeyKeyMaterialGenerator.Verify(PasskeyAlgorithm.Es256, publicKey, payload, tamperedSignature));
        payload[0] ^= 1;
        Assert.False(PasskeyKeyMaterialGenerator.Verify(PasskeyAlgorithm.Es256, publicKey, payload, signature));
    }

    [Fact]
    public void W3cPublishedEs256AssertionVectorVerifiesWithoutReserializingClientData()
    {
        // Frozen W3C WebAuthn Level 3 §16.2, "ES256 Credential with No Attestation".
        // Public coordinates are taken from the registration's COSE key; the assertion uses
        // the exact published clientDataJSON/authenticatorData/signature, not Monica output.
        // https://www.w3.org/TR/2026/REC-webauthn-3-20260825/ (section 16.2)
        using var publicKey = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = Convert.FromHexString("afefa16f97ca9b2d23eb86ccb64098d20db90856062eb249c33a9b672f26df61"),
                Y = Convert.FromHexString("930a56b87a2fca66334b03458abf879717c12cc68ed73290af2e2664796b9220")
            }
        });
        var authenticatorData = Convert.FromHexString("bfabc37432958b063360d3ad6461c9c4735ae7f8edd46592a5e0f01452b2e4b51900000000");
        var clientDataJson = Convert.FromHexString("7b2274797065223a22776562617574686e2e676574222c226368616c6c656e6765223a224f63446e55685158756c5455506f334a5558543049393770767a7a59425039745a63685879617630314167222c226f726967696e223a2268747470733a2f2f6578616d706c652e6f7267222c2263726f73734f726967696e223a66616c73657d");
        var signature = Convert.FromHexString("3046022100f50a4e2e4409249c4a853ba361282f09841df4dd4547a13a87780218deffcd380221008480ac0f0b93538174f575bf11a1dd5d78c6e486013f937295ea13653e331e87");
        var signedPayload = authenticatorData.Concat(SHA256.HashData(clientDataJson)).ToArray();

        Assert.True(publicKey.VerifyData(signedPayload, signature, HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence));
        var spki = Convert.ToBase64String(publicKey.ExportSubjectPublicKeyInfo());
        Assert.True(PasskeyKeyMaterialGenerator.Verify(PasskeyAlgorithm.Es256, spki, signedPayload, signature));
        signedPayload[0] ^= 1;
        Assert.False(PasskeyKeyMaterialGenerator.Verify(PasskeyAlgorithm.Es256, spki, signedPayload, signature));
    }

    [Theory]
    [InlineData(PasskeyAlgorithm.Es256, null)]
    [InlineData(PasskeyAlgorithm.Es256, "not base64")]
    [InlineData(PasskeyAlgorithm.Es256, "AQID")]
    [InlineData(PasskeyAlgorithm.Rs256, null)]
    [InlineData(PasskeyAlgorithm.Rs256, "not base64")]
    [InlineData(PasskeyAlgorithm.Rs256, "AQID")]
    [InlineData(PasskeyAlgorithm.Ps256, null)]
    [InlineData(PasskeyAlgorithm.Ps256, "not base64")]
    [InlineData(PasskeyAlgorithm.Ps256, "AQID")]
    public void InvalidPublicKeysAreRefusedWithoutThrowingFromTheBoolVerifier(int algorithm, string? publicKey)
    {
        Assert.False(PasskeyKeyMaterialGenerator.Verify(algorithm, publicKey!, [1, 2, 3], [1, 2, 3]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("3046022100")]
    [InlineData("3006020101020101")]
    [InlineData("3006020180020101")]
    [InlineData("300702020001020101")]
    [InlineData("308106020101020101")]
    public void MalformedDerOrInvalidEcdsaValuesAreRefusedWithoutThrowing(string signatureHex)
    {
        using var publicKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.False(PasskeyKeyMaterialGenerator.Verify(PasskeyAlgorithm.Es256,
            Convert.ToBase64String(publicKey.ExportSubjectPublicKeyInfo()),
            [1, 2, 3], Convert.FromHexString(signatureHex)));
    }

    [Fact]
    public void TrailingBytesInAnImportedPublicKeyAreRefused()
    {
        using var independentSigner = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] payload = [1, 2, 3];
        var signature = independentSigner.SignData(payload, HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        var spki = independentSigner.ExportSubjectPublicKeyInfo();

        Assert.True(PasskeyKeyMaterialGenerator.Verify(PasskeyAlgorithm.Es256,
            Convert.ToBase64String(spki), payload, signature));
        byte[] invalidSpki = [.. spki, 0];
        Assert.False(PasskeyKeyMaterialGenerator.Verify(PasskeyAlgorithm.Es256,
            Convert.ToBase64String(invalidSpki), payload, signature));
    }

    [Theory]
    [InlineData(PasskeyAlgorithm.Rs256)]
    [InlineData(PasskeyAlgorithm.Ps256)]
    public void RsaAlgorithmsStillAcceptIndependentPkcs1AndPssSignatures(int algorithm)
    {
        using var independentSigner = RSA.Create(2048);
        var publicKey = Convert.ToBase64String(independentSigner.ExportSubjectPublicKeyInfo());
        byte[] payload = [1, 2, 3];
        var padding = algorithm == PasskeyAlgorithm.Rs256 ? RSASignaturePadding.Pkcs1 : RSASignaturePadding.Pss;
        var signature = independentSigner.SignData(payload, HashAlgorithmName.SHA256, padding);

        Assert.True(PasskeyKeyMaterialGenerator.Verify(algorithm, publicKey, payload, signature));
        if (algorithm == PasskeyAlgorithm.Rs256)
        {
            var monicaSignature = PasskeyKeyMaterialGenerator.Sign(algorithm,
                Convert.ToBase64String(independentSigner.ExportPkcs8PrivateKey()), payload);
            Assert.True(independentSigner.VerifyData(payload, monicaSignature, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1));
        }

        payload[0] ^= 1;
        Assert.False(PasskeyKeyMaterialGenerator.Verify(algorithm, publicKey, payload, signature));
    }

    [Fact]
    public void RsaKeysAreTwoThousandFortyEightBitAsAndroidIssuesThem()
    {
        var registration = PasskeyAuthenticator.Register(RpId, RandomNumberGenerator.GetBytes(16), PasskeyAlgorithm.Rs256);

        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(registration.KeyMaterial.PublicKeySpkiBase64), out _);
        Assert.Equal(2048, rsa.KeySize);
        Assert.Equal(256, rsa.ExportParameters(false).Modulus!.Length);
    }

    [Theory]
    [InlineData(PasskeyAlgorithm.Ps256)]
    [InlineData(PasskeyAlgorithm.EdDsa)]
    public void GeneratedKeysStayInsideTheTwoAlgorithmsAndroidGenerates(int algorithm)
    {
        Assert.False(PasskeyAlgorithm.CanGenerate(algorithm));
        Assert.Throws<NotSupportedException>(() =>
            PasskeyAuthenticator.Register(RpId, RandomNumberGenerator.GetBytes(16), algorithm));
    }

    [Fact]
    public void AssertionIsRejectedWhenTheChallengeChanges()
    {
        var (registration, assertion, privateKey) = CreateAssertion(PasskeyAlgorithm.Es256);
        Assert.NotNull(privateKey);

        var verified = PasskeyVerifier.TryVerifyAssertion(
            assertion,
            PasskeyAlgorithm.Es256,
            registration.KeyMaterial.PublicKeySpkiBase64,
            RandomNumberGenerator.GetBytes(32),
            RpId,
            out var failure,
            Origin);

        Assert.False(verified);
        Assert.Equal("challenge", failure);
    }

    [Fact]
    public void AssertionIsRejectedWhenTheRelyingPartyDiffers()
    {
        var (registration, assertion, _) = CreateAssertion(PasskeyAlgorithm.Es256);

        // The origin is left matching so the only thing that can fail is the hash inside authData.
        var verified = PasskeyVerifier.TryVerifyAssertion(
            assertion,
            PasskeyAlgorithm.Es256,
            registration.KeyMaterial.PublicKeySpkiBase64,
            ChallengeOf(assertion),
            "evil.example.com",
            out var failure,
            Origin);

        Assert.False(verified);
        Assert.Equal("rp-id-hash", failure);
    }

    [Fact]
    public void AssertionIsRejectedWhenTheOriginDiffers()
    {
        var (registration, assertion, _) = CreateAssertion(PasskeyAlgorithm.Es256);

        var verified = PasskeyVerifier.TryVerifyAssertion(
            assertion,
            PasskeyAlgorithm.Es256,
            registration.KeyMaterial.PublicKeySpkiBase64,
            ChallengeOf(assertion),
            RpId,
            out var failure,
            "https://attacker.example");

        Assert.False(verified);
        Assert.Equal("origin", failure);
    }

    [Fact]
    public void FlippingOneSignedByteInvalidatesTheAssertion()
    {
        var (registration, assertion, _) = CreateAssertion(PasskeyAlgorithm.Es256);
        var challenge = ChallengeOf(assertion);
        var tampered = (byte[])assertion.AuthenticatorData.Clone();
        tampered[32] ^= 0x20;

        var verified = PasskeyVerifier.TryVerifyAssertion(
            new PasskeyAssertion(tampered, assertion.ClientData, assertion.Signature, "id", "handle"),
            PasskeyAlgorithm.Es256,
            registration.KeyMaterial.PublicKeySpkiBase64,
            challenge,
            RpId,
            out var failure,
            Origin);

        Assert.False(verified);
        Assert.Equal("signature", failure);
    }

    [Fact]
    public void AZeroSignCounterIsAcceptedBecauseTheAuthenticatorNeverCounts()
    {
        var (registration, assertion, _) = CreateAssertion(PasskeyAlgorithm.Es256);

        Assert.True(PasskeyVerifier.TryVerifyAssertion(
            assertion,
            PasskeyAlgorithm.Es256,
            registration.KeyMaterial.PublicKeySpkiBase64,
            ChallengeOf(assertion),
            RpId,
            out _,
            Origin));
        Assert.True(PasskeyAuthenticatorDataCodec.TryParse(assertion.AuthenticatorData, out var parsed));
        Assert.Equal(0, parsed!.SignCount);
    }

    [Fact]
    public void AuthDataFlagsMatchTheAndroidLayout()
    {
        var registration = PasskeyAuthenticator.Register(
            RpId,
            RandomNumberGenerator.GetBytes(32),
            PasskeyAlgorithm.Es256);

        Assert.Equal(0x5D, registration.AuthenticatorData[32]);
        Assert.Equal(0x1D, assertionFlagByte());

        static byte assertionFlagByte()
        {
            var entry = new Monica.Core.Models.PasskeyEntry { RpId = RpId, PublicKeyAlgorithm = PasskeyAlgorithm.Es256 };
            var keys = PasskeyKeyMaterialGenerator.Generate(PasskeyAlgorithm.Es256);
            return PasskeyAuthenticator.Assert(
                    entry,
                    keys.PrivateKeyPkcs8Base64,
                    RandomNumberGenerator.GetBytes(16))
                .AuthenticatorData[32];
        }
    }

    [Fact]
    public void AaguidIsTheStableMonicaIdentifier()
    {
        Assert.Equal(
            "6d6f6e6963614d33a001706173736b79",
            Convert.ToHexString(PasskeyAuthenticator.MonicaAaguid).ToLowerInvariant());
        Assert.Equal("6d6f6e69-6361-4d33-a001-706173736b79", PasskeyAuthenticator.MonicaAaguidText);
    }

    [Fact]
    public void AttestationIsNoneAndNothingElseIsAccepted()
    {
        var registration = PasskeyAuthenticator.Register(
            RpId,
            RandomNumberGenerator.GetBytes(32),
            PasskeyAlgorithm.Es256);

        var decoded = PasskeyVerifier.TryReadNoneAttestationObject(
            registration.AttestationObject,
            out var authData);

        Assert.True(decoded);
        Assert.Equal(registration.AuthenticatorData, authData);
        Assert.False(PasskeyVerifier.TryReadNoneAttestationObject(
            [.. registration.AttestationObject, 0xA0],
            out _));
    }

    [Fact]
    public void ARegistrationWithTheWrongPublicKeyInAuthDataIsRefused()
    {
        var challenge = RandomNumberGenerator.GetBytes(32);
        var registration = PasskeyAuthenticator.Register(RpId, challenge, PasskeyAlgorithm.Es256);
        var other = PasskeyAuthenticator.Register(RpId, challenge, PasskeyAlgorithm.Es256);

        // Keep the honest credential id and swap only the key, so this reaches the public-key check
        // instead of tripping the earlier credential-id one.
        var forgedAuthData = PasskeyAuthenticatorDataCodec.BuildCreation(
            RpId,
            PasskeyAuthenticator.MonicaAaguid,
            registration.CredentialId,
            other.KeyMaterial.CosePublicKey,
            signCount: 0);
        var swapped = registration with
        {
            AuthenticatorData = forgedAuthData,
            AttestationObject = PasskeyAuthenticator.BuildNoneAttestationObject(forgedAuthData)
        };

        Assert.False(PasskeyVerifier.TryVerifyRegistration(
            swapped,
            challenge,
            RpId,
            out var failure,
            Origin));
        Assert.Equal("public-key-mismatch", failure);

        // A wholesale foreign authData is caught one check earlier, by the credential id it attests.
        Assert.False(PasskeyVerifier.TryVerifyRegistration(
            registration with
            {
                AuthenticatorData = other.AuthenticatorData,
                AttestationObject = PasskeyAuthenticator.BuildNoneAttestationObject(other.AuthenticatorData)
            },
            challenge,
            RpId,
            out var foreignFailure,
            Origin));
        Assert.Equal("credential-id-mismatch", foreignFailure);
    }

    [Fact]
    public void CosePublicKeyRoundTripsThroughBothKeyTypesAndAnyLabelOrder()
    {
        foreach (var algorithm in new[] { PasskeyAlgorithm.Es256, PasskeyAlgorithm.Rs256 })
        {
            var material = PasskeyKeyMaterialGenerator.Generate(algorithm);
            Assert.True(PasskeyKeyMaterialGenerator.TryParseCosePublicKey(
                material.CosePublicKey,
                out var parsed,
                out var consumed));
            Assert.Equal(material.CosePublicKey.Length, consumed);
            Assert.Equal(material.PublicKeySpkiBase64, parsed!.ToSubjectPublicKeyInfoBase64());
        }

        var ec2 = PasskeyKeyMaterialGenerator.Generate(PasskeyAlgorithm.Es256);
        var reordered = ReorderEc2Labels(ec2.CosePublicKey);
        Assert.True(PasskeyKeyMaterialGenerator.TryParseCosePublicKey(
            reordered,
            out var parsedAgain,
            out _));
        Assert.Equal(32, parsedAgain!.XCoordinate!.Length);
        Assert.Equal(32, parsedAgain.YCoordinate!.Length);
        Assert.Equal(ec2.PublicKeySpkiBase64, parsedAgain.ToSubjectPublicKeyInfoBase64());
    }

    [Fact]
    public void TruncatedOrOversizedAuthDataIsRefused()
    {
        var registration = PasskeyAuthenticator.Register(
            RpId,
            RandomNumberGenerator.GetBytes(32),
            PasskeyAlgorithm.Es256);

        Assert.False(PasskeyAuthenticatorDataCodec.TryParse(
            registration.AuthenticatorData.AsSpan(0, 30).ToArray(),
            out _));
        Assert.False(PasskeyAuthenticatorDataCodec.TryParse(
            [.. registration.AuthenticatorData, 0x00],
            out _));
    }

    [Theory]
    [InlineData("Example.COM.", "example.com")]
    [InlineData("  example.com  ", "example.com")]
    [InlineData("xn--80ak6aa92e.com", "xn--80ak6aa92e.com")]
    [InlineData("例子.测试", "xn--fsqu00a.xn--0zwm56d")]
    [InlineData("例子.测试.", "xn--fsqu00a.xn--0zwm56d")]
    [InlineData("EXAMPLE.测试.COM", "example.xn--0zwm56d.com")]
    [InlineData("", null)]
    [InlineData("...", null)]
    public void RpIdNormalizationMatchesAndroid(string input, string? expected)
    {
        Assert.Equal(expected, PasskeyRpId.Normalize(input));
    }

    [Fact]
    public void RpIdEquivalenceIsExactAfterNormalizationOnly()
    {
        Assert.True(PasskeyRpId.IsEquivalent("Example.com.", "example.com"));
        Assert.False(PasskeyRpId.IsEquivalent("example.com", "sub.example.com"));
        Assert.False(PasskeyRpId.IsEquivalent("example.com", "example.co"));
    }

    [Fact]
    public void CredentialIdAcceptsUuidAndBase64urlSpellingsOfTheSameBytes()
    {
        var bytes = PasskeyCredentialId.NewRandom();
        var uuidText = PasskeyCredentialId.ToUuidText(bytes);
        var base64Url = PasskeyBase64Url.Encode(bytes);

        Assert.Equal(36, uuidText.Length);
        Assert.Equal(uuidText, PasskeyCredentialId.Normalize(uuidText));
        Assert.Equal(uuidText, PasskeyCredentialId.Normalize(base64Url));
        Assert.Equal(base64Url, PasskeyCredentialId.ToWebAuthnId(uuidText));
        Assert.Equal(base64Url, PasskeyCredentialId.ToWebAuthnId(base64Url));

        var nonUuid = PasskeyBase64Url.Encode(RandomNumberGenerator.GetBytes(22));
        Assert.Equal(nonUuid, PasskeyCredentialId.Normalize(nonUuid));
    }

    [Fact]
    public void Base64UrlDecodingToleratesPaddingAndBothAlphabets()
    {
        var payload = new byte[] { 0xfb, 0xff, 0xfe, 0x10 };
        var urlSafe = PasskeyBase64Url.Encode(payload);

        Assert.False(urlSafe.Contains('+') || urlSafe.Contains('/') || urlSafe.Contains('='));
        Assert.True(PasskeyBase64Url.TryDecode(urlSafe, out var decoded));
        Assert.Equal(payload, decoded);
        Assert.True(PasskeyBase64Url.TryDecode(Convert.ToBase64String(payload), out var padded));
        Assert.Equal(payload, padded);
        Assert.False(PasskeyBase64Url.TryDecode("not!base64", out _));
        Assert.False(PasskeyBase64Url.TryDecode(null, out _));
    }

    [Fact]
    public void ClientDataHashIsTakenOverTheExactSerializedBytes()
    {
        var challenge = RandomNumberGenerator.GetBytes(32);
        var clientData = PasskeyClientData.Build(PasskeyClientData.AssertionType, challenge, Origin);

        var json = Encoding.UTF8.GetString(clientData.Json);
        Assert.Contains("\"type\":\"webauthn.get\"", json, StringComparison.Ordinal);
        Assert.Contains($"\"challenge\":\"{PasskeyBase64Url.Encode(challenge)}\"", json, StringComparison.Ordinal);
        Assert.Contains("\"crossOrigin\":false", json, StringComparison.Ordinal);
        Assert.Equal(SHA256.HashData(clientData.Json), clientData.ClientDataHash);

        Assert.True(PasskeyClientData.TryParse(clientData.Json, out var parsed));
        Assert.Equal(Origin, parsed!.Origin);
        Assert.False(parsed.CrossOrigin);
        Assert.False(PasskeyClientData.TryParse(Encoding.UTF8.GetBytes("{\"type\":"), out _));
    }

    [Fact]
    public void PrivateKeyReferencesAreStableAndScopedToTheKeyTheyWrap()
    {
        const string credentialId = "cred";
        const string pkcs8 = "pkcs8-material";

        var first = PasskeyPrivateKeyRef.StorageKeyFor(credentialId, RpId, "user", pkcs8);
        var sameKey = PasskeyPrivateKeyRef.StorageKeyFor(credentialId, RpId, "user", pkcs8);
        var otherKey = PasskeyPrivateKeyRef.StorageKeyFor(credentialId, RpId, "user", "different");
        var reference = PasskeyPrivateKeyRef.ToReference(first);

        Assert.Equal(first, sameKey);
        Assert.NotEqual(first, otherKey);
        Assert.StartsWith("passkey_private_key_v1_", first, StringComparison.Ordinal);
        Assert.Equal(55, first.Length);
        Assert.Equal(81, reference.Length);
        Assert.True(PasskeyPrivateKeyRef.IsProtectedReference(reference));
        Assert.Equal(first, PasskeyPrivateKeyRef.StorageKeyFrom(reference));
        Assert.False(PasskeyPrivateKeyRef.IsProtectedReference(pkcs8));
        Assert.Null(PasskeyPrivateKeyRef.StorageKeyFrom(pkcs8));
    }

    private static (PasskeyRegistration Registration, PasskeyAssertion Assertion, string? PrivateKey) CreateAssertion(
        int algorithm)
    {
        var challenge = RandomNumberGenerator.GetBytes(32);
        var registration = PasskeyAuthenticator.Register(RpId, challenge, algorithm, Origin);
        var entry = new Monica.Core.Models.PasskeyEntry
        {
            CredentialId = registration.CredentialIdBase64Url,
            RpId = RpId,
            PublicKeyAlgorithm = algorithm,
            PublicKey = registration.KeyMaterial.PublicKeySpkiBase64
        };
        var assertion = PasskeyAuthenticator.Assert(
            entry,
            registration.KeyMaterial.PrivateKeyPkcs8Base64,
            RandomNumberGenerator.GetBytes(32),
            rpId: null,
            Origin);
        return (registration, assertion, registration.KeyMaterial.PrivateKeyPkcs8Base64);
    }

    private static byte[] ChallengeOf(PasskeyAssertion assertion)
    {
        Assert.True(PasskeyBase64Url.TryDecode(assertion.ClientData.Challenge, out var challenge));
        return challenge!;
    }

    /// <summary>
    /// Re-emits an EC2 map with the coordinate labels in the other order and the key type last, so a
    /// parser that typed a label before it knew the key type would read the curve as the x coordinate.
    /// </summary>
    private static byte[] ReorderEc2Labels(byte[] coseKey)
    {
        Assert.True(PasskeyKeyMaterialGenerator.TryParseCosePublicKey(coseKey, out var parsed, out _));
        return new CborBuilder()
            .WriteStartMap(5)
            .WriteInteger(PasskeyAlgorithm.LabelAlgorithm)
            .WriteInteger(PasskeyAlgorithm.Es256)
            .WriteInteger(PasskeyAlgorithm.LabelYCoordinate)
            .WriteByteString(parsed!.YCoordinate!)
            .WriteInteger(PasskeyAlgorithm.LabelCurve)
            .WriteInteger(PasskeyAlgorithm.P256Curve)
            .WriteInteger(PasskeyAlgorithm.LabelXCoordinate)
            .WriteByteString(parsed.XCoordinate!)
            .WriteInteger(PasskeyAlgorithm.LabelKeyType)
            .WriteInteger(PasskeyAlgorithm.Ec2KeyType)
            .Build();
    }
}
