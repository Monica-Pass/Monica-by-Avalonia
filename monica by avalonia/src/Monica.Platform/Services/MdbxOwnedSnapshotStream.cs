namespace Monica.Platform.Services;

/// <summary>Immutable encrypted snapshot whose private temporary files live exactly as long as the stream.</summary>
internal sealed class MdbxOwnedSnapshotStream : Stream
{
    private readonly FileStream _stream;
    private string? _scratch;
    internal string SourcePath { get; }
    internal byte[] ContentHash { get; }

    internal MdbxOwnedSnapshotStream(string path, string scratch, string sourcePath, byte[] contentHash)
    {
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        _scratch = scratch;
        SourcePath = sourcePath;
        ContentHash = contentHash;
    }

    public override bool CanRead => _stream.CanRead;
    public override bool CanSeek => _stream.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _stream.Length;
    public override long Position { get => _stream.Position; set => _stream.Position = value; }
    public override void Flush() => _stream.Flush();
    public override int Read(byte[] buffer, int offset, int count) => _stream.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => _stream.Read(buffer);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _stream.ReadAsync(buffer, cancellationToken);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _stream.ReadAsync(buffer, offset, count, cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => _stream.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException("Snapshots are immutable.");
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Snapshots are immutable.");

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _stream.Dispose(); Cleanup(); }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync();
        Cleanup();
        GC.SuppressFinalize(this);
    }

    private void Cleanup()
    {
        var directory = Interlocked.Exchange(ref _scratch, null);
        if (directory is null) return;
        try { Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
