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

    private static readonly object BuilderGate = new();
    private const int MaximumBuildAttempts = 5;

    /// <summary>
    /// Builds a fixture and proves it unlocks. KPCLib writes are not thread safe: concurrent
    /// saves have produced databases that reject the key that created them, so the build runs
    /// under a process-wide gate and is retried until the payload actually unlocks.
    /// </summary>
    internal static Fixture Create(
        string password,
        bool useKdbx3 = false,
        bool withAttachment = false,
        byte[]? rootUuidBytes = null)
    {
        lock (BuilderGate)
        {
            for (var attempt = 0; attempt < MaximumBuildAttempts; attempt++)
            {
                var fixture = CreateOnce(password, useKdbx3, withAttachment, rootUuidBytes);
                if (Unlocks(fixture))
                {
                    return fixture;
                }
            }
        }

        throw new InvalidOperationException(
            $"KeePass test fixture could not be written in a readable state after {MaximumBuildAttempts} attempts.");
    }

    private static bool Unlocks(Fixture fixture)
    {
        try
        {
            using var session = new KeePassVaultService()
                .OpenAsync(fixture.Content, "fixture.kdbx", fixture.Password)
                .GetAwaiter()
                .GetResult();
            return session.EntryCount == 2;
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
        byte[]? rootUuidBytes)
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
        if (useKdbx3)
        {
            typeof(KdbxFile)
                .GetProperty("ForceVersion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(writer, 0x00030001u);
        }

        writer.Save(stream, database.RootGroup, KdbxFormat.Default, null);
        var fixture = new Fixture(
            stream.ToArray(),
            password,
            database.RootGroup.Uuid.ToHexString(),
            database.RootGroup.Uuid.UuidBytes,
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
        IReadOnlyList<GroupIdentity> Groups,
        IReadOnlyList<EntryIdentity> Entries);

    internal sealed record GroupIdentity(string Uuid, string Path);

    internal sealed record EntryIdentity(string Uuid, string GroupUuid, string GroupPath, string Title);
}
