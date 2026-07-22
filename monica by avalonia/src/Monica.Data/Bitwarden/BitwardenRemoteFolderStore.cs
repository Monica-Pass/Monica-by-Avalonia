using System.Text;
using Dapper;
using Monica.Core.Bitwarden;
using Monica.Core.Services;

namespace Monica.Data.Bitwarden;

public interface IBitwardenRemoteFolderStore
{
    Task<IReadOnlyList<BitwardenStoredRemoteFolder>> GetAsync(
        long vaultId,
        CancellationToken cancellationToken = default);

    Task ReplaceCompleteSnapshotAsync(
        long vaultId,
        IReadOnlyList<BitwardenRemoteFolder> folders,
        DateTimeOffset seenAt,
        CancellationToken cancellationToken = default);

    Task BindLocalCategoryAsync(
        long vaultId,
        string remoteFolderId,
        long? localCategoryId,
        CancellationToken cancellationToken = default);
}

public sealed class BitwardenRemoteFolderStore(
    ISqliteConnectionFactory connectionFactory,
    IDatabaseMigrator migrator,
    ICryptoService cryptoService) : IBitwardenRemoteFolderStore
{
    private readonly BitwardenAccountSecretProtector _protector = new(cryptoService);

    public async Task<IReadOnlyList<BitwardenStoredRemoteFolder>> GetAsync(
        long vaultId,
        CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<RemoteFolderRow>(new CommandDefinition(
            """
            SELECT bitwarden_vault_id AS VaultId,
                   remote_folder_id AS RemoteFolderId,
                   encrypted_name AS EncryptedName,
                   parent_remote_folder_id AS ParentRemoteFolderId,
                   local_category_id AS LocalCategoryId,
                   is_deleted AS IsDeleted,
                   last_seen_at AS LastSeenAt
            FROM bitwarden_remote_folders
            WHERE bitwarden_vault_id = @VaultId
            ORDER BY is_deleted ASC, parent_remote_folder_id ASC, remote_folder_id ASC
            """,
            new { VaultId = vaultId },
            cancellationToken: cancellationToken));

        return rows.Select(row => new BitwardenStoredRemoteFolder(
            row.VaultId,
            row.RemoteFolderId,
            _protector.UnprotectString(row.EncryptedName),
            row.ParentRemoteFolderId,
            row.LocalCategoryId,
            row.IsDeleted,
            DateTimeOffset.FromUnixTimeMilliseconds(row.LastSeenAt))).ToList();
    }

    public async Task ReplaceCompleteSnapshotAsync(
        long vaultId,
        IReadOnlyList<BitwardenRemoteFolder> folders,
        DateTimeOffset seenAt,
        CancellationToken cancellationToken = default)
    {
        Validate(vaultId, folders);
        await migrator.MigrateAsync(cancellationToken);
        var marker = Guid.NewGuid().ToString("N");
        var seenAtUnix = seenAt.ToUniversalTime().ToUnixTimeMilliseconds();
        var protectedFolders = folders.Select(folder => new ProtectedFolder(
            folder,
            _protector.ProtectString(folder.Name))).ToList();

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var folder in protectedFolders)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO bitwarden_remote_folders (
                    bitwarden_vault_id, remote_folder_id, encrypted_name,
                    parent_remote_folder_id, is_deleted, last_seen_at, sync_marker)
                VALUES (
                    @VaultId, @RemoteFolderId, @EncryptedName,
                    @ParentRemoteFolderId, @IsDeleted, @LastSeenAt, @SyncMarker)
                ON CONFLICT(bitwarden_vault_id, remote_folder_id) DO UPDATE SET
                    encrypted_name = excluded.encrypted_name,
                    parent_remote_folder_id = excluded.parent_remote_folder_id,
                    is_deleted = excluded.is_deleted,
                    last_seen_at = excluded.last_seen_at,
                    sync_marker = excluded.sync_marker
                """,
                new
                {
                    VaultId = vaultId,
                    RemoteFolderId = folder.Folder.Id,
                    EncryptedName = folder.EncryptedName,
                    ParentRemoteFolderId = folder.Folder.ParentId,
                    IsDeleted = folder.Folder.IsDeleted,
                    LastSeenAt = seenAtUnix,
                    SyncMarker = marker
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE bitwarden_remote_folders
            SET is_deleted = 1, last_seen_at = @LastSeenAt, sync_marker = @SyncMarker
            WHERE bitwarden_vault_id = @VaultId AND sync_marker <> @SyncMarker
            """,
            new { VaultId = vaultId, LastSeenAt = seenAtUnix, SyncMarker = marker },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task BindLocalCategoryAsync(
        long vaultId,
        string remoteFolderId,
        long? localCategoryId,
        CancellationToken cancellationToken = default)
    {
        if (vaultId <= 0 || string.IsNullOrWhiteSpace(remoteFolderId))
        {
            throw new ArgumentException("A Bitwarden vault and remote folder identity are required.");
        }

        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE bitwarden_remote_folders SET local_category_id = @LocalCategoryId WHERE bitwarden_vault_id = @VaultId AND remote_folder_id = @RemoteFolderId",
            new { VaultId = vaultId, RemoteFolderId = remoteFolderId, LocalCategoryId = localCategoryId },
            cancellationToken: cancellationToken));
        if (updated == 0)
        {
            throw new KeyNotFoundException("The Bitwarden remote folder does not exist.");
        }
    }

    private static void Validate(long vaultId, IReadOnlyList<BitwardenRemoteFolder> folders)
    {
        if (vaultId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(vaultId));
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var parentById = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder.Id) || folder.Id.Length > 256 || !ids.Add(folder.Id))
            {
                throw new BitwardenProtocolException("Bitwarden remote folder identities must be unique and bounded.");
            }

            if (string.IsNullOrWhiteSpace(folder.Name) || Encoding.UTF8.GetByteCount(folder.Name) > 4096)
            {
                throw new BitwardenProtocolException("Bitwarden remote folder names must be non-empty and bounded.");
            }

            if (folder.ParentId is { Length: > 256 })
            {
                throw new BitwardenProtocolException("Bitwarden remote folder parent identities are too long.");
            }

            parentById.Add(folder.Id, folder.ParentId);
        }

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in parentById.Keys)
        {
            Visit(id, parentById, visiting, visited);
        }
    }

    private static void Visit(
        string id,
        IReadOnlyDictionary<string, string?> parentById,
        ISet<string> visiting,
        ISet<string> visited)
    {
        if (visited.Contains(id))
        {
            return;
        }

        if (!visiting.Add(id))
        {
            throw new BitwardenProtocolException("Bitwarden remote folder hierarchy contains a cycle.");
        }

        if (parentById.TryGetValue(id, out var parent) && parent is not null && parentById.ContainsKey(parent))
        {
            Visit(parent, parentById, visiting, visited);
        }

        visiting.Remove(id);
        visited.Add(id);
    }

    private sealed record ProtectedFolder(BitwardenRemoteFolder Folder, string EncryptedName);

    private sealed class RemoteFolderRow
    {
        public long VaultId { get; init; }
        public string RemoteFolderId { get; init; } = "";
        public string EncryptedName { get; init; } = "";
        public string? ParentRemoteFolderId { get; init; }
        public long? LocalCategoryId { get; init; }
        public bool IsDeleted { get; init; }
        public long LastSeenAt { get; init; }
    }
}
