using System.Text;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data.Services;

namespace Monica.App.Services;

public sealed record PasswordAttachmentFileDraft(string FileName, string StoragePath, long SizeBytes, string ContentType, byte[]? Content = null);

public interface IPasswordAttachmentFileService
{
    Task<PasswordAttachmentFileDraft?> PickAndStoreAttachmentAsync(PasswordEntry entry, CancellationToken cancellationToken = default);
    Task DeleteStoredAttachmentAsync(string storagePath, CancellationToken cancellationToken = default);
}

public sealed class PasswordAttachmentFileService(
    Func<Window> ownerProvider,
    ILocalizationService localization,
    ICryptoService cryptoService) : IPasswordAttachmentFileService, IAttachmentContentStore
{
    private const string AttachmentFolderName = "secure_attachments";

    public async Task<PasswordAttachmentFileDraft?> PickAndStoreAttachmentAsync(PasswordEntry entry, CancellationToken cancellationToken = default)
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

        await using var source = await file.OpenReadAsync();
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);
        var content = buffer.ToArray();

        var encryptedPayload = cryptoService.EncryptString(Convert.ToBase64String(content));
        var storageName = $"{Guid.NewGuid():N}.monicaattachment";
        var relativeStoragePath = $"{AttachmentFolderName}/{storageName}";
        var absoluteStoragePath = ResolveAttachmentPath(relativeStoragePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absoluteStoragePath)!);
        await File.WriteAllTextAsync(absoluteStoragePath, encryptedPayload, Encoding.UTF8, cancellationToken);

        return new PasswordAttachmentFileDraft(
            file.Name,
            relativeStoragePath,
            buffer.Length,
            InferContentType(file.Name),
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

        var encryptedPayload = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
        var base64Content = cryptoService.DecryptString(encryptedPayload);
        return Convert.FromBase64String(base64Content);
    }

    public Task DeleteAttachmentContentAsync(Attachment attachment, CancellationToken cancellationToken = default) =>
        DeleteStoredAttachmentAsync(attachment.StoragePath, cancellationToken);

    private static string ResolveAttachmentPath(string storagePath)
    {
        var root = GetAttachmentRoot();
        var normalized = storagePath.Replace('\\', '/');
        if (normalized.StartsWith($"{AttachmentFolderName}/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[(AttachmentFolderName.Length + 1)..];
        }

        var candidate = Path.GetFullPath(Path.Combine(root, normalized));
        var fullRoot = Path.GetFullPath(root);
        if (!candidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(candidate, fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Attachment path is outside the Monica attachment store.");
        }

        return candidate;
    }

    private static string GetAttachmentRoot()
    {
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = AppContext.BaseDirectory;
        }

        return Path.Combine(basePath, "Monica by Avalonia", AttachmentFolderName);
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
