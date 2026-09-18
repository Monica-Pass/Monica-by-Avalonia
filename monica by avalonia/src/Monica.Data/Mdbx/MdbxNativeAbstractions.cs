using Monica.Core.Models;

namespace Monica.Data.Mdbx;

public interface IMdbxNativeBridge
{
    bool IsAvailable { get; }

    /// <summary>Storage format the loaded native runtime writes, or empty when it could not be probed.</summary>
    string WritableStorageFormat { get; }

    Task<IMdbxNativeVault> CreateVaultAsync(string path, string password, string deviceId, MdbxTigaMode mode, CancellationToken cancellationToken = default);
    Task<IMdbxNativeVault> OpenVaultAsync(string path, string password, string deviceId, CancellationToken cancellationToken = default);
}

public interface IMdbxNativeVault : IDisposable
{
    Task<MdbxNativeVaultInfo> GetInfoAsync(CancellationToken cancellationToken = default);
    Task<MdbxNativeProjectRecord> CreateProjectAsync(string title, CancellationToken cancellationToken = default);
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
