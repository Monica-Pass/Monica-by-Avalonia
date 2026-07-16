using Microsoft.Data.Sqlite;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed class MdbxUniffiBindingTests
{
    [Fact]
    public async Task Native_vault_service_uses_real_uniffi_bridge_without_cli_fallback()
    {
        var bridge = new MdbxUniffiNativeBridge();
        Assert.True(bridge.IsAvailable);
        var service = new MdbxVaultService(new ThrowingMdbxVaultEngine(), bridge);
        var path = TestTempPaths.CreateFilePath(".mdbx");

        var metadata = await service.CreateLocalMetadataAsync("Native service", path, MdbxTigaMode.Multi);
        await using var stream = await service.OpenLocalStreamAsync(metadata);

        Assert.True(stream.CanWrite);
        Assert.Equal(path, metadata.WorkingCopyPath);
        Assert.Equal("MDBX-1", await ReadFormatVersionAsync(path));
        Assert.StartsWith("MDBX-1 vault ", metadata.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Native_bridge_creates_mdbx1_vault_and_roundtrips_entry()
    {
        var bridge = new MdbxUniffiNativeBridge();
        Assert.True(bridge.IsAvailable);

        var path = TestTempPaths.CreateFilePath(".mdbx");
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
        var attachment = await created.CreateAttachmentMetadataAsync(
            project.ProjectId,
            entry.EntryId,
            "recovery.txt",
            "text/plain",
            "",
            0);
        var attachmentContent = "native attachment bytes"u8.ToArray();
        var writtenAttachment = await created.WriteAttachmentInlineContentAsync(attachment.AttachmentId, attachmentContent);
        var readAttachment = await created.ReadAttachmentContentAsync(attachment.AttachmentId);
        await created.DeleteAttachmentAsync(attachment.AttachmentId);
        DisposeVault(created);

        var reopened = await bridge.OpenVaultAsync(path, password, deviceId);
        var entries = await reopened.ListEntriesAsync(project.ProjectId, "login");
        DisposeVault(reopened);

        Assert.False(string.IsNullOrWhiteSpace(info.VaultId));
        Assert.Equal(deviceId, info.DeviceId);
        Assert.False(project.Deleted);
        Assert.Equal("GitHub", entry.Title);
        Assert.Equal("embedded-inline", writtenAttachment.StorageMode);
        Assert.Equal(attachmentContent, readAttachment);
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

    private sealed class ThrowingMdbxVaultEngine : IMdbxVaultEngine
    {
        public Task CreateVaultAsync(string path, string password, MdbxTigaMode mode, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Fallback engine should not be used when real native MDBX is available.");

        public Task OpenVaultAsync(string path, string password, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Fallback engine should not be used when real native MDBX is available.");

        public Task<MdbxVaultInspection> InspectAsync(string path, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Fallback engine should not be used when real native MDBX is available.");
    }
}
