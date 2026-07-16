using Monica.App.ViewModels;
using Monica.Core.Models;
using Monica.Data;
using Monica.Data.Repositories;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed partial class AppSettingsTests
{
    [Fact]
    public async Task KeePass_import_selects_previews_and_clears_the_master_password()
    {
        var picker = new KeePassFilePicker(new PickedBinaryFile("business.kdbx", [1, 2, 3]));
        var service = new StubKeePassVaultService(CreateKeePassSnapshot());
        var viewModel = CreateViewModel(
            GetTempPath(),
            fileSystemPickerService: picker,
            keePassVaultService: service);

        await viewModel.SelectKeePassFileCommand.ExecuteAsync(null);
        viewModel.KeePassImportPassword = "temporary-master-password";
        await viewModel.PreviewKeePassImportCommand.ExecuteAsync(null);

        Assert.Equal("business.kdbx", viewModel.KeePassSelectedFileName);
        Assert.Equal("temporary-master-password", service.ReceivedPassword);
        Assert.Empty(viewModel.KeePassImportPassword);
        Assert.True(viewModel.HasKeePassImportPreview);
        Assert.Equal(2, viewModel.KeePassPreviewEntryCount);
        Assert.Equal(2, viewModel.KeePassPreviewGroupCount);

        viewModel.SelectedSyncPage = "Export";

        Assert.Empty(viewModel.KeePassImportPassword);
        Assert.Empty(viewModel.KeePassSelectedFileName);
        Assert.False(viewModel.HasKeePassImportPreview);
    }

    [Fact]
    public async Task KeePass_import_skips_existing_source_entries_and_maps_metadata()
    {
        var databasePath = TestTempPaths.CreateFilePath(".db");
        var factory = new SqliteConnectionFactory(databasePath);
        var repository = new MonicaRepository(factory, new DatabaseMigrator(factory));
        await repository.SavePasswordAsync(new PasswordEntry
        {
            Title = "Existing",
            Username = "existing@example.com",
            Password = "existing-secret",
            KeepassDatabaseId = 42,
            KeepassEntryUuid = "ENTRY-1",
            KeepassGroupUuid = "GROUP-1",
            KeepassGroupPath = "Personal"
        });
        var picker = new KeePassFilePicker(new PickedBinaryFile("business.kdbx", [1, 2, 3]));
        var service = new StubKeePassVaultService(CreateKeePassSnapshot());
        var viewModel = CreateViewModel(
            GetTempPath(),
            fileSystemPickerService: picker,
            repository: repository,
            confirmationDialogService: new ApprovingConfirmationDialogService(),
            keePassVaultService: service);

        await viewModel.SelectKeePassFileCommand.ExecuteAsync(null);
        viewModel.KeePassImportPassword = "password";
        await viewModel.PreviewKeePassImportCommand.ExecuteAsync(null);
        await viewModel.ImportKeePassVaultCommand.ExecuteAsync(null);

        var passwords = await repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true);
        Assert.Equal(2, passwords.Count);
        var imported = Assert.Single(passwords, item => item.KeepassEntryUuid == "ENTRY-2");
        Assert.Equal("Cloud account", imported.Title);
        Assert.Equal("cloud@example.com", imported.Username);
        Assert.Equal("cloud-secret", imported.Password);
        Assert.Equal("https://cloud.example.com", imported.Website);
        Assert.Equal("Personal/Cloud", imported.KeepassGroupPath);
        Assert.Equal(42, imported.KeepassDatabaseId);
        Assert.Equal("otpauth://totp/Cloud?secret=JBSWY3DPEHPK3PXP", imported.AuthenticatorKey);
        var customField = Assert.Single(await repository.GetCustomFieldsAsync(imported.Id));
        Assert.Equal("Tenant", customField.Title);
        Assert.Equal("Production", customField.Value);
        Assert.True(customField.IsProtected);
        Assert.False(viewModel.HasKeePassImportPreview);
        Assert.Empty(viewModel.KeePassSelectedFileName);
        Assert.Contains("1", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeePass_import_failure_clears_password_and_uses_safe_error_text()
    {
        var picker = new KeePassFilePicker(new PickedBinaryFile("client-name.kdbx", [1, 2, 3]));
        var service = new StubKeePassVaultService(
            new KeePassVaultException(
                KeePassVaultError.InvalidCredentialsOrFile,
                "The KeePass database could not be unlocked. Check the password and file integrity."));
        var viewModel = CreateViewModel(
            GetTempPath(),
            fileSystemPickerService: picker,
            keePassVaultService: service);

        await viewModel.SelectKeePassFileCommand.ExecuteAsync(null);
        viewModel.KeePassImportPassword = "never-display-this";
        await viewModel.PreviewKeePassImportCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.KeePassImportPassword);
        Assert.False(viewModel.HasKeePassImportPreview);
        Assert.DoesNotContain("never-display-this", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("client-name", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static KeePassVaultSnapshot CreateKeePassSnapshot() => new(
        42,
        "Business",
        "business.kdbx",
        "ROOT",
        [
            new KeePassGroupSnapshot("Personal", "Personal", "GROUP-1", "ROOT"),
            new KeePassGroupSnapshot("Cloud", "Personal/Cloud", "GROUP-2", "GROUP-1")
        ],
        [
            new KeePassEntrySnapshot(
                "Existing from source",
                "existing@example.com",
                "source-secret",
                "https://existing.example.com",
                "Existing note",
                "",
                "Personal",
                "ENTRY-1",
                "GROUP-1",
                DateTimeOffset.UtcNow.AddDays(-3),
                DateTimeOffset.UtcNow.AddDays(-1),
                [],
                []),
            new KeePassEntrySnapshot(
                "Cloud account",
                "cloud@example.com",
                "cloud-secret",
                "https://cloud.example.com",
                "Cloud note",
                "otpauth://totp/Cloud?secret=JBSWY3DPEHPK3PXP",
                "Personal/Cloud",
                "ENTRY-2",
                "GROUP-2",
                DateTimeOffset.UtcNow.AddDays(-2),
                DateTimeOffset.UtcNow,
                [new KeePassCustomFieldSnapshot("Tenant", "Production", true)],
                [])
        ]);

    private sealed class StubKeePassVaultService : IKeePassVaultService
    {
        private readonly KeePassVaultSnapshot? _snapshot;
        private readonly Exception? _error;

        public StubKeePassVaultService(KeePassVaultSnapshot snapshot) => _snapshot = snapshot;

        public StubKeePassVaultService(Exception error) => _error = error;

        public string? ReceivedPassword { get; private set; }

        public Task<KeePassVaultSummary> InspectAsync(string path, string? password, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<KeePassVaultSnapshot> ReadAsync(
            ReadOnlyMemory<byte> content,
            string fileName,
            string? password,
            CancellationToken cancellationToken = default)
        {
            ReceivedPassword = password;
            return _error is null
                ? Task.FromResult(_snapshot!)
                : Task.FromException<KeePassVaultSnapshot>(_error);
        }
    }

    private sealed class KeePassFilePicker(PickedBinaryFile? file) : IFileSystemPickerService
    {
        public PlatformIntegrationCapability Capability { get; } =
            PlatformIntegrationService.Available(PlatformFeatureKeys.FilePicker, "Test file picker");

        public Task<PickedTextFile?> OpenTextFileAsync(string title, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
            Task.FromResult<PickedTextFile?>(null);

        public Task<PickedBinaryFile?> OpenBinaryFileAsync(string title, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
            Task.FromResult(file);

        public Task<string?> SaveTextFileAsync(string title, string suggestedFileName, string content, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<string?> SaveBinaryFileAsync(string title, string suggestedFileName, ReadOnlyMemory<byte> content, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
