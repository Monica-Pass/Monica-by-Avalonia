namespace Monica.Data.Repositories;

public sealed partial class MdbxBackedMonicaRepository
{
    public async Task<IReadOnlyList<long>> GetAttachmentOwnerIdsAsync(
        string ownerType,
        CancellationToken cancellationToken = default)
    {
        if (!IsPasswordOwnerType(ownerType) && !IsSecureItemOwnerType(ownerType))
        {
            return [];
        }

        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            return [];
        }

        if (IsSecureItemOwnerType(ownerType))
        {
            var items = await GetSecureItemsAsync(includeDeleted: false, cancellationToken: cancellationToken);
            var attachments = await mdbxVaultStore.GetSecureItemAttachmentsByItemIdsAsync(
                database,
                items.Select(item => item.Id).ToArray(),
                cancellationToken);
            return attachments.Keys.OrderBy(id => id).ToList();
        }

        var categories = await EnsureMdbxCategoriesAsync(database, cancellationToken);
        var snapshot = await GetPasswordReadSnapshotAsync(database, categories, cancellationToken);
        var activeIds = snapshot.Passwords
            .Where(entry => !entry.IsDeleted)
            .Select(entry => entry.Id)
            .ToHashSet();
        return snapshot.AttachmentsByEntryId
            .Where(pair => pair.Value.Count > 0 && activeIds.Contains(pair.Key))
            .Select(pair => pair.Key)
            .OrderBy(id => id)
            .ToList();
    }

    public async Task<IReadOnlyList<long>> SearchAttachmentOwnerIdsAsync(
        string ownerType,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) ||
            (!IsPasswordOwnerType(ownerType) && !IsSecureItemOwnerType(ownerType)))
        {
            return [];
        }

        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        if (database is null)
        {
            return [];
        }

        if (IsPasswordOwnerType(ownerType))
        {
            return await mdbxVaultStore.SearchPasswordEntryIdsByAttachmentMetadataAsync(
                database,
                query,
                cancellationToken);
        }

        var ownerIds = await GetAttachmentOwnerIdsAsync(ownerType, cancellationToken);
        var attachments = await GetAttachmentsByOwnerIdsAsync(ownerType, ownerIds, cancellationToken);
        var term = query.Trim();
        return attachments
            .Where(pair => pair.Value.Any(attachment =>
                attachment.FileName.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                attachment.ContentType.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                attachment.StoragePath.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                (attachment.KeepassBinaryRef?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false)))
            .Select(pair => pair.Key)
            .OrderBy(id => id)
            .ToList();
    }
}
