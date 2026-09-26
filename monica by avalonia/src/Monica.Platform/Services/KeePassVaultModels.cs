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
    bool HasEntries = false,
    bool IsRecycleBin = false);

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
/// What one search found: the rows to show, in the order to show them, and how many matched in all.
/// The two numbers differ whenever the list was cut short, which is the difference between a screen
/// that says "first 200 of 1.234" and one that quietly pretends the database is smaller than it is.
/// </summary>
public sealed record KeePassSearchResults(
    IReadOnlyList<KeePassEntryRow> Entries,
    int TotalMatches)
{
    public static KeePassSearchResults Empty { get; } = new([], 0);

    public bool HasMore => Entries.Count < TotalMatches;
}

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

/// <summary>
/// What deleting a folder did. A folder that still holds anything reports its contents back instead
/// of emptying itself, so the caller can show what would go and ask once.
/// </summary>
public sealed record KeePassGroupDeleteResult(
    KeePassGroupDeleteStatus Status,
    int EntryCount,
    int GroupCount);

public enum KeePassGroupDeleteStatus
{
    Deleted,
    NotEmpty,
    NotFound
}

/// <summary>
/// How an entry leaves the tree. Recycling keeps the entry inside the database so a later KeePass
/// client can still find it; permanent deletion detaches it and records it in the deletion list,
/// which is the only trace a syncing client gets that the entry is gone rather than hidden.
/// </summary>
public enum KeePassDeleteMode
{
    RecycleBin,
    Permanent
}

public enum KeePassEntryDeleteStatus
{
    Recycled,
    PermanentlyDeleted,
    NotFound
}
