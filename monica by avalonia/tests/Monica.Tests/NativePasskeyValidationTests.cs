using System.Security.Cryptography;
using Monica.Core.Passkeys;

namespace Monica.Tests;

public sealed class NativePasskeyValidationTests
{
    [Theory]
    [InlineData("https://evil.test")]
    [InlineData("https://example.com.evil.test")]
    [InlineData("http://example.com")]
    [InlineData("https://user@example.com")]
    [InlineData("https://example.com/login")]
    public void Origin_outside_the_requested_relying_party_is_refused(string origin) =>
        Assert.Throws<ArgumentException>(() => NativePasskeyValidation.ValidateRpAndOrigin("example.com", origin, new byte[32]));

    [Fact]
    public void Legitimate_secure_subdomain_origin_is_accepted() =>
        Assert.Equal("example.com", NativePasskeyValidation.ValidateRpAndOrigin("example.com", "https://login.example.com", new byte[32]));

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(1025)]
    public void Invalid_challenge_size_is_refused(int length) =>
        Assert.Throws<ArgumentException>(() => NativePasskeyValidation.ValidateRpAndOrigin("example.com", null, new byte[length]));

    [Fact]
    public void Client_data_origin_and_challenge_are_checked_without_reserializing()
    {
        var challenge = RandomNumberGenerator.GetBytes(32);
        var data = PasskeyClientData.Build(PasskeyClientData.CreateType, challenge, "https://example.com");
        Assert.True(NativePasskeyValidation.VerifyClientData(data.Json, PasskeyClientData.CreateType, challenge, "https://example.com"));
        Assert.False(NativePasskeyValidation.VerifyClientData(data.Json, PasskeyClientData.AssertionType, challenge, "https://example.com"));
        Assert.False(NativePasskeyValidation.VerifyClientData(data.Json, PasskeyClientData.CreateType, challenge, "https://evil.test"));
        Assert.False(NativePasskeyValidation.VerifyClientData(data.Json, PasskeyClientData.CreateType, new byte[32], "https://example.com"));
    }

    [Fact]
    public void Native_es256_signatures_use_der_not_the_legacy_internal_format()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        var payload = RandomNumberGenerator.GetBytes(80);
        var der = key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        var internalSignature = key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        Assert.True(NativePasskeyValidation.VerifySignature(PasskeyAlgorithm.Es256, publicKey, payload, der));
        Assert.False(NativePasskeyValidation.VerifySignature(PasskeyAlgorithm.Es256, publicKey, payload, internalSignature));
        payload[0] ^= 1;
        Assert.False(NativePasskeyValidation.VerifySignature(PasskeyAlgorithm.Es256, publicKey, payload, der));
    }
}
