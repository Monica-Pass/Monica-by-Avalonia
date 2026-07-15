using Monica.Core.Services;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed partial class AppSettingsTests
{
    [Fact]
    public async Task ViewModel_imports_encrypted_aegis_and_clears_password()
    {
        var viewModel = CreateViewModel(GetTempPath());
        viewModel.ImportAegisJsonText = AegisEncryptedTestData.Json;
        viewModel.AegisImportPassword = AegisEncryptedTestData.Password;

        await viewModel.ImportAegisJsonCommand.ExecuteAsync(null);

        var imported = Assert.Single(viewModel.TotpItems);
        var data = TotpDataResolver.ParseStoredItemData(imported.ItemData);
        Assert.Equal("JBSWY3DPEHPK3PXP", data?.Secret);
        Assert.Equal("", viewModel.AegisImportPassword);
        Assert.False(viewModel.IsAegisImportPasswordRequired);
        Assert.Equal("", viewModel.ImportAegisJsonText);
    }

    [Fact]
    public async Task ViewModel_prompts_for_encrypted_aegis_password_without_discarding_payload()
    {
        var viewModel = CreateViewModel(GetTempPath());
        viewModel.ImportAegisJsonText = AegisEncryptedTestData.Json;

        await viewModel.ImportAegisJsonCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.TotpItems);
        Assert.True(viewModel.IsAegisImportPasswordRequired);
        Assert.Equal(AegisEncryptedTestData.Json, viewModel.ImportAegisJsonText);
        Assert.Equal(viewModel.L.AegisImportPasswordRequired, viewModel.StatusMessage);
    }

    [Fact]
    public async Task ViewModel_file_picker_stages_encrypted_aegis_until_password_is_entered()
    {
        var integration = new PlatformIntegrationService("TestOS",
        [
            PlatformIntegrationService.Available(PlatformFeatureKeys.FilePicker, "File picking works.")
        ]);
        var filePicker = new CapturingFileSystemPickerService(
            integration,
            new PickedTextFile("encrypted-aegis.json", AegisEncryptedTestData.Json));
        var viewModel = CreateViewModel(
            GetTempPath(),
            platformIntegrationService: integration,
            fileSystemPickerService: filePicker);

        await viewModel.ImportAegisJsonFileCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.TotpItems);
        Assert.True(viewModel.IsAegisImportPasswordRequired);
        Assert.Equal(AegisEncryptedTestData.Json, viewModel.ImportAegisJsonText);
        Assert.Equal("Sync", viewModel.SelectedSection);
        Assert.Equal("Import", viewModel.SelectedSyncPage);
        Assert.Equal(viewModel.L.AegisImportPasswordRequired, viewModel.StatusMessage);
    }

    [Fact]
    public async Task ViewModel_localizes_encrypted_aegis_decryption_failure_and_clears_password()
    {
        var viewModel = CreateViewModel(GetTempPath());
        viewModel.ImportAegisJsonText = AegisEncryptedTestData.Json;
        viewModel.AegisImportPassword = "wrong password";

        await viewModel.ImportAegisJsonCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.TotpItems);
        Assert.True(viewModel.IsAegisImportPasswordRequired);
        Assert.Equal("", viewModel.AegisImportPassword);
        Assert.Equal(AegisEncryptedTestData.Json, viewModel.ImportAegisJsonText);
        Assert.Equal(viewModel.L.AegisImportDecryptionFailed, viewModel.StatusMessage);
    }

    [Theory]
    [InlineData("Export", "Sync")]
    [InlineData("Import", "Passwords")]
    public void ViewModel_clears_sensitive_import_buffers_when_leaving_import_context(string nextPage, string nextSection)
    {
        var viewModel = CreateViewModel(GetTempPath());
        viewModel.SelectedSection = "Sync";
        viewModel.SelectedSyncPage = "Import";
        viewModel.ImportJsonText = "password data";
        viewModel.ImportCsvText = "password csv";
        viewModel.ImportNoteCsvText = "private note";
        viewModel.ImportTotpCsvText = "totp secret";
        viewModel.ImportAegisJsonText = AegisEncryptedTestData.Json;
        viewModel.AegisImportPassword = AegisEncryptedTestData.Password;
        viewModel.IsAegisImportPasswordRequired = true;

        viewModel.SelectedSyncPage = nextPage;
        viewModel.SelectedSection = nextSection;

        Assert.Equal("", viewModel.ImportJsonText);
        Assert.Equal("", viewModel.ImportCsvText);
        Assert.Equal("", viewModel.ImportNoteCsvText);
        Assert.Equal("", viewModel.ImportTotpCsvText);
        Assert.Equal("", viewModel.ImportAegisJsonText);
        Assert.Equal("", viewModel.AegisImportPassword);
        Assert.False(viewModel.IsAegisImportPasswordRequired);
    }
}
