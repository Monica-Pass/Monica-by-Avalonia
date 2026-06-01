using System.Text;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Monica.Platform.Services;

namespace Monica.App.Services;

public sealed class AvaloniaFileSystemPickerService(
    Func<Window> ownerProvider,
    IPlatformIntegrationService platformIntegrationService) : IFileSystemPickerService
{
    public PlatformIntegrationCapability Capability => platformIntegrationService.GetCapability(PlatformFeatureKeys.FilePicker);

    public async Task<PickedTextFile?> OpenTextFileAsync(
        string title,
        IReadOnlyList<PlatformFilePickerFileType> fileTypes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureUsable();

        var owner = ownerProvider();
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = ToAvaloniaFileTypes(fileTypes)
        });

        var file = files.FirstOrDefault();
        if (file is null)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(cancellationToken);
        return new PickedTextFile(file.Name, content);
    }

    public async Task<string?> SaveTextFileAsync(
        string title,
        string suggestedFileName,
        string content,
        IReadOnlyList<PlatformFilePickerFileType> fileTypes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureUsable();

        var owner = ownerProvider();
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = InferDefaultExtension(fileTypes),
            FileTypeChoices = ToAvaloniaFileTypes(fileTypes)
        });

        if (file is null)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = await file.OpenWriteAsync();
        var bytes = Encoding.UTF8.GetBytes(content);
        await stream.WriteAsync(bytes, cancellationToken);
        return file.Name;
    }

    private void EnsureUsable()
    {
        if (!Capability.IsUsable)
        {
            throw new InvalidOperationException(Capability.UnsupportedReason ?? "File picking is not supported on this platform.");
        }
    }

    private static IReadOnlyList<FilePickerFileType>? ToAvaloniaFileTypes(IReadOnlyList<PlatformFilePickerFileType> fileTypes)
    {
        if (fileTypes.Count == 0)
        {
            return null;
        }

        return fileTypes
            .Select(type => new FilePickerFileType(type.Name)
            {
                Patterns = type.Patterns.ToArray()
            })
            .ToArray();
    }

    private static string? InferDefaultExtension(IReadOnlyList<PlatformFilePickerFileType> fileTypes)
    {
        var pattern = fileTypes
            .SelectMany(type => type.Patterns)
            .FirstOrDefault(item => item.StartsWith("*.", StringComparison.Ordinal));

        return pattern is null ? null : pattern[2..];
    }
}
