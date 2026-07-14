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

    private static PasswordEntry Clone(PasswordEntry source)
    {
        return new PasswordEntry
        {
            Id = source.Id,
            Title = source.Title,
            Website = source.Website,
            Username = source.Username,
            Password = source.Password,
            Notes = source.Notes,
            IsFavorite = source.IsFavorite,
            SortOrder = source.SortOrder,
            IsGroupCover = source.IsGroupCover,
            AppPackageName = source.AppPackageName,
            AppName = source.AppName,
            Email = source.Email,
            Phone = source.Phone,
            AddressLine = source.AddressLine,
            City = source.City,
            State = source.State,
            ZipCode = source.ZipCode,
            Country = source.Country,
            CreditCardNumber = source.CreditCardNumber,
            CreditCardHolder = source.CreditCardHolder,
            CreditCardExpiry = source.CreditCardExpiry,
            CreditCardCvv = source.CreditCardCvv,
            CategoryId = source.CategoryId,
            BoundNoteId = source.BoundNoteId,
            KeepassDatabaseId = source.KeepassDatabaseId,
            KeepassGroupPath = source.KeepassGroupPath,
            KeepassEntryUuid = source.KeepassEntryUuid,
            KeepassGroupUuid = source.KeepassGroupUuid,
            MdbxDatabaseId = source.MdbxDatabaseId,
            MdbxFolderId = source.MdbxFolderId,
            AuthenticatorKey = source.AuthenticatorKey,
            PasskeyBindings = source.PasskeyBindings,
            SshKeyData = source.SshKeyData,
            LoginType = source.LoginType,
            SsoProvider = source.SsoProvider,
            SsoRefEntryId = source.SsoRefEntryId,
            WifiMetadata = source.WifiMetadata,
            CustomIconType = source.CustomIconType,
            CustomIconValue = source.CustomIconValue,
            CustomIconUpdatedAt = source.CustomIconUpdatedAt,
            IsDeleted = source.IsDeleted,
            DeletedAt = source.DeletedAt,
            IsArchived = source.IsArchived,
            ArchivedAt = source.ArchivedAt,
            ReplicaGroupId = source.ReplicaGroupId,
            BitwardenVaultId = source.BitwardenVaultId,
            BitwardenCipherId = source.BitwardenCipherId,
            BitwardenFolderId = source.BitwardenFolderId,
            BitwardenRevisionDate = source.BitwardenRevisionDate,
            BitwardenCipherType = source.BitwardenCipherType,
            BitwardenLocalModified = source.BitwardenLocalModified,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt
        };
    }
}
