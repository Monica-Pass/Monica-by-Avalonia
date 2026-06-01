using Monica.Core.Models;
using Monica.Data;
using Monica.Data.Repositories;

namespace Monica.Tests;

public sealed class DataRepositoryTests
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
            SourceType = "LOCAL_INTERNAL"
        };
        await repository.SaveMdbxDatabaseAsync(mdbx);

        Assert.Single(await repository.GetCategoriesAsync());
        Assert.Single(await repository.GetPasswordsAsync());
        Assert.Single(await repository.GetSecureItemsAsync(VaultItemType.Totp));
        Assert.Single(await repository.GetMdbxDatabasesAsync());
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
        await repository.RecordPasswordQuickAccessAsync(second.Id);
        await repository.RecordPasswordQuickAccessAsync(archived.Id);

        var records = await repository.GetPasswordQuickAccessRecordsAsync();

        Assert.Equal([second.Id, first.Id], records.Select(item => item.PasswordId).ToArray());
        Assert.Equal(2, records[0].OpenCount);
        Assert.Equal(1, records[1].OpenCount);
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
