using Dapper;
using Monica.Core.Models;

namespace Monica.Data.Repositories;

public interface IMonicaRepository
{
    Task<IReadOnlyList<PasswordEntry>> GetPasswordsAsync(bool includeDeleted = false, bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<long> SavePasswordAsync(PasswordEntry entry, CancellationToken cancellationToken = default);
    Task SoftDeletePasswordAsync(long id, CancellationToken cancellationToken = default);
    Task RestorePasswordAsync(long id, CancellationToken cancellationToken = default);
    Task DeletePasswordPermanentlyAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomField>> GetCustomFieldsAsync(long entryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<long, IReadOnlyList<CustomField>>> GetCustomFieldsByEntryIdsAsync(IReadOnlyList<long> entryIds, CancellationToken cancellationToken = default);
    Task ReplaceCustomFieldsAsync(long entryId, IReadOnlyList<CustomField> fields, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<long>> SearchEntryIdsByCustomFieldContentAsync(string query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Attachment>> GetAttachmentsAsync(string ownerType, long ownerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<long, IReadOnlyList<Attachment>>> GetAttachmentsByOwnerIdsAsync(string ownerType, IReadOnlyList<long> ownerIds, CancellationToken cancellationToken = default);
    Task<long> SaveAttachmentAsync(Attachment attachment, CancellationToken cancellationToken = default);
    Task DeleteAttachmentAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PasswordHistoryEntry>> GetPasswordHistoryAsync(long entryId, CancellationToken cancellationToken = default);
    Task<long> SavePasswordHistoryAsync(PasswordHistoryEntry entry, CancellationToken cancellationToken = default);
    Task TrimPasswordHistoryAsync(long entryId, int limit, CancellationToken cancellationToken = default);
    Task DeletePasswordHistoryAsync(long id, CancellationToken cancellationToken = default);
    Task ClearPasswordHistoryAsync(long entryId, CancellationToken cancellationToken = default);
    Task RecordPasswordQuickAccessAsync(long passwordId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PasswordQuickAccessRecord>> GetPasswordQuickAccessRecordsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SecureItem>> GetSecureItemsAsync(VaultItemType? itemType = null, bool includeDeleted = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SecureItem>> GetSecureItemsByBoundPasswordIdAsync(long passwordId, bool includeDeleted = false, CancellationToken cancellationToken = default);
    Task<long> SaveSecureItemAsync(SecureItem item, CancellationToken cancellationToken = default);
    Task SoftDeleteSecureItemAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<long> SaveCategoryAsync(Category category, CancellationToken cancellationToken = default);
    Task DeleteCategoryAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalMdbxDatabase>> GetMdbxDatabasesAsync(CancellationToken cancellationToken = default);
    Task<long> SaveMdbxDatabaseAsync(LocalMdbxDatabase database, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OperationLog>> GetOperationLogsAsync(int limit = 100, string? itemType = null, CancellationToken cancellationToken = default);
    Task LogAsync(OperationLog log, CancellationToken cancellationToken = default);
    Task ClearVaultDataAsync(VaultClearScope scope, CancellationToken cancellationToken = default);
}

public sealed class MonicaRepository(
    ISqliteConnectionFactory connectionFactory,
    IDatabaseMigrator migrator,
    IVaultDataProtector? vaultDataProtector = null) : IMonicaRepository
{
    private readonly IVaultDataProtector _vaultDataProtector = vaultDataProtector ?? NoopVaultDataProtector.Instance;

    public async Task<IReadOnlyList<PasswordEntry>> GetPasswordsAsync(bool includeDeleted = false, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<PasswordEntryRow>(
            """
            SELECT * FROM password_entries
            WHERE (@IncludeDeleted = 1 OR is_deleted = 0)
              AND (@IncludeArchived = 1 OR is_archived = 0)
            ORDER BY is_favorite DESC, sort_order ASC, updated_at DESC
            """,
            new { IncludeDeleted = includeDeleted ? 1 : 0, IncludeArchived = includeArchived ? 1 : 0 });

        return rows.Select(ToModel).Select(entry => _vaultDataProtector.Unprotect(entry)).ToList();
    }

    public async Task<long> SavePasswordAsync(PasswordEntry entry, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        entry.UpdatedAt = DateTimeOffset.UtcNow;
        if (entry.CreatedAt == default)
        {
            entry.CreatedAt = entry.UpdatedAt;
        }

        await using var connection = connectionFactory.CreateConnection();
        if (entry.Id == 0)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO password_entries (
                    title, website, username, password, notes, created_at, updated_at, is_favorite, sort_order, is_group_cover,
                    app_package_name, app_name, email, phone, address_line, city, state, zip_code, country,
                    credit_card_number, credit_card_holder, credit_card_expiry, credit_card_cvv, category_id, bound_note_id,
                    keepass_database_id, keepass_group_path, keepass_entry_uuid, keepass_group_uuid, mdbx_database_id, mdbx_folder_id,
                    authenticator_key, passkey_bindings, ssh_key_data, login_type, sso_provider, sso_ref_entry_id, wifi_metadata,
                    custom_icon_type, custom_icon_value, custom_icon_updated_at, is_deleted, deleted_at, is_archived, archived_at,
                    replica_group_id, bitwarden_vault_id, bitwarden_cipher_id, bitwarden_folder_id, bitwarden_revision_date,
                    bitwarden_cipher_type, bitwarden_local_modified)
                VALUES (
                    @Title, @Website, @Username, @Password, @Notes, @CreatedAt, @UpdatedAt, @IsFavorite, @SortOrder, @IsGroupCover,
                    @AppPackageName, @AppName, @Email, @Phone, @AddressLine, @City, @State, @ZipCode, @Country,
                    @CreditCardNumber, @CreditCardHolder, @CreditCardExpiry, @CreditCardCvv, @CategoryId, @BoundNoteId,
                    @KeepassDatabaseId, @KeepassGroupPath, @KeepassEntryUuid, @KeepassGroupUuid, @MdbxDatabaseId, @MdbxFolderId,
                    @AuthenticatorKey, @PasskeyBindings, @SshKeyData, @LoginType, @SsoProvider, @SsoRefEntryId, @WifiMetadata,
                    @CustomIconType, @CustomIconValue, @CustomIconUpdatedAt, @IsDeleted, @DeletedAt, @IsArchived, @ArchivedAt,
                    @ReplicaGroupId, @BitwardenVaultId, @BitwardenCipherId, @BitwardenFolderId, @BitwardenRevisionDate,
                    @BitwardenCipherType, @BitwardenLocalModified);
                """,
                ToRow(_vaultDataProtector.Protect(entry)));
            entry.Id = await connection.ExecuteScalarAsync<long>("SELECT last_insert_rowid();");
        }
        else
        {
            await connection.ExecuteAsync(
                """
                UPDATE password_entries SET
                    title=@Title, website=@Website, username=@Username, password=@Password, notes=@Notes,
                    updated_at=@UpdatedAt, is_favorite=@IsFavorite, sort_order=@SortOrder, is_group_cover=@IsGroupCover,
                    app_package_name=@AppPackageName, app_name=@AppName, email=@Email, phone=@Phone, address_line=@AddressLine,
                    city=@City, state=@State, zip_code=@ZipCode, country=@Country, credit_card_number=@CreditCardNumber,
                    credit_card_holder=@CreditCardHolder, credit_card_expiry=@CreditCardExpiry, credit_card_cvv=@CreditCardCvv,
                    category_id=@CategoryId, bound_note_id=@BoundNoteId, keepass_database_id=@KeepassDatabaseId,
                    keepass_group_path=@KeepassGroupPath, keepass_entry_uuid=@KeepassEntryUuid, keepass_group_uuid=@KeepassGroupUuid,
                    mdbx_database_id=@MdbxDatabaseId, mdbx_folder_id=@MdbxFolderId, authenticator_key=@AuthenticatorKey,
                    passkey_bindings=@PasskeyBindings, ssh_key_data=@SshKeyData, login_type=@LoginType, sso_provider=@SsoProvider,
                    sso_ref_entry_id=@SsoRefEntryId, wifi_metadata=@WifiMetadata, custom_icon_type=@CustomIconType,
                    custom_icon_value=@CustomIconValue, custom_icon_updated_at=@CustomIconUpdatedAt, is_deleted=@IsDeleted,
                    deleted_at=@DeletedAt, is_archived=@IsArchived, archived_at=@ArchivedAt, replica_group_id=@ReplicaGroupId,
                    bitwarden_vault_id=@BitwardenVaultId, bitwarden_cipher_id=@BitwardenCipherId, bitwarden_folder_id=@BitwardenFolderId,
                    bitwarden_revision_date=@BitwardenRevisionDate, bitwarden_cipher_type=@BitwardenCipherType,
                    bitwarden_local_modified=@BitwardenLocalModified
                WHERE id=@Id;
                """,
                ToRow(_vaultDataProtector.Protect(entry)));
        }

        return entry.Id;
    }

    public async Task SoftDeletePasswordAsync(long id, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        var deletedAt = ToUnixMilliseconds(DateTimeOffset.UtcNow);
        await connection.ExecuteAsync(
            "UPDATE password_entries SET is_deleted = 1, deleted_at = @DeletedAt, is_archived = 0, archived_at = NULL, updated_at = @DeletedAt WHERE id = @Id",
            new { Id = id, DeletedAt = deletedAt });
        await connection.ExecuteAsync(
            "UPDATE secure_items SET is_deleted = 1, deleted_at = @DeletedAt, updated_at = @DeletedAt WHERE bound_password_id = @Id AND item_type = 'TOTP'",
            new { Id = id, DeletedAt = deletedAt });
    }

    public async Task RestorePasswordAsync(long id, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        var updatedAt = ToUnixMilliseconds(DateTimeOffset.UtcNow);
        await connection.ExecuteAsync(
            "UPDATE password_entries SET is_deleted = 0, deleted_at = NULL, updated_at = @UpdatedAt WHERE id = @Id",
            new { Id = id, UpdatedAt = updatedAt });
        await connection.ExecuteAsync(
            "UPDATE secure_items SET is_deleted = 0, deleted_at = NULL, updated_at = @UpdatedAt WHERE bound_password_id = @Id AND item_type = 'TOTP'",
            new { Id = id, UpdatedAt = updatedAt });
    }

    public async Task DeletePasswordPermanentlyAsync(long id, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync("DELETE FROM custom_fields WHERE entry_id = @Id", new { Id = id }, transaction);
        await connection.ExecuteAsync("DELETE FROM secure_items WHERE bound_password_id = @Id AND item_type = 'TOTP'", new { Id = id }, transaction);
        await connection.ExecuteAsync("DELETE FROM attachments WHERE owner_type = 'PASSWORD' AND owner_id = @Id", new { Id = id }, transaction);
        await connection.ExecuteAsync("DELETE FROM password_history_entries WHERE entry_id = @Id", new { Id = id }, transaction);
        await connection.ExecuteAsync("DELETE FROM password_entries WHERE id = @Id", new { Id = id }, transaction);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CustomField>> GetCustomFieldsAsync(long entryId, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<CustomFieldRow>(
            """
            SELECT id, entry_id, title, value, is_protected, sort_order
            FROM custom_fields
            WHERE entry_id = @EntryId
            ORDER BY sort_order ASC, id ASC
            """,
            new { EntryId = entryId });

        return rows.Select(ToModel).Select(field => _vaultDataProtector.Unprotect(field)).ToList();
    }

    public async Task<IReadOnlyDictionary<long, IReadOnlyList<CustomField>>> GetCustomFieldsByEntryIdsAsync(IReadOnlyList<long> entryIds, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        var ids = entryIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<long, IReadOnlyList<CustomField>>();
        }

        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<CustomFieldRow>(
            """
            SELECT id, entry_id, title, value, is_protected, sort_order
            FROM custom_fields
            WHERE entry_id IN @EntryIds
            ORDER BY entry_id ASC, sort_order ASC, id ASC
            """,
            new { EntryIds = ids });

        return rows
            .Select(ToModel)
            .Select(field => _vaultDataProtector.Unprotect(field))
            .GroupBy(field => field.EntryId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<CustomField>)group.ToList());
    }

    public async Task ReplaceCustomFieldsAsync(long entryId, IReadOnlyList<CustomField> fields, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync("DELETE FROM custom_fields WHERE entry_id = @EntryId", new { EntryId = entryId }, transaction);

        var rows = fields
            .Where(field => !string.IsNullOrWhiteSpace(field.Title) && !string.IsNullOrWhiteSpace(field.Value))
            .Select((field, index) => _vaultDataProtector.Protect(new CustomField
            {
                EntryId = entryId,
                Title = field.Title.Trim(),
                Value = field.Value.Trim(),
                IsProtected = field.IsProtected,
                SortOrder = index
            }))
            .Select(field => new
            {
                field.EntryId,
                field.Title,
                field.Value,
                IsProtected = field.IsProtected ? 1 : 0,
                field.SortOrder
            })
            .ToArray();

        if (rows.Length > 0)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO custom_fields(entry_id, title, value, is_protected, sort_order)
                VALUES(@EntryId, @Title, @Value, @IsProtected, @SortOrder)
                """,
                rows,
                transaction);
        }

        await connection.ExecuteAsync(
            "UPDATE password_entries SET updated_at = @UpdatedAt WHERE id = @EntryId",
            new { EntryId = entryId, UpdatedAt = ToUnixMilliseconds(DateTimeOffset.UtcNow) },
            transaction);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<long>> SearchEntryIdsByCustomFieldContentAsync(string query, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<CustomFieldRow>(
            """
            SELECT id, entry_id, title, value, is_protected, sort_order
            FROM custom_fields
            ORDER BY entry_id ASC
            """);

        var term = query.Trim();
        return rows
            .Select(ToModel)
            .Select(field => _vaultDataProtector.Unprotect(field))
            .Where(field =>
                field.Title.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                field.Value.Contains(term, StringComparison.CurrentCultureIgnoreCase))
            .Select(field => field.EntryId)
            .Distinct()
            .OrderBy(id => id)
            .ToList();
    }

    public async Task<IReadOnlyList<Attachment>> GetAttachmentsAsync(string ownerType, long ownerId, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<AttachmentRow>(
            """
            SELECT id, owner_type, owner_id, file_name, content_type, storage_path, size_bytes, created_at, bitwarden_vault_id, keepass_binary_ref
            FROM attachments
            WHERE owner_type = @OwnerType AND owner_id = @OwnerId
            ORDER BY created_at DESC, id DESC
            """,
            new { OwnerType = NormalizeOwnerType(ownerType), OwnerId = ownerId });

        return rows.Select(ToModel).ToList();
    }

    public async Task<IReadOnlyDictionary<long, IReadOnlyList<Attachment>>> GetAttachmentsByOwnerIdsAsync(string ownerType, IReadOnlyList<long> ownerIds, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        var ids = ownerIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<long, IReadOnlyList<Attachment>>();
        }

        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<AttachmentRow>(
            """
            SELECT id, owner_type, owner_id, file_name, content_type, storage_path, size_bytes, created_at, bitwarden_vault_id, keepass_binary_ref
            FROM attachments
            WHERE owner_type = @OwnerType AND owner_id IN @OwnerIds
            ORDER BY owner_id ASC, created_at DESC, id DESC
            """,
            new { OwnerType = NormalizeOwnerType(ownerType), OwnerIds = ids });

        return rows
            .Select(ToModel)
            .GroupBy(attachment => attachment.OwnerId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Attachment>)group.ToList());
    }

    public async Task<long> SaveAttachmentAsync(Attachment attachment, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        attachment.OwnerType = NormalizeOwnerType(attachment.OwnerType);
        attachment.FileName = attachment.FileName.Trim();
        attachment.ContentType = attachment.ContentType.Trim();
        attachment.StoragePath = attachment.StoragePath.Trim();
        if (attachment.CreatedAt == default)
        {
            attachment.CreatedAt = DateTimeOffset.UtcNow;
        }

        await using var connection = connectionFactory.CreateConnection();
        if (attachment.Id == 0)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO attachments(owner_type, owner_id, file_name, content_type, storage_path, size_bytes, created_at, bitwarden_vault_id, keepass_binary_ref)
                VALUES(@OwnerType, @OwnerId, @FileName, @ContentType, @StoragePath, @SizeBytes, @CreatedAt, @BitwardenVaultId, @KeepassBinaryRef);
                """,
                ToRow(attachment));
            attachment.Id = await connection.ExecuteScalarAsync<long>("SELECT last_insert_rowid();");
        }
        else
        {
            await connection.ExecuteAsync(
                """
                UPDATE attachments SET owner_type=@OwnerType, owner_id=@OwnerId, file_name=@FileName, content_type=@ContentType,
                    storage_path=@StoragePath, size_bytes=@SizeBytes, created_at=@CreatedAt, bitwarden_vault_id=@BitwardenVaultId,
                    keepass_binary_ref=@KeepassBinaryRef
                WHERE id=@Id;
                """,
                ToRow(attachment));
        }

        return attachment.Id;
    }

    public async Task DeleteAttachmentAsync(long id, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync("DELETE FROM attachments WHERE id = @Id", new { Id = id });
    }

    public async Task<IReadOnlyList<PasswordHistoryEntry>> GetPasswordHistoryAsync(long entryId, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<PasswordHistoryEntryRow>(
            """
            SELECT id, entry_id, password, last_used_at
            FROM password_history_entries
            WHERE entry_id = @EntryId
            ORDER BY last_used_at DESC, id DESC
            """,
            new { EntryId = entryId });

        return rows.Select(ToModel).Select(entry => _vaultDataProtector.Unprotect(entry)).ToList();
    }

    public async Task<long> SavePasswordHistoryAsync(PasswordHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        if (entry.LastUsedAt == default)
        {
            entry.LastUsedAt = DateTimeOffset.UtcNow;
        }

        await using var connection = connectionFactory.CreateConnection();
        if (entry.Id == 0)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO password_history_entries(entry_id, password, last_used_at)
                VALUES(@EntryId, @Password, @LastUsedAt)
                """,
                ToRow(_vaultDataProtector.Protect(entry)));
            entry.Id = await connection.ExecuteScalarAsync<long>("SELECT last_insert_rowid();");
        }
        else
        {
            await connection.ExecuteAsync(
                """
                UPDATE password_history_entries
                SET entry_id=@EntryId, password=@Password, last_used_at=@LastUsedAt
                WHERE id=@Id
                """,
                ToRow(_vaultDataProtector.Protect(entry)));
        }

        return entry.Id;
    }

    public async Task TrimPasswordHistoryAsync(long entryId, int limit, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            """
            DELETE FROM password_history_entries
            WHERE entry_id = @EntryId
              AND id NOT IN (
                  SELECT id
                  FROM password_history_entries
                  WHERE entry_id = @EntryId
                  ORDER BY last_used_at DESC, id DESC
                  LIMIT @Limit
              )
            """,
            new { EntryId = entryId, Limit = Math.Max(limit, 0) });
    }

    public async Task DeletePasswordHistoryAsync(long id, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync("DELETE FROM password_history_entries WHERE id = @Id", new { Id = id });
    }

    public async Task ClearPasswordHistoryAsync(long entryId, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync("DELETE FROM password_history_entries WHERE entry_id = @EntryId", new { EntryId = entryId });
    }

    public async Task ClearVaultDataAsync(VaultClearScope scope, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var sql in GetClearVaultStatements(scope))
            {
                await connection.ExecuteAsync(sql, transaction: transaction);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task RecordPasswordQuickAccessAsync(long passwordId, CancellationToken cancellationToken = default)
    {
        if (passwordId <= 0)
        {
            return;
        }

        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            """
            INSERT INTO password_quick_access_records(password_id, open_count, last_opened_at)
            VALUES(@PasswordId, 1, @LastOpenedAt)
            ON CONFLICT(password_id) DO UPDATE SET
                open_count = open_count + 1,
                last_opened_at = excluded.last_opened_at
            """,
            new { PasswordId = passwordId, LastOpenedAt = ToUnixMilliseconds(DateTimeOffset.UtcNow) });
    }

    public async Task<IReadOnlyList<PasswordQuickAccessRecord>> GetPasswordQuickAccessRecordsAsync(CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<PasswordQuickAccessRecordRow>(
            """
            SELECT password_id, open_count, last_opened_at
            FROM password_quick_access_records
            WHERE password_id IN (
                SELECT id
                FROM password_entries
                WHERE is_deleted = 0 AND is_archived = 0
            )
            ORDER BY last_opened_at DESC
            """);

        return rows.Select(ToModel).ToList();
    }

    public async Task<IReadOnlyList<SecureItem>> GetSecureItemsAsync(VaultItemType? itemType = null, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<SecureItemRow>(
            """
            SELECT * FROM secure_items
            WHERE (@IncludeDeleted = 1 OR is_deleted = 0)
              AND (@ItemType IS NULL OR item_type = @ItemType)
            ORDER BY is_favorite DESC, sort_order ASC, updated_at DESC
            """,
            new { IncludeDeleted = includeDeleted ? 1 : 0, ItemType = itemType?.ToString().ToUpperInvariant() });
        return rows.Select(ToModel).Select(item => _vaultDataProtector.Unprotect(item)).ToList();
    }

    public async Task<IReadOnlyList<SecureItem>> GetSecureItemsByBoundPasswordIdAsync(long passwordId, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<SecureItemRow>(
            """
            SELECT * FROM secure_items
            WHERE bound_password_id = @PasswordId
              AND item_type = 'TOTP'
              AND (@IncludeDeleted = 1 OR is_deleted = 0)
            ORDER BY updated_at DESC, id DESC
            """,
            new { PasswordId = passwordId, IncludeDeleted = includeDeleted ? 1 : 0 });
        return rows.Select(ToModel).Select(item => _vaultDataProtector.Unprotect(item)).ToList();
    }

    public async Task<long> SaveSecureItemAsync(SecureItem item, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        item.UpdatedAt = DateTimeOffset.UtcNow;
        if (item.CreatedAt == default)
        {
            item.CreatedAt = item.UpdatedAt;
        }

        await using var connection = connectionFactory.CreateConnection();
        if (item.Id == 0)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO secure_items (
                    item_type, title, notes, is_favorite, sort_order, created_at, updated_at, item_data, image_paths, bound_password_id,
                    category_id, keepass_database_id, keepass_group_path, keepass_entry_uuid, keepass_group_uuid,
                    mdbx_database_id, mdbx_folder_id, is_deleted, deleted_at, replica_group_id, bitwarden_vault_id,
                    bitwarden_cipher_id, bitwarden_folder_id, bitwarden_revision_date, bitwarden_local_modified, sync_status)
                VALUES (
                    @ItemType, @Title, @Notes, @IsFavorite, @SortOrder, @CreatedAt, @UpdatedAt, @ItemData, @ImagePaths, @BoundPasswordId,
                    @CategoryId, @KeepassDatabaseId, @KeepassGroupPath, @KeepassEntryUuid, @KeepassGroupUuid,
                    @MdbxDatabaseId, @MdbxFolderId, @IsDeleted, @DeletedAt, @ReplicaGroupId, @BitwardenVaultId,
                    @BitwardenCipherId, @BitwardenFolderId, @BitwardenRevisionDate, @BitwardenLocalModified, @SyncStatus);
                """,
                ToRow(_vaultDataProtector.Protect(item)));
            item.Id = await connection.ExecuteScalarAsync<long>("SELECT last_insert_rowid();");
        }
        else
        {
            await connection.ExecuteAsync(
                """
                UPDATE secure_items SET item_type=@ItemType, title=@Title, notes=@Notes, is_favorite=@IsFavorite,
                    sort_order=@SortOrder, updated_at=@UpdatedAt, item_data=@ItemData, image_paths=@ImagePaths,
                    bound_password_id=@BoundPasswordId,
                    category_id=@CategoryId, keepass_database_id=@KeepassDatabaseId, keepass_group_path=@KeepassGroupPath,
                    keepass_entry_uuid=@KeepassEntryUuid, keepass_group_uuid=@KeepassGroupUuid, mdbx_database_id=@MdbxDatabaseId,
                    mdbx_folder_id=@MdbxFolderId, is_deleted=@IsDeleted, deleted_at=@DeletedAt, replica_group_id=@ReplicaGroupId,
                    bitwarden_vault_id=@BitwardenVaultId, bitwarden_cipher_id=@BitwardenCipherId, bitwarden_folder_id=@BitwardenFolderId,
                    bitwarden_revision_date=@BitwardenRevisionDate, bitwarden_local_modified=@BitwardenLocalModified,
                    sync_status=@SyncStatus
                WHERE id=@Id;
                """,
                ToRow(_vaultDataProtector.Protect(item)));
        }

        return item.Id;
    }

    public async Task SoftDeleteSecureItemAsync(long id, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE secure_items SET is_deleted = 1, deleted_at = @DeletedAt, updated_at = @DeletedAt WHERE id = @Id",
            new { Id = id, DeletedAt = ToUnixMilliseconds(DateTimeOffset.UtcNow) });
    }

    public async Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<CategoryRow>("SELECT id, name, sort_order, mdbx_database_id FROM categories ORDER BY sort_order ASC, name ASC");
        return rows.Select(row => new Category { Id = row.Id, Name = row.Name, SortOrder = row.SortOrder, MdbxDatabaseId = row.MdbxDatabaseId }).ToList();
    }

    public async Task<long> SaveCategoryAsync(Category category, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        if (category.Id == 0)
        {
            category.Id = await connection.ExecuteScalarAsync<long>(
                "INSERT INTO categories(name, sort_order, mdbx_database_id) VALUES(@Name, @SortOrder, @MdbxDatabaseId); SELECT last_insert_rowid();",
                new { category.Name, category.SortOrder, category.MdbxDatabaseId });
        }
        else
        {
            await connection.ExecuteAsync(
                "UPDATE categories SET name=@Name, sort_order=@SortOrder, mdbx_database_id=@MdbxDatabaseId WHERE id=@Id",
                new { category.Id, category.Name, category.SortOrder, category.MdbxDatabaseId });
        }

        return category.Id;
    }

    public async Task DeleteCategoryAsync(long id, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var now = ToUnixMilliseconds(DateTimeOffset.UtcNow);
        await connection.ExecuteAsync(
            "UPDATE password_entries SET category_id = NULL, updated_at = @Now WHERE category_id = @Id",
            new { Id = id, Now = now },
            transaction);
        await connection.ExecuteAsync(
            "UPDATE secure_items SET category_id = NULL, updated_at = @Now WHERE category_id = @Id",
            new { Id = id, Now = now },
            transaction);
        await connection.ExecuteAsync(
            "UPDATE passkeys SET category_id = NULL WHERE category_id = @Id",
            new { Id = id },
            transaction);
        await connection.ExecuteAsync("DELETE FROM categories WHERE id = @Id", new { Id = id }, transaction);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LocalMdbxDatabase>> GetMdbxDatabasesAsync(CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<MdbxDatabaseRow>("SELECT * FROM local_mdbx_databases ORDER BY sort_order ASC, created_at DESC");
        return rows.Select(ToModel).Select(database => _vaultDataProtector.Unprotect(database)).ToList();
    }

    public async Task<long> SaveMdbxDatabaseAsync(LocalMdbxDatabase database, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        if (database.CreatedAt == default)
        {
            database.CreatedAt = DateTimeOffset.UtcNow;
        }

        if (database.LastAccessedAt == default)
        {
            database.LastAccessedAt = database.CreatedAt;
        }

        if (database.Id == 0)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO local_mdbx_databases (
                    name, file_path, storage_location, source_type, source_id, tiga_mode, encrypted_password, unlock_method,
                    kdf_profile, key_file_name, key_file_uri, key_file_fingerprint, description, created_at, last_accessed_at,
                    last_synced_at, is_default, project_count, sort_order, working_copy_path, cache_copy_path, is_offline_available,
                    last_sync_status, last_sync_error)
                VALUES (
                    @Name, @FilePath, @StorageLocation, @SourceType, @SourceId, @TigaMode, @EncryptedPassword, @UnlockMethod,
                    @KdfProfile, @KeyFileName, @KeyFileUri, @KeyFileFingerprint, @Description, @CreatedAt, @LastAccessedAt,
                    @LastSyncedAt, @IsDefault, @ProjectCount, @SortOrder, @WorkingCopyPath, @CacheCopyPath, @IsOfflineAvailable,
                    @LastSyncStatus, @LastSyncError);
                """,
                ToRow(_vaultDataProtector.Protect(database)));
            database.Id = await connection.ExecuteScalarAsync<long>("SELECT last_insert_rowid();");
        }
        else
        {
            await connection.ExecuteAsync(
                """
                UPDATE local_mdbx_databases SET name=@Name, file_path=@FilePath, storage_location=@StorageLocation,
                    source_type=@SourceType, source_id=@SourceId, tiga_mode=@TigaMode, encrypted_password=@EncryptedPassword,
                    unlock_method=@UnlockMethod, kdf_profile=@KdfProfile, key_file_name=@KeyFileName, key_file_uri=@KeyFileUri,
                    key_file_fingerprint=@KeyFileFingerprint, description=@Description, last_accessed_at=@LastAccessedAt,
                    last_synced_at=@LastSyncedAt, is_default=@IsDefault, project_count=@ProjectCount, sort_order=@SortOrder,
                    working_copy_path=@WorkingCopyPath, cache_copy_path=@CacheCopyPath, is_offline_available=@IsOfflineAvailable,
                    last_sync_status=@LastSyncStatus, last_sync_error=@LastSyncError
                WHERE id=@Id;
                """,
                ToRow(_vaultDataProtector.Protect(database)));
        }

        return database.Id;
    }

