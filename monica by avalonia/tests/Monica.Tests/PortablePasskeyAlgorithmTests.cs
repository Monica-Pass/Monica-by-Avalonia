using System.Text;
using Monica.App.Services;
using Monica.Core.Models;
using Monica.Core.Passkeys;

namespace Monica.Tests;

public sealed class PortablePasskeyAlgorithmTests
{
    [Theory]
    [InlineData(PasskeyAlgorithm.Es256)]
    [InlineData(PasskeyAlgorithm.Rs256)]
    [InlineData(PasskeyAlgorithm.Ps256)]
    public void Android_registration_spki_can_be_received_and_used(int algorithm)
    {
        var material = PasskeyKeyMaterialGenerator.Generate(
            algorithm == PasskeyAlgorithm.Ps256 ? PasskeyAlgorithm.Rs256 : algorithm);
        var entry = new PasskeyEntry
        {
            CredentialId = "synthetic-credential",
            RpId = "example.test",
            RpName = "Android registration",
            UserId = "Zml4dHVyZQ==",
            PublicKeyAlgorithm = algorithm,
            PublicKey = material.PublicKeySpkiBase64
        };
        var data = DevicePasskeyShape.Export(entry, material.PrivateKeyPkcs8Base64);
        // Android registration keeps SPKI in its store and in the sync payload.
        data["payload"]!["public_key"] = material.PublicKeySpkiBase64;
        var imported = DevicePasskeyShape.Import(data, 1);
        Assert.Equal(material.PublicKeySpkiBase64, imported.PublicKey);
        var challenge = Encoding.UTF8.GetBytes("synthetic Android SPKI sync challenge");
        Assert.True(PasskeyKeyMaterialGenerator.Verify(algorithm, imported.PublicKey, challenge,
            PasskeyKeyMaterialGenerator.Sign(algorithm, imported.PrivateKeyAlias, challenge)));
    }

    [Fact]
    public void Imported_rsa_pss_key_can_sign_after_synchronization()
    {
        var material = PasskeyKeyMaterialGenerator.Generate(PasskeyAlgorithm.Rs256);
        var challenge = Encoding.UTF8.GetBytes("synthetic synchronized passkey challenge");
        var signature = PasskeyKeyMaterialGenerator.Sign(PasskeyAlgorithm.Ps256, material.PrivateKeyPkcs8Base64, challenge);
        Assert.True(PasskeyKeyMaterialGenerator.Verify(PasskeyAlgorithm.Ps256, material.PublicKeySpkiBase64, challenge, signature));
        Assert.False(PasskeyKeyMaterialGenerator.Verify(PasskeyAlgorithm.Rs256, material.PublicKeySpkiBase64, challenge, signature));
    }

    [Fact]
    public void Imported_ed25519_key_matches_rfc8032_and_roundtrips_android_cose()
    {
        // RFC8032 public test vector, not user key material.
        var privateKey = Convert.ToBase64String(Convert.FromHexString(
            "302e020100300506032b6570042204209d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60"));
        var publicKey = Convert.ToBase64String(Convert.FromHexString(
            "302a300506032b6570032100d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a"));
        var signature = PasskeyKeyMaterialGenerator.Sign(PasskeyAlgorithm.EdDsa, privateKey, []);
        Assert.Equal(Convert.FromHexString("e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e06522490155" +
            "5fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b"), signature);
        Assert.True(PasskeyKeyMaterialGenerator.Verify(PasskeyAlgorithm.EdDsa, publicKey, [], signature));
        var cose = PasskeyKeyMaterialGenerator.BuildCosePublicKey(PasskeyAlgorithm.EdDsa, publicKey);
        Assert.True(PasskeyKeyMaterialGenerator.TryParseCosePublicKey(cose, out var decoded, out var consumed));
        Assert.Equal(cose.Length, consumed);
        Assert.Equal(publicKey, decoded!.ToSubjectPublicKeyInfoBase64());
        signature[0] ^= 1;
        Assert.False(PasskeyKeyMaterialGenerator.Verify(PasskeyAlgorithm.EdDsa, publicKey, [], signature));
    }
}
