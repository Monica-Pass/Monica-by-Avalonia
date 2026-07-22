using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class WalletItemEditorViewModel
{
    private string ResolveTitle()
    {
        if (!string.IsNullOrWhiteSpace(Title))
        {
            return Title.Trim();
        }

        return ItemType switch
        {
            VaultItemType.BankCard => string.IsNullOrWhiteSpace(BankName) ? L.Get("BankCard") : BankName.Trim(),
            VaultItemType.BillingAddress => string.IsNullOrWhiteSpace(AddressFullName) ? L.Get("BillingAddress") : AddressFullName.Trim(),
            VaultItemType.PaymentAccount => string.IsNullOrWhiteSpace(PaymentProvider) ? L.Get("PaymentAccount") : PaymentProvider.Trim(),
            _ => SelectedDocumentType.Label
        };
    }

    private IReadOnlyList<string> GetImagePaths()
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in ImagePathsText
                     .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Concat(_hiddenMdbxImagePaths))
        {
            if (!string.IsNullOrWhiteSpace(path) && seen.Add(path))
            {
                result.Add(path);
            }
        }

        return result;
    }

    private static IReadOnlyList<string> FilterEditableImagePaths(IEnumerable<string> imagePaths) =>
        imagePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(path => !IsMdbxImagePath(path))
            .ToArray();

    private static IReadOnlyList<string> GetMdbxImagePaths(IEnumerable<string> imagePaths) =>
        imagePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(IsMdbxImagePath)
            .ToArray();

    private static bool IsMdbxImagePath(string path) =>
        path.StartsWith("mdbx:", StringComparison.OrdinalIgnoreCase);
}
