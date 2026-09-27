using Monica.Platform.Services;

namespace Monica.Tests;

/// <summary>
/// Guards the fixture generator the artifact memory gate depends on. The gate itself cannot tell a
/// wrong shape from a regression, so the shape is asserted here instead.
/// </summary>
[Collection(KeePassVaultTestCollection.Name)]
public sealed class KeePassSmokeVaultWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"monica-keepass-seed-{Guid.NewGuid():N}");

    [Fact]
    public async Task Smoke_fixture_open_back_with_the_requested_shape()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "probe.kdbx");
        const int entries = 500;
        const int groups = 5;

        var info = KeePassSmokeVaultWriter.Write(path, KeePassSmokeVaultWriter.DefaultPassword, entries, groups);

        Assert.True(new FileInfo(path).Length == info.FileBytes);
        Assert.Equal(entries, info.Entries);
        Assert.Equal(groups, info.Groups);

        var content = await File.ReadAllBytesAsync(path);
        using var session = await new KeePassVaultService().OpenAsync(
            content,
            "probe.kdbx",
            KeePassSmokeVaultWriter.DefaultPassword);
        Assert.Equal(entries, session.EntryCount);
        Assert.Equal(groups, session.GroupCount);

        var withAttachment = 0;
        var seenTitles = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var detail in session.ReadDetailsAsync())
        {
            Assert.True(seenTitles.Add(detail.Row.Title));
            Assert.StartsWith("Entry ", detail.Row.Title);
            Assert.StartsWith("secret-", detail.Password);
            Assert.Contains("otpauth://totp", detail.AuthenticatorKey);
            Assert.Contains(detail.CustomFields, field => field.Name == "Reference" && field.IsProtected);
            if (detail.Attachments.Count > 0)
            {
                withAttachment++;
                Assert.All(detail.Attachments, attachment => Assert.Equal(2_048, attachment.Content.Length));
            }
        }

        Assert.Equal(entries / 64, withAttachment);
    }

    [Fact]
    public void Smoke_fixture_refuses_a_shape_no_reader_would_accept()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "probe.kdbx");

        Assert.Throws<ArgumentOutOfRangeException>(
            () => KeePassSmokeVaultWriter.Write(path, KeePassSmokeVaultWriter.DefaultPassword, 0, 5));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => KeePassSmokeVaultWriter.Write(path, KeePassSmokeVaultWriter.DefaultPassword, 50, 0));
        Assert.Throws<ArgumentException>(
            () => KeePassSmokeVaultWriter.Write(path, string.Empty, 50, 5));
        Assert.False(File.Exists(path));
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
