using System.Runtime.InteropServices;
using Monica.Core.Models;
using Monica.Data.Mdbx;
using Monica.Mdbx.Ffi;
using CoreTigaMode = Monica.Core.Models.MdbxTigaMode;
using FfiTigaMode = Monica.Mdbx.Ffi.MdbxTigaMode;

namespace Monica.Platform.Services;

// Typed wrapper over the generated UniFFI surface. The bindings in Mdbx/Generated and the
// native library must always come from the same mdbx-ffi revision as the one the Android
// main repo ships (see Monica for Android/mdbx-engine/MDBX3_RUNTIME_PROVENANCE.json);
// regenerate both with eng/mdbx/generate-csharp-bindings.ps1.
public sealed class MdbxUniffiNativeBridge : IMdbxNativeBridge
{
    // Android pages collection summaries at 200 per request.
    private const uint CollectionPageSize = 200;

    // Android falls back to this when an attachment carries no declared mime type.
    private const string DefaultMediaType = "application/octet-stream";

    private static readonly Lazy<MdbxRuntimeManifest?> Manifest = new(() =>
    {
        try
        {
            // Touching the bindings validates the scaffolding contract version and every
            // method checksum, so an ABI skew is reported here rather than on first use.
            return MdbxFfi.MdbxRuntimeManifest();
        }
        catch (Exception ex) when (ex is UniffiException or DllNotFoundException or SEHException
                                   or EntryPointNotFoundException or BadImageFormatException
                                   or TypeInitializationException)
        {
            return null;
        }
    });

    public bool IsAvailable => Manifest.Value is not null;

    public string WritableStorageFormat => Manifest.Value?.WritableStorageFormat ?? "";

    public Task<IMdbxNativeVault> CreateVaultAsync(
        string path,
        string password,
        string deviceId,
        CoreTigaMode mode,
        CancellationToken cancellationToken = default) =>
        RunBlockingNativeAsync<IMdbxNativeVault>(
            () => new MdbxUniffiNativeVault(MdbxFfi.CreateVaultWithTigaMode(path, password, deviceId, ToFfiMode(mode))),
            cancellationToken);

    public Task<IMdbxNativeVault> OpenVaultAsync(
        string path,
        string password,
        string deviceId,
        CancellationToken cancellationToken = default) =>
        RunBlockingNativeAsync<IMdbxNativeVault>(
            () => new MdbxUniffiNativeVault(MdbxFfi.OpenVault(path, password, deviceId)),
            cancellationToken);

    // Monica.Core declares Power, Multi, Sky; the native contract declares Sky, Multi, Power.
    private static FfiTigaMode ToFfiMode(CoreTigaMode mode) => mode switch
    {
        CoreTigaMode.Sky => FfiTigaMode.Sky,
        CoreTigaMode.Multi => FfiTigaMode.Multi,
        CoreTigaMode.Power => FfiTigaMode.Power,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown Tiga mode."),
    };

