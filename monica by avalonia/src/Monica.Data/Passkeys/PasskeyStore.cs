using Dapper;
using Monica.Core.Models;
using Monica.Core.Passkeys;
using Monica.Core.Services;

namespace Monica.Data.Passkeys;

public sealed class NativePasskeyProjection
{
    public long RecordId { get; set; }
    public long DatabaseId { get; set; }
    public string ObjectId { get; set; } = "";
}

public interface IPasskeyStore
{
    Task<long> SaveAsync(
        PasskeyEntry entry,
        string? privateKeyPkcs8Base64 = null,
        CancellationToken cancellationToken = default);

    Task<PasskeyEntry?> GetAsync(long id, CancellationToken cancellationToken = default);

    Task<PasskeyEntry?> FindAsync(
        string credentialId,
        string? rpId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PasskeyEntry>> ListByRpIdAsync(
        string rpId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PasskeyEntry>> ListAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PasskeyEntry>>([]);

    Task<string?> ResolvePrivateKeyAsync(
        PasskeyEntry entry,
        CancellationToken cancellationToken = default);

    Task MarkUsedAsync(long id, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NativePasskeyProjection>> ListNativeProjectionsAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<NativePasskeyProjection>>([]);
    Task SaveNativeProjectionAsync(long recordId, long databaseId, string objectId, CancellationToken token) => Task.CompletedTask;
    Task RemoveNativeProjectionAsync(long databaseId, string objectId, CancellationToken token) => Task.CompletedTask;
}

public sealed class PasskeyStore(
    ISqliteConnectionFactory connectionFactory,
    IDatabaseMigrator migrator,
    IPasskeyPrivateKeyStore privateKeyStore) : IPasskeyStore
{
    private const string ProjectionTable = "CREATE TABLE IF NOT EXISTS device_sync_native_passkeys(database_id INTEGER NOT NULL,object_id TEXT NOT NULL,record_id INTEGER NOT NULL,PRIMARY KEY(database_id,object_id))";
    public async Task<IReadOnlyList<NativePasskeyProjection>> ListNativeProjectionsAsync(CancellationToken token)
    {
        await migrator.MigrateAsync(token); await using var connection = connectionFactory.CreateConnection(); await connection.OpenAsync(token);
        await connection.ExecuteAsync(new CommandDefinition(ProjectionTable, cancellationToken: token));
        return (await connection.QueryAsync<NativePasskeyProjection>(new CommandDefinition(
            "SELECT record_id AS RecordId,database_id AS DatabaseId,object_id AS ObjectId FROM device_sync_native_passkeys", cancellationToken: token))).ToArray();
    }
    public async Task SaveNativeProjectionAsync(long recordId, long databaseId, string objectId, CancellationToken token)
    {
        await migrator.MigrateAsync(token); await using var connection = connectionFactory.CreateConnection(); await connection.OpenAsync(token);
        await connection.ExecuteAsync(new CommandDefinition(ProjectionTable, cancellationToken: token));
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO device_sync_native_passkeys(database_id,object_id,record_id) VALUES(@DatabaseId,@ObjectId,@RecordId) ON CONFLICT(database_id,object_id) DO UPDATE SET record_id=excluded.record_id",
            new { DatabaseId = databaseId, ObjectId = objectId, RecordId = recordId }, cancellationToken: token));
    }
    public async Task RemoveNativeProjectionAsync(long databaseId, string objectId, CancellationToken token)
    {
        await migrator.MigrateAsync(token); await using var connection = connectionFactory.CreateConnection(); await connection.OpenAsync(token);
        await connection.ExecuteAsync(new CommandDefinition(ProjectionTable, cancellationToken: token));
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM device_sync_native_passkeys WHERE database_id=@DatabaseId AND object_id=@ObjectId", new { DatabaseId = databaseId, ObjectId = objectId }, cancellationToken: token));
    }
    private const string SelectColumns =
        """
        SELECT id AS Id,
               credential_id AS CredentialId,
               rp_id AS RpId,
               rp_name AS RpName,
               user_id AS UserId,
               user_name AS UserName,
               user_display_name AS UserDisplayName,
               public_key_algorithm AS PublicKeyAlgorithm,
               public_key AS PublicKey,
               private_key_alias AS PrivateKeyAlias,
               created_at AS CreatedAt,
               last_used_at AS LastUsedAt,
               use_count AS UseCount,
               icon_url AS IconUrl,
               is_discoverable AS IsDiscoverable,
               is_user_verification_required AS IsUserVerificationRequired,
               transports AS Transports,
               aaguid AS Aaguid,
               sign_count AS SignCount,
               is_backed_up AS IsBackedUp,
               backup_eligible AS BackupEligible,
               backup_state AS BackupState,
               notes AS Notes,
               bound_password_id AS BoundPasswordId,
               category_id AS CategoryId,
               keepass_database_id AS KeepassDatabaseId,
               keepass_group_path AS KeepassGroupPath,
               mdbx_database_id AS MdbxDatabaseId,
               mdbx_folder_id AS MdbxFolderId,
               bitwarden_vault_id AS BitwardenVaultId,
               bitwarden_folder_id AS BitwardenFolderId,
               bitwarden_cipher_id AS BitwardenCipherId,
               sync_status AS SyncStatus,
               passkey_mode AS PasskeyMode
        FROM passkeys
        """;

    // Every statement stays a compile-time constant, including the ones built on top of SelectColumns:
    // Dapper.AOT only intercepts a call whose command text it can fold at build time, and an interpolated
    // string is not a constant, so it would quietly fall back to reflection and fail in the AOT build.
    private const string SelectById = SelectColumns + " WHERE id = @Id";

    private const string SelectByCredential = SelectColumns +
        " WHERE (credential_id = @CredentialId OR credential_id = @WebAuthnId)" +
        " AND (@RpId IS NULL OR rp_id = @RpId)" +
        " ORDER BY id LIMIT 1";

    private const string SelectByRpId = SelectColumns + " WHERE rp_id = @RpId ORDER BY user_name, id";

    private const string SelectAll = SelectColumns + " ORDER BY rp_id, user_name, id";

    private const string SelectDuplicate = SelectColumns +
        " WHERE (credential_id = @CredentialId OR credential_id = @WebAuthnId)" +
        " AND rp_id = @RpId AND id <> @Id LIMIT 1";

    public async Task<long> SaveAsync(
        PasskeyEntry entry,
        string? privateKeyPkcs8Base64 = null,
        CancellationToken cancellationToken = default)
    {
        Validate(entry);
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var existing = entry.Id > 0
            ? await connection.QueryFirstOrDefaultAsync<PasskeyRow>(new CommandDefinition(
                SelectById, new { entry.Id }, transaction, cancellationToken: cancellationToken))
            : null;
        if (entry.Id > 0 && existing is null)
        {
            throw new InvalidOperationException("The passkey being updated no longer exists.");
        }
        if (existing is not null)
        {
            var oldEligibility = existing.BackupEligible is null ? (bool?)null : existing.BackupEligible != 0;
            if (oldEligibility is not null && entry.BackupEligible is not null && oldEligibility != entry.BackupEligible)
                throw new InvalidDataException("passkey_conflict");
            entry.BackupEligible ??= oldEligibility;
            entry.BackupState ??= existing.BackupState is null ? null : existing.BackupState != 0;
        }
        _ = PasskeyAuthenticatorDataCodec.ResolveAssertionFlags(entry.BackupEligible, entry.BackupState);

        var duplicate = await connection.QueryFirstOrDefaultAsync<PasskeyRow>(new CommandDefinition(
            SelectDuplicate,
            new
            {
                entry.Id,
                entry.RpId,
                CredentialId = PasskeyCredentialId.Normalize(entry.CredentialId),
                WebAuthnId = PasskeyCredentialId.ToWebAuthnId(entry.CredentialId)
            },
            transaction,
            cancellationToken: cancellationToken));
        if (duplicate is not null)
        {
            throw new InvalidOperationException("This relying party already has a passkey with that credential id.");
        }

        var protectedReference = await privateKeyStore.ProtectInTransactionAsync(
            connection, transaction, entry.CredentialId, entry.RpId, entry.UserId,
            privateKeyPkcs8Base64 ?? entry.PrivateKeyAlias, cancellationToken);
        var savedId = entry.Id;
        if (entry.Id > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                UpdateSql,
                ToParameters(entry, protectedReference),
                transaction,
                cancellationToken: cancellationToken));
        }
        else
        {
            await connection.ExecuteAsync(new CommandDefinition(
                InsertSql,
                ToParameters(entry, protectedReference),
                transaction,
                cancellationToken: cancellationToken));
            savedId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                LastInsertRowIdSql,
                transaction: transaction,
                cancellationToken: cancellationToken));
        }

        if (existing is not null && existing.PrivateKeyAlias != protectedReference)
        {
            await privateKeyStore.RemoveInTransactionAsync(
                connection, transaction, existing.PrivateKeyAlias, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        entry.Id = savedId;
        entry.PrivateKeyAlias = protectedReference;
        return savedId;
    }

    public async Task<PasskeyEntry?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync<PasskeyRow>(new CommandDefinition(
            SelectById,
            new { Id = id },
            cancellationToken: cancellationToken));
        return row?.ToEntry();
    }

    public async Task<PasskeyEntry?> FindAsync(
        string credentialId,
        string? rpId = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = PasskeyCredentialId.Normalize(credentialId);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var normalizedRpId = rpId is null ? null : PasskeyRpId.Normalize(rpId);
        if (rpId is not null && normalizedRpId is null)
        {
            return null;
        }

        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync<PasskeyRow>(new CommandDefinition(
            SelectByCredential,
            new
            {
                CredentialId = normalized,
                WebAuthnId = PasskeyCredentialId.ToWebAuthnId(normalized),
                RpId = normalizedRpId
            },
            cancellationToken: cancellationToken));
        return row?.ToEntry();
    }

    public async Task<IReadOnlyList<PasskeyEntry>> ListByRpIdAsync(
        string rpId,
        CancellationToken cancellationToken = default)
    {
        var normalized = PasskeyRpId.Normalize(rpId);
        if (normalized is null)
        {
            return [];
        }

        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<PasskeyRow>(new CommandDefinition(
            SelectByRpId,
            new { RpId = normalized },
            cancellationToken: cancellationToken));
        return rows.Select(static row => row.ToEntry()).ToList();
    }

    public async Task<IReadOnlyList<PasskeyEntry>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<PasskeyRow>(new CommandDefinition(
            SelectAll,
            cancellationToken: cancellationToken));
        return rows.Select(static row => row.ToEntry()).ToList();
    }

    public Task<string?> ResolvePrivateKeyAsync(
        PasskeyEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return privateKeyStore.ResolveAsync(entry.PrivateKeyAlias, cancellationToken);
    }

    public async Task MarkUsedAsync(long id, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE passkeys
            SET use_count = use_count + 1,
                last_used_at = @Now,
                sign_count = 0
            WHERE id = @Id
            """,
            new { Id = id, Now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        var entry = await connection.QueryFirstOrDefaultAsync<PasskeyRow>(new CommandDefinition(
            SelectById, new { Id = id }, transaction, cancellationToken: cancellationToken));
        if (entry is null)
        {
            return false;
        }

        var deleted = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM passkeys WHERE id = @Id",
            new { Id = id },
            transaction,
            cancellationToken: cancellationToken)) == 1;
        await privateKeyStore.RemoveInTransactionAsync(
            connection, transaction, entry.PrivateKeyAlias, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }

    private const string InsertSql =
        """
        INSERT INTO passkeys (
            credential_id, rp_id, rp_name, user_id, user_name, user_display_name,
            public_key_algorithm, public_key, private_key_alias, created_at, last_used_at, use_count,
            icon_url, is_discoverable, is_user_verification_required, transports, aaguid, sign_count,
            is_backed_up, backup_eligible, backup_state, notes, bound_password_id, category_id, keepass_database_id, keepass_group_path,
            mdbx_database_id, mdbx_folder_id, bitwarden_vault_id, bitwarden_folder_id, bitwarden_cipher_id,
            sync_status, passkey_mode)
        VALUES (
            @CredentialId, @RpId, @RpName, @UserId, @UserName, @UserDisplayName,
            @PublicKeyAlgorithm, @PublicKey, @PrivateKeyAlias, @CreatedAt, @LastUsedAt, @UseCount,
            @IconUrl, @IsDiscoverable, @IsUserVerificationRequired, @Transports, @Aaguid, @SignCount,
            @IsBackedUp, @BackupEligible, @BackupState, @Notes, @BoundPasswordId, @CategoryId, @KeepassDatabaseId, @KeepassGroupPath,
            @MdbxDatabaseId, @MdbxFolderId, @BitwardenVaultId, @BitwardenFolderId, @BitwardenCipherId,
            @SyncStatus, @PasskeyMode);
        """;

    private const string LastInsertRowIdSql = "SELECT last_insert_rowid();";

    private const string UpdateSql =
        """
        UPDATE passkeys SET
            credential_id = @CredentialId, rp_id = @RpId, rp_name = @RpName,
            user_id = @UserId, user_name = @UserName, user_display_name = @UserDisplayName,
            public_key_algorithm = @PublicKeyAlgorithm, public_key = @PublicKey,
            private_key_alias = @PrivateKeyAlias, created_at = @CreatedAt, last_used_at = MAX(last_used_at, @LastUsedAt),
            use_count = MAX(use_count, @UseCount), icon_url = @IconUrl, is_discoverable = @IsDiscoverable,
            is_user_verification_required = @IsUserVerificationRequired, transports = @Transports,
            aaguid = @Aaguid, sign_count = @SignCount, is_backed_up = @IsBackedUp, notes = @Notes,
            backup_eligible = @BackupEligible, backup_state = @BackupState,
            bound_password_id = @BoundPasswordId, category_id = @CategoryId,
            keepass_database_id = @KeepassDatabaseId, keepass_group_path = @KeepassGroupPath,
            mdbx_database_id = @MdbxDatabaseId, mdbx_folder_id = @MdbxFolderId,
            bitwarden_vault_id = @BitwardenVaultId, bitwarden_folder_id = @BitwardenFolderId,
            bitwarden_cipher_id = @BitwardenCipherId, sync_status = @SyncStatus,
            passkey_mode = @PasskeyMode
        WHERE id = @Id
        """;

    private static object ToParameters(PasskeyEntry entry, string privateKeyAlias) => new
    {
        entry.Id,
        entry.CredentialId,
        entry.RpId,
        entry.RpName,
        entry.UserId,
        entry.UserName,
        entry.UserDisplayName,
        entry.PublicKeyAlgorithm,
        entry.PublicKey,
        PrivateKeyAlias = privateKeyAlias,
        CreatedAt = entry.CreatedAt.ToUnixTimeMilliseconds(),
        LastUsedAt = entry.LastUsedAt.ToUnixTimeMilliseconds(),
        entry.UseCount,
        entry.IconUrl,
        IsDiscoverable = entry.IsDiscoverable ? 1 : 0,
        IsUserVerificationRequired = entry.IsUserVerificationRequired ? 1 : 0,
        entry.Transports,
        entry.Aaguid,
        entry.SignCount,
        IsBackedUp = entry.IsBackedUp ? 1 : 0,
        BackupEligible = entry.BackupEligible is null ? (int?)null : entry.BackupEligible.Value ? 1 : 0,
        BackupState = entry.BackupState is null ? (int?)null : entry.BackupState.Value ? 1 : 0,
        entry.Notes,
        entry.BoundPasswordId,
        entry.CategoryId,
        entry.KeepassDatabaseId,
        entry.KeepassGroupPath,
        entry.MdbxDatabaseId,
        entry.MdbxFolderId,
        entry.BitwardenVaultId,
        entry.BitwardenFolderId,
        entry.BitwardenCipherId,
        SyncStatus = entry.SyncStatus.ToString().ToUpperInvariant(),
        entry.PasskeyMode
    };

    private static void Validate(PasskeyEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(entry.CredentialId))
        {
            throw new ArgumentException("A passkey needs a credential id.");
        }

        // Normalizing is also the point where a junk rp id becomes null, so the result has to be checked
        // before it is written back: assigning null into the non-null column only surfaces later as a
        // NOT NULL constraint failure from SQLite.
        var rpId = PasskeyRpId.Normalize(entry.RpId)
            ?? throw new ArgumentException($"'{entry.RpId}' is not a usable relying-party id.");
        if (string.IsNullOrWhiteSpace(entry.PublicKey) || !PasskeyAlgorithm.CanVerify(entry.PublicKeyAlgorithm))
        {
            throw new ArgumentException(
                $"A passkey needs a public key and a verifiable algorithm ({entry.PublicKeyAlgorithm}).");
        }

        try { _ = PasskeyKeyMaterialGenerator.BuildCosePublicKey(entry.PublicKeyAlgorithm, entry.PublicKey); }
        catch (Exception error) when (error is System.Security.Cryptography.CryptographicException or FormatException or ArgumentException or IOException)
        { throw new ArgumentException("The public key does not match the passkey algorithm.", nameof(entry), error); }

        entry.CredentialId = PasskeyCredentialId.ToWebAuthnId(entry.CredentialId)!;
        entry.RpId = rpId;
    }

    internal sealed class PasskeyRow
    {
        public long Id { get; init; }
        public string CredentialId { get; init; } = "";
        public string RpId { get; init; } = "";
        public string RpName { get; init; } = "";
        public string UserId { get; init; } = "";
        public string UserName { get; init; } = "";
        public string UserDisplayName { get; init; } = "";
        public int PublicKeyAlgorithm { get; init; }
        public string PublicKey { get; init; } = "";
        public string PrivateKeyAlias { get; init; } = "";
        public long CreatedAt { get; init; }
        public long LastUsedAt { get; init; }
        public int UseCount { get; init; }
        public string? IconUrl { get; init; }
        public int IsDiscoverable { get; init; }
        public int IsUserVerificationRequired { get; init; }
        public string Transports { get; init; } = "";
        public string Aaguid { get; init; } = "";
        public long SignCount { get; init; }
        public int IsBackedUp { get; init; }
        public int? BackupEligible { get; init; }
        public int? BackupState { get; init; }
        public string Notes { get; init; } = "";
        public long? BoundPasswordId { get; init; }
        public long? CategoryId { get; init; }
        public long? KeepassDatabaseId { get; init; }
        public string? KeepassGroupPath { get; init; }
        public long? MdbxDatabaseId { get; init; }
        public string? MdbxFolderId { get; init; }
        public long? BitwardenVaultId { get; init; }
        public string? BitwardenFolderId { get; init; }
        public string? BitwardenCipherId { get; init; }
        public string SyncStatus { get; init; } = "";
        public string PasskeyMode { get; init; } = "";

        public PasskeyEntry ToEntry() => new()
        {
            Id = Id,
            CredentialId = CredentialId,
            RpId = RpId,
            RpName = RpName,
            UserId = UserId,
            UserName = UserName,
            UserDisplayName = UserDisplayName,
            PublicKeyAlgorithm = PublicKeyAlgorithm,
            PublicKey = PublicKey,
            PrivateKeyAlias = PrivateKeyAlias,
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(CreatedAt),
            LastUsedAt = DateTimeOffset.FromUnixTimeMilliseconds(LastUsedAt),
            UseCount = UseCount,
            IconUrl = IconUrl,
            IsDiscoverable = IsDiscoverable != 0,
            IsUserVerificationRequired = IsUserVerificationRequired != 0,
            Transports = Transports,
            Aaguid = Aaguid,
            SignCount = SignCount,
            IsBackedUp = IsBackedUp != 0,
            BackupEligible = BackupEligible is null ? null : BackupEligible != 0,
            BackupState = BackupState is null ? null : BackupState != 0,
            Notes = Notes,
            BoundPasswordId = BoundPasswordId,
            CategoryId = CategoryId,
            KeepassDatabaseId = KeepassDatabaseId,
            KeepassGroupPath = KeepassGroupPath,
            MdbxDatabaseId = MdbxDatabaseId,
            MdbxFolderId = MdbxFolderId,
            BitwardenVaultId = BitwardenVaultId,
            BitwardenFolderId = BitwardenFolderId,
            BitwardenCipherId = BitwardenCipherId,
            SyncStatus = Enum.TryParse<SyncStatus>(SyncStatus, true, out var status)
                ? status
                : Monica.Core.Models.SyncStatus.None,
            PasskeyMode = PasskeyMode
        };
    }
}
