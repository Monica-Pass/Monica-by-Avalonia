using Monica.Core.ImportExport;

namespace Monica.Tests;

public sealed class MonicaEncryptedArchiveTests
{
    // Produced by Node.js crypto (PBKDF2-HMAC-SHA256, 100000 iterations, fixed salt/IV,
    // AES-256-GCM) from a plain Android-style ZIP, independent of the .NET implementation.
    private const string EncryptedFixtureBase64 =
        "TU9OSUNBX0VOQ19WMQcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHCQkJCQkJCQkJCQkJ0ak4/RdrKqB8KhpRhhqtPkJ2S7rhQ+sqh/8ope++g6NHIsOJhiqbY7T5lumqsMfyiL13dOsuf7VDpOGvSBi/y2KC4J+AIH5ksqfe95YZz2DWaSEwGj0Jyu6jwamPxcmLS8HAGucgDq1//URKcZ720bkJMoqwua5/fnX2GMscH7vJ8HLLiAdHoewaTyuYXmvjTwKUxsPLgqapLwzkJZ4YpbMeJsNNsAYRuG7lEryVmrmhbB/2Y8GdZtzeqsv6+gCCQiS72KI15rB2srUGLUi03nqgayJ4l3na4NBZKZYQTOdV4iDLfmiBsNsWgfzegHieUAlygtTECiQ8gw+kOky6Y5hHkGAQ96UJkVU2iEigyFvzXWv4X+oVtKJHmh+VjzKUY6pK96n6vPBwSWfT5IqEaSWffoF33YVth3Vn";

    [Fact]
    public void Imports_archive_encrypted_with_android_file_format()
    {
        var bytes = Convert.FromBase64String(EncryptedFixtureBase64);

        var package = MonicaZipImportParser.Import(bytes, "fixture-password");

        var password = Assert.Single(package.Passwords);
        Assert.Equal("Encrypted Android login", password.Title);
        Assert.Equal("alice", password.Username);
        Assert.Equal("secret", password.Password);
        Assert.Equal("Work", Assert.Single(package.Categories).Name);
        Assert.Equal("prod", Assert.Single(Assert.Single(package.PasswordCustomFields).Fields).Value);
    }

    [Fact]
    public void Encrypted_archive_without_password_requests_password()
    {
        var bytes = Convert.FromBase64String(EncryptedFixtureBase64);

        var error = Assert.Throws<MonicaJsonImportException>(() => MonicaZipImportParser.Import(bytes));

        Assert.Equal(MonicaJsonImportError.PasswordRequired, error.Error);
    }

    [Fact]
    public void Encrypted_archive_with_wrong_password_reports_decryption_failure()
    {
        var bytes = Convert.FromBase64String(EncryptedFixtureBase64);

        var error = Assert.Throws<MonicaJsonImportException>(() =>
            MonicaZipImportParser.Import(bytes, "wrong-password"));

        Assert.Equal(MonicaJsonImportError.DecryptionFailed, error.Error);
    }

    [Fact]
    public void Truncated_encrypted_archive_reports_decryption_failure()
    {
        var bytes = Convert.FromBase64String(EncryptedFixtureBase64)[..40];

        var error = Assert.Throws<MonicaJsonImportException>(() =>
            MonicaZipImportParser.Import(bytes, "fixture-password"));

        Assert.Equal(MonicaJsonImportError.DecryptionFailed, error.Error);
    }

    [Fact]
    public void Tampered_authentication_tag_reports_decryption_failure()
    {
        var bytes = Convert.FromBase64String(EncryptedFixtureBase64);
        bytes[^1] ^= 0xFF;

        var error = Assert.Throws<MonicaJsonImportException>(() =>
            MonicaZipImportParser.Import(bytes, "fixture-password"));

        Assert.Equal(MonicaJsonImportError.DecryptionFailed, error.Error);
    }
}
