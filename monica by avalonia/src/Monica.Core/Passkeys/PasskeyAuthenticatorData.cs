using System.Buffers.Binary;

namespace Monica.Core.Passkeys;

[Flags]
public enum PasskeyAuthenticatorFlags : byte
{
    None = 0x00,
    UserPresent = 0x01,
    UserVerified = 0x04,
    BackupEligible = 0x08,
    BackupState = 0x10,
    Signed = 0x20,
    AttestedCredentialData = 0x40
}

public sealed record PasskeyAuthenticatorData(
    byte[] RpIdHash,
    PasskeyAuthenticatorFlags Flags,
    int SignCount,
    byte[]? CredentialId,
    CosePublicKey? PublicKey)
{
    public bool UserPresent => Flags.HasFlag(PasskeyAuthenticatorFlags.UserPresent);

    public bool UserVerified => Flags.HasFlag(PasskeyAuthenticatorFlags.UserVerified);
}

/// <summary>
/// authData layout: sha256(rpId) || flags || 4-byte big-endian sign count, plus attested credential
/// data (16-byte AAGUID || 2-byte credential id length || credential id || COSE public key) on creation.
/// </summary>
public static class PasskeyAuthenticatorDataCodec
{
    public const PasskeyAuthenticatorFlags AssertionFlags =
        PasskeyAuthenticatorFlags.UserPresent
        | PasskeyAuthenticatorFlags.UserVerified
        | PasskeyAuthenticatorFlags.BackupEligible
        | PasskeyAuthenticatorFlags.BackupState;

    public const PasskeyAuthenticatorFlags CreationFlags =
        AssertionFlags | PasskeyAuthenticatorFlags.AttestedCredentialData;

    public const int AaguidLength = 16;

    private const int HeaderLength = 32 + 1 + 4;

    public static byte[] BuildAssertion(string rpId, int signCount) => BuildHeader(rpId, AssertionFlags, signCount);

    public static byte[] BuildCreation(
        string rpId,
        byte[] aaguid,
        byte[] credentialId,
        byte[] cosePublicKey,
        int signCount)
    {
        if (aaguid.Length != AaguidLength)
        {
            throw new ArgumentException($"An AAGUID must be {AaguidLength} bytes.", nameof(aaguid));
        }

        if (credentialId.Length > ushort.MaxValue)
        {
            throw new ArgumentException("A credential id longer than 65535 bytes cannot be encoded in authData.");
        }

        var header = BuildHeader(rpId, CreationFlags, signCount);
        var payload = new byte[header.Length + AaguidLength + 2 + credentialId.Length + cosePublicKey.Length];
        header.CopyTo(payload, 0);
        var offset = header.Length;
        aaguid.CopyTo(payload, offset);
        offset += AaguidLength;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset), (ushort)credentialId.Length);
        offset += 2;
        credentialId.CopyTo(payload, offset);
        offset += credentialId.Length;
        cosePublicKey.CopyTo(payload, offset);
        return payload;
    }

    public static bool TryParse(byte[] authData, out PasskeyAuthenticatorData? parsed)
    {
        parsed = null;
        if (authData.Length < HeaderLength)
        {
            return false;
        }

        var flags = (PasskeyAuthenticatorFlags)authData[32];
        var signCount = BinaryPrimitives.ReadInt32BigEndian(authData.AsSpan(33, 4));
        var rpIdHash = authData.AsSpan(0, 32).ToArray();
        if (!flags.HasFlag(PasskeyAuthenticatorFlags.AttestedCredentialData))
        {
            parsed = new PasskeyAuthenticatorData(rpIdHash, flags, signCount, null, null);
            return authData.Length == HeaderLength;
        }

        var credentialIdOffset = HeaderLength + AaguidLength;
        if (authData.Length < credentialIdOffset + 2)
        {
            return false;
        }

        var credentialIdLength = BinaryPrimitives.ReadUInt16BigEndian(authData.AsSpan(HeaderLength + AaguidLength));
        var coseOffset = credentialIdOffset + 2 + credentialIdLength;
        if (authData.Length < coseOffset)
        {
            return false;
        }

        var credentialId = authData.AsSpan(credentialIdOffset + 2, credentialIdLength).ToArray();
        if (!PasskeyKeyMaterialGenerator.TryParseCosePublicKey(
                authData.AsSpan(coseOffset),
                out var publicKey,
                out var consumedBytes) ||
            coseOffset + consumedBytes != authData.Length)
        {
            return false;
        }

        parsed = new PasskeyAuthenticatorData(rpIdHash, flags, signCount, credentialId, publicKey);
        return true;
    }

    private static byte[] BuildHeader(string rpId, PasskeyAuthenticatorFlags flags, int signCount)
    {
        var header = new byte[HeaderLength];
        PasskeyRpId.Hash(rpId).CopyTo(header, 0);
        header[32] = (byte)flags;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(33, 4), signCount);
        return header;
    }
}
