using Microsoft.Data.Sqlite;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed class MdbxUniffiBindingTests
{
    [Fact]
    public async Task Native_bridge_creates_mdbx1_vault_and_roundtrips_entry()
    {
        var bridge = new MdbxUniffiNativeBridge();
        Assert.True(bridge.IsAvailable);

        var path = Path.Combine(Path.GetTempPath(), "monica-tests", $"{Guid.NewGuid():N}.mdbx");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string password = "native-test-password";
        const string deviceId = "native-test-device";

        var created = await bridge.CreateVaultAsync(path, password, deviceId, MdbxTigaMode.Multi);
        var info = await created.GetInfoAsync();
        var project = await created.CreateProjectAsync("Personal");
        var entry = await created.CreateEntryAsync(
            project.ProjectId,
            "login",
            "GitHub",
            """{"kind":"password","username":"dev","password":"secret"}""");
        DisposeVault(created);

        var reopened = await bridge.OpenVaultAsync(path, password, deviceId);
        var entries = await reopened.ListEntriesAsync(project.ProjectId, "login");
        DisposeVault(reopened);

        Assert.False(string.IsNullOrWhiteSpace(info.VaultId));
        Assert.Equal(deviceId, info.DeviceId);
        Assert.False(project.Deleted);
        Assert.Equal("GitHub", entry.Title);
        Assert.Equal("MDBX-1", await ReadFormatVersionAsync(path));
        var reloaded = Assert.Single(entries);
        Assert.Equal(entry.EntryId, reloaded.EntryId);
        Assert.Contains("\"username\":\"dev\"", reloaded.PayloadJson, StringComparison.Ordinal);
    }

    private static async Task<string> ReadFormatVersionAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT format_version FROM vault_meta LIMIT 1";
        return (string)(await command.ExecuteScalarAsync() ?? "");
    }

    private static void DisposeVault(object vault)
    {
        if (vault is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
