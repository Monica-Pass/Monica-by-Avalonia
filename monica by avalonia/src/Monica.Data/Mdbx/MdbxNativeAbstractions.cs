using Monica.Core.Models;

namespace Monica.Data.Mdbx;

public interface IMdbxNativeBridge
{
    bool IsAvailable { get; }
    Task<IMdbxNativeVault> CreateVaultAsync(string path, string password, string deviceId, MdbxTigaMode mode, CancellationToken cancellationToken = default);
    Task<IMdbxNativeVault> OpenVaultAsync(string path, string password, string deviceId, CancellationToken cancellationToken = default);
}

public interface IMdbxNativeVault : IDisposable
{
    Task<MdbxNativeVaultInfo> GetInfoAsync(CancellationToken cancellationToken = default);
    Task<MdbxNativeProjectRecord> CreateProjectAsync(string title, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MdbxNativeProjectRecord>> ListProjectsAsync(bool includeDeleted, CancellationToken cancellationToken = default);
    Task<MdbxNativeEntryRecord> CreateEntryAsync(string projectId, string entryType, string title, string payloadJson, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MdbxNativeEntryRecord>> ListEntriesAsync(string projectId, string? entryType = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MdbxNativeEntryRecord>> ListDeletedEntriesAsync(string projectId, string? entryType = null, CancellationToken cancellationToken = default);
    Task<MdbxNativeEntryRecord> UpdateEntryAsync(string projectId, string entryId, string entryType, string title, string payloadJson, CancellationToken cancellationToken = default);
    Task DeleteEntryAsync(string projectId, string entryId, CancellationToken cancellationToken = default);
    Task<MdbxNativeEntryRecord> RestoreEntryAsync(string projectId, string entryId, CancellationToken cancellationToken = default);
}

public sealed record MdbxNativeVaultInfo(string VaultId, string DeviceId);

public sealed record MdbxNativeProjectRecord(
    string ProjectId,
    string Title,
    bool Deleted);

public sealed record MdbxNativeEntryRecord(
    string EntryId,
    string ProjectId,
    string EntryType,
    string Title,
    string PayloadJson,
    bool Deleted);