    private static Task<T> RunBlockingNativeAsync<T>(Func<T> operation, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Translate(operation);
        }, cancellationToken);

    private static Task RunBlockingNativeAsync(Action operation, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Translate(operation);
        }, cancellationToken);

    // Callers above the abstraction only ever see InvalidOperationException, so a missing
    // attachment and a contract violation both surface as an ordinary failure.
    private static T Translate<T>(Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (UniffiException ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    private static void Translate(Action operation)
    {
        try
        {
            operation();
        }
        catch (UniffiException ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    private sealed class MdbxUniffiNativeVault(MdbxVault vault) : IMdbxNativeVault, IDisposable
    {
        public Task<MdbxNativeVaultInfo> GetInfoAsync(CancellationToken cancellationToken = default) =>
            RunBlockingNativeAsync(() =>
            {
                var info = vault.Info();
                return new MdbxNativeVaultInfo(info.VaultId, info.DeviceId);
            }, cancellationToken);

        public Task<MdbxNativeProjectRecord> CreateProjectAsync(string title, CancellationToken cancellationToken = default) =>
            RunBlockingNativeAsync(() => ToProject(vault.CreateProject(title)), cancellationToken);

        public Task<IReadOnlyList<MdbxNativeProjectRecord>> ListProjectsAsync(CancellationToken cancellationToken = default) =>
            RunBlockingNativeAsync<IReadOnlyList<MdbxNativeProjectRecord>>(
                () =>
                {
                    var projects = new List<MdbxNativeProjectRecord>();
                    string? cursor = null;
                    do
                    {
                        var page = vault.ListCollectionSummaries(CollectionPageSize, cursor);
                        projects.AddRange(page.Items.Where(summary => !summary.Deleted).Select(ToProject));
                        cursor = page.NextCursor;
                    }
                    while (cursor is not null);

                    return projects;
                },
                cancellationToken);

        public Task<MdbxNativeEntryRecord> CreateEntryAsync(
            string projectId,
            string entryType,
            string title,
            string payloadJson,
            CancellationToken cancellationToken = default) =>
            RunBlockingNativeAsync(
                () => ToEntry(vault.CreateEntry(projectId, entryType, title, payloadJson)),
                cancellationToken);

        public Task<IReadOnlyList<MdbxNativeEntryRecord>> ListEntriesAsync(
            string projectId,
            string? entryType = null,
            CancellationToken cancellationToken = default) =>
            RunBlockingNativeAsync<IReadOnlyList<MdbxNativeEntryRecord>>(
                () => vault.ListEntries(projectId, entryType).Select(ToEntry).ToList(),
                cancellationToken);

        public Task<IReadOnlyList<MdbxNativeEntryRecord>> ListDeletedEntriesAsync(
            string projectId,
            string? entryType = null,
            CancellationToken cancellationToken = default) =>
            RunBlockingNativeAsync<IReadOnlyList<MdbxNativeEntryRecord>>(
                () => vault.ListDeletedEntries(projectId, entryType).Select(ToEntry).ToList(),
                cancellationToken);

        public Task<MdbxNativeEntryRecord> UpdateEntryAsync(
            string projectId,
            string entryId,
            string entryType,
            string title,
            string payloadJson,
            CancellationToken cancellationToken = default) =>
            RunBlockingNativeAsync(
                () => ToEntry(vault.UpdateEntry(projectId, entryId, entryType, title, payloadJson)),
                cancellationToken);

        public Task<MdbxNativeEntryRecord> MoveEntryAsync(
            string projectId,
            string entryId,
            string targetProjectId,
            CancellationToken cancellationToken = default) =>
            RunBlockingNativeAsync(
                () => ToEntry(vault.MoveEntry(projectId, entryId, targetProjectId)),
                cancellationToken);

        public Task DeleteEntryAsync(
            string projectId,
            string entryId,
            CancellationToken cancellationToken = default) =>
            RunBlockingNativeAsync(() => vault.DeleteEntry(projectId, entryId), cancellationToken);

        public Task<MdbxNativeEntryRecord> RestoreEntryAsync(
            string projectId,
            string entryId,
            CancellationToken cancellationToken = default) =>
            RunBlockingNativeAsync(
                () => ToEntry(vault.RestoreEntry(projectId, entryId)),
                cancellationToken);

        public Task<MdbxNativeAttachmentRecord> CreateAttachmentAsync(
            string projectId,
            string? entryId,
            string fileName,
            string? mediaType,
            byte[] content,
            CancellationToken cancellationToken = default) =>
            RunBlockingNativeAsync(
                () => ToAttachment(vault.CreateAttachmentWithContent(
                    Guid.NewGuid().ToString(),
                    new MdbxAttachmentCreateRequest(
                        AttachmentId: Guid.NewGuid().ToString(),
                        ProjectId: projectId,
                        EntryId: entryId,
                        FileName: fileName,
                        MediaType: string.IsNullOrWhiteSpace(mediaType) ? DefaultMediaType : mediaType),
                    content,
                    MdbxFfi.DefaultAttachmentContentLimits()).Attachment),
                cancellationToken);

        public Task<IReadOnlyList<MdbxNativeAttachmentRecord>> ListAttachmentsAsync(
            string projectId,
            string? entryId,
            CancellationToken cancellationToken = default) =>
            RunBlockingNativeAsync<IReadOnlyList<MdbxNativeAttachmentRecord>>(
                () => vault.ListAttachments(projectId, entryId).Select(ToAttachment).ToList(),
                cancellationToken);

        public Task<byte[]> ReadAttachmentContentAsync(
            string attachmentId,
            CancellationToken cancellationToken = default) =>
            RunBlockingNativeAsync(
                () => vault.ReadAttachmentContent(attachmentId, MdbxFfi.DefaultAttachmentContentLimits().MaxPlaintextBytes),
                cancellationToken);

        public Task DeleteAttachmentAsync(string attachmentId, CancellationToken cancellationToken = default) =>
            RunBlockingNativeAsync(() => vault.DeleteAttachment(attachmentId), cancellationToken);

        public void Dispose() => vault.Dispose();

        private static MdbxNativeProjectRecord ToProject(ProjectRecord project) =>
            new(project.ProjectId, project.Title);

        private static MdbxNativeProjectRecord ToProject(MdbxCollectionSummary summary) =>
            new(summary.CollectionId, summary.Title);

        private static MdbxNativeEntryRecord ToEntry(EntryRecord entry) =>
            new(entry.EntryId, entry.ProjectId, entry.EntryType, entry.Title, entry.PayloadJson, entry.Deleted);

        private static MdbxNativeAttachmentRecord ToAttachment(MdbxAttachmentRecord attachment) =>
            new(
                attachment.AttachmentId,
                attachment.ProjectId,
                attachment.EntryId,
                attachment.FileName,
                attachment.MediaType,
                attachment.StorageMode,
                attachment.ContentHash,
                attachment.OriginalSize,
                attachment.StoredSize,
                attachment.ChunkCount,
                attachment.Deleted);
    }
}
