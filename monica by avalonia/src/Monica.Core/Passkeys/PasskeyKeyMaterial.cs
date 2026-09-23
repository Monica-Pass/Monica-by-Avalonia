using System.Security.Cryptography;

namespace Monica.Core.Passkeys;

public sealed record PasskeyKeyMaterial(
    int Algorithm,
    string PublicKeySpkiBase64,
    byte[] CosePublicKey,
    string PrivateKeyPkcs8Base64);

public sealed record CosePublicKey(
    int KeyType,
    int Algorithm,
    byte[]? XCoordinate,
    byte[]? YCoordinate,
    byte[]? Modulus,
    byte[]? Exponent)
{
    public string ToSubjectPublicKeyInfoBase64()
    {
        using var key = ToAsymmetricAlgorithm();
        return Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    }

    public bool MatchesSubjectPublicKeyInfo(string spkiBase64)
    {
        try
        {
            return ToSubjectPublicKeyInfoBase64() == spkiBase64;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private AsymmetricAlgorithm ToAsymmetricAlgorithm()
    {
        if (KeyType == PasskeyAlgorithm.Ec2KeyType)
        {
            if (XCoordinate is not { Length: 32 } || YCoordinate is not { Length: 32 })
            {
                throw new CryptographicException("An EC2 COSE key must carry two 32-byte coordinates.");
            }

            var ec = ECDsa.Create();
            ec.ImportParameters(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = XCoordinate, Y = YCoordinate }
            });
            return ec;
        }

        if (KeyType == PasskeyAlgorithm.RsaKeyType)
        {
            if (Modulus is not { Length: > 0 } || Exponent is not { Length: > 0 })
            {
                throw new CryptographicException("An RSA COSE key must carry a modulus and an exponent.");
            }

            var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters
            {
                Modulus = Modulus,
                Exponent = Exponent
            });
            return rsa;
        }

        throw new CryptographicException($"COSE key type {KeyType} is not supported.");
    }
}

public static class PasskeyAlgorithm
{
    public const int Es256 = -7;
    public const int Rs256 = -257;
    public const int Ps256 = -37;
    public const int EdDsa = -8;

    public const int Ec2KeyType = 2;
    public const int RsaKeyType = 3;
    public const int P256Curve = 1;

    public const int LabelKeyType = 1;
    public const int LabelAlgorithm = 3;
    public const int LabelCurve = -1;
    public const int LabelXCoordinate = -2;
    public const int LabelYCoordinate = -3;
    public const int LabelModulus = -1;
    public const int LabelExponent = -2;

    // Mirrors the Android authenticator: only these two are ever generated, while PS256 assertions
    // imported from elsewhere can still be verified.
    public static bool CanGenerate(int algorithm) => algorithm is Es256 or Rs256;

    public static bool CanVerify(int algorithm) => algorithm is Es256 or Rs256 or Ps256;

    public static string Describe(int algorithm) => algorithm switch
    {
        Es256 => "ES256",
        Rs256 => "RS256",
        Ps256 => "PS256",
        EdDsa => "EdDSA",
        _ => $"COSE-{algorithm}"
    };
}

public static class PasskeyKeyMaterialGenerator
{
    public static PasskeyKeyMaterial Generate(int algorithm)
    {
        if (!PasskeyAlgorithm.CanGenerate(algorithm))
        {
            throw new NotSupportedException(
                $"Monica's passkey authenticator cannot generate {PasskeyAlgorithm.Describe(algorithm)} keys.");
        }

        return algorithm switch
        {
            PasskeyAlgorithm.Es256 => GenerateEc(),
            _ => GenerateRsa(algorithm)
        };
    }

