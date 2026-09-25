using Microsoft.Data.Sqlite;
using Monica.Core.Models;
using Monica.Data.Mdbx;
using Monica.Platform.Services;

namespace Monica.Tests;

/// <summary>
/// The expected values in this file are read off two .mdbx files the Android client actually produced
/// (schema 17, format MDBX-2, created 2026-07-31, kept at
/// Monica-all/.codex-tasks/20260731-mdbx2-local-create-failure/raw). They are the only Android-authored
/// vault bytes available on this machine, so the comparison they permit is the envelope both clients
/// write before a single entry exists. What they cannot prove is that the desktop unlocks a vault
/// Android authored end to end: those two files are password-protected and their password is not here,
/// so that half stays unverified rather than assumed.
/// </summary>
public sealed class MdbxVaultShapeParityTests
{
    private const string AndroidVaultFormat = "MDBX-2";
    private const int AndroidVaultSchema = 17;
    private const string AndroidMinReaderVersion = "MDBX-1";
    private const string AndroidMinWriterVersion = "MDBX-2";
    private const string AndroidHeaderIntegrityProfile = "mdbx-vault-header-hmac-sha256-v1";
    private const string AndroidDefaultTigaMode = "multi";
    private const string AndroidTigaPolicyVersion = "2";
    private const string AndroidTigaComplianceStatus = "compliant";

    /// <summary>
    /// The exact column set of <c>vault_meta</c> in both fixtures, in the order SQLite reports it. A
    /// missing or extra column here means the two clients write different files even when every value
    /// they share agrees.
    /// </summary>
    private static readonly IReadOnlyList<string> AndroidVaultMetaColumns = new[]
    {
        "vault_id",
        "format_version",
        "created_at",
        "updated_at",
        "default_tiga_mode",
        "active_key_epoch_id",
        "compat_flags",
        "critical_extensions",
        "schema_version",
        "min_reader_version",
        "min_writer_version",
        "tiga_policy_version",
        "tiga_compliance_status",
        "header_integrity_profile",
        "header_integrity_tag"
    };

    [Fact]
    public async Task Freshly_created_vault_carries_the_envelope_android_authored_vaults_have()
    {
        var bridge = new MdbxUniffiNativeBridge();
        Assert.True(bridge.IsAvailable);
        var path = TestTempPaths.CreateFilePath(".mdbx");
        var service = new MdbxVaultService(new UnavailableEngine(), bridge);
        var database = await service.CreateLocalMetadataAsync("Shape parity", path, MdbxTigaMode.Multi);
        Assert.Equal(Path.GetFullPath(path), database.FilePath);

        var meta = await ReadVaultMetaAsync(path);

        Assert.Equal(AndroidVaultMetaColumns, meta.Keys);
        Assert.Equal(AndroidVaultFormat, meta["format_version"]);
        Assert.Equal(AndroidVaultSchema, int.Parse(meta["schema_version"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(AndroidMinReaderVersion, meta["min_reader_version"]);
        Assert.Equal(AndroidMinWriterVersion, meta["min_writer_version"]);
        Assert.Equal(AndroidHeaderIntegrityProfile, meta["header_integrity_profile"]);
        // Measured, not assumed: feeding the engine a different Tiga mode at creation leaves the file
        // flagged remediation-required, so the mode the desktop asks for is part of the shared envelope.
        Assert.Equal(AndroidTigaPolicyVersion, meta["tiga_policy_version"]);
        Assert.Equal(AndroidTigaComplianceStatus, meta["tiga_compliance_status"]);
        Assert.Equal(AndroidDefaultTigaMode, meta["default_tiga_mode"].ToLowerInvariant());
        // Non-secret by construction: a vault id is a uuid, and the timestamp is the only field that
        // legitimately differs between two creations.
        Assert.Equal(36, meta["vault_id"].Length);
        Guid.Parse(meta["vault_id"]);
        Assert.Equal(36, meta["active_key_epoch_id"].Length);
        Guid.Parse(meta["active_key_epoch_id"]);
    }

    /// <summary>
    /// Both Android fixtures hold exactly one commit and no projects, entries or commit_operations.
    /// That is the shape of a vault that was created and unlocked but never written to, and it is what
    /// the desktop has to reproduce: an extra project in the brand-new file would mean the two clients
    /// create different files. It also records the uncomfortable half of the finding — a root-less
    /// virgin vault is what an Android build dated after the root feature shipped actually left behind,
    /// so the desktop's lazy EnsureRootProjectAsync is load-bearing rather than defensive.
    /// </summary>
    [Fact]
    public async Task Freshly_created_vault_is_as_empty_as_androids_created_but_unused_vaults()
    {
        var bridge = new MdbxUniffiNativeBridge();
        Assert.True(bridge.IsAvailable);
        var path = TestTempPaths.CreateFilePath(".mdbx");
        var service = new MdbxVaultService(new UnavailableEngine(), bridge);
        var database = await service.CreateLocalMetadataAsync("Shape parity", path, MdbxTigaMode.Multi);

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["commits"] = "1",
                ["commit_operations"] = "0",
                ["projects"] = "0",
                ["entries"] = "0"
            },
            await ReadTableCountsAsync(path));

        // Writing through the store is what brings the root project to life.
        using var store = new MdbxVaultStore(bridge);
        await store.SavePasswordAsync(database, new PasswordEntry { Title = "First write", Username = "dev", Password = "secret" });

        var projects = await ReadTableCountsAsync(path);
        Assert.Equal("1", projects["projects"]);
        Assert.Equal("1", projects["entries"]);
        Assert.Equal(
            new[] { "monica-initialize", "monica-upsert-entries" },
            await ReadOperationKindsAsync(path));
    }

    private static async Task<Dictionary<string, string>> ReadVaultMetaAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM vault_meta LIMIT 1";
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < reader.FieldCount; index++)
        {
            values[reader.GetName(index)] = reader.IsDBNull(index) ? "" : reader.GetValue(index).ToString() ?? "";
        }

        return values;
    }

    private static async Task<Dictionary<string, string>> ReadTableCountsAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        await connection.OpenAsync();
        var counts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var table in new[] { "commits", "commit_operations", "projects", "entries" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table}";
            counts[table] = Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) ?? "";
        }

        return counts;
    }

    private static async Task<IReadOnlyList<string>> ReadOperationKindsAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT operation_kind FROM commit_operations ORDER BY rowid";
        var kinds = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            kinds.Add(reader.GetString(0));
        }

        return kinds;
    }

    private sealed class UnavailableEngine : IMdbxVaultEngine
    {
        public Task CreateVaultAsync(string path, string password, MdbxTigaMode mode, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The real native engine must create the vault.");

        public Task OpenVaultAsync(string path, string password, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The real native engine must open the vault.");

        public Task<MdbxVaultInspection> InspectAsync(string path, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The real native engine must inspect the vault.");
    }
}
