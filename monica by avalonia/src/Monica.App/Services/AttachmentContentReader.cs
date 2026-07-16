using System.Buffers;
using System.Security.Cryptography;

namespace Monica.App.Services;

internal static class AttachmentContentReader
{
    internal const long MaximumAttachmentBytes = 256L * 1024L * 1024L;

    internal static async Task<byte[]> ReadAsync(
        Stream source,
        long? declaredLength = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var knownLength = ResolveKnownLength(source, declaredLength);
        if (knownLength is not null)
        {
            ValidateLength(knownLength.Value);
            var content = GC.AllocateUninitializedArray<byte>((int)knownLength.Value);
            try
            {
                await source.ReadExactlyAsync(content, cancellationToken);
            }
            catch (EndOfStreamException ex)
            {
                CryptographicOperations.ZeroMemory(content);
                throw new InvalidDataException("The selected attachment ended before its declared size.", ex);
            }

            if (source.CanSeek)
            {
                return content;
            }

            var extraByte = new byte[1];
            if (await source.ReadAsync(extraByte, cancellationToken) == 0)
            {
                return content;
            }

            try
            {
                return await ReadUnknownLengthAsync(source, content, extraByte[0], cancellationToken);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(content);
            }
        }

        return await ReadUnknownLengthAsync(source, [], null, cancellationToken);
    }

    private static long? ResolveKnownLength(Stream source, long? declaredLength)
    {
        if (source.CanSeek)
        {
            try
            {
                return source.Length - source.Position;
            }
            catch (NotSupportedException)
            {
                // Fall back to storage-provider metadata or bounded streaming.
            }
        }

        return declaredLength;
    }

    private static void ValidateLength(long length)
    {
        if (length < 0)
        {
            throw new InvalidDataException("The selected attachment reported an invalid size.");
        }

        if (length > MaximumAttachmentBytes)
        {
            throw new AttachmentTooLargeException(MaximumAttachmentBytes, length);
        }
    }

    private static async Task<byte[]> ReadUnknownLengthAsync(
        Stream source,
        byte[] initialContent,
        byte? extraByte,
        CancellationToken cancellationToken)
    {
        var initialLength = initialContent.LongLength + (extraByte is null ? 0 : 1);
        if (initialLength > MaximumAttachmentBytes)
        {
            throw new AttachmentTooLargeException(MaximumAttachmentBytes, initialLength);
        }

        using var destination = new MemoryStream(
            Math.Min((int)MaximumAttachmentBytes, Math.Max(81920, initialContent.Length + 1)));
        var transferBuffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            await destination.WriteAsync(initialContent, cancellationToken);
            if (extraByte is not null)
            {
                destination.WriteByte(extraByte.Value);
            }

            while (true)
            {
                var read = await source.ReadAsync(transferBuffer, cancellationToken);
                if (read == 0)
                {
                    return destination.ToArray();
                }

                var nextLength = destination.Length + read;
                if (nextLength > MaximumAttachmentBytes)
                {
                    throw new AttachmentTooLargeException(MaximumAttachmentBytes, nextLength);
                }

                await destination.WriteAsync(transferBuffer.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            if (destination.TryGetBuffer(out var plaintextBuffer))
            {
                CryptographicOperations.ZeroMemory(plaintextBuffer.AsSpan(0, (int)destination.Length));
            }

            CryptographicOperations.ZeroMemory(transferBuffer);
            ArrayPool<byte>.Shared.Return(transferBuffer);
        }
    }
}

public sealed class AttachmentTooLargeException(long maximumBytes, long actualBytes) : IOException(
    $"Attachment size {actualBytes} exceeds the desktop limit {maximumBytes}.")
{
    public long MaximumBytes { get; } = maximumBytes;
    public long ActualBytes { get; } = actualBytes;
}
