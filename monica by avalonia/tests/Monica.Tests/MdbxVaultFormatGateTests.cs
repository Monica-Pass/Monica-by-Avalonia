using Microsoft.Data.Sqlite;
using Monica.Core.Models;
using Monica.Data.Mdbx;
using Monica.Platform.Services;

namespace Monica.Tests;

/// <summary>
/// The format gate required by docs/storage/MDBX-CROSS-CLIENT-CONTRACT.zh-CN.md §4. It decides what this
/// client may do with a file from the header the <em>file</em> declares, instead of the previous check
/// that compared the runtime's own writable format against a constant and therefore could only pass.
/// Assertions name formats, version numbers and booleans — all engine constants, never vault content.
/// </summary>
/// <remarks>
/// What the engine itself was measured to do is part of these tests, not an assumption: a file declaring
/// an unsupported format, a newer schema, or critical extensions this build does not implement is refused
/// at open ("validation error: unsupported MDBX format version: MDBX-3", "...unsupported critical
/// extensions", "...schema version 999; expected 17"). So the ReadOnly verdict is a forward guard for the
/// case the engine does open — a format it can read but this build does not write — and the store refusal
/// is exercised against a synthetic verdict rather than against a real older file, because this build
/// cannot author one.
/// </remarks>
public sealed class MdbxVaultFormatGateTests
{
    private const string FixturePassword = "gate-fixture-password-not-a-secret";
    private const string ThisBuildFormat = "MDBX-2";
    private const uint ThisBuildSchema = 17;

    [Fact]
    public async Task A_vault_this_build_created_declares_a_format_it_can_write()
    {
        var (bridge, path) = await CreateOwnedVaultAsync();

        var header = await bridge.InspectMigrationAsync(path);
        var verdict = MdbxNativeFormatGate.Assess(header, bridge.WritableStorageFormat, bridge.ReadableStorageFormats);

        Assert.NotNull(header);
        Assert.True(header!.Initialized);
        Assert.Equal(ThisBuildFormat, header.FormatVersion);
        Assert.Equal(ThisBuildSchema, header.SchemaVersion);
        Assert.False(header.RequiresUpgrade);
        Assert.False(header.UnknownCriticalExtensions);
        Assert.Equal(MdbxNativeVaultAccess.ReadWrite, verdict.Access);
        Assert.True(verdict.AllowsWrites);
    }

