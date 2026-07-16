namespace Monica.Core.ImportExport;

public enum CsvImportError
{
    InvalidFormat,
    ResourceLimitExceeded
}

public sealed class CsvImportException : InvalidOperationException
{
    public CsvImportException(CsvImportError error, string message)
        : base(message)
    {
        Error = error;
    }

    public CsvImportError Error { get; }
}
