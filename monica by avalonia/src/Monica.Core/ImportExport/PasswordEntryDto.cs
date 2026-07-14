using Monica.Core.Models;

namespace Monica.Core.ImportExport;

internal sealed class PasswordEntryDto
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Website { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool IsFavorite { get; set; }
    public int SortOrder { get; set; }
    public bool IsGroupCover { get; set; }
    public string AppPackageName { get; set; } = "";
    public string AppName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string AddressLine { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string ZipCode { get; set; } = "";
    public string Country { get; set; } = "";
    public string CreditCardNumber { get; set; } = "";
    public string CreditCardHolder { get; set; } = "";
    public string CreditCardExpiry { get; set; } = "";
    public string CreditCardCvv { get; set; } = "";
    public long? CategoryId { get; set; }
    public long? BoundNoteId { get; set; }
    public long? KeepassDatabaseId { get; set; }
    public string? KeepassGroupPath { get; set; }
    public string? KeepassEntryUuid { get; set; }
    public string? KeepassGroupUuid { get; set; }
    public long? MdbxDatabaseId { get; set; }
    public string? MdbxFolderId { get; set; }
    public string AuthenticatorKey { get; set; } = "";
    public string PasskeyBindings { get; set; } = "";
    public string SshKeyData { get; set; } = "";
    public PasswordLoginType LoginType { get; set; } = PasswordLoginType.Password;
    public string SsoProvider { get; set; } = "";
    public long? SsoRefEntryId { get; set; }
    public string WifiMetadata { get; set; } = "";
    public string CustomIconType { get; set; } = "NONE";
    public string? CustomIconValue { get; set; }
    public long CustomIconUpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public string? ReplicaGroupId { get; set; }
    public long? BitwardenVaultId { get; set; }
    public string? BitwardenCipherId { get; set; }
    public string? BitwardenFolderId { get; set; }
    public string? BitwardenRevisionDate { get; set; }
    public int BitwardenCipherType { get; set; } = 1;
    public bool BitwardenLocalModified { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public static PasswordEntryDto FromModel(PasswordEntry source) =>
        new()
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

    public PasswordEntry ToModel() =>
        new()
        {
            Id = Id,
            Title = Title,
            Website = Website,
            Username = Username,
            Password = Password,
            Notes = Notes,
            IsFavorite = IsFavorite,
            SortOrder = SortOrder,
            IsGroupCover = IsGroupCover,
            AppPackageName = AppPackageName,
            AppName = AppName,
            Email = Email,
            Phone = Phone,
            AddressLine = AddressLine,
            City = City,
            State = State,
            ZipCode = ZipCode,
            Country = Country,
            CreditCardNumber = CreditCardNumber,
            CreditCardHolder = CreditCardHolder,
            CreditCardExpiry = CreditCardExpiry,
            CreditCardCvv = CreditCardCvv,
            CategoryId = CategoryId,
            BoundNoteId = BoundNoteId,
            KeepassDatabaseId = KeepassDatabaseId,
            KeepassGroupPath = KeepassGroupPath,
            KeepassEntryUuid = KeepassEntryUuid,
            KeepassGroupUuid = KeepassGroupUuid,
            MdbxDatabaseId = MdbxDatabaseId,
            MdbxFolderId = MdbxFolderId,
            AuthenticatorKey = AuthenticatorKey,
            PasskeyBindings = PasskeyBindings,
            SshKeyData = SshKeyData,
            LoginType = LoginType,
            SsoProvider = SsoProvider,
            SsoRefEntryId = SsoRefEntryId,
            WifiMetadata = WifiMetadata,
            CustomIconType = CustomIconType,
            CustomIconValue = CustomIconValue,
            CustomIconUpdatedAt = CustomIconUpdatedAt,
            IsDeleted = IsDeleted,
            DeletedAt = DeletedAt,
            IsArchived = IsArchived,
            ArchivedAt = ArchivedAt,
            ReplicaGroupId = ReplicaGroupId,
            BitwardenVaultId = BitwardenVaultId,
            BitwardenCipherId = BitwardenCipherId,
            BitwardenFolderId = BitwardenFolderId,
            BitwardenRevisionDate = BitwardenRevisionDate,
            BitwardenCipherType = BitwardenCipherType,
            BitwardenLocalModified = BitwardenLocalModified,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };
}
