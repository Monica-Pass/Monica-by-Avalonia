using Microsoft.Data.Sqlite;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Repositories;

namespace Monica.Tests;

public sealed partial class DataRepositoryTests
{
    [Fact]
    public async Task Migration_adds_protected_OneDrive_account_binding_to_v71_metadata()
    {
        var path = GetTempDatabasePath();
        var factory = new SqliteConnectionFactory(path);
        await using (var connection = factory.CreateConnection())
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE local_mdbx_databases (
                    id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
                    name TEXT NOT NULL,
                    file_path TEXT NOT NULL,
                    storage_location TEXT NOT NULL,
                    source_type TEXT NOT NULL,
                    source_id INTEGER DEFAULT NULL,
                    tiga_mode TEXT NOT NULL,
                    encrypted_password TEXT DEFAULT NULL,
                    unlock_method TEXT NOT NULL,
                    kdf_profile TEXT NOT NULL,
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
                    last_sync_status TEXT NOT NULL,
                    last_sync_error TEXT DEFAULT NULL,
                    remote_etag TEXT DEFAULT NULL,
                    remote_last_modified_at INTEGER DEFAULT NULL
                );
                INSERT INTO local_mdbx_databases (
                    name, file_path, storage_location, source_type, tiga_mode, unlock_method, kdf_profile,
                    created_at, last_accessed_at, last_sync_status)
                VALUES ('Legacy OneDrive', 'OneDrive:/Monica/local.mdbx', 'REMOTE_ONEDRIVE', 'REMOTE_ONEDRIVE',
                    'MULTI', 'password', 'argon2id', 1000, 2000, 'SYNCED');
                PRAGMA user_version=71;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await new DatabaseMigrator(factory).MigrateAsync();

        await using var migrated = factory.CreateConnection();
        await migrated.OpenAsync();
        await using var columns = migrated.CreateCommand();
        columns.CommandText = "PRAGMA table_info(local_mdbx_databases);";
        await using var reader = await columns.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(1));
        }

        Assert.Contains("remote_account_id", names);
        Assert.Equal(72, await ReadUserVersionAsync(migrated));
    }

    [Fact]
    public async Task Repository_encrypts_and_roundtrips_OneDrive_account_binding()
    {
        var path = GetTempDatabasePath();
        var factory = new SqliteConnectionFactory(path);
        var crypto = new CryptoService();
        crypto.InitializeSession("test password", new byte[16]);
        var repository = new MonicaRepository(factory, new DatabaseMigrator(factory), new VaultDataProtector(crypto));
        var database = new LocalMdbxDatabase
        {
            Name = "OneDrive",
            FilePath = "OneDrive:/Monica/local.mdbx",
            StorageLocation = MdbxStorageLocation.RemoteOneDrive,
            SourceType = "REMOTE_ONEDRIVE",
            RemoteAccountId = "home-account-id"
        };

        await repository.SaveMdbxDatabaseAsync(database);
        var reloaded = Assert.Single(await repository.GetMdbxDatabasesAsync());

        Assert.Equal("home-account-id", reloaded.RemoteAccountId);
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT remote_account_id FROM local_mdbx_databases WHERE id = $id";
        command.Parameters.AddWithValue("$id", database.Id);
        var stored = Assert.IsType<string>(await command.ExecuteScalarAsync());
        Assert.NotEqual("home-account-id", stored);
        Assert.DoesNotContain("home-account-id", stored, StringComparison.Ordinal);
    }

    private static async Task<long> ReadUserVersionAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
