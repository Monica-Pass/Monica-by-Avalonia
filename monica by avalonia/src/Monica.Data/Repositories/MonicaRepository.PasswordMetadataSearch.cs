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
        var command = new CommandDefinition(
            """
            SELECT id, entry_id, title, value, is_protected, sort_order
            FROM custom_fields
            ORDER BY entry_id ASC;

            SELECT id, owner_type, owner_id, file_name, content_type, storage_path, size_bytes, created_at, bitwarden_vault_id, keepass_binary_ref
            FROM attachments
            WHERE owner_type = 'PASSWORD'
            ORDER BY owner_id ASC;
            """,
            cancellationToken: cancellationToken);
        using var results = await connection.QueryMultipleAsync(command);
        var customFieldRows = (await results.ReadAsync<CustomFieldRow>()).ToArray();
        var attachmentRows = (await results.ReadAsync<AttachmentRow>()).ToArray();
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
