using Microsoft.Data.Sqlite;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.Tests;

internal sealed class MdbxTestVaultEngine : IMdbxVaultEngine
{
    private readonly Dictionary<string, string> _passwords = new(StringComparer.OrdinalIgnoreCase);

    public async Task CreateVaultAsync(string path, string password, MdbxTigaMode mode, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Environment.CurrentDirectory);
        await using var connection = new SqliteConnection(CreateConnectionString(path));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS vault_meta (
                vault_id TEXT NOT NULL,
                format_version TEXT NOT NULL,
                default_tiga_mode TEXT NOT NULL
            );
            DELETE FROM vault_meta;
            INSERT INTO vault_meta(vault_id, format_version, default_tiga_mode)
            VALUES ($vault_id, 'MDBX-1', $mode);
            """;
        command.Parameters.AddWithValue("$vault_id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$mode", mode.ToString().ToLowerInvariant());
        await command.ExecuteNonQueryAsync(cancellationToken);
        _passwords[path] = password;
    }

    public Task OpenVaultAsync(string path, string password, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("MDBX test vault does not exist.");
        }

        if (_passwords.TryGetValue(path, out var expected) && expected != password)
        {
            throw new InvalidOperationException("MDBX test vault password is incorrect.");
        }

        return Task.CompletedTask;
    }

    public async Task<MdbxVaultInspection> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return new MdbxVaultInspection(path, false, "", "", "File not found");
        }

        await using var connection = new SqliteConnection(CreateConnectionString(path));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT vault_id, format_version FROM vault_meta LIMIT 1";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new MdbxVaultInspection(path, true, "", "", "vault_meta row not found");
        }

        return new MdbxVaultInspection(path, true, reader.GetString(1), reader.GetString(0), "Available");
    }

    private static string CreateConnectionString(string path) => new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Pooling = false
    }.ToString();
}
