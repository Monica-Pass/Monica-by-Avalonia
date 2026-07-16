using Monica.App.Services;
using Monica.Core.Models;
using Monica.Core.Services;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Monica.Tests;

public sealed class PasswordAttachmentFileServiceTests
{
    [Fact]
    public async Task New_attachment_format_uses_versioned_binary_envelope_and_roundtrips()
    {
        var root = TestTempPaths.CreateDirectoryPath();
        var crypto = CreateUnlockedCryptoService();
        var service = CreateService(root, crypto);
        var content = "private attachment payload"u8.ToArray();

        var draft = await service.StoreAttachmentAsync("secret.txt", content, "text/plain");

        var path = GetStoredPath(root, draft.StoragePath);
        var persisted = await File.ReadAllBytesAsync(path);
        Assert.True(persisted.AsSpan().StartsWith("MONATTCH"u8));
        Assert.False(persisted.AsSpan().IndexOf(content) >= 0);
        Assert.Empty(Directory.GetFiles(root, $".{Path.GetFileName(path)}.*.tmp"));

        var restored = await service.TryReadAttachmentContentAsync(new Attachment
        {
            StoragePath = draft.StoragePath
        });
        Assert.Equal(content, restored);
    }

    [Fact]
    public async Task New_attachment_format_uses_independent_file_keys()
    {
        var root = TestTempPaths.CreateDirectoryPath();
        var service = CreateService(root, CreateUnlockedCryptoService());
        var content = "same private attachment payload"u8.ToArray();

        var first = await service.StoreAttachmentAsync("first.txt", content);
        var second = await service.StoreAttachmentAsync("second.txt", content);

        var firstBytes = await File.ReadAllBytesAsync(GetStoredPath(root, first.StoragePath));
        var secondBytes = await File.ReadAllBytesAsync(GetStoredPath(root, second.StoragePath));
        Assert.NotEqual(firstBytes, secondBytes);
        Assert.Equal(content, await service.TryReadAttachmentContentAsync(new Attachment { StoragePath = first.StoragePath }));
        Assert.Equal(content, await service.TryReadAttachmentContentAsync(new Attachment { StoragePath = second.StoragePath }));
    }

    [Fact]
    public async Task Tampered_attachment_ciphertext_is_rejected()
    {
        var root = TestTempPaths.CreateDirectoryPath();
        var service = CreateService(root, CreateUnlockedCryptoService());
        var draft = await service.StoreAttachmentAsync("secret.txt", "authenticated content"u8.ToArray());
        var path = GetStoredPath(root, draft.StoragePath);
        var persisted = await File.ReadAllBytesAsync(path);
        persisted[^1] ^= 0x5A;
        await File.WriteAllBytesAsync(path, persisted);

        await Assert.ThrowsAnyAsync<CryptographicException>(() =>
            service.TryReadAttachmentContentAsync(new Attachment { StoragePath = draft.StoragePath }));
    }

    [Fact]
    public async Task Reordered_attachment_chunks_are_rejected_without_full_payload_expansion()
    {
        const int chunkSize = 64 * 1024;
        const int fixedHeaderSize = 33;
        const int wrappedKeyLengthOffset = 29;
        const int headerTagSize = 16;
        const int chunkRecordOverhead = 4 + 16;
        var root = TestTempPaths.CreateDirectoryPath();
        var service = CreateService(root, CreateUnlockedCryptoService());
        var content = RandomNumberGenerator.GetBytes((chunkSize * 2) + 97);
        var draft = await service.StoreAttachmentAsync("large.bin", content);
        var path = GetStoredPath(root, draft.StoragePath);
        var persisted = await File.ReadAllBytesAsync(path);

        Assert.InRange(persisted.Length, content.Length, content.Length + 2048);
        Assert.Equal(content, await service.TryReadAttachmentContentAsync(new Attachment { StoragePath = draft.StoragePath }));

        var wrappedKeyLength = BinaryPrimitives.ReadInt32LittleEndian(
            persisted.AsSpan(wrappedKeyLengthOffset, sizeof(int)));
        var firstChunkOffset = fixedHeaderSize + wrappedKeyLength + headerTagSize;
        var firstChunkLength = BinaryPrimitives.ReadInt32LittleEndian(
            persisted.AsSpan(firstChunkOffset, sizeof(int)));
        var chunkRecordSize = chunkRecordOverhead + firstChunkLength;
        var secondChunkOffset = firstChunkOffset + chunkRecordSize;
        Assert.Equal(chunkSize, firstChunkLength);
        Assert.Equal(chunkSize, BinaryPrimitives.ReadInt32LittleEndian(
            persisted.AsSpan(secondChunkOffset, sizeof(int))));

        var firstRecord = persisted.AsSpan(firstChunkOffset, chunkRecordSize).ToArray();
        persisted.AsSpan(secondChunkOffset, chunkRecordSize).CopyTo(
            persisted.AsSpan(firstChunkOffset, chunkRecordSize));
        firstRecord.CopyTo(persisted, secondChunkOffset);
        await File.WriteAllBytesAsync(path, persisted);

        await Assert.ThrowsAnyAsync<CryptographicException>(() =>
            service.TryReadAttachmentContentAsync(new Attachment { StoragePath = draft.StoragePath }));
    }