    public async Task LogAsync(OperationLog log, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        var protectedLog = _vaultDataProtector.Protect(log);
        await connection.ExecuteAsync(
            """
            INSERT INTO operation_logs(item_type, item_id, item_title, operation_type, changes_json, device_id, device_name, timestamp, is_reverted)
            VALUES(@ItemType, @ItemId, @ItemTitle, @OperationType, @ChangesJson, @DeviceId, @DeviceName, @Timestamp, @IsReverted)
            """,
            new
            {
                protectedLog.ItemType,
                protectedLog.ItemId,
                protectedLog.ItemTitle,
                protectedLog.OperationType,
                protectedLog.ChangesJson,
                protectedLog.DeviceId,
                protectedLog.DeviceName,
                Timestamp = ToUnixMilliseconds(protectedLog.Timestamp),
                IsReverted = protectedLog.IsReverted ? 1 : 0
            });
    }

    public async Task<IReadOnlyList<OperationLog>> GetOperationLogsAsync(int limit = 100, string? itemType = null, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<OperationLogRow>(
            """
            SELECT id, item_type, item_id, item_title, operation_type, changes_json, device_id, device_name, timestamp, is_reverted
            FROM operation_logs
            WHERE (@ItemType IS NULL OR item_type = @ItemType)
            ORDER BY timestamp DESC, id DESC
            LIMIT @Limit
            """,
            new
            {
                ItemType = string.IsNullOrWhiteSpace(itemType) ? null : itemType.Trim().ToUpperInvariant(),
                Limit = Math.Clamp(limit, 1, 500)
            });

        return rows.Select(ToModel).Select(log => _vaultDataProtector.Unprotect(log)).ToList();
    }

