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
        var fixture = KeePassTestVault.Create("temporary-master-password");
        var picker = new KeePassFilePicker(new PickedBinaryFile("business.kdbx", fixture.Content));
        var service = new RecordingKeePassVaultService();
        var viewModel = CreateViewModel(
            GetTempPath(),
            fileSystemPickerService: picker,
            keePassVaultService: service);

        await viewModel.SelectKeePassFileCommand.ExecuteAsync(null);
        viewModel.KeePassImportPassword = fixture.Password;
        await viewModel.PreviewKeePassImportCommand.ExecuteAsync(null);

        Assert.Equal("business.kdbx", viewModel.KeePassSelectedFileName);
        Assert.Equal(fixture.Password, service.ReceivedPassword);
        Assert.Empty(viewModel.KeePassImportPassword);
        Assert.True(viewModel.HasKeePassImportPreview);
        Assert.NotNull(service.OpenedSession);
        Assert.Equal(2, service.OpenedSession!.EntryCount);
        Assert.Equal(2, service.OpenedSession.GroupCount);
        Assert.Contains("2", viewModel.KeePassPreviewSummaryText, StringComparison.Ordinal);

        viewModel.SelectedSyncPage = "Export";

        Assert.Empty(viewModel.KeePassImportPassword);
        Assert.Empty(viewModel.KeePassSelectedFileName);
        Assert.False(viewModel.HasKeePassImportPreview);
        Assert.Equal(0, service.OpenedSession!.EntryCount);
        Assert.Empty(service.OpenedSession.Groups);
    }

    [Fact]
    public async Task KeePass_import_skips_existing_source_entries_and_maps_metadata()
    {
        var fixture = KeePassTestVault.Create("password");
        var databaseId = await GetKeePassDatabaseIdAsync(fixture);
        var existingEntry = fixture.Entries.Single(item => item.Title == KeePassTestVault.ExistingTitle);
        var databasePath = TestTempPaths.CreateFilePath(".db");
        var factory = new SqliteConnectionFactory(databasePath);
        var repository = new MonicaRepository(factory, new DatabaseMigrator(factory));
        await repository.SavePasswordAsync(new PasswordEntry
        {
            Title = "Existing",
            Username = "existing@example.com",
            Password = "existing-secret",
            KeepassDatabaseId = databaseId,
            KeepassEntryUuid = existingEntry.Uuid,
            KeepassGroupUuid = existingEntry.GroupUuid,
            KeepassGroupPath = existingEntry.GroupPath
        });
        var picker = new KeePassFilePicker(new PickedBinaryFile("business.kdbx", fixture.Content));
        var service = new RecordingKeePassVaultService();
        var viewModel = CreateViewModel(
            GetTempPath(),
            fileSystemPickerService: picker,
            repository: repository,
            confirmationDialogService: new ApprovingConfirmationDialogService(),
            keePassVaultService: service);

        await viewModel.SelectKeePassFileCommand.ExecuteAsync(null);
        viewModel.KeePassImportPassword = fixture.Password;
        await viewModel.PreviewKeePassImportCommand.ExecuteAsync(null);
        await viewModel.ImportKeePassVaultCommand.ExecuteAsync(null);

        var passwords = await repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true);
        Assert.Equal(2, passwords.Count);
        var imported = Assert.Single(
            passwords,
            item => item.KeepassEntryUuid != existingEntry.Uuid && item.KeepassDatabaseId == databaseId);
        Assert.Equal(KeePassTestVault.CloudTitle, imported.Title);
        Assert.Equal("cloud@example.com", imported.Username);
        Assert.Equal(KeePassTestVault.CloudPassword, imported.Password);
        Assert.Equal("https://cloud.example.com", imported.Website);
        Assert.Equal(fixture.Entries.Single(item => item.Title == KeePassTestVault.CloudTitle).GroupPath,
            imported.KeepassGroupPath);
        Assert.Equal(databaseId, imported.KeepassDatabaseId);
        Assert.Equal(KeePassTestVault.CloudTotp, imported.AuthenticatorKey);
        var customField = Assert.Single(await repository.GetCustomFieldsAsync(imported.Id));
        Assert.Equal("Tenant", customField.Title);
        Assert.Equal("Production", customField.Value);
        Assert.True(customField.IsProtected);
        Assert.False(viewModel.HasKeePassImportPreview);
        Assert.Equal(0, service.OpenedSession!.EntryCount);
        Assert.Empty(viewModel.KeePassSelectedFileName);
        Assert.Contains("1", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeePass_import_failure_clears_password_and_uses_safe_error_text()
    {
        var fixture = KeePassTestVault.Create("real-password");
        var picker = new KeePassFilePicker(new PickedBinaryFile("client-name.kdbx", fixture.Content));
        var service = new RecordingKeePassVaultService();
        var viewModel = CreateViewModel(
            GetTempPath(),
            fileSystemPickerService: picker,
            keePassVaultService: service);

        await viewModel.SelectKeePassFileCommand.ExecuteAsync(null);
        viewModel.KeePassImportPassword = "never-display-this";
        await viewModel.PreviewKeePassImportCommand.ExecuteAsync(null);

        Assert.Equal("never-display-this", service.ReceivedPassword);
        Assert.Null(service.OpenedSession);
        Assert.Empty(viewModel.KeePassImportPassword);
        Assert.False(viewModel.HasKeePassImportPreview);
        Assert.DoesNotContain("never-display-this", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("client-name", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<long> GetKeePassDatabaseIdAsync(KeePassTestVault.Fixture fixture)
    {
        using var session = await new KeePassVaultService().OpenAsync(
            fixture.Content,
            "business.kdbx",
            fixture.Password);
        return session.DatabaseId;
    }

    private sealed class RecordingKeePassVaultService : IKeePassVaultService
    {
        private readonly KeePassVaultService _inner = new();

        public string? ReceivedPassword { get; private set; }
        public KeePassVaultSession? OpenedSession { get; private set; }

        public async Task<KeePassVaultSession> OpenAsync(
            ReadOnlyMemory<byte> content,
            string fileName,
            string? password,
            string? localPath = null,
            CancellationToken cancellationToken = default)
        {
            ReceivedPassword = password;
            var session = await _inner.OpenAsync(content, fileName, password, localPath, cancellationToken);
            OpenedSession = session;
            return session;
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
