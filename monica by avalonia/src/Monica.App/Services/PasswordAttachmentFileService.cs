using System.Text;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Services;

namespace Monica.App.Services;

public sealed record PasswordAttachmentFileDraft(string FileName, string StoragePath, long SizeBytes, string ContentType, byte[]? Content = null);

public interface IPasswordAttachmentFileService
{
    Task<PasswordAttachmentFileDraft?> PickAttachmentAsync(CancellationToken cancellationToken = default);
    Task<PasswordAttachmentFileDraft> StoreAttachmentAsync(string fileName, byte[] content, string contentType = "", CancellationToken cancellationToken = default);
    Task DeleteStoredAttachmentAsync(string storagePath, CancellationToken cancellationToken = default);
}

public sealed class PasswordAttachmentFileService(
    Func<Window> ownerProvider,
    ILocalizationService localization,
    ICryptoService cryptoService,
    string? attachmentRoot = null) : IPasswordAttachmentFileService, IAttachmentContentStore
{
    private const string AttachmentFolderName = "secure_attachments";
    private readonly string _attachmentRoot = attachmentRoot ?? MonicaAppDataPaths.GetPath(AttachmentFolderName);

    public async Task<PasswordAttachmentFileDraft?> PickAttachmentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = ownerProvider();
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = localization.Get("SelectAttachment"),
            AllowMultiple = false
        });
        var file = files.FirstOrDefault();
        if (file is null)
        {
            return null;
        }

        var properties = await file.GetBasicPropertiesAsync();
        long? declaredLength = null;
        if (properties.Size is { } reportedSize)
        {
            declaredLength = reportedSize > long.MaxValue ? long.MaxValue : (long)reportedSize;
        }

        await using var source = await file.OpenReadAsync();
        var content = await AttachmentContentReader.ReadAsync(source, declaredLength, cancellationToken);
        return new PasswordAttachmentFileDraft(
            file.Name,
            "",
            content.LongLength,
            InferContentType(file.Name),
            content);
    }

    public async Task<PasswordAttachmentFileDraft> StoreAttachmentAsync(string fileName, byte[] content, string contentType = "", CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var storageName = $"{Guid.NewGuid():N}.monicaattachment";
        var relativeStoragePath = $"{AttachmentFolderName}/{storageName}";
        var absoluteStoragePath = ResolveAttachmentPath(relativeStoragePath);
        await AtomicFileWriter.WriteAsync(
            absoluteStoragePath,
            (stream, token) => AttachmentFileCodec.WriteAsync(stream, content, cryptoService, token),
            cancellationToken);

        return new PasswordAttachmentFileDraft(
            string.IsNullOrWhiteSpace(fileName) ? localization.Get("Attachment") : fileName.Trim(),
            relativeStoragePath,
            content.LongLength,
            string.IsNullOrWhiteSpace(contentType) ? InferContentType(fileName) : contentType.Trim(),
            content);
    }

    public Task DeleteStoredAttachmentAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            return Task.CompletedTask;
        }

        if (storagePath.StartsWith("mdbx:", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        var path = ResolveAttachmentPath(storagePath);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public async Task<byte[]?> TryReadAttachmentContentAsync(Attachment attachment, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(attachment.StoragePath) ||
            attachment.StoragePath.StartsWith("mdbx:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var path = ResolveAttachmentPath(attachment.StoragePath);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var prefix = new byte[AttachmentFileCodec.PrefixLength];
        var prefixLength = await stream.ReadAtLeastAsync(
            prefix,
            prefix.Length,
            throwOnEndOfStream: false,
            cancellationToken);
        stream.Position = 0;
        if (AttachmentFileCodec.IsCurrentFormat(prefix.AsSpan(0, prefixLength)))
        {
            return await AttachmentFileCodec.ReadAsync(stream, cryptoService, cancellationToken);
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, leaveOpen: false);
        var encryptedPayload = await reader.ReadToEndAsync(cancellationToken);
        var base64Content = cryptoService.DecryptString(encryptedPayload);
        return Convert.FromBase64String(base64Content);
    }

    public Task DeleteAttachmentContentAsync(Attachment attachment, CancellationToken cancellationToken = default) =>
        DeleteStoredAttachmentAsync(attachment.StoragePath, cancellationToken);

    private string ResolveAttachmentPath(string storagePath)
    {
        var normalized = storagePath.Replace('\\', '/');
        if (normalized.StartsWith($"{AttachmentFolderName}/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[(AttachmentFolderName.Length + 1)..];
        }

        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized is "." or ".." ||
            Path.IsPathRooted(normalized) ||
            !string.Equals(normalized, Path.GetFileName(normalized), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Attachment storage paths must name a file directly inside the Monica attachment store.");
        }

        var candidate = Path.GetFullPath(Path.Combine(_attachmentRoot, normalized));
        var fullRoot = Path.GetFullPath(_attachmentRoot);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!IsPathWithinRoot(candidate, fullRoot, comparison))
        {
            throw new InvalidOperationException("Attachment path is outside the Monica attachment store.");
        }

        return candidate;
    }

    internal static bool IsPathWithinRoot(string candidate, string root, StringComparison comparison)
    {
        var fullCandidate = Path.GetFullPath(candidate);
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return string.Equals(fullCandidate, fullRoot, comparison) ||
            fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static string InferContentType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".txt" or ".md" or ".csv" or ".json" or ".xml" => "text/plain",
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };
    }
}