    private static PasswordEntry ToModel(PasswordEntryRow row) => new()
    {
        Id = row.Id,
        Title = row.Title,
        Website = row.Website,
        Username = row.Username,
        Password = row.Password,
        Notes = row.Notes,
        CreatedAt = FromUnixMilliseconds(row.CreatedAt),
        UpdatedAt = FromUnixMilliseconds(row.UpdatedAt),
        IsFavorite = row.IsFavorite,
        SortOrder = row.SortOrder,
        IsGroupCover = row.IsGroupCover,
        AppPackageName = row.AppPackageName,
        AppName = row.AppName,
        Email = row.Email,
        Phone = row.Phone,
        AddressLine = row.AddressLine,
        City = row.City,
        State = row.State,
        ZipCode = row.ZipCode,
        Country = row.Country,
        CreditCardNumber = row.CreditCardNumber,
        CreditCardHolder = row.CreditCardHolder,
        CreditCardExpiry = row.CreditCardExpiry,
        CreditCardCvv = row.CreditCardCvv,
        CategoryId = row.CategoryId,
        BoundNoteId = row.BoundNoteId,
        KeepassDatabaseId = row.KeepassDatabaseId,
        KeepassGroupPath = row.KeepassGroupPath,
        KeepassEntryUuid = row.KeepassEntryUuid,
        KeepassGroupUuid = row.KeepassGroupUuid,
        MdbxDatabaseId = row.MdbxDatabaseId,
        MdbxFolderId = row.MdbxFolderId,
        AuthenticatorKey = row.AuthenticatorKey,
        PasskeyBindings = row.PasskeyBindings,
        SshKeyData = row.SshKeyData,
        LoginType = ParseLoginType(row.LoginType),
        SsoProvider = row.SsoProvider,
        SsoRefEntryId = row.SsoRefEntryId,
        WifiMetadata = row.WifiMetadata,
        CustomIconType = row.CustomIconType,
        CustomIconValue = row.CustomIconValue,
        CustomIconUpdatedAt = row.CustomIconUpdatedAt,
        IsDeleted = row.IsDeleted,
        DeletedAt = FromNullableUnixMilliseconds(row.DeletedAt),
        IsArchived = row.IsArchived,
        ArchivedAt = FromNullableUnixMilliseconds(row.ArchivedAt),
        ReplicaGroupId = row.ReplicaGroupId,
        BitwardenVaultId = row.BitwardenVaultId,
        BitwardenCipherId = row.BitwardenCipherId,
        BitwardenFolderId = row.BitwardenFolderId,
        BitwardenRevisionDate = row.BitwardenRevisionDate,
        BitwardenCipherType = row.BitwardenCipherType,
        BitwardenLocalModified = row.BitwardenLocalModified
    };

