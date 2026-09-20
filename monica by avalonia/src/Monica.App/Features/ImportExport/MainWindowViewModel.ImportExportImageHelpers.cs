using Monica.App.Services.VaultOperations;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    // Delegated to shared ImportExportHelpers for use-case extraction
    private static IReadOnlyList<string> DecodeSecureItemImagePaths(SecureItem item) =>
        ImportExportHelpers.DecodeSecureItemImagePaths(item);

    private static Attachment CreateSecureItemImageAttachmentForExport(SecureItem item, string imagePath, int index) =>
        ImportExportHelpers.CreateSecureItemImageAttachmentForExport(item, imagePath, index);

    private static string ResolveSecureItemImageFileName(SecureItem item, string imagePath, int index) =>
        ImportExportHelpers.ResolveSecureItemImageFileName(item, imagePath, index);

    private static string InferAttachmentContentType(string path) =>
        ImportExportHelpers.InferAttachmentContentType(path);

    private static void ApplySecureItemImagePaths(SecureItem item, IReadOnlyList<string> imagePaths) =>
        ImportExportHelpers.ApplySecureItemImagePaths(item, imagePaths);
}
