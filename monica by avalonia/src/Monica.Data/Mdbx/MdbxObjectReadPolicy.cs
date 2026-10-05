namespace Monica.Data.Mdbx;

/// <summary>Exact native type/version pairs owned by the current desktop business codecs.</summary>
public static class MdbxObjectReadPolicy
{
    public const uint SupportedPayloadVersion = 1;

    public static bool Supports(string entryType, uint payloadVersion) =>
        payloadVersion == SupportedPayloadVersion &&
        entryType is "login" or "ssh-key" or "note" or "totp" or "card" or "document-ref" or "identity" or "billing-address" or "payment-account";
}