    private static OperationLog ToModel(OperationLogRow row) => new()
    {
        Id = row.Id,
        ItemType = row.ItemType,
        ItemId = row.ItemId,
        ItemTitle = row.ItemTitle,
        OperationType = row.OperationType,
        ChangesJson = row.ChangesJson,
        DeviceId = row.DeviceId,
        DeviceName = row.DeviceName,
        Timestamp = FromUnixMilliseconds(row.Timestamp),
        IsReverted = row.IsReverted
    };

    private static PasswordEntryParameters ToRow(PasswordEntry entry) => new()
    {
        Id = entry.Id,
        Title = entry.Title,
        Website = entry.Website,
        Username = entry.Username,
        Password = entry.Password,
        Notes = entry.Notes,
        CreatedAt = ToUnixMilliseconds(entry.CreatedAt),
        UpdatedAt = ToUnixMilliseconds(entry.UpdatedAt),
        IsFavorite = entry.IsFavorite ? 1 : 0,
        SortOrder = entry.SortOrder,
        IsGroupCover = entry.IsGroupCover ? 1 : 0,
        AppPackageName = entry.AppPackageName,
        AppName = entry.AppName,
        Email = entry.Email,
        Phone = entry.Phone,
        AddressLine = entry.AddressLine,
        City = entry.City,
        State = entry.State,
        ZipCode = entry.ZipCode,
        Country = entry.Country,
        CreditCardNumber = entry.CreditCardNumber,
        CreditCardHolder = entry.CreditCardHolder,
        CreditCardExpiry = entry.CreditCardExpiry,
        CreditCardCvv = entry.CreditCardCvv,
        CategoryId = entry.CategoryId,
        BoundNoteId = entry.BoundNoteId,
        KeepassDatabaseId = entry.KeepassDatabaseId,
        KeepassGroupPath = entry.KeepassGroupPath,
        KeepassEntryUuid = entry.KeepassEntryUuid,
        KeepassGroupUuid = entry.KeepassGroupUuid,
        MdbxDatabaseId = entry.MdbxDatabaseId,
        MdbxFolderId = entry.MdbxFolderId,
        AuthenticatorKey = entry.AuthenticatorKey,
        PasskeyBindings = entry.PasskeyBindings,
        SshKeyData = entry.SshKeyData,
        LoginType = entry.LoginType.ToString().ToUpperInvariant(),
        SsoProvider = entry.SsoProvider,
        SsoRefEntryId = entry.SsoRefEntryId,
        WifiMetadata = entry.WifiMetadata,
        CustomIconType = entry.CustomIconType,
        CustomIconValue = entry.CustomIconValue,
        CustomIconUpdatedAt = entry.CustomIconUpdatedAt,
        IsDeleted = entry.IsDeleted ? 1 : 0,
        DeletedAt = ToNullableUnixMilliseconds(entry.DeletedAt),
        IsArchived = entry.IsArchived ? 1 : 0,
        ArchivedAt = ToNullableUnixMilliseconds(entry.ArchivedAt),
        ReplicaGroupId = entry.ReplicaGroupId,
        BitwardenVaultId = entry.BitwardenVaultId,
        BitwardenCipherId = entry.BitwardenCipherId,
        BitwardenFolderId = entry.BitwardenFolderId,
        BitwardenRevisionDate = entry.BitwardenRevisionDate,
        BitwardenCipherType = entry.BitwardenCipherType,
        BitwardenLocalModified = entry.BitwardenLocalModified ? 1 : 0
    };