    public static byte[] Sign(int algorithm, string privateKeyPkcs8Base64, byte[] payload)
    {
        if (!PasskeyAlgorithm.CanGenerate(algorithm))
        {
            throw new NotSupportedException(
                $"Monica's passkey authenticator cannot sign with {PasskeyAlgorithm.Describe(algorithm)}.");
        }

        var pkcs8 = Convert.FromBase64String(privateKeyPkcs8Base64);
        if (algorithm == PasskeyAlgorithm.Es256)
        {
            using var ec = ECDsa.Create();
            ec.ImportPkcs8PrivateKey(pkcs8, out _);
            return ec.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }

        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(pkcs8, out _);
        return rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    public static bool Verify(
        int algorithm,
        string publicKeySpkiBase64,
        byte[] payload,
        ReadOnlySpan<byte> signature)
    {
        if (!PasskeyAlgorithm.CanVerify(algorithm))
        {
            return false;
        }

        var subjectPublicKeyInfo = Convert.FromBase64String(publicKeySpkiBase64);
        if (algorithm == PasskeyAlgorithm.Es256)
        {
            if (signature.Length != 64)
            {
                return false;
            }

            using var ec = ECDsa.Create();
            ec.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
            return ec.VerifyData(
                payload,
                signature.ToArray(),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }

        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
        return rsa.VerifyData(
            payload,
            signature.ToArray(),
            HashAlgorithmName.SHA256,
            algorithm == PasskeyAlgorithm.Ps256 ? RSASignaturePadding.Pss : RSASignaturePadding.Pkcs1);
    }

    public static byte[] BuildCosePublicKey(int algorithm, string publicKeySpkiBase64)
    {
        var subjectPublicKeyInfo = Convert.FromBase64String(publicKeySpkiBase64);
        if (algorithm is PasskeyAlgorithm.Es256)
        {
            using var ec = ECDsa.Create();
            ec.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
            var parameters = ec.ExportParameters(false);
            return new CborBuilder()
                .WriteStartMap(5)
                .WriteInteger(PasskeyAlgorithm.LabelKeyType)
                .WriteInteger(PasskeyAlgorithm.Ec2KeyType)
                .WriteInteger(PasskeyAlgorithm.LabelAlgorithm)
                .WriteInteger(algorithm)
                .WriteInteger(PasskeyAlgorithm.LabelCurve)
                .WriteInteger(PasskeyAlgorithm.P256Curve)
                .WriteInteger(PasskeyAlgorithm.LabelXCoordinate)
                .WriteByteString(parameters.Q.X!)
                .WriteInteger(PasskeyAlgorithm.LabelYCoordinate)
                .WriteByteString(parameters.Q.Y!)
                .Build();
        }

        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
        var rsaParameters = rsa.ExportParameters(false);
        return new CborBuilder()
            .WriteStartMap(4)
            .WriteInteger(PasskeyAlgorithm.LabelKeyType)
            .WriteInteger(PasskeyAlgorithm.RsaKeyType)
            .WriteInteger(PasskeyAlgorithm.LabelAlgorithm)
            .WriteInteger(algorithm)
            .WriteInteger(PasskeyAlgorithm.LabelModulus)
            .WriteByteString(rsaParameters.Modulus!)
            .WriteInteger(PasskeyAlgorithm.LabelExponent)
            .WriteByteString(rsaParameters.Exponent!)
            .Build();
    }

    public static bool TryParseCosePublicKey(
        ReadOnlySpan<byte> coseKey,
        out CosePublicKey? parsed,
        out int consumedBytes)
    {
        parsed = null;
        consumedBytes = 0;
        try
        {
            var reader = new CborReader(coseKey.ToArray());
            var entries = reader.ReadStartMap();
            var values = new Dictionary<int, CborValue>();

            for (var index = 0; index < entries; index++)
            {
                var label = reader.ReadValue().AsInteger();
                values[label] = reader.ReadValue();
            }

            if (!values.TryGetValue(PasskeyAlgorithm.LabelKeyType, out var keyTypeEntry) ||
                !values.TryGetValue(PasskeyAlgorithm.LabelAlgorithm, out var algorithmEntry))
            {
                return false;
            }

            var keyType = keyTypeEntry.AsInteger();
            var algorithm = algorithmEntry.AsInteger();

            // The coordinate labels are -1/-2 for RSA and -1/-2/-3 for EC2, so a value can only be
            // typed once the key type is known; that is why nothing is interpreted inside the loop.
            parsed = keyType switch
            {
                PasskeyAlgorithm.Ec2KeyType
                    when TryBytes(values, PasskeyAlgorithm.LabelXCoordinate, out var x) &&
                         TryBytes(values, PasskeyAlgorithm.LabelYCoordinate, out var y) =>
                    new CosePublicKey(keyType, algorithm, x, y, null, null),
                PasskeyAlgorithm.RsaKeyType
                    when TryBytes(values, PasskeyAlgorithm.LabelModulus, out var modulus) &&
                         TryBytes(values, PasskeyAlgorithm.LabelExponent, out var exponent) =>
                    new CosePublicKey(keyType, algorithm, null, null, modulus, exponent),
                _ => null
            };
            if (parsed is null)
            {
                return false;
            }

            consumedBytes = reader.Position;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool TryBytes(
        IReadOnlyDictionary<int, CborValue> values,
        int label,
        out byte[]? payload)
    {
        payload = values.TryGetValue(label, out var entry) ? entry.Bytes : null;
        return payload is not null;
    }

    private static PasskeyKeyMaterial GenerateEc()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return Material(
            PasskeyAlgorithm.Es256,
            ec.ExportSubjectPublicKeyInfo(),
            ec.ExportPkcs8PrivateKey());
    }

    private static PasskeyKeyMaterial GenerateRsa(int algorithm)
    {
        using var rsa = RSA.Create(2048);
        return Material(
            algorithm,
            rsa.ExportSubjectPublicKeyInfo(),
            rsa.ExportPkcs8PrivateKey());
    }

    private static PasskeyKeyMaterial Material(int algorithm, byte[] spki, byte[] pkcs8)
    {
        var publicKeyBase64 = Convert.ToBase64String(spki);
        return new PasskeyKeyMaterial(
            algorithm,
            publicKeyBase64,
            BuildCosePublicKey(algorithm, publicKeyBase64),
            Convert.ToBase64String(pkcs8));
    }
}
