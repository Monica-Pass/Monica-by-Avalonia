namespace Monica.App.Services;

internal static class AtomicFileWriter
{
    private const int BufferSize = 81920;

    public static async Task WriteAsync(
        string targetPath,
        Func<Stream, CancellationToken, Task> writeAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(writeAsync);

        var fullTargetPath = Path.GetFullPath(targetPath);
        var fileName = Path.GetFileName(fullTargetPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("The target path must include a file name.", nameof(targetPath));
        }

        var directory = Path.GetDirectoryName(fullTargetPath) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.tmp");
        Exception? writeFailure = null;

        try
        {
            var streamOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = BufferSize,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            };
            if (!OperatingSystem.IsWindows())
            {
                streamOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            await using (var stream = new FileStream(temporaryPath, streamOptions))
            {
                await writeAsync(stream, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullTargetPath, overwrite: true);
        }
        catch (Exception ex)
        {
            writeFailure = ex;
            throw;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch when (writeFailure is not null)
            {
                // Preserve the original write failure if best-effort cleanup also fails.
            }
        }
    }
}
