using Monica.App.Services;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.App.ViewModels;

public sealed partial class PasswordDetailViewModel
{
    private static IReadOnlyList<PasswordDetailGroup> BuildGroups(
        ILocalizationService localization,
        ICryptoService cryptoService,
        ITotpService totpService,
        PasswordEntry entry,
        IReadOnlyList<PasswordEntry> siblings,
        Category? category,
        SecureItem? boundNote,
        IReadOnlyList<Attachment> attachments,
        IReadOnlyList<CustomField> customFields)
    {
        var groups = new List<PasswordDetailGroup>();

        AddGroup(groups, localization.Get("General"), true,
            Field(localization.Get("PasswordTitle"), entry.Title),
            Field(localization.Get("Username"), entry.Username),
            Field(localization.Get("Website"), entry.Website),
            Field(localization.Get("Category"), category?.Name ?? ""),
            Field(localization.Get("BoundNote"), boundNote?.Title ?? ""));

        var passwordFields = new List<PasswordDetailField>();
        for (var index = 0; index < siblings.Count; index++)
        {
            var password = TryUnprotectPassword(siblings[index].Password, cryptoService);
            var label = siblings.Count == 1
                ? localization.Get("Password")
                : $"{localization.Get("Password")} {index + 1}";
            passwordFields.Add(Field(
                label,
                password.DisplayValue,
                password.CopyValue,
                password.CanCopy,
                isSensitive: true));
        }

        AddGroup(groups, localization.Get("Passwords"), true, passwordFields.ToArray());

        var totpData = TotpDataResolver.FromAuthenticatorKey(entry.AuthenticatorKey, entry.Title, entry.Username);
        if (totpData is not null)
        {
            var code = totpService.GenerateCode(totpData.Secret, totpData.Period, totpData.Digits, totpData.OtpType, totpData.Counter);
            AddGroup(groups, localization.Get("SecurityVerification"), true,
                Field(localization.Get("TotpCode"), code),
                Field(
                    localization.Get("RemainingTime"),
                    localization.Format("SecondFormat", totpService.GetRemainingSeconds(totpData.Period)),
                    canCopy: false),
                Field(localization.Get("Issuer"), totpData.Issuer),
                Field(localization.Get("Account"), totpData.AccountName),
                Field(localization.Get("TotpSecret"), totpData.Secret, isSensitive: true),
                Field(localization.Get("AuthenticatorKey"), entry.AuthenticatorKey, isSensitive: true));
        }

        AddGroup(groups, localization.Get("AppBinding"), false,
            Field(localization.Get("AppName"), entry.AppName),
            Field(localization.Get("AppPackageName"), entry.AppPackageName));

        AddGroup(groups, localization.Get("PersonalInfo"), false,
            Field(localization.Get("Email"), entry.Email),
            Field(localization.Get("Phone"), entry.Phone),
            Field(localization.Get("AddressLine"), entry.AddressLine),
            Field(localization.Get("City"), entry.City),
            Field(localization.Get("State"), entry.State),
            Field(localization.Get("ZipCode"), entry.ZipCode),
            Field(localization.Get("Country"), entry.Country));

        AddGroup(groups, localization.Get("CardInfo"), false,
            Field(localization.Get("CreditCardNumber"), entry.CreditCardNumber, isSensitive: true),
            Field(localization.Get("CreditCardHolder"), entry.CreditCardHolder),
            Field(localization.Get("CreditCardExpiry"), entry.CreditCardExpiry),
            Field(localization.Get("CreditCardCvv"), entry.CreditCardCvv, isSensitive: true));

        AddGroup(groups, localization.Get("AdvancedLogin"), false,
            Field(localization.Get("LoginType"), LocalizeLoginType(localization, entry.LoginType)),
            Field(localization.Get("SsoProvider"), entry.SsoProvider),
            Field(localization.Get("PasskeyBindings"), entry.PasskeyBindings),
            Field(localization.Get("WifiMetadata"), entry.WifiMetadata),
            Field(localization.Get("SshKeyData"), entry.SshKeyData));

        AddGroup(groups, localization.Get("CustomIcon"), false,
            Field(localization.Get("CustomIconType"), LocalizeCustomIconType(localization, entry.CustomIconType), canCopy: false),
            Field(localization.Get("CustomIconValue"), entry.CustomIconValue ?? ""),
            Field(
                localization.Get("UpdatedAt"),
                entry.CustomIconUpdatedAt == 0
                    ? ""
                    : DateTimeOffset.FromUnixTimeMilliseconds(entry.CustomIconUpdatedAt).ToString("g", localization.Culture),
                canCopy: false));

        AddGroup(groups, localization.Get("Notes"), false,
            Field(localization.Get("Notes"), entry.Notes),
            Field(localization.Get("BoundNote"), boundNote is null ? "" : NoteContentCodec.ToPlainPreview(
                NoteContentCodec.DecodeFromItem(boundNote).Content,
                NoteContentCodec.DecodeFromItem(boundNote).IsMarkdown)));

        AddGroup(groups, localization.Get("CustomFields"), false,
            customFields
                .OrderBy(field => field.SortOrder)
                .ThenBy(field => field.Id)
                .Select(field => Field(field.Title, field.Value, isSensitive: field.IsProtected))
                .ToArray());

        AddSourceMetadataGroup(groups, localization, entry);
        ConfigureSensitiveVisibilityLabels(groups, localization);
        return groups;
    }

    private static void AddSourceMetadataGroup(
        List<PasswordDetailGroup> groups,
        ILocalizationService localization,
        PasswordEntry entry)
    {
        AddGroup(groups, localization.Get("SourceMetadata"), false,
            Field(localization.Get("BitwardenVault"), entry.BitwardenVaultId?.ToString() ?? ""),
            Field(localization.Get("BitwardenCipher"), entry.BitwardenCipherId ?? ""),
            Field(localization.Get("KeePassDatabase"), entry.KeepassDatabaseId?.ToString() ?? ""),
            Field(localization.Get("KeePassGroup"), entry.KeepassGroupPath ?? ""),
            Field(localization.Get("MdbxDatabase"), entry.MdbxDatabaseId?.ToString() ?? ""),
            Field(localization.Get("MdbxFolder"), entry.MdbxFolderId ?? ""),
            Field(localization.Get("CreatedAt"), entry.CreatedAt.ToString("g", localization.Culture), canCopy: false),
            Field(localization.Get("UpdatedAt"), entry.UpdatedAt.ToString("g", localization.Culture), canCopy: false));
    }

    private static void ConfigureSensitiveVisibilityLabels(
        IEnumerable<PasswordDetailGroup> groups,
        ILocalizationService localization)
    {
        var showLabel = localization.Get("ShowPassword");
        var hideLabel = localization.Get("HidePassword");
        foreach (var field in groups.SelectMany(group => group.Fields).Where(field => field.IsSensitive))
        {
            field.ConfigureVisibilityLabels(showLabel, hideLabel);
        }
    }
}
