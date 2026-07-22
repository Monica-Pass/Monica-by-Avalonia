using Monica.Core.Models;

namespace Monica.Core.Bitwarden;

public sealed record BitwardenRemoteFolder(
    string Id,
    string Name,
    string? ParentId,
    bool IsDeleted = false);

public sealed record BitwardenRemoteCipherMetadata(
    string CipherId,
    string? FolderId,
    string RevisionDate,
    int CipherType,
    bool IsDeleted,
    string PayloadHash,
    DateTimeOffset? UpdatedAt = null);

public sealed record BitwardenLocalCipherReference(
    long LocalId,
    string CipherId,
    string? FolderId,
    string? RevisionDate,
    int CipherType,
    bool IsDeleted,
    bool LocalModified,
    string PayloadHash,
    string ItemKind,
    DateTimeOffset UpdatedAt);

public sealed record BitwardenPullSnapshot(
    IReadOnlyList<BitwardenRemoteFolder> Folders,
    IReadOnlyList<BitwardenRemoteCipherMetadata> Ciphers,
    string? RevisionDate,
    bool IsComplete,
    DateTimeOffset ReceivedAt)
{
    public int ActiveCipherCount => Ciphers.Count(cipher => !cipher.IsDeleted);
}

public enum BitwardenPullBlockReason
{
    None = 0,
    IncompleteSnapshot,
    EmptyRemoteVault,
    SharpDataReduction,
    DuplicateRemoteCipher,
    InvalidRemoteRevision,
    UnsupportedCipherType
}

public sealed record BitwardenPullSafetyResult(
    bool CanApply,
    BitwardenPullBlockReason BlockReason,
    string Message,
    int LocalCipherCount,
    int RemoteCipherCount);

public enum BitwardenMergeAction
{
    NoChange = 0,
    AddRemote,
    ApplyRemoteUpdate,
    ApplyRemoteDeletion,
    PreserveLocalUnmatched,
    MarkLocalClean,
    CreateConflictBackupThenApplyRemote
}

public sealed record BitwardenMergeDecision(
    BitwardenMergeAction Action,
    string CipherId,
    long? LocalId,
    int CipherType,
    string? LocalRevisionDate,
    string? RemoteRevisionDate,
    string Reason);

public sealed record BitwardenStoredRemoteFolder(
    long VaultId,
    string RemoteFolderId,
    string Name,
    string? ParentRemoteFolderId,
    long? LocalCategoryId,
    bool IsDeleted,
    DateTimeOffset LastSeenAt);

public sealed record BitwardenConflictBackup(
    long Id,
    long VaultId,
    string CipherId,
    string ItemKind,
    long LocalItemId,
    string? LocalRevisionDate,
    string? RemoteRevisionDate,
    string PayloadJson,
    string Reason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt = null);

public sealed record BitwardenDecodedCipher(
    BitwardenRemoteCipherMetadata Metadata,
    PasswordEntry? Password,
    SecureItem? SecureItem,
    IReadOnlyList<CustomField> CustomFields,
    IReadOnlyList<PasswordHistoryEntry> PasswordHistory);

public sealed record BitwardenPullMergeResult(
    int Added,
    int Updated,
    int Deleted,
    int ConflictsBackedUp,
    int MarkedClean,
    int PreservedLocalOnly,
    int Unchanged);
