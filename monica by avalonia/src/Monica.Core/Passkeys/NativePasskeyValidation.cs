using System.Security.Cryptography;

namespace Monica.Core.Passkeys;

/// <summary>Input and response checks for the native WebAuthn client boundary.</summary>
public static class NativePasskeyValidation
{
    public const int MaximumResponseBytes = 4 * 1024 * 1024;

    public static string ValidateRpAndOrigin(string rpId, string? origin, byte[] challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        if (challenge.Length is < 16 or > 1024)
            throw new ArgumentException("A WebAuthn challenge must contain 16 to 1024 bytes.", nameof(challenge));

        var normalized = PasskeyRpId.Normalize(rpId)
            ?? throw new ArgumentException("A relying-party id is required.", nameof(rpId));
        var resolvedOrigin = origin ?? $"https://{normalized}";
        if (!Uri.TryCreate(resolvedOrigin, UriKind.Absolute, out var uri) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            uri.AbsolutePath != "/" ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) ||
            !(string.Equals(uri.IdnHost, normalized, StringComparison.OrdinalIgnoreCase) ||
              uri.IdnHost.EndsWith("." + normalized, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("The WebAuthn origin must be a secure origin belonging to the relying party.", nameof(origin));

        return normalized;
    }

    public static void ValidateCreate(NativePasskeyCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRpAndOrigin(request.RpId, request.Origin, request.Challenge);
        if (request.UserId is null || request.UserId.Length is < 1 or > 64)
            throw new ArgumentException("A WebAuthn user handle must contain 1 to 64 bytes.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.RpName))
            throw new ArgumentException("Account and relying-party names are required.", nameof(request));
        if (!PasskeyAlgorithm.CanGenerate(request.Algorithm))
            throw new NotSupportedException("The requested native credential algorithm is unsupported.");
    }

    public static void ValidateAssertion(NativePasskeyAssertionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRpAndOrigin(request.RpId, request.Origin, request.Challenge);
        if (request.CredentialId is { Length: 0 or > 1024 })
            throw new ArgumentException("The credential id is invalid.", nameof(request));
    }

    public static bool VerifyClientData(byte[] json, string type, byte[] challenge, string expectedOrigin)
    {
        return json.Length <= MaximumResponseBytes &&
            PasskeyClientData.TryParse(json, out var data) && data is not null && !data.CrossOrigin &&
            data.Type == type && string.Equals(data.Origin, expectedOrigin, StringComparison.Ordinal) &&
            PasskeyBase64Url.TryDecode(data.Challenge, out var returnedChallenge) &&
            CryptographicOperations.FixedTimeEquals(returnedChallenge, challenge);
    }

    /// <summary>Native WebAuthn ES256 signatures use the DER sequence defined by WebAuthn.</summary>
    public static bool VerifySignature(int algorithm, string publicKey, byte[] payload, byte[] signature)
    {
        try
        {
            if (algorithm != PasskeyAlgorithm.Es256)
                return PasskeyKeyMaterialGenerator.Verify(algorithm, publicKey, payload, signature);
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            return key.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception error) when (error is CryptographicException or FormatException or ArgumentException)
        {
            return false;
        }
    }
}