    private sealed class PasswordEntryParameters
    {
        public long Id { get; init; }
        public string Title { get; init; } = "";
        public string Website { get; init; } = "";
        public string Username { get; init; } = "";
        public string Password { get; init; } = "";
        public string Notes { get; init; } = "";
        public long CreatedAt { get; init; }
        public long UpdatedAt { get; init; }
        public int IsFavorite { get; init; }
        public int SortOrder { get; init; }
        public int IsGroupCover { get; init; }
        public string AppPackageName { get; init; } = "";
        public string AppName { get; init; } = "";
        public string Email { get; init; } = "";
        public string Phone { get; init; } = "";
        public string AddressLine { get; init; } = "";
        public string City { get; init; } = "";
        public string State { get; init; } = "";
        public string ZipCode { get; init; } = "";
        public string Country { get; init; } = "";
        public string CreditCardNumber { get; init; } = "";
        public string CreditCardHolder { get; init; } = "";
        public string CreditCardExpiry { get; init; } = "";
        public string CreditCardCvv { get; init; } = "";
        public long? CategoryId { get; init; }
        public long? BoundNoteId { get; init; }
        public long? KeepassDatabaseId { get; init; }
        public string? KeepassGroupPath { get; init; }
        public string? KeepassEntryUuid { get; init; }
        public string? KeepassGroupUuid { get; init; }
        public long? MdbxDatabaseId { get; init; }
        public string? MdbxFolderId { get; init; }
        public string AuthenticatorKey { get; init; } = "";
        public string PasskeyBindings { get; init; } = "";
        public string SshKeyData { get; init; } = "";
        public string LoginType { get; init; } = "";
        public string SsoProvider { get; init; } = "";
        public long? SsoRefEntryId { get; init; }
        public string WifiMetadata { get; init; } = "";
        public string CustomIconType { get; init; } = "";
        public string? CustomIconValue { get; init; }
        public long CustomIconUpdatedAt { get; init; }
        public int IsDeleted { get; init; }
        public long? DeletedAt { get; init; }
        public int IsArchived { get; init; }
        public long? ArchivedAt { get; init; }
        public string? ReplicaGroupId { get; init; }
        public long? BitwardenVaultId { get; init; }
        public string? BitwardenCipherId { get; init; }
        public string? BitwardenFolderId { get; init; }
        public string? BitwardenRevisionDate { get; init; }
        public int BitwardenCipherType { get; init; }
        public int BitwardenLocalModified { get; init; }
    }

