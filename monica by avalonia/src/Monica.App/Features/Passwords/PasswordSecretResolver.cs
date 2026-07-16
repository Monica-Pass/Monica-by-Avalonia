using System.Security.Cryptography;
using Monica.Core.Services;

namespace Monica.App.ViewModels;

internal enum PasswordSecretState
{
    Available,
    Empty,
    Locked,
    Unreadable
}

internal enum PasswordSecretOrigin
{
    None,
    Plaintext,
    Encrypted
}

internal readonly record struct PasswordSecretReadResult(
    PasswordSecretState State,
    string Value,
    PasswordSecretOrigin Origin)
{
    public bool IsReadable => State is PasswordSecretState.Available or PasswordSecretState.Empty;
}

internal static class PasswordSecretResolver
{
    private const string ProtectedPrefix = "vault:v1:";
    private const int AesGcmEnvelopeSize = 28;

    public static PasswordSecretReadResult Read(string storedValue, ICryptoService cryptoService)
    {
        if (string.IsNullOrEmpty(storedValue))
        {
            return new PasswordSecretReadResult(
                PasswordSecretState.Empty,
                "",
                PasswordSecretOrigin.None);
        }

        if (!cryptoService.IsUnlocked)
        {
            return new PasswordSecretReadResult(
                PasswordSecretState.Locked,
                "",
                PasswordSecretOrigin.None);
        }

        if (!LooksLikeEncryptedPayload(storedValue))
        {
            return new PasswordSecretReadResult(
                PasswordSecretState.Available,
                storedValue,
                PasswordSecretOrigin.Plaintext);
        }

        var encryptedValue = storedValue.StartsWith(ProtectedPrefix, StringComparison.Ordinal)
            ? storedValue[ProtectedPrefix.Length..]
            : storedValue;
        try
        {
            return new PasswordSecretReadResult(
                PasswordSecretState.Available,
                cryptoService.DecryptString(encryptedValue),
                PasswordSecretOrigin.Encrypted);
        }
        catch (Exception ex) when (IsExpectedDecryptFailure(ex))
        {
            return new PasswordSecretReadResult(
                PasswordSecretState.Unreadable,
                "",
                PasswordSecretOrigin.Encrypted);
        }
    }

    private static bool LooksLikeEncryptedPayload(string value)
    {
        if (value.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
        {
            return true;
        }

        var base64CharacterCount = 0;
        var paddingCount = 0;
        var paddingStarted = false;
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            if (character == '=')
            {
                paddingStarted = true;
                if (++paddingCount > 2)
                {
                    return false;
                }

                continue;
            }

            if (paddingStarted || !IsBase64Character(character))
            {
                return false;
            }

            base64CharacterCount++;
        }

        var encodedLength = base64CharacterCount + paddingCount;
        if (encodedLength == 0 || encodedLength % 4 != 0)
        {
            return false;
        }

        var decodedLength = (encodedLength / 4 * 3) - paddingCount;
        return decodedLength > AesGcmEnvelopeSize;
    }

    private static bool IsBase64Character(char character) =>
        character is >= 'A' and <= 'Z' or
            >= 'a' and <= 'z' or
            >= '0' and <= '9' or
            '+' or '/';

    private static bool IsExpectedDecryptFailure(Exception exception) =>
        exception is FormatException or CryptographicException or ArgumentException;
}

internal enum PasswordSecretUnavailableReason
{
    VaultLocked,
    UnreadableData
}

internal sealed class PasswordSecretUnavailableException(PasswordSecretUnavailableReason reason)
    : InvalidOperationException("The password secret is unavailable.")
{
    public PasswordSecretUnavailableReason Reason { get; } = reason;
}