    [Fact]
    public async Task Truncated_attachment_ciphertext_is_rejected()
    {
        var root = TestTempPaths.CreateDirectoryPath();
        var service = CreateService(root, CreateUnlockedCryptoService());
        var draft = await service.StoreAttachmentAsync("secret.txt", "complete content"u8.ToArray());
        var path = GetStoredPath(root, draft.StoragePath);
        var persisted = await File.ReadAllBytesAsync(path);
        await File.WriteAllBytesAsync(path, persisted[..^1]);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.TryReadAttachmentContentAsync(new Attachment { StoragePath = draft.StoragePath }));
    }

    [Fact]
    public async Task Legacy_text_attachment_remains_readable()
    {
        var root = TestTempPaths.CreateDirectoryPath();
        var crypto = CreateUnlockedCryptoService();
        var service = CreateService(root, crypto);
        var content = "legacy private attachment"u8.ToArray();
        const string storagePath = "secure_attachments/legacy.monicaattachment";
        var encryptedPayload = crypto.EncryptString(Convert.ToBase64String(content));
        await File.WriteAllTextAsync(GetStoredPath(root, storagePath), encryptedPayload, Encoding.UTF8);

        var restored = await service.TryReadAttachmentContentAsync(new Attachment { StoragePath = storagePath });

        Assert.Equal(content, restored);
    }

    [Fact]
    public async Task Attachment_path_traversal_is_rejected()
    {
        var root = TestTempPaths.CreateDirectoryPath();
        var service = CreateService(root, CreateUnlockedCryptoService());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.TryReadAttachmentContentAsync(new Attachment
            {
                StoragePath = Path.Combine("..", "outside.monicaattachment")
            }));
    }

    [Fact]
    public async Task Nested_attachment_storage_path_is_rejected()
    {
        var root = TestTempPaths.CreateDirectoryPath();
        var service = CreateService(root, CreateUnlockedCryptoService());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.TryReadAttachmentContentAsync(new Attachment
            {
                StoragePath = "secure_attachments/nested/attachment.monicaattachment"
            }));
    }

    [Fact]
    public void Unix_path_comparison_rejects_case_variant_sibling()
    {
        var parent = TestTempPaths.CreateDirectoryPath();
        var root = Path.Combine(parent, "SecureStore");
        var caseVariantSibling = Path.Combine(parent, "securestore", "attachment.enc");

        Assert.False(PasswordAttachmentFileService.IsPathWithinRoot(
            caseVariantSibling,
            root,
            StringComparison.Ordinal));
        Assert.True(PasswordAttachmentFileService.IsPathWithinRoot(
            caseVariantSibling,
            root,
            StringComparison.OrdinalIgnoreCase));
    }

    private static PasswordAttachmentFileService CreateService(string root, ICryptoService cryptoService) =>
        new(
            () => throw new InvalidOperationException("A window is not required for direct storage tests."),
            new LocalizationService(),
            cryptoService,
            root);

    private static CryptoService CreateUnlockedCryptoService()
    {
        var crypto = new CryptoService();
        var hash = crypto.HashMasterPassword("attachment test password");
        crypto.InitializeSession("attachment test password", hash.Salt);
        return crypto;
    }

    private static string GetStoredPath(string root, string storagePath) =>
        Path.Combine(root, Path.GetFileName(storagePath));
}
