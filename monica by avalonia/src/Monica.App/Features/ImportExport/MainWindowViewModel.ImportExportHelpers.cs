using Monica.App.Services.VaultOperations;
using Monica.Core.ImportExport;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    // Delegated to shared ImportExportHelpers for use-case extraction
    private static bool TryDecodeAttachmentContent(string contentBase64, out byte[] content) =>
        ImportExportHelpers.TryDecodeAttachmentContent(contentBase64, out content);

    private async Task<IReadOnlyList<string>> ImportSecureItemAttachmentsAsync(SecureItem item, IReadOnlyList<SecureItemAttachmentExport> attachments)
    {
        if (attachments.Count == 0)
        {
            return [];
        }

        var restoredPaths = new List<string>();
        foreach (var source in attachments)
        {
            if (!TryDecodeAttachmentContent(source.ContentBase64, out var content))
            {
                continue;
            }

            var draft = await _passwordAttachmentFileService.StoreAttachmentAsync(
                source.Metadata.FileName,
                content,
                source.Metadata.ContentType);
            restoredPaths.Add(draft.StoragePath);
        }

        return restoredPaths;
    }

    private static SecureItem CloneSecureItemForExport(SecureItem source, bool includeCategory = true, bool includeImages = true) =>
        ImportExportHelpers.CloneSecureItemForExport(source, includeCategory, includeImages);

    private static SecureItem CloneSecureItemForImport(
        SecureItem source,
        IReadOnlyDictionary<long, long> passwordIdMap,
        IReadOnlyDictionary<long, long>? categoryIdMap = null) =>
        ImportExportHelpers.CloneSecureItemForImport(source, passwordIdMap, categoryIdMap);

    private static SecureItem CloneSecureItem(SecureItem source) => ImportExportHelpers.CloneSecureItem(source);

    private static Category CloneCategory(Category source) => ImportExportHelpers.CloneCategory(source);

    private static void StripSecureItemImages(SecureItem item) => ImportExportHelpers.StripSecureItemImages(item);
}
