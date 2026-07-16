using Dapper;

namespace Monica.Data.Repositories;

public sealed partial class MonicaRepository
{
    public async Task<IReadOnlyList<long>> GetAttachmentOwnerIdsAsync(
        string ownerType,
        CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        var ownerIds = await connection.QueryAsync<long>(
            """
            SELECT DISTINCT owner_id
            FROM attachments
            WHERE owner_type = @OwnerType
            ORDER BY owner_id ASC
            """,
            new { OwnerType = NormalizeOwnerType(ownerType) });

        return ownerIds.ToList();
    }

    public async Task<IReadOnlyList<long>> SearchAttachmentOwnerIdsAsync(
        string ownerType,
        string query,
        CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<AttachmentRow>(
            """
            SELECT id, owner_type, owner_id, file_name, content_type, storage_path, size_bytes, created_at, bitwarden_vault_id, keepass_binary_ref
            FROM attachments
            WHERE owner_type = @OwnerType
            ORDER BY owner_id ASC
            """,
            new { OwnerType = NormalizeOwnerType(ownerType) });

        var term = query.Trim();
        return rows
            .Where(attachment => ContainsAttachmentMetadata(attachment, term))
            .Select(attachment => attachment.OwnerId)
            .Distinct()
            .OrderBy(id => id)
            .ToList();
    }

    private static bool ContainsAttachmentMetadata(AttachmentRow attachment, string term) =>
        attachment.FileName.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
        attachment.ContentType.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
        attachment.StoragePath.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
        (attachment.KeepassBinaryRef?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false);
}
