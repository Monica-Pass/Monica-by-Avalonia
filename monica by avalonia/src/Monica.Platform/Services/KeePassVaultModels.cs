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

public sealed record KeePassGroupRow(
    string Name,
    string Path,
    string Uuid,
    string? ParentUuid);

public sealed record KeePassEntryRow(
    string EntryUuid,
    string GroupUuid,
    string GroupPath,
    string Title,
    string UserName,
    string Url,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<KeePassAttachmentRow> Attachments);

public sealed record KeePassAttachmentRow(
    string Name,
    string BinaryReference,
    long SizeBytes);

public sealed record KeePassEntryDetail(
    KeePassEntryRow Row,
    string Password,
    string Notes,
    string AuthenticatorKey,
    IReadOnlyList<KeePassCustomField> CustomFields,
    IReadOnlyList<KeePassAttachmentContent> Attachments);

public sealed record KeePassCustomField(
    string Name,
    string Value,
    bool IsProtected);

public sealed record KeePassAttachmentContent(
    KeePassAttachmentRow Row,
    ReadOnlyMemory<byte> Content);
