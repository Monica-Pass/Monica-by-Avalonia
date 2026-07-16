using Monica.App.Services;

namespace Monica.Tests;

public sealed class AttachmentContentReaderTests
{
    [Fact]
    public async Task Known_length_read_allocates_only_one_full_payload_buffer()
    {
        const int payloadLength = 4 * 1024 * 1024;
        var payload = new byte[payloadLength];
        _ = await AttachmentContentReader.ReadAsync(new MemoryStream([1]), 1);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var source = new MemoryStream(payload, writable: false);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = await AttachmentContentReader.ReadAsync(source, payloadLength);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(payloadLength, result.Length);
        Assert.True(
            allocated < payloadLength + (256 * 1024),
            $"Known-length attachment read allocated {allocated:N0} bytes for a {payloadLength:N0}-byte payload.");
    }

    [Fact]
    public async Task Declared_length_reader_handles_short_async_reads_without_reallocation()
    {
        var payload = Enumerable.Range(0, 4096).Select(index => (byte)(index % 251)).ToArray();
        await using var source = new ShortReadStream(payload, maximumReadSize: 7);

        var result = await AttachmentContentReader.ReadAsync(source, payload.Length);

        Assert.Equal(payload, result);
    }

    [Fact]
    public async Task Unknown_length_reader_uses_bounded_fallback()
    {
        var payload = "unknown-length attachment"u8.ToArray();
        await using var source = new ShortReadStream(payload, maximumReadSize: 3);

        var result = await AttachmentContentReader.ReadAsync(source);

        Assert.Equal(payload, result);
    }

    [Fact]
    public async Task Oversize_declared_length_is_rejected_before_reading_or_allocating_payload()
    {
        await using var source = new ThrowOnReadStream();

        var exception = await Assert.ThrowsAsync<AttachmentTooLargeException>(() =>
            AttachmentContentReader.ReadAsync(
                source,
                AttachmentContentReader.MaximumAttachmentBytes + 1));

        Assert.Equal(AttachmentContentReader.MaximumAttachmentBytes, exception.MaximumBytes);
        Assert.Equal(0, source.ReadCallCount);
    }

    [Fact]
    public async Task Avalonia_binary_picker_reader_rejects_oversize_content_before_reading()
    {
        await using var source = new ThrowOnReadStream();

        var exception = await Assert.ThrowsAsync<AttachmentTooLargeException>(() =>
            AvaloniaFileSystemPickerService.ReadBinaryContentAsync(
                source,
                AttachmentContentReader.MaximumAttachmentBytes + 1));

        Assert.Equal(AttachmentContentReader.MaximumAttachmentBytes, exception.MaximumBytes);
        Assert.Equal(0, source.ReadCallCount);
    }

    [Fact]
    public async Task Truncated_declared_length_is_rejected()
    {
        await using var source = new ShortReadStream("short"u8.ToArray(), maximumReadSize: 2);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            AttachmentContentReader.ReadAsync(source, declaredLength: 20));
    }

    private sealed class ShortReadStream(byte[] content, int maximumReadSize) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var length = Math.Min(Math.Min(count, maximumReadSize), content.Length - _position);
            if (length <= 0)
            {
                return 0;
            }

            content.AsSpan(_position, length).CopyTo(buffer.AsSpan(offset, length));
            _position += length;
            return length;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = Math.Min(Math.Min(buffer.Length, maximumReadSize), content.Length - _position);
            if (length <= 0)
            {
                return ValueTask.FromResult(0);
            }

            content.AsMemory(_position, length).CopyTo(buffer);
            _position += length;
            return ValueTask.FromResult(length);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ThrowOnReadStream : Stream
    {
        public int ReadCallCount { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadCallCount++;
            throw new InvalidOperationException("Oversize content must be rejected before reading.");
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadCallCount++;
            return ValueTask.FromException<int>(
                new InvalidOperationException("Oversize content must be rejected before reading."));
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