    [Fact]
    public async Task A_file_declaring_a_format_this_runtime_cannot_read_never_reaches_a_verdict()
    {
        // Measured: the engine refuses to report a header for a format it does not know, so the gate has
        // no opinion to offer and the refusal has to come from the open itself. A silent "no verdict"
        // must never read as permission to write someone else's file.
        var (bridge, path) = await CreateOwnedVaultAsync();
        await TamperAsync(path, "format_version", "'MDBX-3'");

        Assert.Null(await bridge.InspectMigrationAsync(path));
        var openFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => bridge.OpenVaultAsync(path, FixturePassword, "gate-probe-device"));
        Assert.True(openFailure.Message.Contains("MDBX-3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_file_that_demands_extensions_this_build_does_not_implement_is_neither_read_nor_written()
    {
        var (bridge, path) = await CreateOwnedVaultAsync();
        await TamperAsync(path, "critical_extensions", "1");

        var header = await bridge.InspectMigrationAsync(path);
        Assert.NotNull(header);
        Assert.True(header!.UnknownCriticalExtensions);
        var verdict = MdbxNativeFormatGate.Assess(header, bridge.WritableStorageFormat, bridge.ReadableStorageFormats);
        Assert.Equal(MdbxNativeVaultAccess.ReadOnly, verdict.Access);
        Assert.Equal("unknown-critical-extensions", verdict.ReasonCode);

        // And the engine will not hand back a handle at all, so the desktop cannot write it either way.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => bridge.OpenVaultAsync(path, FixturePassword, "gate-probe-device"));
    }

    [Fact]
    public async Task A_schema_newer_than_this_build_is_read_only_and_the_engine_refuses_the_open()
    {
        var (bridge, path) = await CreateOwnedVaultAsync();
        await TamperAsync(path, "schema_version", "999");

        var header = await bridge.InspectMigrationAsync(path);
        var verdict = MdbxNativeFormatGate.Assess(header!, bridge.WritableStorageFormat, bridge.ReadableStorageFormats);
        Assert.Equal(MdbxNativeVaultAccess.ReadOnly, verdict.Access);
        Assert.Equal("schema-newer", verdict.ReasonCode);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => bridge.OpenVaultAsync(path, FixturePassword, "gate-probe-device"));
    }

    [Fact]
    public void A_readable_format_that_this_build_does_not_write_is_limited_to_reads()
    {
        // The runtime advertises MDBX-1 as readable; writing such a file would restamp it as MDBX-2 and
        // leave the other client's declared envelope behind.
        var readableButForeign = Header(formatVersion: "MDBX-1", requiresUpgrade: false);
        var verdict = MdbxNativeFormatGate.Assess(
            readableButForeign,
            ThisBuildFormat,
            ["MDBX-1", "MDBX-1-DRAFT", ThisBuildFormat]);

        Assert.Equal(MdbxNativeVaultAccess.ReadOnly, verdict.Access);
        Assert.Equal("format-not-writable", verdict.ReasonCode);
        Assert.False(verdict.AllowsWrites);
    }

    [Fact]
    public void A_file_awaiting_an_upgrade_this_build_has_not_implemented_is_read_only()
    {
        var verdict = MdbxNativeFormatGate.Assess(
            Header(requiresUpgrade: true),
            ThisBuildFormat,
            [ThisBuildFormat]);

        Assert.Equal(MdbxNativeVaultAccess.ReadOnly, verdict.Access);
        Assert.Equal("requires-upgrade", verdict.ReasonCode);
        Assert.True(verdict.Detail.Contains("MDBX-2", StringComparison.Ordinal));
    }

    [Fact]
    public void An_uninitialized_header_is_refused_rather_than_created_around()
    {
        var verdict = MdbxNativeFormatGate.Assess(
            Header(initialized: false),
            ThisBuildFormat,
            [ThisBuildFormat]);

        Assert.Equal(MdbxNativeVaultAccess.Refused, verdict.Access);
        Assert.Equal("not-initialized", verdict.ReasonCode);
    }

    [Fact]
    public async Task A_restricted_session_reads_the_vault_and_writes_nothing_into_it()
    {
        // The store's read path materializes Android's root project on every call, because the engine
        // rejects folder-less writes without it. On a restricted session that create is the very write the
        // gate forbids, so listing has to work without it and saving has to refuse loudly.
        var handle = new CountingNativeVault();
        var bridge = new VerdictBridge(handle, Header(requiresUpgrade: true));
        var store = new MdbxVaultStore(bridge);
        var database = new LocalMdbxDatabase
        {
            Id = 1,
            Name = "Restricted",
            FilePath = "restricted.mdbx",
            EncryptedPassword = FixturePassword
        };

        _ = await store.GetCategoriesAsync(database);
        _ = await store.GetPasswordsAsync(database);

        Assert.Equal(0, handle.ProjectCreateCalls);
        Assert.Equal(0, handle.EntryWriteCalls);

        var blocked = await Assert.ThrowsAsync<MdbxVaultReadOnlyException>(
            () => store.SavePasswordAsync(
                database,
                new PasswordEntry { Id = 1, Title = "Blocked by the gate", Username = "gate-user" }));
        Assert.Equal("requires-upgrade", blocked.ReasonCode);
        Assert.Equal(0, handle.EntryWriteCalls);
        Assert.NotNull(store.LastVaultAccess);
        Assert.False(store.LastVaultAccess!.AllowsWrites);
    }

    [Fact]
    public async Task An_unreadable_file_is_refused_before_the_engine_is_asked_to_open_it()
    {
        var handle = new CountingNativeVault();
        var bridge = new VerdictBridge(handle, Header(initialized: false));
        var store = new MdbxVaultStore(bridge);
        var database = new LocalMdbxDatabase
        {
            Id = 1,
            Name = "Unreadable",
            FilePath = "unreadable.mdbx",
            EncryptedPassword = FixturePassword
        };

        await Assert.ThrowsAsync<MdbxVaultFormatException>(() => store.GetCategoriesAsync(database));
        // The refusal is the whole point: opening runs the vault KDF, so an unreadable file must never
        // reach that step just to be rejected one call later.
        Assert.False(bridge.OpenCalled);
        Assert.False(handle.OpenUsed);
        Assert.Equal(MdbxNativeVaultAccess.Refused, store.LastVaultAccess?.Access);
    }

    [Fact]
    public async Task A_restricted_handle_forwards_reads_and_refuses_every_write()
    {
        var handle = new CountingNativeVault();
        var gated = new MdbxReadOnlyNativeVault(
            handle,
            MdbxNativeAccessDecision.ReadOnly("format-not-writable", "measured in tests"));

        Assert.True(gated.IsReadOnly);
        _ = await gated.GetInfoAsync();
        _ = await gated.ListProjectsAsync();
        _ = await gated.ListEntriesAsync("project", entryType: null);
        _ = await gated.ListDeletedEntriesAsync("project", entryType: null);
        _ = await gated.ListAttachmentsAsync("project", entryId: null);

        Assert.True(handle.ReadCalls > 0);
        Assert.Equal(0, handle.WriteCalls);
        await Assert.ThrowsAsync<MdbxVaultReadOnlyException>(() => gated.CreateEntryAsync("project", "login", "t", "{}"));
        await Assert.ThrowsAsync<MdbxVaultReadOnlyException>(() => gated.UpdateEntryAsync("project", "id", "login", "t", "{}"));
        await Assert.ThrowsAsync<MdbxVaultReadOnlyException>(() => gated.DeleteEntryAsync("project", "id"));
        await Assert.ThrowsAsync<MdbxVaultReadOnlyException>(() => gated.RestoreEntryAsync("project", "id"));
        await Assert.ThrowsAsync<MdbxVaultReadOnlyException>(() => gated.MoveEntryAsync("project", "id", "other"));
        await Assert.ThrowsAsync<MdbxVaultReadOnlyException>(() => gated.CreateProjectAsync("t"));
        await Assert.ThrowsAsync<MdbxVaultReadOnlyException>(
            () => gated.CreateProjectWithIdentityAsync("project", "t", parentProjectId: null));
        await Assert.ThrowsAsync<MdbxVaultReadOnlyException>(
            () => gated.CreateAttachmentAsync("project", "id", "f.bin", null, [1, 2]));
        await Assert.ThrowsAsync<MdbxVaultReadOnlyException>(() => gated.DeleteAttachmentAsync("attachment"));
        Assert.Equal(0, handle.WriteCalls);
    }

    private static MdbxNativeMigrationInfo Header(
        string formatVersion = ThisBuildFormat,
        uint? schemaVersion = ThisBuildSchema,
        bool requiresUpgrade = false,
        bool unknownCriticalExtensions = false,
        bool initialized = true) =>
        new(
            initialized,
            formatVersion,
            schemaVersion,
            "MDBX-1",
            ThisBuildFormat,
            requiresUpgrade,
            unknownCriticalExtensions,
            ThisBuildFormat,
            ThisBuildSchema);

    private static async Task<(MdbxUniffiNativeBridge Bridge, string Path)> CreateOwnedVaultAsync()
    {
        var bridge = new MdbxUniffiNativeBridge();
        Assert.True(bridge.IsAvailable);
        var path = TestTempPaths.CreateFilePath(".mdbx");
        var service = new MdbxVaultService(new MdbxCliVaultEngine(), bridge);
        await service.CreateLocalMetadataAsync("Gate fixture", path);
        return (bridge, path);
    }

    private static async Task TamperAsync(string path, string column, string value)
    {
        // The declared envelope is edited in place, which is what a file from a client with a different
        // format or extension set looks like to this build. The engine's header integrity check then
        // refuses some of these with a credential error — that refusal is itself asserted.
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE vault_meta SET {column} = {value}";
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    /// <summary>Reports a fixed verdict and hands back one shared handle, so the store's choices are observable.</summary>
    private sealed class VerdictBridge(IMdbxNativeVault handle, MdbxNativeMigrationInfo? header) : IMdbxNativeBridge
    {
        public bool IsAvailable => true;
        public string? AvailabilityError => null;
        public string WritableStorageFormat => ThisBuildFormat;
        public IReadOnlyList<string> ReadableStorageFormats => [ThisBuildFormat];
        public bool OpenCalled { get; private set; }

        public Task<MdbxNativeMigrationInfo?> InspectMigrationAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(header);

        public Task<IMdbxNativeVault> CreateVaultAsync(
            string path,
            string password,
            string deviceId,
            MdbxTigaMode mode,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The gate decides before a vault is created.");

        public Task<IMdbxNativeVault> OpenVaultAsync(
            string path,
            string password,
            string deviceId,
            CancellationToken cancellationToken = default)
        {
            OpenCalled = true;
            return Task.FromResult(handle);
        }
    }

    private sealed class CountingNativeVault : IMdbxNativeVault
    {
        private readonly string _vaultId = Guid.NewGuid().ToString();

        public int ReadCalls { get; private set; }
        public int WriteCalls { get; private set; }
        public int ProjectCreateCalls { get; private set; }
        public int EntryWriteCalls { get; private set; }
        public bool OpenUsed { get; private set; }

        public bool IsReadOnly => false;

        public void Dispose()
        {
        }

        public Task<MdbxNativeVaultInfo> GetInfoAsync(CancellationToken cancellationToken = default)
        {
            OpenUsed = true;
            ReadCalls++;
            return Task.FromResult(new MdbxNativeVaultInfo(_vaultId, "gate-counting-device"));
        }

        public Task<IReadOnlyList<MdbxNativeProjectRecord>> ListProjectsAsync(CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return Task.FromResult<IReadOnlyList<MdbxNativeProjectRecord>>([]);
        }

        public Task<IReadOnlyList<MdbxNativeEntryRecord>> ListEntriesAsync(
            string projectId,
            string? entryType = null,
            CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return Task.FromResult<IReadOnlyList<MdbxNativeEntryRecord>>([]);
        }

        public Task<IReadOnlyList<MdbxNativeEntryRecord>> ListDeletedEntriesAsync(
            string projectId,
            string? entryType = null,
            CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return Task.FromResult<IReadOnlyList<MdbxNativeEntryRecord>>([]);
        }

        public Task<IReadOnlyList<MdbxNativeAttachmentRecord>> ListAttachmentsAsync(
            string projectId,
            string? entryId,
            CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return Task.FromResult<IReadOnlyList<MdbxNativeAttachmentRecord>>([]);
        }

        public Task<byte[]> ReadAttachmentContentAsync(string attachmentId, CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return Task.FromResult(Array.Empty<byte>());
        }

        public Task<MdbxNativeProjectRecord> CreateProjectAsync(string title, CancellationToken cancellationToken = default)
        {
            ProjectCreateCalls++;
            return Write(() => new MdbxNativeProjectRecord(Guid.NewGuid().ToString(), title));
        }

        public Task<MdbxNativeProjectRecord> CreateProjectWithIdentityAsync(
            string projectId,
            string title,
            string? parentProjectId,
            CancellationToken cancellationToken = default)
        {
            ProjectCreateCalls++;
            return Write(() => new MdbxNativeProjectRecord(projectId, title));
        }

        public Task<MdbxNativeEntryRecord> CreateEntryAsync(
            string projectId,
            string entryType,
            string title,
            string payloadJson,
            CancellationToken cancellationToken = default)
        {
            EntryWriteCalls++;
            return Write(() => new MdbxNativeEntryRecord(Guid.NewGuid().ToString(), projectId, entryType, title, payloadJson, false));
        }

        public Task<MdbxNativeEntryRecord> UpdateEntryAsync(
            string projectId,
            string entryId,
            string entryType,
            string title,
            string payloadJson,
            CancellationToken cancellationToken = default)
        {
            EntryWriteCalls++;
            return Write(() => new MdbxNativeEntryRecord(entryId, projectId, entryType, title, payloadJson, false));
        }

        public Task<MdbxNativeEntryRecord> MoveEntryAsync(
            string projectId,
            string entryId,
            string targetProjectId,
            CancellationToken cancellationToken = default)
        {
            EntryWriteCalls++;
            return Write(() => new MdbxNativeEntryRecord(entryId, targetProjectId, "login", "moved", "{}", false));
        }

        public Task<MdbxNativeEntryRecord> RestoreEntryAsync(string projectId, string entryId, CancellationToken cancellationToken = default)
        {
            EntryWriteCalls++;
            return Write(() => new MdbxNativeEntryRecord(entryId, projectId, "login", "restored", "{}", false));
        }

        public Task DeleteEntryAsync(string projectId, string entryId, CancellationToken cancellationToken = default)
        {
            EntryWriteCalls++;
            _ = Write(() => 0);
            return Task.CompletedTask;
        }

        public Task<MdbxNativeAttachmentRecord> CreateAttachmentAsync(
            string projectId,
            string? entryId,
            string fileName,
            string? mediaType,
            byte[] content,
            CancellationToken cancellationToken = default) =>
            Write(() => new MdbxNativeAttachmentRecord(
                Guid.NewGuid().ToString(),
                projectId,
                entryId,
                fileName,
                mediaType,
                "inline",
                "hash",
                (ulong)content.Length,
                (ulong)content.Length,
                1,
                false));

        public Task DeleteAttachmentAsync(string attachmentId, CancellationToken cancellationToken = default)
        {
            _ = Write(() => 0);
            return Task.CompletedTask;
        }

        private Task<T> Write<T>(Func<T> result)
        {
            WriteCalls++;
            return Task.FromResult(result());
        }
    }
}