    private static CustomField ToModel(CustomFieldRow row) => new()
    {
        Id = row.Id,
        EntryId = row.EntryId,
        Title = row.Title,
        Value = row.Value,
        IsProtected = row.IsProtected,
        SortOrder = row.SortOrder
    };

    private static Attachment ToModel(AttachmentRow row) => new()
    {
        Id = row.Id,
        OwnerType = row.OwnerType,
        OwnerId = row.OwnerId,
        FileName = row.FileName,
        ContentType = row.ContentType,
        StoragePath = row.StoragePath,
        SizeBytes = row.SizeBytes,
        CreatedAt = FromUnixMilliseconds(row.CreatedAt),
        BitwardenVaultId = row.BitwardenVaultId,
        KeepassBinaryRef = row.KeepassBinaryRef
    };

    private static PasswordHistoryEntry ToModel(PasswordHistoryEntryRow row) => new()
    {
        Id = row.Id,
        EntryId = row.EntryId,
        Password = row.Password,
        LastUsedAt = FromUnixMilliseconds(row.LastUsedAt)
    };

    private static PasswordQuickAccessRecord ToModel(PasswordQuickAccessRecordRow row) => new()
    {
        PasswordId = row.PasswordId,
        OpenCount = row.OpenCount,
        LastOpenedAt = FromUnixMilliseconds(row.LastOpenedAt)
    };

    private static AttachmentParameters ToRow(Attachment attachment) => new()
    {
        Id = attachment.Id,
        OwnerType = attachment.OwnerType,
        OwnerId = attachment.OwnerId,
        FileName = attachment.FileName,
        ContentType = attachment.ContentType,
        StoragePath = attachment.StoragePath,
        SizeBytes = attachment.SizeBytes,
        CreatedAt = ToUnixMilliseconds(attachment.CreatedAt),
        BitwardenVaultId = attachment.BitwardenVaultId,
        KeepassBinaryRef = attachment.KeepassBinaryRef
    };

    private static PasswordHistoryEntryParameters ToRow(PasswordHistoryEntry entry) => new()
    {
        Id = entry.Id,
        EntryId = entry.EntryId,
        Password = entry.Password,
        LastUsedAt = ToUnixMilliseconds(entry.LastUsedAt)
    };

    private sealed class AttachmentParameters
    {
        public long Id { get; init; }
        public string OwnerType { get; init; } = "";
        public long OwnerId { get; init; }
        public string FileName { get; init; } = "";
        public string ContentType { get; init; } = "";
        public string StoragePath { get; init; } = "";
        public long SizeBytes { get; init; }
        public long CreatedAt { get; init; }
        public long? BitwardenVaultId { get; init; }
        public string? KeepassBinaryRef { get; init; }
    }

    private sealed class PasswordHistoryEntryParameters
    {
        public long Id { get; init; }
        public long EntryId { get; init; }
        public string Password { get; init; } = "";
        public long LastUsedAt { get; init; }
    }

    private static SecureItem ToModel(SecureItemRow row) => new()
    {
        Id = row.Id,
        ItemType = Enum.TryParse<VaultItemType>(row.ItemType, true, out var itemType) ? itemType : VaultItemType.Note,
        Title = row.Title,
        Notes = row.Notes,
        IsFavorite = row.IsFavorite,
        SortOrder = row.SortOrder,
        CreatedAt = FromUnixMilliseconds(row.CreatedAt),
        UpdatedAt = FromUnixMilliseconds(row.UpdatedAt),
        ItemData = row.ItemData,
        ImagePaths = row.ImagePaths,
        BoundPasswordId = row.BoundPasswordId,
        CategoryId = row.CategoryId,
        KeepassDatabaseId = row.KeepassDatabaseId,
        KeepassGroupPath = row.KeepassGroupPath,
        KeepassEntryUuid = row.KeepassEntryUuid,
        KeepassGroupUuid = row.KeepassGroupUuid,
        MdbxDatabaseId = row.MdbxDatabaseId,
        MdbxFolderId = row.MdbxFolderId,
        IsDeleted = row.IsDeleted,
        DeletedAt = FromNullableUnixMilliseconds(row.DeletedAt),
        ReplicaGroupId = row.ReplicaGroupId,
        BitwardenVaultId = row.BitwardenVaultId,
        BitwardenCipherId = row.BitwardenCipherId,
        BitwardenFolderId = row.BitwardenFolderId,
        BitwardenRevisionDate = row.BitwardenRevisionDate,
        BitwardenLocalModified = row.BitwardenLocalModified,
        SyncStatus = Enum.TryParse<SyncStatus>(row.SyncStatus, true, out var sync) ? sync : SyncStatus.None
    };

    private static SecureItemParameters ToRow(SecureItem item) => new()
    {
        Id = item.Id,
        ItemType = item.ItemType.ToString().ToUpperInvariant(),
        Title = item.Title,
        Notes = item.Notes,
        IsFavorite = item.IsFavorite ? 1 : 0,
        SortOrder = item.SortOrder,
        CreatedAt = ToUnixMilliseconds(item.CreatedAt),
        UpdatedAt = ToUnixMilliseconds(item.UpdatedAt),
        ItemData = item.ItemData,
        ImagePaths = item.ImagePaths,
        BoundPasswordId = item.BoundPasswordId,
        CategoryId = item.CategoryId,
        KeepassDatabaseId = item.KeepassDatabaseId,
        KeepassGroupPath = item.KeepassGroupPath,
        KeepassEntryUuid = item.KeepassEntryUuid,
        KeepassGroupUuid = item.KeepassGroupUuid,
        MdbxDatabaseId = item.MdbxDatabaseId,
        MdbxFolderId = item.MdbxFolderId,
        IsDeleted = item.IsDeleted ? 1 : 0,
        DeletedAt = ToNullableUnixMilliseconds(item.DeletedAt),
        ReplicaGroupId = item.ReplicaGroupId,
        BitwardenVaultId = item.BitwardenVaultId,
        BitwardenCipherId = item.BitwardenCipherId,
        BitwardenFolderId = item.BitwardenFolderId,
        BitwardenRevisionDate = item.BitwardenRevisionDate,
        BitwardenLocalModified = item.BitwardenLocalModified ? 1 : 0,
        SyncStatus = item.SyncStatus.ToString().ToUpperInvariant()
    };

