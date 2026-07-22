using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class PasswordEditorViewModel
{
    public PasswordEntry BuildEntry(string storedPassword) => BuildEntryFrom(Source, storedPassword);

    public PasswordEntry BuildEntryFrom(PasswordEntry? source, string storedPassword)
    {
        var entry = source is null ? new PasswordEntry() : Clone(source);
        entry.Title = Title.Trim();
        entry.Website = EncodeWebsites();
        entry.Username = Username.Trim();
        entry.Password = storedPassword;
        entry.Notes = Notes.Trim();
        entry.AuthenticatorKey = AuthenticatorKey.Trim();
        entry.AppPackageName = AppPackageName.Trim();
        entry.AppName = AppName.Trim();
        entry.Email = Email.Trim();
        entry.Phone = Phone.Trim();
        entry.AddressLine = AddressLine.Trim();
        entry.City = City.Trim();
        entry.State = State.Trim();
        entry.ZipCode = ZipCode.Trim();
        entry.Country = Country.Trim();
        entry.CreditCardNumber = CreditCardNumber.Trim();
        entry.CreditCardHolder = CreditCardHolder.Trim();
        entry.CreditCardExpiry = CreditCardExpiry.Trim();
        entry.CreditCardCvv = CreditCardCvv.Trim();
        entry.PasskeyBindings = PasskeyBindings.Trim();
        entry.SshKeyData = SshKeyData.Trim();
        entry.SsoProvider = SsoProvider.Trim();
        entry.WifiMetadata = WifiMetadata.Trim();
        entry.LoginType = SelectedLoginType?.Value ?? PasswordLoginType.Password;
        if (entry.LoginType == PasswordLoginType.Barcode)
        {
            ClearBarcodeInapplicableFields(entry);
        }
        entry.CategoryId = SelectedCategory?.Id;
        entry.BoundNoteId = SelectedBoundNote?.Id;
        var customIconType = NormalizeCustomIconType(SelectedCustomIconType?.Value);
        var customIconValue = CustomIconValue.Trim();
        if (customIconType == "NONE" || string.IsNullOrWhiteSpace(customIconValue))
        {
            customIconType = "NONE";
            customIconValue = "";
        }

        entry.CustomIconType = customIconType;
        entry.CustomIconValue = customIconType == "NONE" ? null : customIconValue;
        entry.CustomIconUpdatedAt = ShouldUpdateCustomIconTimestamp(source, entry)
            ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            : source?.CustomIconUpdatedAt ?? 0;
        entry.IsFavorite = IsFavorite;
        return entry;
    }

    public IReadOnlyList<PasswordEntry> BuildEntries(IReadOnlyList<string> storedPasswords)
    {
        if (storedPasswords.Count == 0)
        {
            return [BuildEntry("")];
        }

        var entries = storedPasswords.Select(BuildEntry).ToArray();
        if (entries.Length > 1)
        {
            var replicaGroupId = entries
                .Select(entry => entry.ReplicaGroupId)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? $"editor-{Guid.NewGuid():N}";
            foreach (var entry in entries)
            {
                entry.ReplicaGroupId = replicaGroupId;
            }
        }

        return entries;
    }

    private static bool ShouldUpdateCustomIconTimestamp(PasswordEntry? source, PasswordEntry entry)
    {
        return !string.Equals(source?.CustomIconType ?? "NONE", entry.CustomIconType, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(source?.CustomIconValue ?? "", entry.CustomIconValue ?? "", StringComparison.Ordinal);
    }

    private static void ClearBarcodeInapplicableFields(PasswordEntry entry)
    {
        entry.Website = "";
        entry.Username = "";
        entry.AuthenticatorKey = "";
        entry.PasskeyBindings = "";
        entry.SsoProvider = "";
        entry.SsoRefEntryId = null;
        entry.WifiMetadata = "";
        entry.SshKeyData = "";
    }

    private static PasswordEntry Clone(PasswordEntry source) => source.CreateDetachedCopy();
}
