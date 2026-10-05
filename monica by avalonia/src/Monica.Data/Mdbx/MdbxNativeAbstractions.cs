using Monica.Core.Models;

namespace Monica.Data.Mdbx;

public interface IMdbxNativeBridge
{
    bool IsAvailable { get; }

    /// <summary>Why the native engine could not be loaded, or null when it loaded. An empty vault is not
    /// an acceptable answer while this is set: callers have to surface it instead.</summary>
    string? AvailabilityError { get; }

    /// <summary>Storage format the loaded native runtime writes, or empty when it could not be probed.</summary>
    string WritableStorageFormat { get; }

    /// <summary>Storage formats the loaded native runtime can read. A file outside this set cannot be
    /// trusted even by a read, because the runtime only promises to understand the listed formats.</summary>
    IReadOnlyList<string> ReadableStorageFormats { get; }

    /// <summary>
    /// Reads the format header of <paramref name="path"/> without opening the vault for writing, so the
    /// client can decide what it may do with the file before touching it. Null when the engine could not
    /// read the header at all.
    /// </summary>
    Task<MdbxNativeMigrationInfo?> InspectMigrationAsync(string path, CancellationToken cancellationToken = default);

    Task<IMdbxNativeVault> CreateVaultAsync(string path, string password, string deviceId, MdbxTigaMode mode, CancellationToken cancellationToken = default);
    Task<IMdbxNativeVault> OpenVaultAsync(string path, string password, string deviceId, CancellationToken cancellationToken = default);
}

/// <summary>
/// What the file itself declares, as measured by the engine rather than by this build's own constants.
/// </summary>
public sealed record MdbxNativeMigrationInfo(
    bool Initialized,
    string? FormatVersion,
    uint? SchemaVersion,
    string? MinReaderVersion,
    string? MinWriterVersion,
    bool RequiresUpgrade,
    bool UnknownCriticalExtensions,
    string TargetFormatVersion,
    uint TargetSchemaVersion);

public interface IMdbxNativeVault : IDisposable
{
    /// <summary>True when this handle may only be read. Callers use it to skip the writes that a normal
    /// read path performs to materialize state, because on a foreign file those writes are the damage.</summary>
    bool IsReadOnly { get; }

    Task<MdbxNativeVaultInfo> GetInfoAsync(CancellationToken cancellationToken = default);
    Task<MdbxNativeProjectRecord> CreateProjectAsync(string title, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a project whose id is chosen by the caller and, where a parent is given, files it under
    /// that parent. Android creates its root and every folder this way; the plain CreateProjectAsync
    /// leaves the id to the engine and never sets a parent, so the other client cannot address the
    /// result by the id it expects.
    /// </summary>
    Task<MdbxNativeProjectRecord> CreateProjectWithIdentityAsync(
        string projectId,
        string title,
        string? parentProjectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MdbxNativeProjectRecord>> ListProjectsAsync(CancellationToken cancellationToken = default);
    Task<MdbxNativeEntryRecord> CreateEntryAsync(string projectId, string entryType, string title, string payloadJson, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MdbxNativeEntryRecord>> ListEntriesAsync(string projectId, string? entryType = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MdbxNativeEntryRecord>> ListDeletedEntriesAsync(string projectId, string? entryType = null, CancellationToken cancellationToken = default);
    Task<MdbxNativeEntryRecord> UpdateEntryAsync(string projectId, string entryId, string entryType, string title, string payloadJson, CancellationToken cancellationToken = default);
    Task<MdbxNativeEntryRecord> MoveEntryAsync(string projectId, string entryId, string targetProjectId, CancellationToken cancellationToken = default);
    Task DeleteEntryAsync(string projectId, string entryId, CancellationToken cancellationToken = default);
    Task<MdbxNativeEntryRecord> RestoreEntryAsync(string projectId, string entryId, CancellationToken cancellationToken = default);
    Task<MdbxNativeAttachmentRecord> CreateAttachmentAsync(string projectId, string? entryId, string fileName, string? mediaType, byte[] content, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MdbxNativeAttachmentRecord>> ListAttachmentsAsync(string projectId, string? entryId, CancellationToken cancellationToken = default);
    Task<byte[]> ReadAttachmentContentAsync(string attachmentId, CancellationToken cancellationToken = default);
    Task DeleteAttachmentAsync(string attachmentId, CancellationToken cancellationToken = default);
}

public sealed record MdbxNativeVaultInfo(string VaultId, string DeviceId);

public sealed record MdbxNativeProjectRecord(
    string ProjectId,
    string Title);

public sealed record MdbxNativeEntryRecord(
    string EntryId,
    string ProjectId,
    string EntryType,
    string Title,
    string PayloadJson,
    bool Deleted);

/// <summary>
/// Metadata for a native object whose type is outside the desktop reader's known set. The descriptor keeps
/// payload out of the returned diagnostics model; the current legacy discovery path still needs to be
/// replaced by native summaries before this becomes a strict metadata-only disclosure boundary.
/// </summary>
public sealed record MdbxUnknownEntryDescriptor(
    string EntryId,
    string ProjectId,
    string EntryType,
    string Title,
    bool Deleted);

public sealed record MdbxNativeAttachmentRecord(
    string AttachmentId,
    string ProjectId,
    string? EntryId,
    string FileName,
    string? MediaType,
    string StorageMode,
    string ContentHash,
    ulong OriginalSize,
    ulong StoredSize,
    uint ChunkCount,
    bool Deleted);
