using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Monica.Core.Services;

namespace Monica.App.Services;

internal static class AttachmentFileCodec
{
    private static readonly byte[] Magic = "MONATTCH"u8.ToArray();
    private const byte Version = 1;
    private const int FileKeySize = 32;
    private const int NoncePrefixSize = 8;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int ChunkSize = 64 * 1024;
    private const int FixedHeaderSize = 8 + 1 + 8 + 4 + NoncePrefixSize + 4;
    private const int MaximumWrappedKeySize = 4096;

    internal static int PrefixLength => Magic.Length;

    internal static bool IsCurrentFormat(ReadOnlySpan<byte> prefix) =>
        prefix.Length >= Magic.Length && prefix[..Magic.Length].SequenceEqual(Magic);

    internal static async Task WriteAsync(
        Stream destination,
        byte[] content,
        ICryptoService cryptoService,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(cryptoService);

        var fileKey = RandomNumberGenerator.GetBytes(FileKeySize);
        var noncePrefix = RandomNumberGenerator.GetBytes(NoncePrefixSize);
        try
        {
            var wrappedKey = Encoding.UTF8.GetBytes(cryptoService.EncryptBytes(fileKey));
            if (wrappedKey.Length is <= 0 or > MaximumWrappedKeySize)
            {
                throw new CryptographicException("The attachment file key could not be wrapped safely.");
            }

            var header = BuildHeader(content.LongLength, noncePrefix, wrappedKey);
            var headerTag = new byte[TagSize];
            var nonce = new byte[NonceSize];
            var chunkTag = new byte[TagSize];
            var chunkLength = new byte[sizeof(int)];
            var chunkAad = new byte[TagSize + sizeof(uint) + sizeof(int)];
            var cipherBuffer = new byte[ChunkSize];
            noncePrefix.CopyTo(nonce, 0);

            using var aes = new AesGcm(fileKey, TagSize);
            BinaryPrimitives.WriteUInt32LittleEndian(nonce.AsSpan(NoncePrefixSize), uint.MaxValue);
            aes.Encrypt(
                nonce,
                ReadOnlySpan<byte>.Empty,
                Span<byte>.Empty,
                headerTag,
                header);

            await destination.WriteAsync(header, cancellationToken);
            await destination.WriteAsync(headerTag, cancellationToken);

            var offset = 0;
            uint chunkIndex = 0;
            while (offset < content.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var length = Math.Min(ChunkSize, content.Length - offset);
                BinaryPrimitives.WriteUInt32LittleEndian(nonce.AsSpan(NoncePrefixSize), chunkIndex);
                BuildChunkAssociatedData(headerTag, chunkIndex, length, chunkAad);
                aes.Encrypt(
                    nonce,
                    content.AsSpan(offset, length),
                    cipherBuffer.AsSpan(0, length),
                    chunkTag,
                    chunkAad);

                BinaryPrimitives.WriteInt32LittleEndian(chunkLength, length);
                await destination.WriteAsync(chunkLength, cancellationToken);
                await destination.WriteAsync(chunkTag, cancellationToken);
                await destination.WriteAsync(cipherBuffer.AsMemory(0, length), cancellationToken);
                offset += length;
                chunkIndex++;
            }

            CryptographicOperations.ZeroMemory(cipherBuffer);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fileKey);
        }
    }

    internal static async Task<byte[]> ReadAsync(
        Stream source,
        ICryptoService cryptoService,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(cryptoService);

        byte[]? fileKey = null;
        byte[]? plaintext = null;
        try
        {
            var fixedHeader = new byte[FixedHeaderSize];
            await source.ReadExactlyAsync(fixedHeader, cancellationToken);
            if (!IsCurrentFormat(fixedHeader))
            {
                throw new InvalidDataException("The attachment file signature is invalid.");
            }

            if (fixedHeader[Magic.Length] != Version)
            {
                throw new InvalidDataException("The attachment file version is not supported.");
            }

            var wrappedKeyLength = BinaryPrimitives.ReadInt32LittleEndian(fixedHeader.AsSpan(FixedHeaderSize - sizeof(int)));
            if (wrappedKeyLength is <= 0 or > MaximumWrappedKeySize)
            {
                throw new InvalidDataException("The wrapped attachment key length is invalid.");
            }

            var wrappedKey = new byte[wrappedKeyLength];
            await source.ReadExactlyAsync(wrappedKey, cancellationToken);
            var header = new byte[fixedHeader.Length + wrappedKey.Length];
            fixedHeader.CopyTo(header, 0);
            wrappedKey.CopyTo(header, fixedHeader.Length);
            var headerTag = new byte[TagSize];
            await source.ReadExactlyAsync(headerTag, cancellationToken);

            fileKey = cryptoService.DecryptBytes(Encoding.UTF8.GetString(wrappedKey));
            if (fileKey.Length != FileKeySize)
            {
                throw new CryptographicException("The unwrapped attachment file key is invalid.");
            }

            var noncePrefixOffset = Magic.Length + 1 + sizeof(long) + sizeof(int);
            var noncePrefix = fixedHeader.AsSpan(noncePrefixOffset, NoncePrefixSize).ToArray();
            var nonce = new byte[NonceSize];
            noncePrefix.CopyTo(nonce, 0);
            using var aes = new AesGcm(fileKey, TagSize);
            BinaryPrimitives.WriteUInt32LittleEndian(nonce.AsSpan(NoncePrefixSize), uint.MaxValue);
            aes.Decrypt(
                nonce,
                ReadOnlySpan<byte>.Empty,
                headerTag,
                Span<byte>.Empty,
                header);

            var totalLength = BinaryPrimitives.ReadInt64LittleEndian(fixedHeader.AsSpan(Magic.Length + 1));
            var declaredChunkSize = BinaryPrimitives.ReadInt32LittleEndian(fixedHeader.AsSpan(Magic.Length + 1 + sizeof(long)));
            if (totalLength is < 0 or > int.MaxValue || declaredChunkSize != ChunkSize)
            {
                throw new InvalidDataException("The authenticated attachment dimensions are invalid.");
            }

            plaintext = new byte[(int)totalLength];
            var cipherBuffer = new byte[ChunkSize];
            var chunkTag = new byte[TagSize];
            var chunkLength = new byte[sizeof(int)];
            var chunkAad = new byte[TagSize + sizeof(uint) + sizeof(int)];
            var written = 0;
            uint chunkIndex = 0;
            while (written < plaintext.Length)
            {
                await source.ReadExactlyAsync(chunkLength, cancellationToken);
                var length = BinaryPrimitives.ReadInt32LittleEndian(chunkLength);
                var expectedLength = Math.Min(ChunkSize, plaintext.Length - written);
                if (length != expectedLength)
                {
                    throw new InvalidDataException("The attachment chunk length is invalid.");
                }

                await source.ReadExactlyAsync(chunkTag, cancellationToken);
                await source.ReadExactlyAsync(cipherBuffer.AsMemory(0, length), cancellationToken);
                BinaryPrimitives.WriteUInt32LittleEndian(nonce.AsSpan(NoncePrefixSize), chunkIndex);
                BuildChunkAssociatedData(headerTag, chunkIndex, length, chunkAad);
                aes.Decrypt(
                    nonce,
                    cipherBuffer.AsSpan(0, length),
                    chunkTag,
                    plaintext.AsSpan(written, length),
                    chunkAad);
                written += length;
                chunkIndex++;
            }

            var trailingByte = new byte[1];
            if (await source.ReadAsync(trailingByte, cancellationToken) != 0)
            {
                throw new InvalidDataException("The attachment file contains trailing data.");
            }

            CryptographicOperations.ZeroMemory(cipherBuffer);
            return plaintext;
        }
        catch (EndOfStreamException ex)
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }

            throw new InvalidDataException("The attachment file is truncated.", ex);
        }
        catch
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }

            throw;
        }
        finally
        {
            if (fileKey is not null)
            {
                CryptographicOperations.ZeroMemory(fileKey);
            }
        }
    }

    private static byte[] BuildHeader(long totalLength, byte[] noncePrefix, byte[] wrappedKey)
    {
        var header = new byte[FixedHeaderSize + wrappedKey.Length];
        Magic.CopyTo(header, 0);
        var offset = Magic.Length;
        header[offset++] = Version;
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(offset), totalLength);
        offset += sizeof(long);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(offset), ChunkSize);
        offset += sizeof(int);
        noncePrefix.CopyTo(header, offset);
        offset += NoncePrefixSize;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(offset), wrappedKey.Length);
        wrappedKey.CopyTo(header, FixedHeaderSize);
        return header;
    }

    private static void BuildChunkAssociatedData(
        byte[] headerTag,
        uint chunkIndex,
        int chunkLength,
        byte[] destination)
    {
        headerTag.CopyTo(destination, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(TagSize), chunkIndex);
        BinaryPrimitives.WriteInt32LittleEndian(destination.AsSpan(TagSize + sizeof(uint)), chunkLength);
    }
}
