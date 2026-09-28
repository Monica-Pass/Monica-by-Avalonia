namespace Monica.Data.Mdbx;

/// <summary>
/// Read-only view over a native vault handle. The gate decided this file may not be written, so every
/// mutation raises instead of reaching the engine — the alternative is that a desktop edit silently
/// restamps a vault another client authored under a format or extension set this build does not own.
/// </summary>
/// <remarks>
/// Deliberately not a partial-enforcement helper: reads forward untouched, including the attachment
/// content reads, so a restricted vault is still fully usable for viewing, copying and search.
/// </remarks>
public sealed class MdbxReadOnlyNativeVault(IMdbxNativeVault inner, MdbxNativeAccessDecision decision) : IMdbxNativeVault
{
    public bool IsReadOnly => true;

    public Task<MdbxNativeVaultInfo> GetInfoAsync(CancellationToken cancellationToken = default) =>
        inner.GetInfoAsync(cancellationToken);

    public Task<IReadOnlyList<MdbxNativeProjectRecord>> ListProjectsAsync(CancellationToken cancellationToken = default) =>
        inner.ListProjectsAsync(cancellationToken);

    public Task<IReadOnlyList<MdbxNativeEntryRecord>> ListEntriesAsync(
        string projectId,
        string? entryType = null,
        CancellationToken cancellationToken = default) =>
        inner.ListEntriesAsync(projectId, entryType, cancellationToken);

    public Task<IReadOnlyList<MdbxNativeEntryRecord>> ListDeletedEntriesAsync(
        string projectId,
        string? entryType = null,
        CancellationToken cancellationToken = default) =>
        inner.ListDeletedEntriesAsync(projectId, entryType, cancellationToken);

    public Task<IReadOnlyList<MdbxNativeAttachmentRecord>> ListAttachmentsAsync(
        string projectId,
        string? entryId,
        CancellationToken cancellationToken = default) =>
        inner.ListAttachmentsAsync(projectId, entryId, cancellationToken);

    public Task<byte[]> ReadAttachmentContentAsync(string attachmentId, CancellationToken cancellationToken = default) =>
        inner.ReadAttachmentContentAsync(attachmentId, cancellationToken);

    public Task<MdbxNativeProjectRecord> CreateProjectAsync(string title, CancellationToken cancellationToken = default) =>
        Blocked<MdbxNativeProjectRecord>();

    public Task<MdbxNativeProjectRecord> CreateProjectWithIdentityAsync(
        string projectId,
        string title,
        string? parentProjectId,
        CancellationToken cancellationToken = default) =>
        Blocked<MdbxNativeProjectRecord>();

    public Task<MdbxNativeEntryRecord> CreateEntryAsync(
        string projectId,
        string entryType,
        string title,
        string payloadJson,
        CancellationToken cancellationToken = default) =>
        Blocked<MdbxNativeEntryRecord>();

    public Task<MdbxNativeEntryRecord> UpdateEntryAsync(
        string projectId,
        string entryId,
        string entryType,
        string title,
        string payloadJson,
        CancellationToken cancellationToken = default) =>
        Blocked<MdbxNativeEntryRecord>();

    public Task<MdbxNativeEntryRecord> MoveEntryAsync(
        string projectId,
        string entryId,
        string targetProjectId,
        CancellationToken cancellationToken = default) =>
        Blocked<MdbxNativeEntryRecord>();

    public Task<MdbxNativeEntryRecord> RestoreEntryAsync(
        string projectId,
        string entryId,
        CancellationToken cancellationToken = default) =>
        Blocked<MdbxNativeEntryRecord>();

    public Task DeleteEntryAsync(string projectId, string entryId, CancellationToken cancellationToken = default) =>
        Blocked();

    public Task<MdbxNativeAttachmentRecord> CreateAttachmentAsync(
        string projectId,
        string? entryId,
        string fileName,
        string? mediaType,
        byte[] content,
        CancellationToken cancellationToken = default) =>
        Blocked<MdbxNativeAttachmentRecord>();

    public Task DeleteAttachmentAsync(string attachmentId, CancellationToken cancellationToken = default) =>
        Blocked();

    public void Dispose() => inner.Dispose();

    private Task Blocked() => Task.FromException(Reject());

    private Task<T> Blocked<T>() => Task.FromException<T>(Reject());

    private MdbxVaultReadOnlyException Reject() =>
        new(decision.ReasonCode ?? "unspecified", decision.Detail);
}
