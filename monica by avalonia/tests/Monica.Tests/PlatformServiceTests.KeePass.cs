using System.Reflection;
using System.Runtime.CompilerServices;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed partial class PlatformServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeePass_service_opens_kdbx3_and_kdbx4_and_streams_every_field(bool useKdbx3)
    {
        var fixture = KeePassTestVault.Create("correct horse battery staple", useKdbx3, withAttachment: true);
        var service = new KeePassVaultService();
        using var session = await service.OpenAsync(fixture.Content, "business-vault.kdbx", fixture.Password);

        Assert.Equal("Business", session.DatabaseName);
        Assert.Equal("business-vault.kdbx", session.SourceFileName);
        Assert.Equal(fixture.RootUuid, session.RootGroupUuid);
        Assert.True(session.DatabaseId > 0);
        Assert.Equal(2, session.GroupCount);
        Assert.Equal(2, session.EntryCount);
        Assert.Equal(
            fixture.Groups.Select(group => group.Path),
            session.Groups.Select(group => group.Path));
        Assert.Equal("Personal/Cloud", session.Groups.Single(group => group.Name == "Cloud").Path);

        var details = await CollectDetailsAsync(session);
        var cloud = Assert.Single(details, detail => detail.Row.Title == KeePassTestVault.CloudTitle);
        Assert.Equal("cloud@example.com", cloud.Row.UserName);
        Assert.Equal(KeePassTestVault.CloudPassword, cloud.Password);
        Assert.Equal("Cloud note", cloud.Notes);
        Assert.Equal(KeePassTestVault.CloudTotp, cloud.AuthenticatorKey);
        Assert.Equal("https://cloud.example.com", cloud.Row.Url);
        Assert.Equal("Personal/Cloud", cloud.Row.GroupPath);
        Assert.Equal(
            fixture.Entries.Single(entry => entry.Title == KeePassTestVault.CloudTitle).Uuid,
            cloud.Row.EntryUuid);
        Assert.Equal(
            fixture.Entries.Single(entry => entry.Title == KeePassTestVault.CloudTitle).GroupUuid,
            cloud.Row.GroupUuid);
        var customField = Assert.Single(cloud.CustomFields);
        Assert.Equal("Tenant", customField.Name);
        Assert.Equal("Production", customField.Value);
        Assert.True(customField.IsProtected);
        var attachment = Assert.Single(cloud.Attachments);
        Assert.Equal(KeePassTestVault.AttachmentName, attachment.Row.Name);
        Assert.Equal(KeePassTestVault.AttachmentName, attachment.Row.BinaryReference);
        Assert.Equal(KeePassTestVault.AttachmentContent.Length, attachment.Row.SizeBytes);
        Assert.Equal(
            System.Text.Encoding.UTF8.GetBytes(KeePassTestVault.AttachmentContent),
            attachment.Content.ToArray());
    }

    [Fact]
    public async Task KeePass_session_reports_counts_without_resolving_any_entry_secret()
    {
        var fixture = KeePassTestVault.Create("password", withAttachment: true);
        var service = new KeePassVaultService();
        using var session = await service.OpenAsync(fixture.Content, "business.kdbx", fixture.Password);

        Assert.Equal(2, session.EntryCount);
        Assert.Equal(2, session.GroupCount);
        Assert.All(session.Groups, group => Assert.False(string.IsNullOrWhiteSpace(group.Uuid)));
        Assert.DoesNotContain(
            KeePassTestVault.CloudPassword,
            string.Join('|', session.Groups.Select(group => group.Path + group.Name + group.Uuid)),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeePass_session_releases_the_unlocked_database_when_disposed()
    {
        var fixture = KeePassTestVault.Create("password");
        var service = new KeePassVaultService();
        var session = await service.OpenAsync(fixture.Content, "business.kdbx", fixture.Password);

        session.Dispose();
        session.Dispose();

        Assert.Equal(0, session.EntryCount);
        Assert.Empty(session.Groups);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await CollectDetailsAsync(session));
    }

    [Fact]
    public async Task KeePass_released_session_lets_the_decrypted_model_be_collected()
    {
        var fixture = KeePassTestVault.Create("password");
        var weakRoot = await OpenIndexAndReleaseAsync(fixture);
        for (var attempt = 0; attempt < 5 && weakRoot.IsAlive; attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true);
            GC.WaitForPendingFinalizers();
        }

        Assert.False(weakRoot.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> OpenIndexAndReleaseAsync(KeePassTestVault.Fixture fixture)
    {
        var service = new KeePassVaultService();
        using var session = await service.OpenAsync(fixture.Content, "business.kdbx", fixture.Password);
        var root = typeof(KeePassVaultSession)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session)!;
        return new WeakReference(root);
    }

    [Fact]
    public async Task KeePass_session_stays_readable_after_an_abandoned_enumeration()
    {
        var fixture = KeePassTestVault.Create("password");
        var service = new KeePassVaultService();
        using var session = await service.OpenAsync(fixture.Content, "business.kdbx", fixture.Password);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var streamed = 0;
        await foreach (var detail in session.ReadDetailsAsync(timeout.Token))
        {
            streamed++;
            Assert.False(string.IsNullOrWhiteSpace(detail.Row.Title));
            break;
        }

        Assert.Equal(1, streamed);
        var resumed = await CollectDetailsAsync(session, timeout.Token);
        Assert.Equal(session.EntryCount, resumed.Count);
    }

    [Fact]
    public async Task KeePass_service_returns_stable_database_identity_for_the_same_root_group()
    {
        var first = KeePassTestVault.Create("password");
        var second = KeePassTestVault.Create("password", rootUuidBytes: first.RootUuidBytes);
        var service = new KeePassVaultService();
        using var firstSession = await service.OpenAsync(first.Content, "first.kdbx", first.Password);
        using var secondSession = await service.OpenAsync(second.Content, "renamed.kdbx", second.Password);

        Assert.Equal(firstSession.DatabaseId, secondSession.DatabaseId);
    }

    [Fact]
    public async Task KeePass_service_normalizes_wrong_password_without_leaking_secret_or_file_name()
    {
        var fixture = KeePassTestVault.Create("real-password");
        var service = new KeePassVaultService();

        var error = await Assert.ThrowsAsync<KeePassVaultException>(() =>
            service.OpenAsync(fixture.Content, "private-client-name.kdbx", "wrong-password"));

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
            service.OpenAsync("not-a-kdbx"u8.ToArray(), "damaged.kdbx", "password"));

        Assert.Equal(KeePassVaultError.InvalidCredentialsOrFile, error.Error);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task KeePass_service_honors_pre_cancelled_reads()
    {
        var fixture = KeePassTestVault.Create("password");
        var service = new KeePassVaultService();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.OpenAsync(fixture.Content, "cancelled.kdbx", fixture.Password, cancellation.Token));
    }

    [Fact]
    public async Task KeePass_browse_exposes_root_row_and_folder_scoped_entries()
    {
        var fixture = KeePassTestVault.Create("password");
        var service = new KeePassVaultService();
        using var session = await service.OpenAsync(fixture.Content, "browse.kdbx", fixture.Password);

        Assert.Equal("Business Root", session.RootGroupRow.Name);
        Assert.Equal(fixture.RootUuid, session.RootGroupRow.Uuid);
        Assert.Null(session.RootGroupRow.ParentUuid);
        Assert.False(session.RootGroupRow.HasEntries);

        var rootEntries = await session.ReadGroupRowsAsync(session.RootGroupUuid);
        Assert.Empty(rootEntries);

        var personal = session.Groups.Single(group => group.Name == "Personal");
        Assert.True(personal.HasEntries);
        var personalEntries = await session.ReadGroupRowsAsync(personal.Uuid);
        var personalRow = Assert.Single(personalEntries);
        Assert.Equal(KeePassTestVault.ExistingTitle, personalRow.Title);
        Assert.Equal("existing@example.com", personalRow.UserName);

        var cloud = session.Groups.Single(group => group.Name == "Cloud");
        Assert.True(cloud.HasEntries);
        var cloudEntries = await session.ReadGroupRowsAsync(cloud.Uuid);
        var cloudRow = Assert.Single(cloudEntries);
        Assert.Equal(KeePassTestVault.CloudTitle, cloudRow.Title);
    }

    [Fact]
    public async Task KeePass_browse_resolves_one_entry_secret_on_demand()
    {
        var fixture = KeePassTestVault.Create("password");
        var service = new KeePassVaultService();
        using var session = await service.OpenAsync(fixture.Content, "browse.kdbx", fixture.Password);

        var cloud = session.Groups.Single(group => group.Name == "Cloud");
        var cloudEntry = fixture.Entries.Single(entry => entry.Title == KeePassTestVault.CloudTitle);
        var detail = await session.ReadDetailAsync(cloud.Uuid, cloudEntry.Uuid);

        Assert.NotNull(detail);
        Assert.Equal(KeePassTestVault.CloudTitle, detail.Row.Title);
        Assert.Equal(KeePassTestVault.CloudPassword, detail.Password);
        Assert.Equal("Cloud note", detail.Notes);
        Assert.Equal(KeePassTestVault.CloudTotp, detail.AuthenticatorKey);
    }

    [Fact]
    public async Task KeePass_browse_returns_null_for_a_missing_entry()
    {
        var fixture = KeePassTestVault.Create("password");
        var service = new KeePassVaultService();
        using var session = await service.OpenAsync(fixture.Content, "browse.kdbx", fixture.Password);

        var cloud = session.Groups.Single(group => group.Name == "Cloud");
        var detail = await session.ReadDetailAsync(cloud.Uuid, "deadbeef");

        Assert.Null(detail);
    }

    private static async Task<List<KeePassEntryDetail>> CollectDetailsAsync(
        KeePassVaultSession session,
        CancellationToken cancellationToken = default)
    {
        var details = new List<KeePassEntryDetail>();
        await foreach (var detail in session.ReadDetailsAsync(cancellationToken))
        {
            details.Add(detail);
        }

        return details;
    }
}
