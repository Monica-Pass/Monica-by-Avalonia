using Dapper;

namespace Monica.Data.Repositories;

public sealed partial class MonicaRepository
{
    public async Task<PasswordMetadataSearchResult> SearchPasswordMetadataAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new PasswordMetadataSearchResult([], []);
        }

        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        // QueryMultipleAsync cannot be intercepted by Dapper.AOT and falls back to
        // Reflection.Emit at runtime. Keep the two reads separate so each call gets
        // a generated row factory in native AOT builds.
        cancellationToken.ThrowIfCancellationRequested();
        var customFieldRows = (await connection.QueryAsync<CustomFieldRow>(
            """
            SELECT id, entry_id, title, value, is_protected, sort_order
            FROM custom_fields
            ORDER BY entry_id ASC
            """))
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        var attachmentRows = (await connection.QueryAsync<AttachmentRow>(
            """
            SELECT id, owner_type, owner_id, file_name, content_type, storage_path, size_bytes, created_at, bitwarden_vault_id, keepass_binary_ref
            FROM attachments
            WHERE owner_type = 'PASSWORD'
            ORDER BY owner_id ASC
            """))
            .ToArray();
        var term = query.Trim();

        return new PasswordMetadataSearchResult(
            customFieldRows
                .Select(ToModel)
                .Select(field => _vaultDataProtector.Unprotect(field))
                .Where(field =>
                    field.Title.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                    field.Value.Contains(term, StringComparison.CurrentCultureIgnoreCase))
                .Select(field => field.EntryId)
                .Distinct()
                .OrderBy(id => id)
                .ToList(),
            attachmentRows
                .Where(attachment => ContainsAttachmentMetadata(attachment, term))
                .Select(attachment => attachment.OwnerId)
                .Distinct()
                .OrderBy(id => id)
                .ToList());
    }
}
