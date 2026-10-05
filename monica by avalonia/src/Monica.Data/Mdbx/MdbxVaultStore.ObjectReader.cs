using System.Text;
using Monica.Core.Models;

namespace Monica.Data.Mdbx;

/// <summary>Transient details obtained only after the native engine authorizes disclosure.</summary>
public sealed record MdbxUnknownEntryDetail(MdbxUnknownEntryDescriptor Descriptor, string PayloadJson)
{
    public override string ToString() => "MdbxUnknownEntryDetail(redacted)";
}

public sealed class MdbxObjectDisclosureException(string outcome) : InvalidOperationException("The native object could not be disclosed.")
{
    public string Outcome { get; } = outcome;
}

public sealed partial class MdbxVaultStore
{
    private const ulong MaximumObjectPayloadBytes = 4UL * 1024 * 1024;
    private const uint SupportedObjectPayloadVersion = MdbxObjectReadPolicy.SupportedPayloadVersion;

    public async Task<IReadOnlyList<MdbxUnknownEntryDescriptor>> GetUnknownEntriesAsync(
        LocalMdbxDatabase database,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        using var vault = await OpenAsync(database, cancellationToken);
        var reader = RequireObjectReader(vault);
        // Browsing metadata must not materialize the Android root or decrypt any payload.
        var projects = await vault.ListProjectsAsync(cancellationToken);
        var entries = new List<MdbxUnknownEntryDescriptor>();
        foreach (var project in projects)
        {
            var summaries = await reader.ListObjectSummariesAsync(project.ProjectId, includeDeleted, cancellationToken);
            entries.AddRange(summaries
                .Where(summary => (includeDeleted || !summary.Deleted) && !HasSupportedPayload(summary))
                .Select(ToUnknownEntryDescriptor));
        }

        return entries.OrderBy(entry => entry.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(entry => entry.EntryId, StringComparer.Ordinal).ToArray();
    }

    public async Task<MdbxUnknownEntryDetail?> ReadUnknownEntryAsync(
        LocalMdbxDatabase database,
        string entryId,
        string projectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        using var vault = await OpenAsync(database, cancellationToken);
        var reader = RequireObjectReader(vault);
        var summary = (await reader.ListObjectSummariesAsync(projectId, false, cancellationToken))
            .FirstOrDefault(item => item.EntryId == entryId && item.ProjectId == projectId && !item.Deleted);
        if (summary is null || HasSupportedPayload(summary))
        {
            return null;
        }

        var disclosure = await reader.RevealObjectAsync(entryId, MaximumObjectPayloadBytes, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureMatchesSummary(summary, disclosure.Summary);
        var payload = RequireDisclosedPayload(disclosure);
        return new MdbxUnknownEntryDetail(ToUnknownEntryDescriptor(summary), payload);
    }

    private static IMdbxNativeObjectReader RequireObjectReader(IMdbxNativeVault vault) =>
        vault is IMdbxNativeObjectReader { SupportsObjectDisclosure: true } reader
            ? reader
            : throw new NotSupportedException("This runtime does not support native object summaries and authorized disclosure.");

    private static bool HasSupportedPayload(MdbxNativeObjectSummary summary) =>
        MdbxObjectReadPolicy.Supports(summary.EntryType, summary.PayloadSchemaVersion);

    private static void RequireSupportedRecord(MdbxNativeEntryRecord record, IReadOnlyList<string>? entryTypes = null)
    {
        if (record.PayloadSchemaVersion != SupportedObjectPayloadVersion ||
            !(entryTypes ?? PasswordEntryTypes.Concat(SecureEntryTypes).ToArray()).Contains(record.EntryType, StringComparer.Ordinal))
        {
            throw new MdbxVaultReadOnlyException("object-type-or-version", "Use the read-only native object inspector.");
        }
    }

    private static MdbxUnknownEntryDescriptor ToUnknownEntryDescriptor(MdbxNativeObjectSummary summary) =>
        new(summary.EntryId, summary.ProjectId, summary.EntryType, summary.Title, summary.Deleted,
            summary.PayloadSchemaVersion, summary.HeadCommitId, summary.UpdatedAt);

    private static void EnsureMatchesSummary(MdbxNativeObjectSummary expected, MdbxNativeObjectSummary? actual)
    {
        if (actual is null || expected != actual)
        {
            throw new MdbxObjectDisclosureException("changed-or-unavailable");
        }
    }

    private static string RequireDisclosedPayload(MdbxNativeObjectDisclosure disclosure)
    {
        if (disclosure.AuthorizationOutcome is not ("Allow" or "AllowWithConstraints") || disclosure.PayloadJson is null)
        {
            throw new MdbxObjectDisclosureException(disclosure.AuthorizationOutcome);
        }

        if ((ulong)Encoding.UTF8.GetByteCount(disclosure.PayloadJson) > MaximumObjectPayloadBytes)
        {
            throw new MdbxObjectDisclosureException("payload-too-large");
        }

        return disclosure.PayloadJson;
    }

    private static async Task<MdbxNativeEntryRecord> ReadSupportedRecordAsync(
        IMdbxNativeObjectReader reader,
        MdbxNativeObjectSummary summary,
        CancellationToken cancellationToken)
    {
        if (!HasSupportedPayload(summary))
        {
            throw new MdbxVaultReadOnlyException("object-type-or-version", "Use the read-only native object inspector.");
        }

        var record = await reader.ReadSupportedObjectAsync(summary, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (record is null || record.EntryId != summary.EntryId || record.ProjectId != summary.ProjectId ||
            record.EntryType != summary.EntryType || record.PayloadSchemaVersion != summary.PayloadSchemaVersion ||
            record.Title != summary.Title || record.Deleted != summary.Deleted)
        {
            throw new MdbxObjectDisclosureException("changed-or-unavailable");
        }

        return record;
    }
}
