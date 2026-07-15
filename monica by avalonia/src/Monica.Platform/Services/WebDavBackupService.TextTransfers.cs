using System.Text;

namespace Monica.Platform.Services;

public sealed partial class WebDavBackupService
{
    private async Task<string> ReadTextWithinLimitAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var limited = new SizeLimitedReadStream(source, _transferLimits.MaximumTextBackupBytes);
        using var reader = new StreamReader(
            limited,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 80 * 1024,
            leaveOpen: false);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class SizeLimitedReadStream(Stream source, long maximumBytes) : Stream
    {
        private long _bytesRead;

        public override bool CanRead => source.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Track(source.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) => Track(source.Read(buffer));

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            Track(await source.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false));

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            Track(await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                source.Dispose();
            }

            base.Dispose(disposing);
        }

        private int Track(int read)
        {
            if (read <= 0)
            {
                return read;
            }

            var nextTotal = checked(_bytesRead + read);
            if (nextTotal > maximumBytes)
            {
                throw new WebDavTextPayloadTooLargeException(maximumBytes, nextTotal);
            }

            _bytesRead = nextTotal;
            return read;
        }
    }
}
