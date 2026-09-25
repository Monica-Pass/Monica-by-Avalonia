using Microsoft.Data.Sqlite;
using Monica.Core.Models;
using Monica.Data.Mdbx;
using Ffi = Monica.Mdbx.Ffi;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed class MdbxUniffiBindingTests
{
    [Fact]
    public async Task Native_vault_service_uses_real_uniffi_bridge_without_cli_fallback()
    {
        var bridge = new MdbxUniffiNativeBridge();
        Assert.True(bridge.IsAvailable);
        Assert.Null(bridge.AvailabilityError);
        var service = new MdbxVaultService(new ThrowingMdbxVaultEngine(), bridge);
        var path = TestTempPaths.CreateFilePath(".mdbx");

        var metadata = await service.CreateLocalMetadataAsync("Native service", path, MdbxTigaMode.Multi);
        await using var stream = await service.OpenLocalStreamAsync(metadata);

        Assert.True(stream.CanWrite);
        Assert.Equal(path, metadata.WorkingCopyPath);
        Assert.Equal("MDBX-2", await ReadFormatVersionAsync(path));
        Assert.StartsWith("MDBX-2 vault ", metadata.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Covers the bridge itself, not just the generated bindings: the plain create_entry/update_entry
    /// facade rejects Android's wallet entry types, so the only way the desktop can write a record
    /// Android reads is the command surface. Reverting MdbxUniffiNativeVault to that facade fails here
    /// while every fake-backed test stays green.
    /// </summary>
    [Fact]
    public async Task Native_bridge_writes_androids_wallet_entry_types_and_labels_the_commits_like_android()
    {
        var bridge = new MdbxUniffiNativeBridge();
        Assert.True(bridge.IsAvailable);

        var path = TestTempPaths.CreateFilePath(".mdbx");
        const string password = "native-test-password";
        const string deviceId = "native-command-bridge";

        var created = await bridge.CreateVaultAsync(path, password, deviceId, MdbxTigaMode.Multi);
        var project = await created.CreateProjectAsync("Monica");
        var entry = await created.CreateEntryAsync(project.ProjectId, "billing-address", "Card", """{"kind":"billing_address"}""");
        await created.UpdateEntryAsync(project.ProjectId, entry.EntryId, "billing-address", "Card", """{"kind":"billing_address","v":2}""");
        DisposeVault(created);

        // Android supplies the entry id itself; an engine-generated one would not line up across devices.
        Assert.True(Guid.TryParse(entry.EntryId, out _));
        Assert.Equal(
            new[] { ("legacy-change", 1), ("monica-upsert-entries", 2) },
            await ReadOperationKindsAsync(path));

        var reopened = await bridge.OpenVaultAsync(path, password, deviceId);
        var stored = Assert.Single(await reopened.ListEntriesAsync(project.ProjectId, entryType: null));
        Assert.Equal(entry.EntryId, stored.EntryId);
        Assert.Equal("billing-address", stored.EntryType);
        Assert.Contains("\"v\":2", stored.PayloadJson, StringComparison.Ordinal);
        DisposeVault(reopened);
    }

    /// <summary>
    /// The layout contract measured on the shipped library instead of the test double: a vault the
    /// desktop authors has to carry the hidden root project Android recomputes from the vault id, its
    /// folders have to hang off that root through the engine's own parent link, and only a root-filed
    /// entry may leave mdbx_folder_id out of its payload. Every one of those is a write the fake cannot
    /// prove, because the fake is the thing being imitated.
    /// </summary>
    [Fact]
    public async Task Native_store_lays_out_the_root_project_and_folder_links_android_expects()
    {
        var bridge = new MdbxUniffiNativeBridge();
        Assert.True(bridge.IsAvailable);
        var path = TestTempPaths.CreateFilePath(".mdbx");
        var service = new MdbxVaultService(new ThrowingMdbxVaultEngine(), bridge);
        var database = await service.CreateLocalMetadataAsync("Interop layout", path, MdbxTigaMode.Multi);
        var category = new Category { Id = 1, Name = "Work" };

        using (var store = new MdbxVaultStore(bridge))
        {
            await store.SaveCategoryAsync(database, category);
            await store.SavePasswordAsync(
                database,
                new PasswordEntry
                {
                    Title = "Work login",
                    Username = "dev",
                    Password = "secret",
                    CategoryId = category.Id
                },
                new Dictionary<long, Category> { [category.Id] = category });
            await store.SavePasswordAsync(database, new PasswordEntry
            {
                Title = "Uncategorized login",
                Username = "solo",
                Password = "secret"
            });
        }

        string rootProjectId;
        var opened = await bridge.OpenVaultAsync(path, database.EncryptedPassword!, "native-layout-probe");
        try
        {
            rootProjectId = MdbxAndroidRoot.ProjectIdFor((await opened.GetInfoAsync()).VaultId);
            var projects = await opened.ListProjectsAsync();
            var root = Assert.Single(projects, project => MdbxAndroidRoot.IsRootTitle(project.Title));
            Assert.Equal(rootProjectId, root.ProjectId);
            var folder = Assert.Single(projects, project => project.Title == "Work");

            var categorized = Assert.Single(
                await opened.ListEntriesAsync(folder.ProjectId, entryType: null),
                entry => entry.Title == "Work login");
            Assert.Contains($"\"mdbx_folder_id\":\"{folder.ProjectId}\"", categorized.PayloadJson, StringComparison.Ordinal);

            var rootFiled = Assert.Single(
                await opened.ListEntriesAsync(rootProjectId, entryType: null),
                entry => entry.Title == "Uncategorized login");
            Assert.DoesNotContain("mdbx_folder_id", rootFiled.PayloadJson, StringComparison.Ordinal);
        }
        finally
        {
            DisposeVault(opened);
        }

        // The folder's parent is stored as the engine's own collection group link — the only field
        // Android reads to rebuild the tree — so it has to be read back through that same accessor.
        using var raw = Ffi.MdbxFfi.OpenVault(path, database.EncryptedPassword!, "native-layout-probe");
        var summaries = raw.ListCollectionSummaries(100, null).Items;
        Assert.Equal(rootProjectId, Assert.Single(summaries, summary => summary.Title == "Work").GroupId);
        Assert.Null(Assert.Single(summaries, summary => MdbxAndroidRoot.IsRootTitle(summary.Title)).GroupId);
    }

    [Fact]
    public void Bridge_that_cannot_load_reports_why()
    {
        var bridge = new UnavailableMdbxNativeBridge();

        Assert.False(bridge.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(bridge.AvailabilityError));
    }

    [Fact]
    public async Task Native_bridge_creates_mdbx2_vault_and_roundtrips_entry()
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
        var attachmentContent = "native attachment bytes"u8.ToArray();
        var writtenAttachment = await created.CreateAttachmentAsync(
            project.ProjectId,
            entry.EntryId,
            "recovery.txt",
            "text/plain",
            attachmentContent);
        var readAttachment = await created.ReadAttachmentContentAsync(writtenAttachment.AttachmentId);
        await created.DeleteAttachmentAsync(writtenAttachment.AttachmentId);
        DisposeVault(created);

        var reopened = await bridge.OpenVaultAsync(path, password, deviceId);
        var entries = await reopened.ListEntriesAsync(project.ProjectId, "login");
        DisposeVault(reopened);

        Assert.False(string.IsNullOrWhiteSpace(info.VaultId));
        Assert.Equal(deviceId, info.DeviceId);
        Assert.Equal("Personal", project.Title);
        Assert.Equal("GitHub", entry.Title);
        Assert.Equal("embedded-inline", writtenAttachment.StorageMode);
        Assert.Equal(attachmentContent, readAttachment);
        Assert.Equal("MDBX-2", await ReadFormatVersionAsync(path));
        var reloaded = Assert.Single(entries);
        Assert.Equal(entry.EntryId, reloaded.EntryId);
        Assert.Contains("\"username\":\"dev\"", reloaded.PayloadJson, StringComparison.Ordinal);
    }

    /// <summary>
    /// Measured against the library the desktop actually ships: the convenience create_entry surface
    /// rejects every non-legacy name, so billing-address/payment-account can never be written there.
    /// </summary>
    [Theory]
    [InlineData("login")]
    [InlineData("note")]
    [InlineData("totp")]
    [InlineData("card")]
    [InlineData("document-ref")]
    [InlineData("identity")]
    [InlineData("ssh-key")]
    public void Native_create_entry_accepts_the_legacy_entry_types(string entryType)
    {
        Assert.True(new MdbxUniffiNativeBridge().IsAvailable);

        var path = TestTempPaths.CreateFilePath(".mdbx");
        using var vault = Ffi.MdbxFfi.CreateVaultWithTigaMode(
            path, "native-test-password", "native-type-probe", Ffi.MdbxTigaMode.Multi);
        var project = vault.CreateProject("Monica");

        var created = vault.CreateEntry(project.ProjectId, entryType, "Probe", """{"kind":"password"}""");

        Assert.Equal(entryType, vault.ListEntries(project.ProjectId, entryType).Single().EntryType);
        Assert.False(string.IsNullOrWhiteSpace(created.EntryId));
    }

    [Theory]
    [InlineData("billing-address")]
    [InlineData("payment-account")]
    [InlineData("steam-mafile")]
    public void Native_create_entry_rejects_entry_types_outside_the_legacy_set(string entryType)
    {
        Assert.True(new MdbxUniffiNativeBridge().IsAvailable);

        var path = TestTempPaths.CreateFilePath(".mdbx");
        using var vault = Ffi.MdbxFfi.CreateVaultWithTigaMode(
            path, "native-test-password", "native-type-probe", Ffi.MdbxTigaMode.Multi);
        var project = vault.CreateProject("Monica");

        var error = Assert.ThrowsAny<Ffi.MdbxFfiException>(() =>
            vault.CreateEntry(project.ProjectId, entryType, "Probe", """{"kind":"password"}"""));

        Assert.Contains("entryType", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Android never calls create_entry. It writes through this command surface, whose entry type is
    /// parsed as a full object type id and therefore takes the names create_entry rejects — which is
    /// how a Monica-for-Android vault ends up holding billing-address entries the desktop cannot write.
    /// </summary>
    [Theory]
    [InlineData("billing-address")]
    [InlineData("payment-account")]
    public void Native_write_command_accepts_androids_wallet_entry_types(string entryType)
    {
        Assert.True(new MdbxUniffiNativeBridge().IsAvailable);

        var path = TestTempPaths.CreateFilePath(".mdbx");
        using var vault = Ffi.MdbxFfi.CreateVaultWithTigaMode(
            path, "native-test-password", "native-command-probe", Ffi.MdbxTigaMode.Multi);
        var project = vault.CreateProject("Monica");
        var entryId = Guid.NewGuid().ToString();

        vault.ExecuteWriteOperation(
            Guid.NewGuid().ToString(),
            "probe",
            [new Ffi.MdbxWriteCommand.CreateEntry(entryId, project.ProjectId, entryType, "Probe", """{"kind":"billing_address"}""")]);

        var reloaded = Assert.Single(vault.ListEntries(project.ProjectId, null));

        Assert.Equal(entryId, reloaded.EntryId);
        Assert.Equal(entryType, reloaded.EntryType);

        // Listing *by* a custom name is rejected on the read side too, so a client that scans one
        // type at a time can never reach Android's wallet entries; it has to list unfiltered.
        Assert.ThrowsAny<Ffi.MdbxFfiException>(() => vault.ListEntries(project.ProjectId, entryType));
    }

    /// <summary>
    /// Updating an Android-authored wallet entry has to keep its native type, because create_entry /
    /// update_entry cannot carry a custom type at all. This is the escape hatch the desktop's write
    /// path needs.
    /// </summary>
    [Fact]
    public void Native_write_command_updates_a_custom_typed_entry_without_retyping_it()
    {
        Assert.True(new MdbxUniffiNativeBridge().IsAvailable);

        var path = TestTempPaths.CreateFilePath(".mdbx");
        using var vault = Ffi.MdbxFfi.CreateVaultWithTigaMode(
            path, "native-test-password", "native-command-probe", Ffi.MdbxTigaMode.Multi);
        var project = vault.CreateProject("Monica");
        var entryId = Guid.NewGuid().ToString();
        vault.ExecuteWriteOperation(
            Guid.NewGuid().ToString(),
            "probe",
            [new Ffi.MdbxWriteCommand.CreateEntry(entryId, project.ProjectId, "billing-address", "Probe", """{"kind":"billing_address","notes":"one"}""")]);

        vault.ExecuteWriteOperation(
            Guid.NewGuid().ToString(),
            "probe",
            [new Ffi.MdbxWriteCommand.UpdateEntry(entryId, project.ProjectId, "billing-address", "Renamed", """{"kind":"billing_address","notes":"two"}""")]);

        var reloaded = Assert.Single(vault.ListEntries(project.ProjectId, null));

        Assert.Equal("billing-address", reloaded.EntryType);
        Assert.Equal("Renamed", reloaded.Title);
        Assert.Contains("two", reloaded.PayloadJson, StringComparison.Ordinal);
    }

    private static async Task<string> ReadFormatVersionAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT format_version FROM vault_meta LIMIT 1";
        return (string)(await command.ExecuteScalarAsync() ?? "");
    }

    private static async Task<IReadOnlyList<(string Kind, int Rows)>> ReadOperationKindsAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT operation_kind, COUNT(*) FROM commit_operations GROUP BY operation_kind ORDER BY operation_kind";
        var rows = new List<(string, int)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetInt32(1)));
        }

        return rows;
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
