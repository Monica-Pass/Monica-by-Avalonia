using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using Monica.Platform.Services;

namespace Monica.Tests;

/// <summary>
/// Builds real KDBX payloads so KeePass tests exercise the same decode path the app uses.
/// </summary>
internal static class KeePassTestVault
{
    internal const string CloudTitle = "Cloud account";
    internal const string ExistingTitle = "Existing from source";
    internal const string CloudPassword = "cloud-secret";
    internal const string CloudTotp = "otpauth://totp/Cloud?secret=JBSWY3DPEHPK3PXP";
    internal const string AttachmentName = "recovery.txt";
    internal const string AttachmentContent = "recovery material";

    /// <summary>
    /// The one KDBX version that records which folder an entry was moved out of. Measured: writing a
    /// set <c>PreviousParentGroup</c> at 3.1 or 4.0 reads back as all zeroes, so a test that needs the
    /// origin to survive the file has to ask for this version.
    /// </summary>
    internal const uint Kdbx41 = 0x0004_0001;

    /// <summary>
    /// What the library writes when nothing is pinned, so a test that means "the older file" can say so
    /// without a bare hex literal.
    /// </summary>
    internal const uint Kdbx31 = 0x0003_0001;

    private static readonly object BuilderGate = new();
    private const int MaximumBuildAttempts = 5;

    /// <summary>
    /// Runs one KPCLib write under the process-wide gate. A save that overlaps another test's save has
    /// produced databases that reject the key that created them, so a payload rewritten in place - the
    /// shape a test cannot get from the standard fixture - goes through here too.
    /// </summary>
    internal static T Gated<T>(Func<T> write)
    {
        lock (BuilderGate)
        {
            return write();
        }
    }

    /// <summary>
    /// Builds a payload under the gate and reads it back until it describes itself, because a fixture
    /// that did not survive its own write would be testing the fixture rather than the code. A write
    /// that will not unlock counts as one failed attempt rather than as a failed test.
    /// </summary>
    internal static T BuildGated<T>(Func<T> build, Func<T, bool> describesItself, string label)
    {
        lock (BuilderGate)
        {
            for (var attempt = 0; attempt < MaximumBuildAttempts; attempt++)
            {
                try
                {
                    var built = build();
                    if (describesItself(built))
                    {
                        return built;
                    }
                }
                catch (Exception error) when (error is KeePassVaultException or InvalidCompositeKeyException)
                {
                    // Retry: the payload is unreadable, which is what a collided write leaves behind.
                }
            }
        }

        throw new InvalidOperationException(
            $"{label} could not be written in a self-describing state after {MaximumBuildAttempts} attempts.");
    }

    /// <summary>
    /// Builds a fixture and proves it unlocks. KPCLib writes are not thread safe: concurrent
    /// saves have produced databases that reject the key that created them, so the build runs
    /// under a process-wide gate and is retried until the payload actually unlocks.
    /// </summary>
    /// <param name="formatVersion">
    /// The version to write the payload at, or null for whatever the library defaults to - which is
    /// 3.1, not 4.0. Pass <see cref="Kdbx41"/> when the shape under test needs a field only that
    /// version carries.
    /// </param>
    internal static Fixture Create(
        string password,
        bool useKdbx3 = false,
        bool withAttachment = false,
        byte[]? rootUuidBytes = null,
        uint? formatVersion = null)
    {
        lock (BuilderGate)
        {
            for (var attempt = 0; attempt < MaximumBuildAttempts; attempt++)
            {
                var fixture = CreateOnce(password, useKdbx3, withAttachment, rootUuidBytes, formatVersion);
                if (Unlocks(fixture))
                {
                    return fixture;
                }
            }
        }

        throw new InvalidOperationException(
            $"KeePass test fixture could not be written in a readable state after {MaximumBuildAttempts} attempts.");
    }

    /// <summary>
    /// The KDBX version a payload's own header claims, which is the only evidence a test has that it
    /// staged the shape it means to.
    /// </summary>
    internal static uint HeaderVersion(byte[] content)
    {
        var minor = (uint)(content[8] | (content[9] << 8));
        var major = (uint)(content[10] | (content[11] << 8));
        return (major << 16) | minor;
    }

