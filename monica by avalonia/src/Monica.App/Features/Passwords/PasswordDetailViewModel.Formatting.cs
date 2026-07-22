using Monica.App.Services;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.App.ViewModels;

public sealed partial class PasswordDetailViewModel
{
    private static IReadOnlyList<PasswordEntry> NormalizeSiblings(
        PasswordEntry entry,
        IReadOnlyList<PasswordEntry> siblings) =>
        siblings.Count == 0 ? [entry] : siblings;

    private static void AddGroup(
        List<PasswordDetailGroup> groups,
        string title,
        bool isExpanded,
        params PasswordDetailField[] fields)
    {
        var visibleFields = fields
            .Where(field => !string.IsNullOrWhiteSpace(field.DisplayValue))
            .ToArray();
        if (visibleFields.Length > 0)
        {
            groups.Add(new PasswordDetailGroup(title, isExpanded, visibleFields));
        }
    }

    private static PasswordDetailField Field(
        string label,
        string value,
        string? copyValue = null,
        bool canCopy = true,
        bool isSensitive = false)
    {
        var normalizedValue = value.Trim();
        return new PasswordDetailField(
            label,
            normalizedValue,
            copyValue ?? normalizedValue,
            canCopy && normalizedValue.Length > 0,
            isSensitive);
    }

    private static (string DisplayValue, string CopyValue, bool CanCopy) TryUnprotectPassword(
        string storedPassword,
        ICryptoService cryptoService)
    {
        var result = PasswordSecretResolver.Read(storedPassword, cryptoService);
        if (result.State == PasswordSecretState.Empty)
        {
            return ("", "", false);
        }

        if (!result.IsReadable)
        {
            return ("********", "", false);
        }

        return (result.Value, result.Value, true);
    }

    private static string LocalizeLoginType(ILocalizationService localization, PasswordLoginType loginType)
    {
        return loginType switch
        {
            PasswordLoginType.Sso => localization.Get("LoginTypeSso"),
            PasswordLoginType.Wifi => localization.Get("LoginTypeWifi"),
            PasswordLoginType.SshKey => localization.Get("LoginTypeSshKey"),
            PasswordLoginType.Barcode => localization.Get("LoginTypeBarcode"),
            _ => localization.Get("LoginTypePassword")
        };
    }

    private static string LocalizeCustomIconType(ILocalizationService localization, string customIconType)
    {
        return customIconType.ToUpperInvariant() switch
        {
            "SIMPLE_ICON" => localization.Get("CustomIconSimple"),
            "UPLOADED" => localization.Get("CustomIconUploaded"),
            _ => localization.Get("CustomIconUseDefault")
        };
    }
}
