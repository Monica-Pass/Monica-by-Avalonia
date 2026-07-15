namespace Monica.Core.ImportExport;

public enum AegisImportFailureReason
{
    InvalidFormat,
    PasswordRequired,
    DecryptionFailed,
    UnsupportedKeySlot,
    UnsafeKeyDerivationParameters
}

public sealed class AegisImportException : InvalidOperationException
{
    public AegisImportException(AegisImportFailureReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    public AegisImportFailureReason Reason { get; }
}
