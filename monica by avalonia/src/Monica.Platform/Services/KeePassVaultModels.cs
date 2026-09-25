namespace Monica.Platform.Services;

public enum KeePassVaultError
{
    InvalidCredentialsOrFile,
    UnsupportedFormat,
    ResourceLimitExceeded,
    WriteFailed,
    ConcurrentChange,
    NoSourceFile
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
    string? ParentUuid,
    bool HasEntries = false);

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

/// <summary>
/// The complete editable content of one entry. Applying an edit replaces the entry's standard
/// fields, its TOTP field and its whole custom field set, so a caller has to build it from a detail
/// it just read; whatever the edit leaves out is dropped when the file is saved.
/// </summary>
public sealed record KeePassEntryEdit(
    string EntryUuid,
    string Title,
    string UserName,
    string Password,
    string Url,
    string Notes,
    string AuthenticatorKey,
    IReadOnlyList<KeePassCustomField> CustomFields);

public sealed record KeePassSaveResult(
    string Path,
    int FileBytes,
    string PayloadSha256,
    int EntryCount);
