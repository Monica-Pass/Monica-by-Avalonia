using System.Drawing;
using System.Security.Cryptography;
using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Keys;
using KeePassLib.Serialization;
using KeePassLib.Security;
using Monica.Platform.Services;

namespace Monica.Tests;

/// <summary>
/// What a KeePass client is actually judged on: not what it can show, but what it keeps. The desktop
/// read model carries five standard fields, a TOTP seed and the custom strings - tags, an expiry date,
/// a chosen icon, an AutoType sequence, entry colours and custom data are outside it entirely. Those
/// have to survive an edit and a save anyway, because a person who opens a real vault here and writes
/// it back must not lose the parts this client never displayed.
/// </summary>
[Collection(KeePassVaultTestCollection.Name)]
public sealed class KeePassRichFieldTests : IDisposable
{
    private const string FixturePassword = "rich-fields-fixture-not-a-secret";
    private const string OriginalPassword = "rich-original-secret";
    private const string OriginalTitle = "rich-entry";
    private const string EditedTitle = "Renamed by Avalonia";

    /// <summary>
    /// A 1x1 PNG, so the custom icon is a real icon rather than a placeholder no reader would accept.
    /// </summary>
    private static readonly byte[] IconPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==");

    private static readonly DateTime Expiry = new(2031, 3, 4, 5, 6, 7, DateTimeKind.Utc);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"monica-kdbx-rich-{Guid.NewGuid():N}");

    [Fact]
    public void Fixture_round_trips_through_the_library_it_was_built_with()
    {
        var content = BuildVault();
        var database = ReadDatabase(content);
        try
        {
            var entry = Assert.Single(database.RootGroup.Entries);
            Assert.Equal(["archive", "bank"], entry.Tags.Order(StringComparer.Ordinal));
            Assert.True(entry.Expires);
            Assert.Equal(Expiry, entry.ExpiryTime);
            Assert.Equal(PwIcon.Warning, entry.IconId);
            Assert.Equal(Color.Red.ToArgb(), entry.ForegroundColor.ToArgb());
            Assert.Equal(Color.Lime.ToArgb(), entry.BackgroundColor.ToArgb());
            Assert.True(entry.AutoType.Enabled);
            Assert.Equal("{USERNAME}{TAB}{PASSWORD}{ENTER}", entry.AutoType.DefaultSequence);
            Assert.Equal("Chrome_ahop*>{URL}", DescribeAssociations(entry));
            Assert.Equal("https://override.example.com", entry.OverrideUrl);
            Assert.Equal("archive-2019", entry.CustomData.Get("Archive") ?? "");
            Assert.Single(entry.History);
            Assert.Equal(2, database.CustomIcons.Count);
            Assert.Single(entry.Binaries);
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public async Task Editing_only_the_title_keeps_every_field_the_read_model_never_shows()
    {
        var path = await StageCopyAsync();
        var before = await File.ReadAllBytesAsync(path);
        string entryUuid;

        using (var session = await OpenAsync(before, path))
        {
            var detail = (await session.DrainAsync()).Single(item => item.Row.Title == OriginalTitle);
            entryUuid = detail.Row.EntryUuid;
            Assert.Equal(OriginalPassword, detail.Password);

            var updated = await session.UpdateEntryAsync(new KeePassEntryEdit(
                entryUuid,
                EditedTitle,
                detail.Row.UserName,
                detail.Password,
                detail.Row.Url,
                detail.Notes,
                detail.AuthenticatorKey,
                detail.CustomFields));
            Assert.NotNull(updated);
            await session.SaveAsync();
        }

        var after = await File.ReadAllBytesAsync(path);
        var database = ReadDatabase(after);
        try
        {
            var entry = Assert.Single(database.RootGroup.Entries);

            // Nothing on this list is something the editor was shown, let alone asked to fill in.
            Assert.Equal(["archive", "bank"], entry.Tags.Order(StringComparer.Ordinal));
            Assert.True(entry.Expires);
            Assert.Equal(Expiry, entry.ExpiryTime);
            Assert.Equal(PwIcon.Warning, entry.IconId);
            Assert.Equal(Color.Red.ToArgb(), entry.ForegroundColor.ToArgb());
            Assert.Equal(Color.Lime.ToArgb(), entry.BackgroundColor.ToArgb());
            Assert.True(entry.AutoType.Enabled);
            Assert.Equal("{USERNAME}{TAB}{PASSWORD}{ENTER}", entry.AutoType.DefaultSequence);
            Assert.Equal("Chrome_ahop*>{URL}", DescribeAssociations(entry));
            Assert.Equal("https://override.example.com", entry.OverrideUrl);
            Assert.Equal("archive-2019", entry.CustomData.Get("Archive") ?? "");
            Assert.Contains("Card Number", entry.Strings.GetKeys());
            Assert.Single(entry.Binaries);
            Assert.Equal(EditedTitle, entry.Strings.ReadSafe(PwDefs.TitleField));
            Assert.Equal(2, database.CustomIcons.Count);
            var icons = Assert.Single(database.RootGroup.Groups);
            Assert.Equal("icon-carrier", icons.Entries.Single().Strings.ReadSafe(PwDefs.TitleField));
            // The custom icon was chosen by another client for the entry this edit never touched;
            // the reference and the image both have to still be in the file.
            Assert.NotEqual(PwUuid.Zero.ToHexString(), icons.Entries.Single().CustomIconUuid.ToHexString());
            Assert.Contains(
                icons.Entries.Single().CustomIconUuid.ToHexString(),
                database.CustomIcons.Select(icon => icon.Uuid.ToHexString()));
            Assert.Equal(0x0004_0001u, VersionOf(after));
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public async Task The_history_snapshot_written_for_an_edit_carries_the_rich_fields_too()
    {
        var path = await StageCopyAsync();
        var before = await File.ReadAllBytesAsync(path);

        using (var session = await OpenAsync(before, path))
        {
            var detail = (await session.DrainAsync()).Single(item => item.Row.Title == OriginalTitle);
            await session.UpdateEntryAsync(new KeePassEntryEdit(
                detail.Row.EntryUuid,
                EditedTitle,
                detail.Row.UserName,
                OriginalPassword,
                detail.Row.Url,
                detail.Notes,
                detail.AuthenticatorKey,
                detail.CustomFields));
            await session.SaveAsync();
        }

        var after = await File.ReadAllBytesAsync(path);
        var database = ReadDatabase(after);
        try
        {
            var entry = Assert.Single(database.RootGroup.Entries);
            var history = entry.History.ToList();

            // One generation the fixture shipped with, one the edit added; the writer must neither
            // drop the elder nor let the chain double up.
            Assert.Equal(2, history.Count);
            var youngest = history.MaxBy(item => item.LastModificationTime);
            Assert.NotNull(youngest);
            Assert.Equal(OriginalTitle, youngest!.Strings.ReadSafe(PwDefs.TitleField));
            Assert.Equal(["archive", "bank"], youngest.Tags.Order(StringComparer.Ordinal));
            Assert.True(youngest.Expires);
            Assert.Equal(Expiry, youngest.ExpiryTime);
            Assert.Equal(PwIcon.Warning, youngest.IconId);
            Assert.Equal(Color.Lime.ToArgb(), youngest.BackgroundColor.ToArgb());
            Assert.Equal("{USERNAME}{TAB}{PASSWORD}{ENTER}", youngest.AutoType.DefaultSequence);
            Assert.Equal("archive-2019", youngest.CustomData.Get("Archive") ?? "");
            Assert.Empty(youngest.History);
        }
        finally
        {
            database.Close();
        }
    }

    private static string DescribeAssociations(PwEntry entry) =>
        string.Join(";", entry.AutoType.Associations.Select(a => $"{a.WindowName}>{a.Sequence}"));

    private async Task<string> StageCopyAsync()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "rich.kdbx");
        await File.WriteAllBytesAsync(path, BuildVault());
        return path;
    }

    private static async Task<KeePassVaultSession> OpenAsync(byte[] content, string path) =>
        await new KeePassVaultService().OpenAsync(
            content,
            Path.GetFileName(path),
            FixturePassword,
            path);

    private static uint VersionOf(byte[] content) =>
        (uint)((content[10] | (content[11] << 8)) << 16) | (uint)(content[8] | (content[9] << 8));

    private static PwDatabase ReadDatabase(byte[] content)
    {
        var key = new CompositeKey();
        key.AddUserKey(new KcpPassword(FixturePassword));
        var database = new PwDatabase { MasterKey = key };
        new KdbxFile(database).Load(new MemoryStream(content), KdbxFormat.Default, null);
        return database;
    }

    /// <summary>
    /// Builds a vault the way another client would: the entry holds an expiry, tags, a chosen icon,
    /// colours, an AutoType sequence with a per-window override, an overridden URL, custom data, a
    /// protected custom field, an attachment and one generation of history.
    /// </summary>
    private static byte[] BuildVault() =>
        KeePassTestVault.BuildGated(
            BuildOnce,
            content =>
            {
                var probe = ReadDatabase(content);
                try
                {
                    var entry = probe.RootGroup.Entries.Single();
                    // A fixture whose own fields did not survive its first write is not a fixture.
                    return entry.Tags.Count == 2 && entry.Expires && entry.CustomData.Exists("Archive")
                        && entry.AutoType.AssociationsCount == 1 && probe.CustomIcons.Count == 2
                        && entry.History.Count() == 1 && VersionOf(content) == 0x0004_0001;
                }
                finally
                {
                    probe.Close();
                }
            },
            "Rich KeePass fixture");

    private static byte[] BuildOnce()
    {
        var key = new CompositeKey();
        key.AddUserKey(new KcpPassword(FixturePassword));
        var database = new PwDatabase();
        database.New(IOConnectionInfo.FromPath("rich.kdbx"), key);
        database.Name = "Rich Fixture";
        database.RootGroup.Name = "Rich Root";
        database.HistoryMaxItems = 10;
        database.HistoryMaxSize = 6 * 1024 * 1024;
        database.RecycleBinEnabled = false;

        var firstIcon = new PwCustomIcon(new PwUuid(RandomNumberGenerator.GetBytes(16)), IconPng);
        var secondIcon = new PwCustomIcon(new PwUuid(RandomNumberGenerator.GetBytes(16)), IconPng);
        database.CustomIcons.Add(firstIcon);
        database.CustomIcons.Add(secondIcon);

        var entry = new PwEntry(true, true);
        entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, OriginalTitle));
        entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, "rich@example.com"));
        entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, OriginalPassword));
        entry.Strings.Set(PwDefs.UrlField, new ProtectedString(false, "https://rich.example.com"));
        entry.Strings.Set(PwDefs.NotesField, new ProtectedString(false, "Rich note"));
        entry.Strings.Set("otp", new ProtectedString(true, "otpauth://totp/Rich?secret=JBSWY3DPEHPK3PXP"));
        entry.Strings.Set("Card Number", new ProtectedString(true, "4111111111111111"));
        entry.Binaries.Set("rich.bin", new ProtectedBinary(true, RandomNumberGenerator.GetBytes(24)));

        entry.Tags.Add("bank");
        entry.Tags.Add("archive");
        entry.Expires = true;
        entry.ExpiryTime = Expiry;
        entry.IconId = PwIcon.Warning;
        entry.ForegroundColor = Color.Red;
        entry.BackgroundColor = Color.Lime;
        entry.OverrideUrl = "https://override.example.com";
        entry.QualityCheck = false;
        entry.AutoType.Enabled = true;
        entry.AutoType.DefaultSequence = "{USERNAME}{TAB}{PASSWORD}{ENTER}";
        entry.AutoType.Add(new AutoTypeAssociation("Chrome_ahop*", "{URL}"));
        entry.CustomData.Set("Archive", "archive-2019");

        var snapshot = entry.CloneDeep();
        snapshot.History.Clear();
        // Dated from the run: the writer prunes versions older than the database's 365-day window, so
        // a fixed year would eventually stop being the elder generation the assertions expect.
        snapshot.LastModificationTime = DateTime.UtcNow.AddDays(-3);
        entry.History.Add(snapshot);

        database.RootGroup.AddEntry(entry, true);

        var bystanderGroup = new PwGroup(true, true, "Icons", PwIcon.Folder);
        database.RootGroup.AddGroup(bystanderGroup, true);
        var bystander = new PwEntry(true, true);
        bystander.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "icon-carrier"));
        bystander.CustomIconUuid = secondIcon.Uuid;
        bystanderGroup.AddEntry(bystander, true);

        using var stream = new MemoryStream();
        var writer = new KdbxFile(database);
        typeof(KdbxFile)
            .GetProperty("ForceVersion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(writer, 0x0004_0001u);
        writer.Save(stream, database.RootGroup, KdbxFormat.Default, null);
        var content = stream.ToArray();
        database.Close();
        return content;
    }

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