    private sealed class SecureItemParameters
    {
        public long Id { get; init; }
        public string ItemType { get; init; } = "";
        public string Title { get; init; } = "";
        public string Notes { get; init; } = "";
        public int IsFavorite { get; init; }
        public int SortOrder { get; init; }
        public long CreatedAt { get; init; }
        public long UpdatedAt { get; init; }
        public string ItemData { get; init; } = "";
        public string ImagePaths { get; init; } = "";
        public long? BoundPasswordId { get; init; }
        public long? CategoryId { get; init; }
        public long? KeepassDatabaseId { get; init; }
        public string? KeepassGroupPath { get; init; }
        public string? KeepassEntryUuid { get; init; }
        public string? KeepassGroupUuid { get; init; }
        public long? MdbxDatabaseId { get; init; }
        public string? MdbxFolderId { get; init; }
        public int IsDeleted { get; init; }
        public long? DeletedAt { get; init; }
        public string? ReplicaGroupId { get; init; }
        public long? BitwardenVaultId { get; init; }
        public string? BitwardenCipherId { get; init; }
        public string? BitwardenFolderId { get; init; }
        public string? BitwardenRevisionDate { get; init; }
        public int BitwardenLocalModified { get; init; }
        public string SyncStatus { get; init; } = "";
    }

    private static LocalMdbxDatabase ToModel(MdbxDatabaseRow row) => new()
    {
        Id = row.Id,
        Name = row.Name,
        FilePath = row.FilePath,
        StorageLocation = Enum.TryParse<MdbxStorageLocation>(row.StorageLocation.Replace("_", "", StringComparison.Ordinal), true, out var location) ? location : MdbxStorageLocation.RemoteWebDav,
        SourceType = row.SourceType,
        SourceId = row.SourceId,
        TigaMode = Enum.TryParse<MdbxTigaMode>(row.TigaMode, true, out var mode) ? mode : MdbxTigaMode.Multi,
        EncryptedPassword = row.EncryptedPassword,
        UnlockMethod = row.UnlockMethod switch
        {
            "key_file" => MdbxUnlockMethod.KeyFile,
            "password+key_file" => MdbxUnlockMethod.MasterPasswordAndKeyFile,
            "device_key" => MdbxUnlockMethod.DeviceKey,
            _ => MdbxUnlockMethod.MasterPassword
        },
        KdfProfile = row.KdfProfile,
        KeyFileName = row.KeyFileName,
        KeyFileUri = row.KeyFileUri,
        KeyFileFingerprint = row.KeyFileFingerprint,
        Description = row.Description,
        CreatedAt = FromUnixMilliseconds(row.CreatedAt),
        LastAccessedAt = FromUnixMilliseconds(row.LastAccessedAt),
        LastSyncedAt = FromNullableUnixMilliseconds(row.LastSyncedAt),
        IsDefault = row.IsDefault,
        ProjectCount = row.ProjectCount,
        SortOrder = row.SortOrder,
        WorkingCopyPath = row.WorkingCopyPath,
        CacheCopyPath = row.CacheCopyPath,
        IsOfflineAvailable = row.IsOfflineAvailable,
        LastSyncStatus = Enum.TryParse<SyncStatus>(row.LastSyncStatus.Replace("_", "", StringComparison.Ordinal), true, out var status) ? status : SyncStatus.LocalOnly,
        LastSyncError = row.LastSyncError
    };

    private static MdbxDatabaseParameters ToRow(LocalMdbxDatabase database) => new()
    {
        Id = database.Id,
        Name = database.Name,
        FilePath = database.FilePath,
        StorageLocation = database.StorageLocation.ToString().ToUpperInvariant(),
        SourceType = database.SourceType,
        SourceId = database.SourceId,
        TigaMode = database.TigaMode.ToString().ToUpperInvariant(),
        EncryptedPassword = database.EncryptedPassword,
        UnlockMethod = database.UnlockMethod switch
        {
            MdbxUnlockMethod.KeyFile => "key_file",
            MdbxUnlockMethod.MasterPasswordAndKeyFile => "password+key_file",
            MdbxUnlockMethod.DeviceKey => "device_key",
            _ => "password"
        },
        KdfProfile = database.KdfProfile,
        KeyFileName = database.KeyFileName,
        KeyFileUri = database.KeyFileUri,
        KeyFileFingerprint = database.KeyFileFingerprint,
        Description = database.Description,
        CreatedAt = ToUnixMilliseconds(database.CreatedAt),
        LastAccessedAt = ToUnixMilliseconds(database.LastAccessedAt),
        LastSyncedAt = ToNullableUnixMilliseconds(database.LastSyncedAt),
        IsDefault = database.IsDefault ? 1 : 0,
        ProjectCount = database.ProjectCount,
        SortOrder = database.SortOrder,
        WorkingCopyPath = database.WorkingCopyPath,
        CacheCopyPath = database.CacheCopyPath,
        IsOfflineAvailable = database.IsOfflineAvailable ? 1 : 0,
        LastSyncStatus = database.LastSyncStatus.ToString().ToUpperInvariant(),
        LastSyncError = database.LastSyncError
    };

    private sealed class MdbxDatabaseParameters
    {
        public long Id { get; init; }
        public string Name { get; init; } = "";
        public string FilePath { get; init; } = "";
        public string StorageLocation { get; init; } = "";
        public string SourceType { get; init; } = "";
        public long? SourceId { get; init; }
        public string TigaMode { get; init; } = "";
        public string? EncryptedPassword { get; init; }
        public string UnlockMethod { get; init; } = "";
        public string KdfProfile { get; init; } = "";
        public string? KeyFileName { get; init; }
        public string? KeyFileUri { get; init; }
        public string? KeyFileFingerprint { get; init; }
        public string? Description { get; init; }
        public long CreatedAt { get; init; }
        public long LastAccessedAt { get; init; }
        public long? LastSyncedAt { get; init; }
        public int IsDefault { get; init; }
        public int ProjectCount { get; init; }
        public int SortOrder { get; init; }
        public string? WorkingCopyPath { get; init; }
        public string? CacheCopyPath { get; init; }
        public int IsOfflineAvailable { get; init; }
        public string LastSyncStatus { get; init; } = "";
        public string? LastSyncError { get; init; }
    }

    private static IReadOnlyList<string> GetClearVaultStatements(VaultClearScope scope) => scope switch
    {
        VaultClearScope.Passwords =>
        [
            "UPDATE secure_items SET bound_password_id = NULL WHERE bound_password_id IS NOT NULL;",
            "UPDATE passkeys SET bound_password_id = NULL WHERE bound_password_id IS NOT NULL;",
            "DELETE FROM attachments WHERE owner_type = 'PASSWORD';",
            "DELETE FROM custom_fields;",
            "DELETE FROM password_history_entries;",
            "DELETE FROM password_quick_access_records;",
            "DELETE FROM password_entries;",
            "DELETE FROM operation_logs WHERE item_type = 'PASSWORD';"
        ],
        VaultClearScope.SecureItems =>
        [
            "UPDATE password_entries SET bound_note_id = NULL WHERE bound_note_id IS NOT NULL;",
            "DELETE FROM secure_items;",
            "DELETE FROM operation_logs WHERE item_type IN ('NOTE', 'TOTP', 'WALLET');"
        ],
        _ =>
        [
            "DELETE FROM attachments;",
            "DELETE FROM custom_fields;",
            "DELETE FROM password_history_entries;",
            "DELETE FROM password_quick_access_records;",
            "DELETE FROM password_entries;",
            "DELETE FROM secure_items;",
            "DELETE FROM passkeys;",
            "DELETE FROM operation_logs;",
            "DELETE FROM categories;",
            "DELETE FROM local_mdbx_databases;",
            "DELETE FROM mdbx_remote_sources;",
            "DELETE FROM bitwarden_vaults;"
        ]
    };