    private static bool Unlocks(Fixture fixture)
    {
        try
        {
            using var session = new KeePassVaultService()
                .OpenAsync(fixture.Content, "fixture.kdbx", fixture.Password)
                .GetAwaiter()
                .GetResult();
            return session.EntryCount == 2 && session.FormatVersion == fixture.FormatVersion;
        }
        catch (KeePassVaultException)
        {
            return false;
        }
    }

    private static Fixture CreateOnce(
        string password,
        bool useKdbx3,
        bool withAttachment,
        byte[]? rootUuidBytes,
        uint? formatVersion)
    {
        var key = new CompositeKey();
        key.AddUserKey(new KcpPassword(password));
        var database = new PwDatabase();
        database.New(IOConnectionInfo.FromPath("fixture.kdbx"), key);
        database.Name = "Business";
        database.RootGroup.Name = "Business Root";
        if (rootUuidBytes is not null)
        {
            database.RootGroup.Uuid = new PwUuid(rootUuidBytes);
        }

        var personal = new PwGroup(true, true, "Personal", PwIcon.Folder);
        database.RootGroup.AddGroup(personal, true);
        var cloud = new PwGroup(true, true, "Cloud", PwIcon.Folder);
        personal.AddGroup(cloud, true);

        var existing = new PwEntry(true, true);
        SetStandardFields(existing, ExistingTitle, "existing@example.com", "source-secret", "https://existing.example.com", "Existing note");
        personal.AddEntry(existing, true);

        var cloudEntry = new PwEntry(true, true);
        SetStandardFields(cloudEntry, CloudTitle, "cloud@example.com", CloudPassword, "https://cloud.example.com", "Cloud note");
        cloudEntry.Strings.Set("otp", new ProtectedString(true, CloudTotp));
        cloudEntry.Strings.Set("Tenant", new ProtectedString(true, "Production"));
        if (withAttachment)
        {
            cloudEntry.Binaries.Set(
                AttachmentName,
                new ProtectedBinary(true, System.Text.Encoding.UTF8.GetBytes(AttachmentContent)));
        }

        cloud.AddEntry(cloudEntry, true);

        if (useKdbx3)
        {
            database.KdfParameters = new AesKdf().GetDefaultParameters();
        }

        using var stream = new MemoryStream();
        var writer = new KdbxFile(database);
        var pin = formatVersion ?? (useKdbx3 ? Kdbx31 : default(uint?));
        if (pin is { } version)
        {
            typeof(KdbxFile)
                .GetProperty("ForceVersion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(writer, version);
        }

        writer.Save(stream, database.RootGroup, KdbxFormat.Default, null);
        var content = stream.ToArray();
        var fixture = new Fixture(
            content,
            password,
            database.RootGroup.Uuid.ToHexString(),
            database.RootGroup.Uuid.UuidBytes,
            HeaderVersion(content),
            [
                new GroupIdentity(personal.Uuid.ToHexString(), "Personal"),
                new GroupIdentity(cloud.Uuid.ToHexString(), "Personal/Cloud")
            ],
            [
                new EntryIdentity(existing.Uuid.ToHexString(), personal.Uuid.ToHexString(), "Personal", ExistingTitle),
                new EntryIdentity(cloudEntry.Uuid.ToHexString(), cloud.Uuid.ToHexString(), "Personal/Cloud", CloudTitle)
            ]);
        database.Close();
        return fixture;
    }

    private static void SetStandardFields(
        PwEntry entry,
        string title,
        string userName,
        string password,
        string url,
        string notes)
    {
        entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
        entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, userName));
        entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, password));
        entry.Strings.Set(PwDefs.UrlField, new ProtectedString(false, url));
        entry.Strings.Set(PwDefs.NotesField, new ProtectedString(false, notes));
    }

    internal sealed record Fixture(
        byte[] Content,
        string Password,
        string RootUuid,
        byte[] RootUuidBytes,
        uint FormatVersion,
        IReadOnlyList<GroupIdentity> Groups,
        IReadOnlyList<EntryIdentity> Entries);

    internal sealed record GroupIdentity(string Uuid, string Path);

    internal sealed record EntryIdentity(string Uuid, string GroupUuid, string GroupPath, string Title);
}
