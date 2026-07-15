using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Repositories;

namespace Monica.Tests;

public sealed partial class DataRepositoryTests
{
    [Fact]
    public async Task Migration_sets_current_schema_version()
    {
        var path = GetTempDatabasePath();
        var factory = new SqliteConnectionFactory(path);
        var migrator = new DatabaseMigrator(factory);

        await migrator.MigrateAsync();

        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(await command.ExecuteScalarAsync());
        Assert.Equal(DatabaseMigrator.CurrentSchemaVersion, version);
    }

    [Fact]
    public async Task Migration_adds_remote_validators_to_existing_mdbx_metadata()
    {
        var path = GetTempDatabasePath();
        var factory = new SqliteConnectionFactory(path);
        var migrator = new DatabaseMigrator(factory);

        await using (var legacyConnection = factory.CreateConnection())
        {
            await legacyConnection.OpenAsync();
            await using var legacyCommand = legacyConnection.CreateCommand();
            legacyCommand.CommandText =
                """
                CREATE TABLE local_mdbx_databases (
                    id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
                    name TEXT NOT NULL,
                    file_path TEXT NOT NULL,
                    storage_location TEXT NOT NULL,
                    source_type TEXT NOT NULL,
                    source_id INTEGER DEFAULT NULL,
                    tiga_mode TEXT NOT NULL DEFAULT 'MULTI',
                    encrypted_password TEXT DEFAULT NULL,
                    unlock_method TEXT NOT NULL DEFAULT 'password',
                    kdf_profile TEXT NOT NULL DEFAULT 'argon2id',
                    key_file_name TEXT DEFAULT NULL,
                    key_file_uri TEXT DEFAULT NULL,
                    key_file_fingerprint TEXT DEFAULT NULL,
                    description TEXT DEFAULT NULL,
                    created_at INTEGER NOT NULL,
                    last_accessed_at INTEGER NOT NULL,
                    last_synced_at INTEGER DEFAULT NULL,
                    is_default INTEGER NOT NULL DEFAULT 0,
                    project_count INTEGER NOT NULL DEFAULT 0,
                    sort_order INTEGER NOT NULL DEFAULT 0,
                    working_copy_path TEXT DEFAULT NULL,
                    cache_copy_path TEXT DEFAULT NULL,
                    is_offline_available INTEGER NOT NULL DEFAULT 0,
                    last_sync_status TEXT NOT NULL DEFAULT 'LOCAL_ONLY',
                    last_sync_error TEXT DEFAULT NULL
                );
                INSERT INTO local_mdbx_databases (
                    name, file_path, storage_location, source_type, created_at, last_accessed_at,
                    last_synced_at, is_default, working_copy_path, is_offline_available, last_sync_status)
                VALUES ('Legacy WebDAV', '/Monica/legacy.mdbx', 'REMOTE_WEBDAV', 'REMOTE_WEBDAV',
                    1000, 2000, 3000, 1, 'legacy-local.mdbx', 1, 'SYNCED');
                PRAGMA user_version=70;
                """;
            await legacyCommand.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync();

        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(local_mdbx_databases);";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        Assert.Contains("remote_etag", columns);
        Assert.Contains("remote_last_modified_at", columns);
        await reader.DisposeAsync();
        command.CommandText = "SELECT name, last_sync_status, remote_etag, remote_last_modified_at FROM local_mdbx_databases WHERE id = 1;";
        await using var preservedReader = await command.ExecuteReaderAsync();
        Assert.True(await preservedReader.ReadAsync());
        Assert.Equal("Legacy WebDAV", preservedReader.GetString(0));
        Assert.Equal("SYNCED", preservedReader.GetString(1));
        Assert.True(preservedReader.IsDBNull(2));
        Assert.True(preservedReader.IsDBNull(3));
    }

    [Fact]
    public async Task Migration_v70_removes_quick_access_foreign_key_and_preserves_existing_counters()
    {
        var path = GetTempDatabasePath();
        var factory = new SqliteConnectionFactory(path);
        var migrator = new DatabaseMigrator(factory);
        await migrator.MigrateAsync();
        var legacyPassword = new PasswordEntry { Title = "Legacy quick access", Password = "secret" };
        await new MonicaRepository(factory, migrator).SavePasswordAsync(legacyPassword);

        await using (var connection = factory.CreateConnection())
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                PRAGMA foreign_keys=ON;
                DROP TABLE password_quick_access_records;
                CREATE TABLE password_quick_access_records (
                    password_id INTEGER PRIMARY KEY NOT NULL,
                    open_count INTEGER NOT NULL DEFAULT 0,
                    last_opened_at INTEGER NOT NULL,
                    FOREIGN KEY(password_id) REFERENCES password_entries(id) ON DELETE CASCADE
                );
                INSERT INTO password_quick_access_records(password_id, open_count, last_opened_at) VALUES(@PasswordId, 7, 1234);
                PRAGMA user_version=69;
                """;
            command.Parameters.AddWithValue("@PasswordId", legacyPassword.Id);
            await command.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync();

        await using var verifyConnection = factory.CreateConnection();
        await verifyConnection.OpenAsync();
        await using (var foreignKeyCommand = verifyConnection.CreateCommand())
        {
            foreignKeyCommand.CommandText = "PRAGMA foreign_key_list(password_quick_access_records);";
            await using var reader = await foreignKeyCommand.ExecuteReaderAsync();
            Assert.False(await reader.ReadAsync());
        }

        await using (var preservedCommand = verifyConnection.CreateCommand())
        {
            preservedCommand.CommandText = "SELECT open_count FROM password_quick_access_records WHERE password_id = @PasswordId;";
            preservedCommand.Parameters.AddWithValue("@PasswordId", legacyPassword.Id);
            Assert.Equal(7L, Convert.ToInt64(await preservedCommand.ExecuteScalarAsync()));
        }

        await using (var canonicalIdCommand = verifyConnection.CreateCommand())
        {
            canonicalIdCommand.CommandText = "INSERT INTO password_quick_access_records(password_id, open_count, last_opened_at) VALUES(9001, 1, 5678);";
            Assert.Equal(1, await canonicalIdCommand.ExecuteNonQueryAsync());
        }
    }

    [Fact]
    public async Task Migration_refuses_legacy_pascal_case_windows_vault()
    {
        var path = GetTempDatabasePath();
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE PasswordEntries (Id INTEGER PRIMARY KEY, Title TEXT NOT NULL);";
            await command.ExecuteNonQueryAsync();
        }

        var factory = new SqliteConnectionFactory(path);
        var migrator = new DatabaseMigrator(factory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => migrator.MigrateAsync());
        Assert.Contains("Monica for Windows", exception.Message, StringComparison.Ordinal);

        await using var verifyConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Mode=ReadOnly");
        await verifyConnection.OpenAsync();
        Assert.True(await HasTableAsync(verifyConnection, "PasswordEntries"));
        Assert.False(await HasTableAsync(verifyConnection, "password_entries"));
    }

    [Fact]
    public async Task Repository_saves_password_secure_item_category_and_mdbx_metadata()
    {
        var path = GetTempDatabasePath();
        var factory = new SqliteConnectionFactory(path);
        var migrator = new DatabaseMigrator(factory);
        var repository = new MonicaRepository(factory, migrator);

        var category = new Category { Name = "Work" };
        await repository.SaveCategoryAsync(category);

        var password = new PasswordEntry
        {
            Title = "GitHub",
            Username = "dev",
            Password = "encrypted",
            Website = "https://github.com",
            CategoryId = category.Id
        };
        await repository.SavePasswordAsync(password);

        var totp = new SecureItem
        {
            ItemType = VaultItemType.Totp,
            Title = "GitHub OTP",
            ItemData = """{"secret":"JBSWY3DPEHPK3PXP"}"""
        };
        await repository.SaveSecureItemAsync(totp);

        var mdbx = new LocalMdbxDatabase
        {
            Name = "Local",
            FilePath = Path.Combine(Path.GetTempPath(), "local.mdbx"),
            StorageLocation = MdbxStorageLocation.Internal,
            SourceType = "LOCAL_INTERNAL",
            RemoteETag = "\"repo-etag\"",
            RemoteLastModifiedAt = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000)
        };
        await repository.SaveMdbxDatabaseAsync(mdbx);

        Assert.Single(await repository.GetCategoriesAsync());
        Assert.Single(await repository.GetPasswordsAsync());
        Assert.Single(await repository.GetSecureItemsAsync(VaultItemType.Totp));
        var reloadedMdbx = Assert.Single(await repository.GetMdbxDatabasesAsync());
        Assert.Equal(mdbx.RemoteETag, reloadedMdbx.RemoteETag);
        Assert.Equal(mdbx.RemoteLastModifiedAt, reloadedMdbx.RemoteLastModifiedAt);
    }

    [Fact]
    public async Task Repository_soft_deletes_password()
    {
        var path = GetTempDatabasePath();
        var factory = new SqliteConnectionFactory(path);
        var repository = new MonicaRepository(factory, new DatabaseMigrator(factory));
        var entry = new PasswordEntry { Title = "Trash me", Password = "encrypted" };
        await repository.SavePasswordAsync(entry);

        await repository.SoftDeletePasswordAsync(entry.Id);

        Assert.Empty(await repository.GetPasswordsAsync());
        Assert.Single(await repository.GetPasswordsAsync(includeDeleted: true));
    }

    [Fact]
    public async Task Repository_restores_and_permanently_deletes_password_with_bound_data()
    {
        var path = GetTempDatabasePath();
        var factory = new SqliteConnectionFactory(path);
        var repository = new MonicaRepository(factory, new DatabaseMigrator(factory));
        var entry = new PasswordEntry { Title = "Recover me", Password = "encrypted" };
        await repository.SavePasswordAsync(entry);
        await repository.ReplaceCustomFieldsAsync(entry.Id, [new CustomField { Title = "PIN", Value = "1234" }]);
        var totp = new SecureItem
        {
            ItemType = VaultItemType.Totp,
            Title = "Recover me",
            BoundPasswordId = entry.Id,
            ItemData = """{"secret":"JBSWY3DPEHPK3PXP"}"""
        };
        await repository.SaveSecureItemAsync(totp);

        await repository.SoftDeletePasswordAsync(entry.Id);

        Assert.Empty(await repository.GetPasswordsAsync());
        Assert.Empty(await repository.GetSecureItemsByBoundPasswordIdAsync(entry.Id));
        Assert.Single(await repository.GetSecureItemsByBoundPasswordIdAsync(entry.Id, includeDeleted: true));

        await repository.RestorePasswordAsync(entry.Id);

        Assert.Single(await repository.GetPasswordsAsync());
        Assert.Single(await repository.GetSecureItemsByBoundPasswordIdAsync(entry.Id));

        await repository.SoftDeletePasswordAsync(entry.Id);
        await repository.DeletePasswordPermanentlyAsync(entry.Id);

        Assert.Empty(await repository.GetPasswordsAsync(includeDeleted: true));
        Assert.Empty(await repository.GetCustomFieldsAsync(entry.Id));
        Assert.Empty(await repository.GetPasswordHistoryAsync(entry.Id));
        Assert.Empty(await repository.GetSecureItemsByBoundPasswordIdAsync(entry.Id, includeDeleted: true));
    }

    [Fact]
    public async Task Repository_excludes_archived_passwords_by_default()
    {
        var path = GetTempDatabasePath();
        var factory = new SqliteConnectionFactory(path);
        var repository = new MonicaRepository(factory, new DatabaseMigrator(factory));
        var active = new PasswordEntry { Title = "Active", Password = "one" };
        var archived = new PasswordEntry
        {
            Title = "Archived",
            Password = "two",
            IsArchived = true,
            ArchivedAt = DateTimeOffset.UtcNow
        };
        await repository.SavePasswordAsync(active);
        await repository.SavePasswordAsync(archived);

        Assert.Equal(["Active"], (await repository.GetPasswordsAsync()).Select(item => item.Title).ToArray());
        Assert.Equal(["Archived", "Active"], (await repository.GetPasswordsAsync(includeArchived: true)).Select(item => item.Title).ToArray());

        await repository.SoftDeletePasswordAsync(archived.Id);

        var deleted = (await repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true)).Single(item => item.Id == archived.Id);
        Assert.True(deleted.IsDeleted);
        Assert.False(deleted.IsArchived);
        Assert.Null(deleted.ArchivedAt);
    }

    [Fact]
    public async Task Repository_reads_operation_logs_newest_first()
    {
        var path = GetTempDatabasePath();
        var factory = new SqliteConnectionFactory(path);
        var repository = new MonicaRepository(factory, new DatabaseMigrator(factory));
        await repository.LogAsync(new OperationLog
        {
            ItemType = "PASSWORD",
            ItemId = 1,
            ItemTitle = "Old",
            OperationType = "CREATE",
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5)
        });
        await repository.LogAsync(new OperationLog
        {
            ItemType = "NOTE",
            ItemId = 2,
            ItemTitle = "Note",
            OperationType = "UPDATE",
            Timestamp = DateTimeOffset.UtcNow
        });

        var all = await repository.GetOperationLogsAsync();
        var passwordOnly = await repository.GetOperationLogsAsync(itemType: "PASSWORD");

        Assert.Equal(["NOTE", "PASSWORD"], all.Select(item => item.ItemType).ToArray());
        var password = Assert.Single(passwordOnly);
        Assert.Equal("Old", password.ItemTitle);
        Assert.Equal("CREATE", password.OperationType);
    }

    [Fact]
    public async Task Repository_saves_reads_and_deletes_attachments()
    {
        var path = GetTempDatabasePath();
        var factory = new SqliteConnectionFactory(path);
        var repository = new MonicaRepository(factory, new DatabaseMigrator(factory));
        var first = new Attachment
        {
            OwnerType = "password",
            OwnerId = 42,
            FileName = "recovery.pdf",
            ContentType = "application/pdf",
            StoragePath = "attachments/recovery.enc",
            SizeBytes = 2048,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        var second = new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = 42,
            FileName = "backup.txt",
            ContentType = "text/plain",
            StoragePath = "attachments/backup.enc",
            SizeBytes = 128,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await repository.SaveAttachmentAsync(first);
        await repository.SaveAttachmentAsync(second);

        var attachments = await repository.GetAttachmentsAsync("PASSWORD", 42);
        var grouped = await repository.GetAttachmentsByOwnerIdsAsync("PASSWORD", [42, 100]);

        Assert.Equal(["backup.txt", "recovery.pdf"], attachments.Select(item => item.FileName).ToArray());
        Assert.Equal("PASSWORD", first.OwnerType);
        Assert.Equal(2, grouped[42].Count);

        await repository.DeleteAttachmentAsync(first.Id);

        var remaining = Assert.Single(await repository.GetAttachmentsAsync("PASSWORD", 42));
        Assert.Equal("backup.txt", remaining.FileName);
    }

    [Fact]
    public async Task Repository_saves_trims_deletes_and_clears_password_history()
    {
        var path = GetTempDatabasePath();
        var factory = new SqliteConnectionFactory(path);
        var repository = new MonicaRepository(factory, new DatabaseMigrator(factory));
        var entry = new PasswordEntry { Title = "History", Password = "current" };
        await repository.SavePasswordAsync(entry);

        for (var index = 0; index < 12; index++)
        {
            await repository.SavePasswordHistoryAsync(new PasswordHistoryEntry
            {
                EntryId = entry.Id,
                Password = $"old-{index}",
                LastUsedAt = DateTimeOffset.UtcNow.AddMinutes(index)
            });
        }

        await repository.TrimPasswordHistoryAsync(entry.Id, 10);

        var history = await repository.GetPasswordHistoryAsync(entry.Id);
        Assert.Equal(10, history.Count);
        Assert.Equal("old-11", history[0].Password);
        Assert.Equal("old-2", history[^1].Password);

        await repository.DeletePasswordHistoryAsync(history[0].Id);

        var afterDelete = await repository.GetPasswordHistoryAsync(entry.Id);
        Assert.Equal(9, afterDelete.Count);
        Assert.DoesNotContain(afterDelete, item => item.Password == "old-11");

        await repository.ClearPasswordHistoryAsync(entry.Id);

        Assert.Empty(await repository.GetPasswordHistoryAsync(entry.Id));
    }

    [Fact]
    public async Task Repository_records_password_quick_access_for_active_passwords()
    {
        var path = GetTempDatabasePath();
        var factory = new SqliteConnectionFactory(path);
        var repository = new MonicaRepository(factory, new DatabaseMigrator(factory));
        var first = new PasswordEntry { Title = "First", Password = "one" };
        var second = new PasswordEntry { Title = "Second", Password = "two" };
        var archived = new PasswordEntry { Title = "Archived", Password = "three", IsArchived = true, ArchivedAt = DateTimeOffset.UtcNow };
        await repository.SavePasswordAsync(first);
        await repository.SavePasswordAsync(second);
        await repository.SavePasswordAsync(archived);

        await repository.RecordPasswordQuickAccessAsync(first.Id);
        await Task.Delay(5);
        await repository.RecordPasswordQuickAccessAsync(second.Id);
        var updatedSecond = await repository.RecordPasswordQuickAccessAsync(second.Id);
        await repository.RecordPasswordQuickAccessAsync(archived.Id);

        var records = await repository.GetPasswordQuickAccessRecordsAsync();

        Assert.Equal([second.Id, first.Id], records.Select(item => item.PasswordId).ToArray());
        Assert.NotNull(updatedSecond);
        Assert.Equal(2, updatedSecond.OpenCount);
        Assert.Equal(2, records[0].OpenCount);
        Assert.Equal(1, records[1].OpenCount);
    }

    [Fact]
    public async Task Repository_clears_password_scope_without_deleting_secure_items()
    {
        var path = GetTempDatabasePath();
        var factory = new SqliteConnectionFactory(path);
        var repository = new MonicaRepository(factory, new DatabaseMigrator(factory));
        var password = new PasswordEntry { Title = "Portal", Password = "one" };
        await repository.SavePasswordAsync(password);
        await repository.ReplaceCustomFieldsAsync(password.Id, [new CustomField { Title = "PIN", Value = "1234" }]);
        await repository.SavePasswordHistoryAsync(new PasswordHistoryEntry
        {
            EntryId = password.Id,
            Password = "old",
            LastUsedAt = DateTimeOffset.UtcNow
        });
        await repository.SaveAttachmentAsync(new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = password.Id,
            FileName = "secret.txt",
            StoragePath = "attachments/secret.enc",
            CreatedAt = DateTimeOffset.UtcNow
        });
        var note = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = "Recovery note",
            BoundPasswordId = password.Id
        };
        await repository.SaveSecureItemAsync(note);
        await repository.LogAsync(new OperationLog { ItemType = "PASSWORD", ItemId = password.Id, ItemTitle = password.Title, OperationType = "CREATE", Timestamp = DateTimeOffset.UtcNow });
        await repository.LogAsync(new OperationLog { ItemType = "NOTE", ItemId = note.Id, ItemTitle = note.Title, OperationType = "CREATE", Timestamp = DateTimeOffset.UtcNow });

        await repository.ClearVaultDataAsync(VaultClearScope.Passwords);

        Assert.Empty(await repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true));
        Assert.Empty(await repository.GetCustomFieldsAsync(password.Id));
        Assert.Empty(await repository.GetPasswordHistoryAsync(password.Id));
        Assert.Empty(await repository.GetAttachmentsAsync("PASSWORD", password.Id));
        Assert.Empty(await repository.GetOperationLogsAsync(itemType: "PASSWORD"));
        Assert.Single(await repository.GetOperationLogsAsync(itemType: "NOTE"));
        var remainingNote = Assert.Single(await repository.GetSecureItemsAsync(VaultItemType.Note));
        Assert.Null(remainingNote.BoundPasswordId);
    }

    [Fact]
    public async Task Repository_clears_all_vault_data_but_keeps_credentials()
    {
        var path = GetTempDatabasePath();
        var factory = new SqliteConnectionFactory(path);
        var migrator = new DatabaseMigrator(factory);
        var repository = new MonicaRepository(factory, migrator);
        var credentialStore = new VaultCredentialStore(factory, migrator);
        await credentialStore.SaveAsync(new MasterPasswordHash("hash", new byte[16], "pbkdf2", 10, 0, 1));

        var category = new Category { Name = "Work" };
        await repository.SaveCategoryAsync(category);
        var password = new PasswordEntry { Title = "Portal", Password = "one", CategoryId = category.Id };
        await repository.SavePasswordAsync(password);
        await repository.SaveSecureItemAsync(new SecureItem { ItemType = VaultItemType.Totp, Title = "OTP", CategoryId = category.Id });
        await repository.SaveAttachmentAsync(new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = password.Id,
            FileName = "secret.txt",
            StoragePath = "attachments/secret.enc",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await repository.SaveMdbxDatabaseAsync(new LocalMdbxDatabase
        {
            Name = "Local",
            FilePath = Path.Combine(Path.GetTempPath(), "local.mdbx"),
            StorageLocation = MdbxStorageLocation.Internal,
            SourceType = "LOCAL_INTERNAL"
        });
        await repository.LogAsync(new OperationLog { ItemType = "PASSWORD", ItemId = password.Id, ItemTitle = password.Title, OperationType = "CREATE", Timestamp = DateTimeOffset.UtcNow });

        await repository.ClearVaultDataAsync(VaultClearScope.All);

        Assert.Empty(await repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true));
        Assert.Empty(await repository.GetSecureItemsAsync());
        Assert.Empty(await repository.GetAttachmentsAsync("PASSWORD", password.Id));
        Assert.Empty(await repository.GetCategoriesAsync());
        Assert.Empty(await repository.GetMdbxDatabasesAsync());
        Assert.Empty(await repository.GetOperationLogsAsync());
        Assert.NotNull(await credentialStore.GetAsync());
    }

    private static string GetTempDatabasePath()
    {
        var path = Path.Combine(Path.GetTempPath(), "monica-tests", $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private static async Task<bool> HasTableAsync(Microsoft.Data.Sqlite.SqliteConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name LIMIT 1;";
        command.Parameters.AddWithValue("$name", tableName);
        return await command.ExecuteScalarAsync() is not null;
    }
}
