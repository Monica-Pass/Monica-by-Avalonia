using Monica.App.Services.VaultOperations;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static SecureItem CloneSecureItemForExport(SecureItem source, bool includeCategory = true, bool includeImages = true) =>
        ImportExportHelpers.CloneSecureItemForExport(source, includeCategory, includeImages);
}
