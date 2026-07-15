using Monica.Core.Models;

namespace Monica.Core.ImportExport;

public enum BitwardenJsonImportError
{
    EncryptedExport,
    InvalidExport,
    ResourceLimitExceeded
}

public sealed class BitwardenJsonImportException(BitwardenJsonImportError error, string message)
    : Exception(message)
{
    public BitwardenJsonImportError Error { get; } = error;
}

public sealed record BitwardenJsonImportLimits(
    int MaximumJsonCharacters = 64 * 1024 * 1024,
    int MaximumFolders = 50_000,
    int MaximumItems = 100_000,
    int MaximumFieldsPerItem = 1_000,
    int MaximumHistoryEntriesPerItem = 1_000,
    int MaximumUrisPerLogin = 1_000);

public sealed record BitwardenFolderSnapshot(string Id, string Name);

public sealed record BitwardenJsonImportSnapshot(
    IReadOnlyList<BitwardenFolderSnapshot> Folders,
    IReadOnlyList<PasswordEntry> Passwords,
    IReadOnlyList<SecureItem> SecureItems,
    IReadOnlyList<PasswordCustomFieldExportGroup> PasswordCustomFields,
    IReadOnlyList<PasswordHistoryExportGroup> PasswordHistory,
    int UnsupportedItemCount,
    int AttachmentMetadataCount)
{
    public int SupportedItemCount => Passwords.Count + SecureItems.Count;
}
