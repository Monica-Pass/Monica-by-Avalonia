namespace Monica.Platform.Services;

public sealed record WebDavBackupTransferLimits(
    long MaximumTextBackupBytes = 96L * 1024 * 1024);

public sealed class WebDavTextPayloadTooLargeException : InvalidOperationException
{
    public WebDavTextPayloadTooLargeException(long maximumBytes, long? actualBytes = null)
        : base(actualBytes is null
            ? $"The WebDAV backup exceeds the safe transfer limit of {maximumBytes} bytes."
            : $"The WebDAV backup is {actualBytes} bytes and exceeds the safe transfer limit of {maximumBytes} bytes.")
    {
        MaximumBytes = maximumBytes;
        ActualBytes = actualBytes;
    }

    public long MaximumBytes { get; }

    public long? ActualBytes { get; }
}
