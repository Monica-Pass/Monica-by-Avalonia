using System.Security.Cryptography;
using Monica.Core.Bitwarden;

namespace Monica.Tests;

public sealed class BitwardenProtocolTests
{
    [Fact]
    public void Pbkdf2AndStretchedKeyMatchProtocolVector()
    {
        var masterKey = BitwardenKeyDerivation.DeriveMasterKey(
            "correct horse battery staple",
            " Alice@Example.com ",
            BitwardenKdfParameters.Pbkdf2(100_000));

        try
        {
            Assert.Equal(
                "cb235fd1233ecd275d1dc2744ea654f7f88a3ec0979b1bca0c4c580112f64e63",
                Convert.ToHexString(masterKey).ToLowerInvariant());
            Assert.Equal(
                "ij4bpg+9sHwyc9ipLMipC5BiUug2hc9KWk8nXWxhz2o=",
                BitwardenKeyDerivation.DeriveMasterPasswordHash(masterKey, "correct horse battery staple"));

            using var stretched = BitwardenKeyDerivation.StretchMasterKey(masterKey);
            Assert.False(stretched.IsDisposed);
            Assert.Equal(
                "stretched-key-vector",
                BitwardenCipherStringCrypto.DecryptToString(
                    "2.EBESExQVFhcYGRobHB0eHw==|sRul4ScdecehgOYDcb3yiyxsYr7zKjdh9kfqRqh/SzA=|sSV1uAYBeyIGSl+1DEvRIX0H0enj914mV1SXzEw2Q48=",
                    stretched));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    [Fact]
    public void Argon2idMatchesProtocolVector()
    {
        var masterKey = BitwardenKeyDerivation.DeriveMasterKey(
            "correct horse battery staple",
            "alice@example.com",
            BitwardenKdfParameters.Argon2id(iterations: 2, memoryMb: 32, parallelism: 2));

        try
        {
            Assert.Equal(
                "de42814dd2661793c529bd83e324eac04f9ede84dcc8fdce4e20adba8ce4db2f",
                Convert.ToHexString(masterKey).ToLowerInvariant());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    [Fact]
    public void Type2CipherVectorDecryptsAndTamperingFails()
    {
        var encryptionKey = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var macKey = Enumerable.Range(32, 32).Select(value => (byte)value).ToArray();
        using var key = new BitwardenSymmetricKey(encryptionKey, macKey);
        const string vector =
            "2.AAECAwQFBgcICQoLDA0ODw==|Fw8EMAN5EuIX1cfz34TalorjqnVFSLE3nvlOLZo7epg=|jWa9x3yzlwfjYG4pum86xUAo68GBNrR4QANwvDrS2ZI=";

        var plaintext = BitwardenCipherStringCrypto.DecryptToString(vector, key);
        Assert.Equal("Monica Bitwarden vector", plaintext);

        var tampered = vector[..^2] + (vector[^2] == 'A' ? 'B' : 'A') + vector[^1];
        Assert.Throws<CryptographicException>(() => BitwardenCipherStringCrypto.Decrypt(tampered, key));
    }

    [Fact]
    public void Type2CipherRoundTripAndKeyLifecycleAreAuthenticated()
    {
        using var key = new BitwardenSymmetricKey(new byte[32], Enumerable.Repeat((byte)7, 32).ToArray());
        var cipher = BitwardenCipherStringCrypto.EncryptString("桌面同步", key);

        Assert.Equal("桌面同步", BitwardenCipherStringCrypto.DecryptToString(cipher, key));
        key.Dispose();
        Assert.True(key.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => BitwardenCipherStringCrypto.Decrypt(cipher, key));
    }

    [Fact]
    public void KdfAndCipherLengthsAreBoundedBeforeWork()
    {
        Assert.Throws<BitwardenProtocolException>(() =>
            BitwardenKeyDerivation.DeriveMasterKey("password", "a@b.test", BitwardenKdfParameters.Pbkdf2(2_000_001)));
        Assert.Throws<BitwardenProtocolException>(() =>
            BitwardenKeyDerivation.DeriveMasterKey("password", "a@b.test", BitwardenKdfParameters.Argon2id(memoryMb: 257)));

        using var key = new BitwardenSymmetricKey(new byte[32], new byte[32]);
        Assert.Throws<BitwardenProtocolException>(() =>
            BitwardenCipherStringCrypto.Decrypt(new string('A', BitwardenCipherStringCrypto.MaximumCipherStringLength + 1), key));
    }

    [Theory]
    [InlineData("0.AAECAwQFBgcICQoLDA0ODw==|AA==")]
    [InlineData("2.AA==|AA==|AA==")]
    [InlineData("2.AAECAwQFBgcICQoLDA0ODw==|AA==|AA==|extra")]
    [InlineData("2.invalid!|AA==|AA==")]
    public void MalformedCipherStringsAreRejectedBeforeDecryption(string cipherString)
    {
        using var key = new BitwardenSymmetricKey(new byte[32], new byte[32]);
        Assert.Throws<BitwardenProtocolException>(() =>
            BitwardenCipherStringCrypto.Decrypt(cipherString, key));
    }

    [Fact]
    public void EndpointsRequireHttpsAndRejectAmbientAuthorityData()
    {
        var endpoints = BitwardenEndpointPolicy.CreateSelfHosted(
            "https://vault.example.test/monica",
            "https://vault.example.test/monica/identity",
            "https://vault.example.test/monica/api");

        Assert.Equal("https://vault.example.test/monica/", endpoints.WebVault.AbsoluteUri);
        Assert.Equal("https://vault.example.test/monica/identity/", endpoints.Identity.AbsoluteUri);
        Assert.Throws<BitwardenProtocolException>(() =>
            BitwardenEndpointPolicy.ValidateBaseAddress("http://vault.example.test", "server"));
        Assert.Throws<BitwardenProtocolException>(() =>
            BitwardenEndpointPolicy.ValidateBaseAddress("https://user:password@vault.example.test", "server"));
        Assert.Throws<BitwardenProtocolException>(() =>
            BitwardenEndpointPolicy.ValidateBaseAddress("https://vault.example.test/api?token=secret", "server"));
        Assert.Throws<BitwardenProtocolException>(() =>
            BitwardenEndpointPolicy.ValidateBaseAddress("https://vault.example.test/%2e%2e/admin", "server"));
    }
}