    private static PasswordLoginType ParseLoginType(string value) => value.ToUpperInvariant() switch
    {
        "SSO" => PasswordLoginType.Sso,
        "WIFI" => PasswordLoginType.Wifi,
        "SSHKEY" or "SSH_KEY" => PasswordLoginType.SshKey,
        _ => PasswordLoginType.Password
    };

    private static string NormalizeOwnerType(string ownerType)
    {
        var normalized = ownerType.Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "PASSWORD" : normalized;
    }

    private static long ToUnixMilliseconds(DateTimeOffset value) => value.ToUnixTimeMilliseconds();
    private static long? ToNullableUnixMilliseconds(DateTimeOffset? value) => value?.ToUnixTimeMilliseconds();
    private static DateTimeOffset FromUnixMilliseconds(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value);
    private static DateTimeOffset? FromNullableUnixMilliseconds(long? value) => value is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(value.Value);

    private sealed class PasswordEntryRow
    {
        public long Id { get; init; }
        public string Title { get; init; } = "";
        public string Website { get; init; } = "";
        public string Username { get; init; } = "";
        public string Password { get; init; } = "";
        public string Notes { get; init; } = "";
        public long CreatedAt { get; init; }
        public long UpdatedAt { get; init; }
        public bool IsFavorite { get; init; }
        public int SortOrder { get; init; }
        public bool IsGroupCover { get; init; }
        public string AppPackageName { get; init; } = "";
        public string AppName { get; init; } = "";
        public string Email { get; init; } = "";
        public string Phone { get; init; } = "";
        public string AddressLine { get; init; } = "";
        public string City { get; init; } = "";
        public string State { get; init; } = "";
        public string ZipCode { get; init; } = "";
        public string Country { get; init; } = "";
        public string CreditCardNumber { get; init; } = "";
        public string CreditCardHolder { get; init; } = "";
        public string CreditCardExpiry { get; init; } = "";
        public string CreditCardCvv { get; init; } = "";
        public long? CategoryId { get; init; }
        public long? BoundNoteId { get; init; }
        public long? KeepassDatabaseId { get; init; }
        public string? KeepassGroupPath { get; init; }
        public string? KeepassEntryUuid { get; init; }
        public string? KeepassGroupUuid { get; init; }
        public long? MdbxDatabaseId { get; init; }
        public string? MdbxFolderId { get; init; }
        public string AuthenticatorKey { get; init; } = "";
        public string PasskeyBindings { get; init; } = "";
        public string SshKeyData { get; init; } = "";
        public string LoginType { get; init; } = "PASSWORD";
        public string SsoProvider { get; init; } = "";
        public long? SsoRefEntryId { get; init; }
        public string WifiMetadata { get; init; } = "";
        public string CustomIconType { get; init; } = "NONE";
        public string? CustomIconValue { get; init; }
        public long CustomIconUpdatedAt { get; init; }
        public bool IsDeleted { get; init; }
        public long? DeletedAt { get; init; }
        public bool IsArchived { get; init; }
        public long? ArchivedAt { get; init; }
        public string? ReplicaGroupId { get; init; }
        public long? BitwardenVaultId { get; init; }
        public string? BitwardenCipherId { get; init; }
        public string? BitwardenFolderId { get; init; }
        public string? BitwardenRevisionDate { get; init; }
        public int BitwardenCipherType { get; init; }
        public bool BitwardenLocalModified { get; init; }
    }

    private sealed class CustomFieldRow
    {
        public long Id { get; init; }
        public long EntryId { get; init; }
        public string Title { get; init; } = "";
        public string Value { get; init; } = "";
        public bool IsProtected { get; init; }
        public int SortOrder { get; init; }
    }

    private sealed class AttachmentRow
    {
        public long Id { get; init; }
        public string OwnerType { get; init; } = "";
        public long OwnerId { get; init; }
        public string FileName { get; init; } = "";
        public string ContentType { get; init; } = "";
        public string StoragePath { get; init; } = "";
        public long SizeBytes { get; init; }
        public long CreatedAt { get; init; }
        public long? BitwardenVaultId { get; init; }
        public string? KeepassBinaryRef { get; init; }
    }

    private sealed class PasswordHistoryEntryRow
    {
        public long Id { get; init; }
        public long EntryId { get; init; }
        public string Password { get; init; } = "";
        public long LastUsedAt { get; init; }
    }

    private sealed class PasswordQuickAccessRecordRow
    {
        public long PasswordId { get; init; }
        public int OpenCount { get; init; }
        public long LastOpenedAt { get; init; }
    }

    private sealed class SecureItemRow
    {
        public long Id { get; init; }
        public string ItemType { get; init; } = "";
        public string Title { get; init; } = "";
        public string Notes { get; init; } = "";
        public bool IsFavorite { get; init; }
        public int SortOrder { get; init; }
        public long CreatedAt { get; init; }
        public long UpdatedAt { get; init; }
        public string ItemData { get; init; } = "{}";
        public string ImagePaths { get; init; } = "[]";
        public long? BoundPasswordId { get; init; }
        public long? CategoryId { get; init; }
        public long? KeepassDatabaseId { get; init; }
        public string? KeepassGroupPath { get; init; }
        public string? KeepassEntryUuid { get; init; }
        public string? KeepassGroupUuid { get; init; }
        public long? MdbxDatabaseId { get; init; }
        public string? MdbxFolderId { get; init; }
        public bool IsDeleted { get; init; }
        public long? DeletedAt { get; init; }
        public string? ReplicaGroupId { get; init; }
        public long? BitwardenVaultId { get; init; }
        public string? BitwardenCipherId { get; init; }
        public string? BitwardenFolderId { get; init; }
        public string? BitwardenRevisionDate { get; init; }
        public bool BitwardenLocalModified { get; init; }
        public string SyncStatus { get; init; } = "NONE";
    }

    private sealed class CategoryRow
    {
        public long Id { get; init; }
        public string Name { get; init; } = "";
        public int SortOrder { get; init; }
        public long? MdbxDatabaseId { get; init; }
    }

    private sealed class MdbxDatabaseRow
    {
        public long Id { get; init; }
        public string Name { get; init; } = "";
        public string FilePath { get; init; } = "";
        public string StorageLocation { get; init; } = "REMOTE_WEBDAV";
        public string SourceType { get; init; } = "REMOTE_WEBDAV";
        public long? SourceId { get; init; }
        public string TigaMode { get; init; } = "MULTI";
        public string? EncryptedPassword { get; init; }
        public string UnlockMethod { get; init; } = "password";
        public string KdfProfile { get; init; } = "argon2id";
        public string? KeyFileName { get; init; }
        public string? KeyFileUri { get; init; }
        public string? KeyFileFingerprint { get; init; }
        public string? Description { get; init; }
        public long CreatedAt { get; init; }
        public long LastAccessedAt { get; init; }
        public long? LastSyncedAt { get; init; }
        public bool IsDefault { get; init; }
        public int ProjectCount { get; init; }
        public int SortOrder { get; init; }
        public string? WorkingCopyPath { get; init; }
        public string? CacheCopyPath { get; init; }
        public bool IsOfflineAvailable { get; init; }
        public string LastSyncStatus { get; init; } = "LOCAL_ONLY";
        public string? LastSyncError { get; init; }
    }

    private sealed class OperationLogRow
    {
        public long Id { get; init; }
        public string ItemType { get; init; } = "";
        public long ItemId { get; init; }
        public string ItemTitle { get; init; } = "";
        public string OperationType { get; init; } = "";
        public string ChangesJson { get; init; } = "";
        public string DeviceId { get; init; } = "";
        public string DeviceName { get; init; } = "";
        public long Timestamp { get; init; }
        public bool IsReverted { get; init; }
    }
}
