using Microsoft.Data.Sqlite;

namespace Monica.Data;

public sealed record LegacyVaultDetection(
    string DatabasePath,
    string SecurityConfigPath,
    bool HasLegacyDatabase,
    bool HasLegacySecurityConfig,
    bool HasCurrentSchema)
{
    public bool RequiresImport => (HasLegacyDatabase || HasLegacySecurityConfig) && !HasCurrentSchema;

    public static LegacyVaultDetection Empty { get; } = new("", "", false, false, false);
}

public interface ILegacyVaultDetector
{
    Task<LegacyVaultDetection> DetectAsync(CancellationToken cancellationToken = default);
}

public sealed class NoLegacyVaultDetector : ILegacyVaultDetector
{
    public Task<LegacyVaultDetection> DetectAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(LegacyVaultDetection.Empty);
}

public sealed class LegacyVaultDetector(ISqliteConnectionFactory connectionFactory) : ILegacyVaultDetector
{
    public async Task<LegacyVaultDetection> DetectAsync(CancellationToken cancellationToken = default)
    {
        var databasePath = connectionFactory.DatabasePath;
        var securityPath = Path.Combine(Path.GetDirectoryName(databasePath) ?? "", "security.json");
        var hasLegacySecurityConfig = File.Exists(securityPath);

        if (!File.Exists(databasePath))
        {
            return new LegacyVaultDetection(databasePath, securityPath, false, hasLegacySecurityConfig, false);
        }

        await using var connection = CreateReadOnlyConnection(databasePath);
        await connection.OpenAsync(cancellationToken);

        var hasLegacyDatabase = await HasAnyTableAsync(connection, cancellationToken, "PasswordEntries", "SecureItems");
        var hasCurrentSchema = await HasAnyTableAsync(connection, cancellationToken, "password_entries", "secure_items", "vault_credentials");
        return new LegacyVaultDetection(databasePath, securityPath, hasLegacyDatabase, hasLegacySecurityConfig, hasCurrentSchema);
    }

    internal static async Task<bool> HasAnyTableAsync(SqliteConnection connection, CancellationToken cancellationToken, params string[] tableNames)
    {
        foreach (var tableName in tableNames)
        {
            if (await HasTableAsync(connection, tableName, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static SqliteConnection CreateReadOnlyConnection(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared
        };

        return new SqliteConnection(builder.ToString());
    }

    private static async Task<bool> HasTableAsync(SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name LIMIT 1;";
        command.Parameters.AddWithValue("$name", tableName);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }
}
