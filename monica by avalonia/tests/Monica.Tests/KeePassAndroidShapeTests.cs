using System.Security.Cryptography;
using System.Text;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Serialization;
using Monica.Platform.Services;

namespace Monica.Tests;

/// <summary>
/// Guards the desktop against the two ways a KeePass client fails in practice: it cannot write at
/// all, or it writes and quietly loses what it did not understand. The subject is a vault authored by
/// kotpass 0.10.0 - the exact library coordinate Monica for Android pins - so every assertion here is
/// about a file the other client really produced, not a stand-in.
/// </summary>
[Collection(KeePassVaultTestCollection.Name)]
public sealed class KeePassAndroidShapeTests : IDisposable
{
    private const string FixturePassword = "kdbx-parity-fixture-not-a-secret";
    private const string AndroidCipherUuid = "31C1F2E6BF714350BE5805216AFC5AFF";
    private const string Argon2Uuid = "EF636DDF8C29444B91F7A9A403E30A0C";
    private const uint Kdbx41 = 0x0004_0001;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"monica-kdbx-parity-{Guid.NewGuid():N}");

    [Fact]
    public async Task Android_authored_vault_opens_through_the_app_path()
    {
        var fixture = ReadFixture();
        using var session = await OpenAsync(fixture, "android-kotpass-v1.kdbx");

        Assert.Equal(2, session.EntryCount);
        Assert.Equal(1, session.GroupCount);
        Assert.Equal(Kdbx41, session.FormatVersion);
        Assert.Equal(HashBytes(fixture), session.PayloadSha256);
        Assert.Equal("Parity Fixture", session.DatabaseName);

        var work = Assert.Single(session.Groups);
        Assert.Equal("Work", work.Name);
        Assert.Equal(session.RootGroupUuid, work.ParentUuid);

        var details = await session.DrainAsync();
        var titles = details.Select(item => item.Row.Title).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["parity-root-01", "parity-work-02"], titles);

        var bank = details.Single(item => item.Row.Title == "parity-work-02");
        Assert.Equal("Work", bank.Row.GroupPath);
        Assert.Contains(bank.CustomFields, field => field.Name == "Card Number" && field.IsProtected);
        Assert.Contains(bank.CustomFields, field => field.Name == "MonicaLocalId" && !field.IsProtected);
        var attachment = Assert.Single(bank.Attachments);
        Assert.Equal("parity.bin", attachment.Row.Name);
        Assert.Equal(32, attachment.Content.Length);
    }

    [Fact]
    public async Task Saving_without_editing_keeps_every_group_entry_and_cryptographic_setting()
    {
        var path = await StageCopyAsync();
        var original = await File.ReadAllBytesAsync(path);

        using (var session = await OpenAsync(original, path))
        {
            Assert.False(session.IsDirty);
            var result = await session.SaveAsync();

            Assert.Equal(new FileInfo(path).Length, result.FileBytes);
            Assert.False(session.IsDirty);
        }

        var rewritten = await File.ReadAllBytesAsync(path);
        Assert.NotEqual(HashBytes(original), HashBytes(rewritten));
        AssertSameShape(original, rewritten);

        using var reopened = await OpenAsync(rewritten, path);
        Assert.Equal(2, reopened.EntryCount);
        Assert.Equal(1, reopened.GroupCount);
        Assert.Equal(Kdbx41, reopened.FormatVersion);
        Assert.Equal(AndroidCipherUuid, ReadDatabase(rewritten).DataCipherUuid.ToHexString());
    }

    [Fact]
    public async Task Editing_one_entry_and_saving_keeps_the_rest_of_the_file_intact()
    {
        var path = await StageCopyAsync();
        var before = await File.ReadAllBytesAsync(path);
        string entryUuid;
        KeePassEntryDetail target;
        KeePassEntryDetail bystander;

        using (var session = await OpenAsync(before, path))
        {
            var details = await session.DrainAsync();
            target = details.Single(item => item.Row.Title == "parity-root-01");
            bystander = details.Single(item => item.Row.Title == "parity-work-02");
            entryUuid = target.Row.EntryUuid;

            var updated = await session.UpdateEntryAsync(new KeePassEntryEdit(
                entryUuid,
                "Renamed by Avalonia",
                target.Row.UserName,
                "rotated-secret-value",
                target.Row.Url,
                "edited note",
                target.AuthenticatorKey,
                target.CustomFields));

            Assert.NotNull(updated);
            Assert.Equal("Renamed by Avalonia", updated!.Row.Title);
            Assert.Equal("rotated-secret-value", updated.Password);
            Assert.True(session.IsDirty);
            var result = await session.SaveAsync();
            Assert.Equal(HashBytes(await File.ReadAllBytesAsync(path)), result.PayloadSha256);
        }

        var after = await File.ReadAllBytesAsync(path);
        using (var session = await OpenAsync(after, path))
        {
            var details = await session.DrainAsync();
            var edited = Assert.Single(details, item => item.Row.EntryUuid == entryUuid);
            Assert.Equal("Renamed by Avalonia", edited.Row.Title);
            Assert.Equal("rotated-secret-value", edited.Password);
            Assert.Equal("edited note", edited.Notes);

            // The fields the edit never mentioned have to come back untouched, protection included.
            Assert.Equal(
                bystander.CustomFields.Select(FieldIdentity).Order(StringComparer.Ordinal),
                details.Single(item => item.Row.EntryUuid == bystander.Row.EntryUuid)
                    .CustomFields.Select(FieldIdentity).Order(StringComparer.Ordinal));
            var kept = Assert.Single(
                details.Single(item => item.Row.EntryUuid == bystander.Row.EntryUuid).Attachments);
            Assert.Equal("parity.bin", kept.Row.Name);
            Assert.Equal("Work", bystander.Row.GroupPath);
            Assert.Equal(2, session.EntryCount);
            Assert.Equal(Kdbx41, session.FormatVersion);
        }

        // The fixture ships with one history item; editing adds exactly one generation of its own
        // and the writer must not silently drop or duplicate what was already there.
        Assert.Equal(1, CountHistory(before));
        Assert.Equal(2, CountHistory(after));
        AssertDoesNotLoseUneditedStructure(before, after, entryUuid);
    }

    [Fact]
    public async Task Saving_refuses_to_overwrite_a_file_that_changed_on_disk()
    {
        var path = await StageCopyAsync();
        var original = await File.ReadAllBytesAsync(path);

        using var session = await OpenAsync(original, path);
        await session.UpdateEntryAsync(new KeePassEntryEdit(
            (await session.DrainAsync()).First().Row.EntryUuid,
            "Conflicting edit",
            "",
            "conflicting-secret",
            "",
            "",
            "",
            []));

        // Somebody else writes the vault while this session holds its own edit.
        using var other = await OpenAsync(original, "android-kotpass-v1.kdbx");
        await other.UpdateEntryAsync(new KeePassEntryEdit(
            (await other.DrainAsync()).First().Row.EntryUuid,
            "Written elsewhere",
            "",
            "other-secret",
            "",
            "",
            "",
            []));
        await File.WriteAllBytesAsync(path, await other.ExportAsync());
        var onDisk = HashBytes(await File.ReadAllBytesAsync(path));

        var error = await Assert.ThrowsAsync<KeePassVaultException>(() => session.SaveAsync());
        Assert.Equal(KeePassVaultError.ConcurrentChange, error.Error);
        Assert.Equal(onDisk, HashBytes(await File.ReadAllBytesAsync(path)));
        Assert.True(session.IsDirty);
    }

    [Fact]
    public async Task A_session_opened_from_bytes_cannot_save_in_place_but_can_export()
    {
        var fixture = ReadFixture();
        using var session = await OpenAsync(fixture, "bytes-only.kdbx");

        var error = await Assert.ThrowsAsync<KeePassVaultException>(() => session.SaveAsync());
        Assert.Equal(KeePassVaultError.NoSourceFile, error.Error);
        Assert.Null(session.SourcePath);

        var payload = await session.ExportAsync();
        Assert.NotEmpty(payload);
        Assert.Equal(Kdbx41, session.FormatVersion);
    }

    [Fact]
    public async Task Exported_payload_unlocks_without_touching_the_disk()
    {
        var fixture = ReadFixture();
        byte[] payload;
        using (var session = await OpenAsync(fixture, "android-kotpass-v1.kdbx"))
        {
            payload = await session.ExportAsync();
        }

        using var reopened = await new KeePassVaultService().OpenAsync(
            payload,
            "android-kotpass-v1.kdbx",
            FixturePassword);
        Assert.Equal(2, reopened.EntryCount);
        Assert.Equal(Kdbx41, reopened.FormatVersion);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(payload)), reopened.PayloadSha256);
    }

    private static string FieldIdentity(KeePassCustomField field) =>
        $"{field.Name}|{(field.IsProtected ? "protected" : "plain")}";

    private static byte[] ReadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Kdbx", "android-kotpass-v1.kdbx");
        return File.ReadAllBytes(path);
    }

    private async Task<string> StageCopyAsync()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "work.kdbx");
        await File.WriteAllBytesAsync(path, ReadFixture());
        return path;
    }

    private static async Task<KeePassVaultSession> OpenAsync(byte[] content, string fileNameOrPath)
    {
        var isPath = Path.IsPathRooted(fileNameOrPath);
        return await new KeePassVaultService().OpenAsync(
            content,
            isPath ? Path.GetFileName(fileNameOrPath) : fileNameOrPath,
            FixturePassword,
            isPath ? fileNameOrPath : null);
    }

    private static PwDatabase ReadDatabase(byte[] content)
    {
        var key = new CompositeKey();
        key.AddUserKey(new KcpPassword(FixturePassword));
        var database = new PwDatabase();
        database.MasterKey = key;
        new KdbxFile(database).Load(new MemoryStream(content), KdbxFormat.Default, null);
        return database;
    }

    /// <summary>
    /// Compares the two files at the level a KeePass client is judged on: header shape plus every
    /// group, entry, field, attachment and history item. Values enter the comparison as hashes so a
    /// failure message cannot carry a secret out of the vault.
    /// </summary>
    private static void AssertSameShape(byte[] before, byte[] after)
    {
        var oldDatabase = ReadDatabase(before);
        var newDatabase = ReadDatabase(after);
        try
        {
            Assert.Equal(AndroidCipherUuid, newDatabase.DataCipherUuid.ToHexString());
            Assert.Equal(oldDatabase.Compression, newDatabase.Compression);
            Assert.Equal(Argon2Uuid, newDatabase.KdfParameters.KdfUuid.ToHexString());
            Assert.Equal(2u, newDatabase.KdfParameters.GetUInt32("P", 0));
            Assert.Equal(33_554_432UL, newDatabase.KdfParameters.GetUInt64("M", 0));
            Assert.Equal(8UL, newDatabase.KdfParameters.GetUInt64("I", 0));
            Assert.Equal(0x13U, newDatabase.KdfParameters.GetUInt32("V", 0));
            Assert.Equal(32, newDatabase.KdfParameters.GetByteArray("S").Length);
            Assert.Equal(Kdbx41, VersionOf(after));
            Assert.Equal(Describe(oldDatabase), Describe(newDatabase));
        }
        finally
        {
            oldDatabase.Close();
            newDatabase.Close();
        }
    }

    /// <summary>
    /// The one place where an edit is allowed to show up: the touched entry and its history. Every
    /// other line of the description has to be identical, which is what keeps a save from being a
    /// rewrite that drops whatever the writer did not model.
    /// </summary>
    private static void AssertDoesNotLoseUneditedStructure(byte[] before, byte[] after, string editedUuid)
    {
        var oldLines = DescribeBytes(before).Where(line => !line.Contains(editedUuid, StringComparison.OrdinalIgnoreCase));
        var newLines = DescribeBytes(after).Where(line => !line.Contains(editedUuid, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(oldLines, newLines);
    }

    private static List<string> DescribeBytes(byte[] content)
    {
        var database = ReadDatabase(content);
        try
        {
            return Describe(database);
        }
        finally
        {
            database.Close();
        }
    }

    private static uint VersionOf(byte[] content)
    {
        var minor = (uint)(content[8] | (content[9] << 8));
        var major = (uint)(content[10] | (content[11] << 8));
        return (major << 16) | minor;
    }

    private static int CountHistory(byte[] content) =>
        DescribeBytes(content).Count(line => line.StartsWith("H|", StringComparison.Ordinal));

    private static List<string> Describe(PwDatabase database)
    {
        var lines = new List<string>();
        Walk(database.RootGroup, lines);
        lines.Add($"M|name={database.Name}|hist={database.HistoryMaxItems}|size={database.HistoryMaxSize}" +
                  $"|maint={database.MaintenanceHistoryDays}|recycle={database.RecycleBinEnabled}" +
                  $"|icons={database.CustomIcons.Count}|deleted={database.DeletedObjects.Count()}");
        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    private static void Walk(PwGroup group, List<string> lines)
    {
        lines.Add($"G|{group.Uuid.ToHexString().ToLowerInvariant()}|{group.Name}");
        foreach (var entry in group.Entries)
        {
            var fields = entry.Strings
                .Select(item => $"{item.Key}:{(item.Value.IsProtected ? "p" : "s")}:{Hash(item.Value.ReadString())}")
                .Order(StringComparer.Ordinal);
            var binaries = entry.Binaries
                .Select(item => $"{item.Key}:{item.Value.Length}:{Hash(item.Value.ReadData())}")
                .Order(StringComparer.Ordinal);
            lines.Add($"E|{entry.Uuid.ToHexString().ToLowerInvariant()}|{string.Join(",", fields)}|{string.Join(",", binaries)}");
            lines.Add($"X|{entry.Uuid.ToHexString().ToLowerInvariant()}|icon={entry.IconId}" +
                      $"|expires={entry.Expires}|autotype={entry.AutoType.Enabled}" +
                      $"|assoc={string.Join(";", entry.AutoType.Associations.Select(a => a.WindowName + ">" + a.Sequence))}");
            foreach (var history in entry.History)
            {
                lines.Add($"H|{entry.Uuid.ToHexString().ToLowerInvariant()}|{history.CreationTime.Ticks}");
            }
        }

        foreach (var child in group.Groups)
        {
            Walk(child, lines);
        }
    }

    private static string Hash(string value) => Short(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Hash(byte[] value) => Short(SHA256.HashData(value));

    private static string Short(byte[] hash) => Convert.ToHexString(hash)[..12];

    private static string HashBytes(byte[] value) => Convert.ToHexString(SHA256.HashData(value));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a locked temp directory is not worth failing a green run over.
        }
    }
}

internal static class KeePassSessionTestExtensions
{
    internal static async Task<List<KeePassEntryDetail>> DrainAsync(this KeePassVaultSession session)
    {
        var details = new List<KeePassEntryDetail>();
        await foreach (var detail in session.ReadDetailsAsync())
        {
            details.Add(detail);
        }

        return details;
    }
}
