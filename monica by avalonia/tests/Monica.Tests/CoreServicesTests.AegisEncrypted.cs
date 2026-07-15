using Monica.Core.ImportExport;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.Tests;

public sealed partial class CoreServicesTests
{
    [Fact]
    public void Import_export_imports_password_encrypted_aegis_json()
    {
        var service = new ImportExportService();

        var imported = Assert.Single(service.ImportAegisJson(AegisEncryptedTestData.Json, AegisEncryptedTestData.Password));
        var data = TotpDataResolver.ParseStoredItemData(imported.ItemData);

        Assert.True(service.IsEncryptedAegisJson(AegisEncryptedTestData.Json));
        Assert.Equal(VaultItemType.Totp, imported.ItemType);
        Assert.Equal("dev@example.com", imported.Title);
        Assert.Equal("work account", imported.Notes);
        Assert.NotNull(data);
        Assert.Equal("JBSWY3DPEHPK3PXP", data.Secret);
        Assert.Equal("GitHub", data.Issuer);
        Assert.Equal("dev@example.com", data.AccountName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Import_export_requires_password_for_encrypted_aegis_json(string? password)
    {
        var service = new ImportExportService();

        var error = Assert.Throws<AegisImportException>(() => service.ImportAegisJson(AegisEncryptedTestData.Json, password));

        Assert.Equal(AegisImportFailureReason.PasswordRequired, error.Reason);
    }

    [Fact]
    public void Import_export_rejects_wrong_aegis_password_without_exposing_crypto_details()
    {
        var service = new ImportExportService();

        var error = Assert.Throws<AegisImportException>(() => service.ImportAegisJson(AegisEncryptedTestData.Json, "wrong password"));

        Assert.Equal(AegisImportFailureReason.DecryptionFailed, error.Reason);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void Import_export_rejects_tampered_aegis_database_tag()
    {
        var service = new ImportExportService();
        var tampered = AegisEncryptedTestData.Json.Replace(
            "55eacaf34b73c85d5dc5e3243864c674",
            "55eacaf34b73c85d5dc5e3243864c675",
            StringComparison.Ordinal);

        var error = Assert.Throws<AegisImportException>(() => service.ImportAegisJson(tampered, AegisEncryptedTestData.Password));

        Assert.Equal(AegisImportFailureReason.DecryptionFailed, error.Reason);
    }

    [Fact]
    public void Import_export_rejects_unsafe_aegis_scrypt_parameters_before_derivation()
    {
        var service = new ImportExportService();
        var unsafePayload = AegisEncryptedTestData.Json.Replace("\"n\":32768", "\"n\":1073741824", StringComparison.Ordinal);

        var error = Assert.Throws<AegisImportException>(() => service.ImportAegisJson(unsafePayload, AegisEncryptedTestData.Password));

        Assert.Equal(AegisImportFailureReason.UnsafeKeyDerivationParameters, error.Reason);
    }

    [Fact]
    public void Import_export_rejects_unsupported_aegis_key_slots()
    {
        var service = new ImportExportService();
        var unsupported = AegisEncryptedTestData.Json.Replace("\"type\":1", "\"type\":2", StringComparison.Ordinal);

        var error = Assert.Throws<AegisImportException>(() => service.ImportAegisJson(unsupported, AegisEncryptedTestData.Password));

        Assert.Equal(AegisImportFailureReason.UnsupportedKeySlot, error.Reason);
    }

    [Fact]
    public void Import_export_rejects_malformed_aegis_ciphertext()
    {
        var service = new ImportExportService();
        var malformed = AegisEncryptedTestData.Json.Replace(
            "mRg4UEZOadCwd97H3veG",
            "not-base64!",
            StringComparison.Ordinal);

        var error = Assert.Throws<AegisImportException>(() => service.ImportAegisJson(malformed, AegisEncryptedTestData.Password));

        Assert.Equal(AegisImportFailureReason.InvalidFormat, error.Reason);
    }

    [Fact]
    public void Import_export_handles_non_object_aegis_json_as_invalid_input()
    {
        var service = new ImportExportService();

        Assert.False(service.IsEncryptedAegisJson("[]"));
        var error = Assert.Throws<AegisImportException>(() => service.ImportAegisJson("[]"));

        Assert.Equal(AegisImportFailureReason.InvalidFormat, error.Reason);
    }
}
