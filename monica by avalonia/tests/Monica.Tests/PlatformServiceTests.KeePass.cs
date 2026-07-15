using System.Reflection;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed partial class PlatformServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeePass_service_reads_kdbx3_and_kdbx4_with_groups_fields_totp_and_attachments(bool useKdbx3)
    {
        var fixture = CreateKeePassFixture("correct horse battery staple", useKdbx3);
        var service = new KeePassVaultService();

        var snapshot = await service.ReadAsync(fixture.Content, "business-vault.kdbx", fixture.Password);

        Assert.Equal("Fixture Vault", snapshot.DatabaseName);
        Assert.Equal("business-vault.kdbx", snapshot.SourceFileName);
        Assert.Equal(fixture.RootUuid, snapshot.RootGroupUuid);
        Assert.True(snapshot.DatabaseId > 0);
        Assert.Equal(2, snapshot.Groups.Count);
        var group = Assert.Single(snapshot.Groups, item => item.Name == "Accounts");
        Assert.Equal("Personal/Accounts", group.Path);
        var entry = Assert.Single(snapshot.Entries);
        Assert.Equal("Example", entry.Title);
        Assert.Equal("person@example.com", entry.UserName);
        Assert.Equal("correct-secret", entry.Password);
        Assert.Equal("https://example.com/login", entry.Url);
        Assert.Equal("Imported note", entry.Notes);
        Assert.Equal("otpauth://totp/Example:person@example.com?secret=JBSWY3DPEHPK3PXP&issuer=Example", entry.AuthenticatorKey);
        Assert.Equal("Personal/Accounts", entry.GroupPath);
        Assert.Equal(fixture.EntryUuid, entry.EntryUuid);
        Assert.Equal(fixture.GroupUuid, entry.GroupUuid);
        Assert.Contains(entry.CustomFields, item => item.Name == "Account number" && item.Value == "AC-42" && item.IsProtected);
        var attachment = Assert.Single(entry.Attachments);
        Assert.Equal("recovery.txt", attachment.Name);
        Assert.Equal("recovery.txt", attachment.BinaryReference);
        Assert.Equal("recovery material"u8.ToArray(), attachment.Content.ToArray());
    }

    [Fact]
    public async Task KeePass_service_returns_stable_database_identity_for_the_same_root_group()
    {
        var first = CreateKeePassFixture("password", useKdbx3: false);
        var second = CreateKeePassFixture("password", useKdbx3: false, first.RootUuidBytes);
        var service = new KeePassVaultService();

        var firstSnapshot = await service.ReadAsync(first.Content, "first.kdbx", first.Password);
        var secondSnapshot = await service.ReadAsync(second.Content, "renamed.kdbx", second.Password);

        Assert.Equal(firstSnapshot.DatabaseId, secondSnapshot.DatabaseId);
    }

    [Fact]
    public async Task KeePass_service_normalizes_wrong_password_without_leaking_secret_or_file_name()
    {
        var fixture = CreateKeePassFixture("real-password", useKdbx3: false);
        var service = new KeePassVaultService();

        var error = await Assert.ThrowsAsync<KeePassVaultException>(() =>
            service.ReadAsync(fixture.Content, "private-client-name.kdbx", "wrong-password"));

        Assert.Equal(KeePassVaultError.InvalidCredentialsOrFile, error.Error);
        Assert.DoesNotContain("wrong-password", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("real-password", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private-client-name", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task KeePass_service_normalizes_malformed_files()
    {
        var service = new KeePassVaultService();

        var error = await Assert.ThrowsAsync<KeePassVaultException>(() =>
            service.ReadAsync("not-a-kdbx"u8.ToArray(), "damaged.kdbx", "password"));

        Assert.Equal(KeePassVaultError.InvalidCredentialsOrFile, error.Error);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task KeePass_service_honors_pre_cancelled_reads()
    {
        var fixture = CreateKeePassFixture("password", useKdbx3: false);
        var service = new KeePassVaultService();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.ReadAsync(fixture.Content, "cancelled.kdbx", fixture.Password, cancellation.Token));
    }

    private static KeePassFixture CreateKeePassFixture(string password, bool useKdbx3, byte[]? rootUuidBytes = null)
    {
        var key = new CompositeKey();
        key.AddUserKey(new KcpPassword(password));
        var database = new PwDatabase();
        database.New(IOConnectionInfo.FromPath("fixture.kdbx"), key);
        database.Name = "Fixture Vault";
        database.RootGroup.Name = "Fixture Root";
        if (rootUuidBytes is not null)
        {
            database.RootGroup.Uuid = new PwUuid(rootUuidBytes);
        }

        var personal = new PwGroup(true, true, "Personal", PwIcon.Folder);
        database.RootGroup.AddGroup(personal, true);
        var accounts = new PwGroup(true, true, "Accounts", PwIcon.Folder);
        personal.AddGroup(accounts, true);
        var entry = new PwEntry(true, true);
        entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "Example"));
        entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, "person@example.com"));
        entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "correct-secret"));
        entry.Strings.Set(PwDefs.UrlField, new ProtectedString(false, "https://example.com/login"));
        entry.Strings.Set(PwDefs.NotesField, new ProtectedString(false, "Imported note"));
        entry.Strings.Set("otp", new ProtectedString(true, "otpauth://totp/Example:person@example.com?secret=JBSWY3DPEHPK3PXP&issuer=Example"));
        entry.Strings.Set("Account number", new ProtectedString(true, "AC-42"));
        entry.Binaries.Set("recovery.txt", new ProtectedBinary(true, "recovery material"u8.ToArray()));
        accounts.AddEntry(entry, true);

        if (useKdbx3)
        {
            database.KdfParameters = new AesKdf().GetDefaultParameters();
        }

        using var stream = new MemoryStream();
        var writer = new KdbxFile(database);
        if (useKdbx3)
        {
            typeof(KdbxFile)
                .GetProperty("ForceVersion", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(writer, 0x00030001u);
        }

        writer.Save(stream, database.RootGroup, KdbxFormat.Default, null);
        var fixture = new KeePassFixture(
            stream.ToArray(),
            password,
            database.RootGroup.Uuid.ToHexString(),
            database.RootGroup.Uuid.UuidBytes,
            accounts.Uuid.ToHexString(),
            entry.Uuid.ToHexString());
        database.Close();
        return fixture;
    }

    private sealed record KeePassFixture(
        byte[] Content,
        string Password,
        string RootUuid,
        byte[] RootUuidBytes,
        string GroupUuid,
        string EntryUuid);
}
