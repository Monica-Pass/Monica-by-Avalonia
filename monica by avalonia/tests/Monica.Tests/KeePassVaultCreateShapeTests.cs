using System.Security.Cryptography;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Serialization;
using Monica.Platform.Services;

namespace Monica.Tests;

/// <summary>
/// A database this client creates, measured against a database Monica for Android creates. The Android
/// side is the authority here rather than a table of numbers transcribed into a test: the shape the
/// desktop writes is compared field by field with a file kotpass 0.10.0 produced, and the same fields
/// are then asserted against the constants directly, so the two sides cannot drift together and stay
/// green. What is at stake is that a vault started on Windows opens on the phone with the cost model,
/// the folder layout and the recycle semantics the phone expects - and, for KDBX 4.1 specifically,
/// that the file has a slot for the folder a recycled entry left, which 3.1 has not got.
/// </summary>
public sealed class KeePassVaultCreateShapeTests
{
    private const string CreatedPassword = "created-vault-fixture-not-a-secret";
    private const string FixturePassword = "kdbx-parity-fixture-not-a-secret";
    private const string ZeroUuid = "00000000000000000000000000000000";
    private const uint Kdbx41 = 0x0004_0001;

    // Both spelled out from the Android fixture's own header, so a change to what this client writes is
    // a decision rather than a default that moved.
    private const string AesCipherUuid = "31C1F2E6BF714350BE5805216AFC5AFF";
    private const string Argon2dKdfUuid = "EF636DDF8C29444B91F7A9A403E30A0C";

    private static readonly Lazy<byte[]> CreatedPayload = new(() =>
    {
        var (database, payload) = KeePassVaultCreate.Build("created.kdbx", CreatedPassword, null);
        if (database.IsOpen)
        {
            database.Close();
        }

        return payload;
    });

    [Fact]
    public void A_new_database_has_the_disk_shape_the_android_client_writes()
    {
        var android = ShapeOf(ReadFixture("android-kotpass-v1.kdbx"), FixturePassword);
        var desktop = ShapeOf(CreatedPayload.Value, CreatedPassword);

        Assert.Equal(android, desktop);

        // The equality above could in principle go green because both sides moved together, so the
        // numbers the Android client is documented to write are pinned once more on their own.
        Assert.Equal(Kdbx41, desktop.Version);
        Assert.Equal(AesCipherUuid, desktop.CipherUuid);
        Assert.Equal(Argon2dKdfUuid, desktop.KdfUuid);
        Assert.Equal(32, desktop.KdfSaltBytes);
        Assert.Equal(KeePassVaultCreate.ArgonIterations, desktop.KdfIterations);
        Assert.Equal(KeePassVaultCreate.ArgonMemoryBytes, desktop.KdfMemory);
        Assert.Equal((ulong)KeePassVaultCreate.ArgonParallelism, desktop.KdfParallelism);
        Assert.Equal((ulong)KeePassVaultCreate.ArgonAlgorithmVersion, desktop.KdfAlgorithmVersion);
        Assert.Equal("System.Byte[]", desktop.KdfSaltKind);
        Assert.Equal("System.UInt64", desktop.KdfIterationsKind);
        Assert.Equal("System.UInt64", desktop.KdfMemoryKind);
        Assert.Equal("System.UInt32", desktop.KdfParallelismKind);
        Assert.Equal("System.UInt32", desktop.KdfAlgorithmVersionKind);
        Assert.True(desktop.KdfSecretKeyAbsent);
        Assert.True(desktop.KdfAssociatedDataAbsent);
        Assert.Equal("Root", desktop.RootGroupName);
        Assert.False(desktop.RecycleBinEnabled);
    }

    [Fact]
    public async Task A_new_database_unlocks_with_the_password_that_created_it_and_with_no_other()
    {
        using (var session = await OpenAsync(CreatedPayload.Value, CreatedPassword))
        {
            Assert.Equal(Kdbx41, session.FormatVersion);
            Assert.Equal("created", session.DatabaseName);
            Assert.Equal("Root", session.RootGroupRow.Name);
        }

        var fault = await Assert.ThrowsAsync<KeePassVaultException>(
            () => OpenAsync(CreatedPayload.Value, "not-the-password"));
        Assert.Equal(KeePassVaultError.InvalidCredentialsOrFile, fault.Error);
    }

    [Fact]
    public async Task A_new_database_arrives_empty_and_clean_the_way_a_browser_reads_it()
    {
        using var session = await new KeePassVaultService().CreateAsync(
            "created.kdbx",
            CreatedPassword);

        Assert.Equal(0, session.EntryCount);
        Assert.Equal(0, session.RecycleBinEntryCount);
        Assert.Empty(session.Groups);
        Assert.Null(session.RecycleBinUuid);
        Assert.False(session.IsDirty);
        Assert.False(session.IsInRecycleBin(session.RootGroupUuid));
        Assert.Equal([], await session.ReadGroupRowsAsync(session.RootGroupUuid));
        Assert.Null(session.SourcePath);

        // A vault without a master password is not something this client will write to disk quietly.
        await Assert.ThrowsAsync<ArgumentException>(
            () => new KeePassVaultService().CreateAsync("created.kdbx", "   "));
    }

