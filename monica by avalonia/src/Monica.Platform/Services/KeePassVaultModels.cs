namespace Monica.Platform.Services;

public enum KeePassVaultError
{
    InvalidCredentialsOrFile,
    UnsupportedFormat,
    ResourceLimitExceeded
}

public sealed class KeePassVaultException(KeePassVaultError error, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public KeePassVaultError Error { get; } = error;
}

public sealed record KeePassVaultSnapshot(
    long DatabaseId,
    string DatabaseName,
    string SourceFileName,
    string RootGroupUuid,
    IReadOnlyList<KeePassGroupSnapshot> Groups,
    IReadOnlyList<KeePassEntrySnapshot> Entries);

public sealed record KeePassGroupSnapshot(
    string Name,
    string Path,
    string Uuid,
    string? ParentUuid);

public sealed record KeePassEntrySnapshot(
    string Title,
    string UserName,
    string Password,
    string Url,
    string Notes,
    string AuthenticatorKey,
    string GroupPath,
    string EntryUuid,
    string GroupUuid,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<KeePassCustomFieldSnapshot> CustomFields,
    IReadOnlyList<KeePassAttachmentSnapshot> Attachments);

public sealed record KeePassCustomFieldSnapshot(
    string Name,
    string Value,
    bool IsProtected);

public sealed record KeePassAttachmentSnapshot(
    string Name,
    string BinaryReference,
    ReadOnlyMemory<byte> Content);
