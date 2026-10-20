namespace Monica.Core.ImportExport;

public enum MonicaJsonImportError
{
    InvalidFormat,
    ResourceLimitExceeded,
    PasswordRequired,
    DecryptionFailed
}

public sealed class MonicaJsonImportException : InvalidOperationException
{
    public MonicaJsonImportException(MonicaJsonImportError error, string message)
        : base(message)
    {
        Error = error;
    }

    public MonicaJsonImportError Error { get; }
}