    /// <summary>
    /// The reason the version is pinned at create rather than left to the writer's default: 4.1 is the
    /// only KDBX revision with a slot for the folder an entry was moved out of, so at 3.1 the recycle
    /// and restore walk below has nothing to remember the origin with.
    /// </summary>
    [Fact]
    public async Task A_recycled_entry_in_a_new_database_remembers_the_folder_it_left()
    {
        using var session = await OpenAsync(CreatedPayload.Value, CreatedPassword);
        var origin = await session.CreateGroupAsync(session.RootGroupUuid, "Origin");
        Assert.NotNull(origin);
        var entry = await session.CreateEntryAsync(origin!.Uuid, Draft("Created Entry"));
        Assert.NotNull(entry);

        // Nothing is recycled yet, so there is no bin to find - the folder appears with the first
        // recycle, which is when the Android client makes it too.
        Assert.Null(session.RecycleBinUuid);
        Assert.Equal(0, session.RecycleBinEntryCount);

        Assert.Equal(
            KeePassEntryDeleteStatus.Recycled,
            await session.DeleteEntryAsync(entry!.Row.EntryUuid, KeePassDeleteMode.RecycleBin));
        var bin = Assert.Single(session.Groups, group => group.IsRecycleBin);
        Assert.Equal(bin.Uuid, session.RecycleBinUuid);
        Assert.Equal(entry.Row.EntryUuid, Assert.Single(await session.ReadGroupRowsAsync(bin.Uuid)).EntryUuid);

        var staged = await session.ExportAsync();
        Assert.Equal(origin.Uuid, ReadPreviousParentGroup(staged, entry.Row.EntryUuid));

        var restored = await session.RestoreEntryAsync(entry.Row.EntryUuid);
        Assert.NotNull(restored);
        Assert.Equal(origin.Uuid, restored!.Row.GroupUuid);
        Assert.Equal("Origin", restored.Row.GroupPath);
        Assert.Equal(0, session.RecycleBinEntryCount);

        // The pointer is cleared for the next reader, and the file it is cleared in is the one the next
        // client opens.
        var written = await session.ExportAsync();
        Assert.Equal(ZeroUuid, ReadPreviousParentGroup(written, entry.Row.EntryUuid));
        using var reopened = await OpenAsync(written, CreatedPassword);
        var back = Assert.Single(await reopened.ReadGroupRowsAsync(origin.Uuid));
        Assert.Equal(entry.Row.EntryUuid, back.EntryUuid);
        Assert.Equal("Created Entry", back.Title);
    }

