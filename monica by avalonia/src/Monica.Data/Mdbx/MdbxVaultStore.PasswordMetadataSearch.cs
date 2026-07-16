using Monica.Core.Models;
using Monica.Data.Repositories;

namespace Monica.Data.Mdbx;

public sealed partial class MdbxVaultStore
{
    public async Task<PasswordMetadataSearchResult> SearchPasswordMetadataAsync(
        LocalMdbxDatabase database,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new PasswordMetadataSearchResult([], []);
        }

        var term = query.Trim();
        var vault = await OpenAsync(database, cancellationToken);
        using var _ = vault;
        var projects = await EnsureProjectsForReadAsync(vault, cancellationToken);
        var records = await ListPasswordRecordsAsync(vault, projects, includeDeleted: true, cancellationToken);
        var customFieldMatches = new HashSet<long>();
        var attachmentMatches = new HashSet<long>();

        foreach (var record in records.Where(record => !record.Deleted))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payload = DeserializePasswordPayload(record.PayloadJson, record.Title);
            if (payload is null || payload.Entry.Id <= 0)
            {
                continue;
            }

            var entryId = payload.Entry.Id;
            if (payload.CustomFields?.Any(field =>
                    field.Title.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                    field.Value.Contains(term, StringComparison.CurrentCultureIgnoreCase)) == true)
            {
                customFieldMatches.Add(entryId);
            }

            if (payload.Attachments?.Any(attachment =>
                    attachment.FileName.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                    attachment.ContentType.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                    attachment.StoragePath.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                    (attachment.KeepassBinaryRef?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false)) == true)
            {
                attachmentMatches.Add(entryId);
            }
        }

        return new PasswordMetadataSearchResult(
            customFieldMatches.OrderBy(id => id).ToList(),
            attachmentMatches.OrderBy(id => id).ToList());
    }
}
