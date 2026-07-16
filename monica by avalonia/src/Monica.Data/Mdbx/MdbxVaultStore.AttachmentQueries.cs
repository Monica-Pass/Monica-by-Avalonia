using Monica.Core.Models;

namespace Monica.Data.Mdbx;

public sealed partial class MdbxVaultStore
{
    public async Task<IReadOnlyList<long>> SearchPasswordEntryIdsByAttachmentMetadataAsync(
        LocalMdbxDatabase database,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var term = query.Trim();
        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var projects = await EnsureProjectsForReadAsync(vault, cancellationToken);
        var records = await ListPasswordRecordsAsync(vault, projects, includeDeleted: true, cancellationToken);

        return records
            .Where(record => !record.Deleted)
            .Select(record => DeserializePasswordPayload(record.PayloadJson, record.Title))
            .Where(payload => payload?.Attachments is not null)
            .Select(payload => payload!)
            .Where(payload => payload.Attachments!.Any(attachment =>
                attachment.FileName.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                attachment.ContentType.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                attachment.StoragePath.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                (attachment.KeepassBinaryRef?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false)))
            .Select(payload => payload.Entry.Id)
            .Where(id => id > 0)
            .Distinct()
            .OrderBy(id => id)
            .ToList();
    }
}