    [Fact]
    public async Task A_new_database_written_to_a_path_reopens_from_that_path_and_saves_in_place()
    {
        var directory = Path.Combine(Path.GetTempPath(), "monica-kdbx-create-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(directory, "created-on-disk.kdbx");
        try
        {
            using var session = await new KeePassVaultService().CreateAsync(
                "created-on-disk.kdbx",
                CreatedPassword,
                target);

            Assert.Equal(target, session.SourcePath);
            var onDisk = await File.ReadAllBytesAsync(target);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(onDisk)), session.PayloadSha256);

            var saved = await session.SaveAsync();
            Assert.Equal(target, saved.Path);
            Assert.False(session.IsDirty);

            await session.CreateEntryAsync(session.RootGroupUuid, Draft("On Disk"));
            Assert.True(session.IsDirty);
            await session.SaveAsync();

            using (var reopened = await OpenAsync(await File.ReadAllBytesAsync(target), CreatedPassword, target))
            {
                var row = Assert.Single(await reopened.ReadGroupRowsAsync(reopened.RootGroupUuid));
                Assert.Equal("On Disk", row.Title);
                Assert.Equal("Root", reopened.RootGroupRow.Name);
                Assert.Equal(Kdbx41, reopened.FormatVersion);
            }

            // The session that wrote the file still notices when somebody else does.
            await File.WriteAllBytesAsync(target, [1, 2, 3]);
            var fault = await Assert.ThrowsAsync<KeePassVaultException>(() => session.SaveAsync());
            Assert.Equal(KeePassVaultError.ConcurrentChange, fault.Error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static KeePassEntryEdit Draft(string title) => new(
        "",
        title,
        "creator@example.com",
        "create-shape-fixture-not-a-secret",
        "https://created.example.com",
        "",
        "",
        []);

    private static Task<KeePassVaultSession> OpenAsync(
        byte[] payload,
        string password,
        string? sourcePath = null) =>
        new KeePassVaultService().OpenAsync(payload, "created.kdbx", password, sourcePath);

    private static DiskShape ShapeOf(byte[] payload, string password)
    {
        var database = Load(payload, password);
        try
        {
            var kdf = database.KdfParameters;
            var protection = database.MemoryProtection;
            return new DiskShape(
                KeePassVaultWrite.ReadFormatVersion(payload) ?? 0,
                database.DataCipherUuid.ToHexString(),
                kdf.KdfUuid.ToHexString(),
                Kind(kdf, Argon2Kdf.ParamSalt),
                kdf.GetByteArray(Argon2Kdf.ParamSalt).Length,
                Kind(kdf, Argon2Kdf.ParamIterations),
                Number(kdf, Argon2Kdf.ParamIterations),
                Kind(kdf, Argon2Kdf.ParamMemory),
                Number(kdf, Argon2Kdf.ParamMemory),
                Kind(kdf, Argon2Kdf.ParamParallelism),
                Number(kdf, Argon2Kdf.ParamParallelism),
                Kind(kdf, Argon2Kdf.ParamVersion),
                Number(kdf, Argon2Kdf.ParamVersion),
                Kind(kdf, Argon2Kdf.ParamSecretKey) == "absent",
                Kind(kdf, Argon2Kdf.ParamAssocData) == "absent",
                database.Compression.ToString(),
                database.RootGroup.Name,
                database.RecycleBinEnabled,
                database.HistoryMaxItems,
                database.HistoryMaxSize,
                database.MaintenanceHistoryDays,
                database.DefaultUserName,
                database.MasterKeyChangeRec,
                database.MasterKeyChangeForce,
                protection.ProtectTitle + "/" + protection.ProtectUserName + "/"
                    + protection.ProtectPassword + "/" + protection.ProtectUrl + "/"
                    + protection.ProtectNotes);
        }
        finally
        {
            database.Close();
        }
    }

    /// <summary>
    /// The value's storage type is part of the shape: a client that writes the memory cost as a 32-bit
    /// number and one that writes it as a 64-bit number produce different files, and the Android client
    /// reads the second.
    /// </summary>
    private static string Kind(KdfParameters parameters, string key)
    {
        try
        {
            return parameters.GetTypeOf(key)?.ToString() ?? "absent";
        }
        catch (NullReferenceException)
        {
            return "absent";
        }
    }

    private static ulong Number(KdfParameters parameters, string key) =>
        Kind(parameters, key) switch
        {
            "System.UInt64" => parameters.GetUInt64(key, 0),
            "System.UInt32" => parameters.GetUInt32(key, 0),
            "System.Int64" => (ulong)parameters.GetInt64(key, 0),
            "System.Int32" => (ulong)parameters.GetInt32(key, 0),
            _ => 0
        };

    /// <summary>
    /// Where the file says the entry used to live, with the reason it says so: a database written at a
    /// version without the slot reads back as no pointer, which is what this whole class is for.
    /// </summary>
    private static string ReadPreviousParentGroup(byte[] payload, string entryUuid)
    {
        var database = Load(payload, CreatedPassword);
        try
        {
            return FindEntry(database.RootGroup, entryUuid).PreviousParentGroup?.ToHexString() ?? ZeroUuid;
        }
        finally
        {
            database.Close();
        }
    }

    private static PwEntry FindEntry(PwGroup group, string entryUuid)
    {
        foreach (var entry in group.Entries)
        {
            if (string.Equals(entry.Uuid.ToHexString(), entryUuid, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        foreach (var child in group.Groups)
        {
            try
            {
                return FindEntry(child, entryUuid);
            }
            catch (InvalidOperationException)
            {
                // Not in this branch.
            }
        }

        throw new InvalidOperationException("entry_not_found");
    }

    private static byte[] ReadFixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Kdbx", name));

    private static PwDatabase Load(byte[] payload, string password)
    {
        var key = new CompositeKey();
        key.AddUserKey(new KcpPassword(password));
        var database = new PwDatabase { MasterKey = key };
        new KdbxFile(database).Load(new MemoryStream(payload, writable: false), KdbxFormat.Default, null);
        return database;
    }

    private sealed record DiskShape(
        uint Version,
        string CipherUuid,
        string KdfUuid,
        string KdfSaltKind,
        int KdfSaltBytes,
        string KdfIterationsKind,
        ulong KdfIterations,
        string KdfMemoryKind,
        ulong KdfMemory,
        string KdfParallelismKind,
        ulong KdfParallelism,
        string KdfAlgorithmVersionKind,
        ulong KdfAlgorithmVersion,
        bool KdfSecretKeyAbsent,
        bool KdfAssociatedDataAbsent,
        string Compression,
        string RootGroupName,
        bool RecycleBinEnabled,
        int HistoryMaxItems,
        long HistoryMaxSize,
        uint MaintenanceHistoryDays,
        string DefaultUserName,
        long MasterKeyChangeRec,
        long MasterKeyChangeForce,
        string MemoryProtection);
}
