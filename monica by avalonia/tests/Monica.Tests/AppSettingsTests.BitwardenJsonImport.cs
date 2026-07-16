using Monica.App.ViewModels;
using Monica.App.Services;
using Monica.Core.ImportExport;
using Monica.Core.Models;
using Monica.Data;
using Monica.Data.Repositories;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed partial class AppSettingsTests
{
    [Fact]
    public async Task Bitwarden_import_file_selection_cancel_keeps_empty_state()
    {
        var viewModel = CreateViewModel(
            GetTempPath(),
            fileSystemPickerService: new BitwardenFilePicker(null));

        await viewModel.SelectBitwardenJsonFileCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasBitwardenSelectedFile);
        Assert.False(viewModel.HasBitwardenImportPreview);
        Assert.Empty(viewModel.BitwardenSelectedFileName);
    }

    [Fact]
    public async Task Bitwarden_import_previews_supported_counts_and_clears_when_leaving_import()
    {
        var viewModel = CreateViewModel(
            GetTempPath(),
            fileSystemPickerService: new BitwardenFilePicker(
                new PickedTextFile("team-export.json", BitwardenImportJson)));

        await viewModel.SelectBitwardenJsonFileCommand.ExecuteAsync(null);
        await viewModel.PreviewBitwardenJsonImportCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasBitwardenImportPreview);
        Assert.Equal(1, viewModel.BitwardenPreviewPasswordCount);
        Assert.Equal(2, viewModel.BitwardenPreviewSecureItemCount);
        Assert.Equal(2, viewModel.BitwardenPreviewFolderCount);
        Assert.Equal(1, viewModel.BitwardenPreviewUnsupportedCount);

        viewModel.SelectedSyncPage = "Export";

        Assert.False(viewModel.HasBitwardenSelectedFile);
        Assert.False(viewModel.HasBitwardenImportPreview);
        Assert.Empty(viewModel.BitwardenSelectedFileName);
    }

    [Fact]
    public async Task Bitwarden_import_rejects_encrypted_export_without_echoing_secrets()
    {
        const string encrypted = "{\"encrypted\":true,\"data\":\"never-echo-this-secret\"}";
        var viewModel = CreateViewModel(
            GetTempPath(),
            fileSystemPickerService: new BitwardenFilePicker(
                new PickedTextFile("customer-name.json", encrypted)));

        await viewModel.SelectBitwardenJsonFileCommand.ExecuteAsync(null);
        await viewModel.PreviewBitwardenJsonImportCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasBitwardenImportPreview);
        Assert.DoesNotContain("never-echo-this-secret", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("customer-name", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Bitwarden_import_requires_confirmation_before_writing()
    {
        var repository = CreateRepository();
        var confirmation = new RecordingConfirmationDialogService(false);
        var viewModel = CreateViewModel(
            GetTempPath(),
            repository: repository,
            confirmationDialogService: confirmation,
            fileSystemPickerService: new BitwardenFilePicker(
                new PickedTextFile("team-export.json", BitwardenImportJson)));

        await PreviewBitwardenAsync(viewModel);
        await viewModel.ImportBitwardenJsonVaultCommand.ExecuteAsync(null);

        Assert.True(confirmation.WasCalled);
        Assert.Empty(await repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true));
        Assert.Empty(await repository.GetSecureItemsAsync(includeDeleted: true));
    }

    [Fact]
    public async Task Bitwarden_import_reuses_folders_persists_metadata_and_skips_source_duplicates()
    {
        var repository = CreateRepository();
        await repository.SaveCategoryAsync(new Category { Name = "Team", SortOrder = 7 });
        var picker = new BitwardenFilePicker(new PickedTextFile("team-export.json", BitwardenImportJson));
        var viewModel = CreateViewModel(
            GetTempPath(),
            repository: repository,
            confirmationDialogService: new RecordingConfirmationDialogService(true),
            fileSystemPickerService: picker);

        await PreviewBitwardenAsync(viewModel);
        await viewModel.ImportBitwardenJsonVaultCommand.ExecuteAsync(null);

        var categories = await repository.GetCategoriesAsync();
        Assert.Equal(2, categories.Count);
        Assert.Single(categories, item => item.Name == "Team");
        var teamId = Assert.Single(categories, item => item.Name == "Team").Id;
        var personalId = Assert.Single(categories, item => item.Name == "Personal").Id;

        var password = Assert.Single(await repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true));
        Assert.Equal("cipher-login", password.BitwardenCipherId);
        Assert.Equal("bitwarden-json:cipher-login", password.ReplicaGroupId);
        Assert.Null(password.BitwardenVaultId);
        Assert.False(password.BitwardenLocalModified);
        Assert.Equal(teamId, password.CategoryId);
        Assert.Equal("JBSWY3DPEHPK3PXP", password.AuthenticatorKey);
        var field = Assert.Single(await repository.GetCustomFieldsAsync(password.Id));
        Assert.Equal("API token", field.Title);
        Assert.Equal("field-secret", field.Value);
        Assert.True(field.IsProtected);
        var history = Assert.Single(await repository.GetPasswordHistoryAsync(password.Id));
        Assert.NotEmpty(history.Password);
        Assert.NotEqual("old-secret", history.Password);

        var secureItems = await repository.GetSecureItemsAsync(includeDeleted: true);
        Assert.Contains(secureItems, item => item.ItemType == VaultItemType.Totp && item.BoundPasswordId == password.Id);
        Assert.Contains(secureItems, item => item.ItemType == VaultItemType.Note && item.IsDeleted && item.CategoryId == personalId);
        Assert.Contains(secureItems, item => item.ItemType == VaultItemType.BankCard && item.CategoryId is null);

        await PreviewBitwardenAsync(viewModel);
        await viewModel.ImportBitwardenJsonVaultCommand.ExecuteAsync(null);

        Assert.Single(await repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true));
        Assert.Equal(3, (await repository.GetSecureItemsAsync(includeDeleted: true)).Count);
        Assert.Contains("3", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bitwarden_preview_can_be_canceled_without_publishing_a_snapshot()
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new DelegatingBitwardenImportExportService(json =>
        {
            started.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(10));
            return new ImportExportService().ImportBitwardenJson(json);
        });
        var viewModel = CreateViewModel(
            GetTempPath(),
            importExportService: service,
            fileSystemPickerService: new BitwardenFilePicker(
                new PickedTextFile("team-export.json", BitwardenImportJson)));

        await viewModel.SelectBitwardenJsonFileCommand.ExecuteAsync(null);
        var previewTask = viewModel.PreviewBitwardenJsonImportCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(viewModel.IsBitwardenImportBusy);

        viewModel.CancelBitwardenImportCommand.Execute(null);
        release.Set();
        await previewTask;

        Assert.False(viewModel.IsBitwardenImportBusy);
        Assert.False(viewModel.HasBitwardenImportPreview);
        Assert.Equal(viewModel.L.Get("BitwardenImportCanceled"), viewModel.StatusMessage);
    }

    [Fact]
    public async Task Bitwarden_import_reports_partial_failure_and_keeps_preview_for_retry()
    {
        var repository = CreateRepository();
        var service = new DelegatingBitwardenImportExportService(json =>
        {
            var parsed = new ImportExportService().ImportBitwardenJson(json);
            var invalid = new PasswordEntry
            {
                Id = 999,
                Title = "Invalid protected entry",
                Password = "vault:v1:not-valid",
                ReplicaGroupId = "bitwarden-json:cipher-invalid",
                BitwardenCipherId = "cipher-invalid"
            };
            return parsed with { Passwords = parsed.Passwords.Concat([invalid]).ToArray() };
        });
        var viewModel = CreateViewModel(
            GetTempPath(),
            repository: repository,
            importExportService: service,
            confirmationDialogService: new RecordingConfirmationDialogService(true),
            fileSystemPickerService: new BitwardenFilePicker(
                new PickedTextFile("team-export.json", BitwardenImportJson)));

        await PreviewBitwardenAsync(viewModel);
        await viewModel.ImportBitwardenJsonVaultCommand.ExecuteAsync(null);

        Assert.Single(await repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true));
        Assert.True(viewModel.HasBitwardenImportPreview);
        Assert.Contains("1", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("not-valid", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    private static async Task PreviewBitwardenAsync(MainWindowViewModel viewModel)
    {
        await viewModel.SelectBitwardenJsonFileCommand.ExecuteAsync(null);
        await viewModel.PreviewBitwardenJsonImportCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasBitwardenImportPreview);
    }

    private static MonicaRepository CreateRepository()
    {
        var path = TestTempPaths.CreateFilePath(".db");
        var factory = new SqliteConnectionFactory(path);
        return new MonicaRepository(factory, new DatabaseMigrator(factory));
    }

    private sealed class BitwardenFilePicker(PickedTextFile? file) : IFileSystemPickerService
    {
        public PlatformIntegrationCapability Capability { get; } =
            PlatformIntegrationService.Available(PlatformFeatureKeys.FilePicker, "Test file picker");

        public Task<PickedTextFile?> OpenTextFileAsync(
            string title,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes,
            CancellationToken cancellationToken = default) => Task.FromResult(file);

        public Task<PickedBinaryFile?> OpenBinaryFileAsync(
            string title,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes,
            CancellationToken cancellationToken = default) => Task.FromResult<PickedBinaryFile?>(null);

        public Task<string?> SaveTextFileAsync(
            string title,
            string suggestedFileName,
            string content,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes,
            CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class RecordingConfirmationDialogService(bool result) : IConfirmationDialogService
    {
        public bool WasCalled { get; private set; }

        public Task<bool> ConfirmAsync(
            string title,
            string message,
            string primaryButtonText,
            string? closeButtonText = null,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(result);
        }

        public Task<bool> ConfirmTypedAsync(
            string title,
            string message,
            string requiredPhrase,
            string instruction,
            string primaryButtonText,
            string? closeButtonText = null,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private const string BitwardenImportJson = """
        {
          "encrypted": false,
          "folders": [
            { "id": "folder-team", "name": "Team" },
            { "id": "folder-personal", "name": "Personal" }
          ],
          "items": [
            {
              "id": "cipher-login", "folderId": "folder-team", "type": 1,
              "name": "Production", "fields": [
                { "name": "API token", "value": "field-secret", "type": 1 }
              ],
              "passwordHistory": [
                { "password": "old-secret", "lastUsedDate": "2025-12-01T00:00:00Z" }
              ],
              "login": {
                "username": "dev@example.com", "password": "login-secret",
                "totp": "JBSWY3DPEHPK3PXP", "uris": [{ "uri": "https://example.com" }]
              }
            },
            {
              "id": "cipher-note", "folderId": "folder-personal", "type": 2,
              "name": "Deleted note", "notes": "Keep deletion state",
              "deletedDate": "2026-02-05T00:00:00Z", "secureNote": { "type": 0 }
            },
            {
              "id": "cipher-card", "type": 3, "name": "Business card",
              "card": { "cardholderName": "Dev User", "number": "4111111111111111" }
            },
            { "id": "cipher-future", "type": 99, "name": "Future item" }
          ]
        }
        """;
}
