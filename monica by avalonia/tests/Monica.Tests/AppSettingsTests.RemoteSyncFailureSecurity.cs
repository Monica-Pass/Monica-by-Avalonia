using Monica.App.Services;
using Monica.Core.Models;
using Monica.App.ViewModels;
using Monica.Platform.Services;
using Monica.Data;
using Monica.Data.Repositories;

namespace Monica.Tests;

public sealed partial class AppSettingsTests
{
    [Fact]
    public void RemoteSyncFailureSecurity_messages_are_actionable_in_english_and_chinese()
    {
        var localization = new LocalizationService();
        var retryKeys = new[]
        {
            "WebDavConnectionTestFailed",
            "WebDavBackupHistoryFailed",
            "CreateWebDavBackupFailed",
            "RestoreWebDavBackupFailed",
            "DeleteWebDavBackupFailed",
            "OneDriveConnectionFailed",
            "MdbxOperationFailed",
            "MdbxWebDavSyncFailed",
            "MdbxOneDriveSyncFailed"
        };

        Assert.All(retryKeys, key => Assert.Contains("try again", localization.Get(key), StringComparison.OrdinalIgnoreCase));
        Assert.Contains("choose", localization.Get("MdbxRemoteConflictDetected"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sign in", localization.Get("MdbxOneDriveAccountRequired"), StringComparison.OrdinalIgnoreCase);

        localization.SetLanguage("zh-CN");

        Assert.All(retryKeys, key => Assert.Contains("重试", localization.Get(key), StringComparison.Ordinal));
        Assert.Contains("选择", localization.Get("MdbxRemoteConflictDetected"), StringComparison.Ordinal);
        Assert.Contains("登录", localization.Get("MdbxOneDriveAccountRequired"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoteSyncFailureSecurity_testing_webdav_keeps_server_details_out_of_status()
    {
        const string rawFailure = "PROPFIND https://private-user:secret@dav.example.com/Monica failed";
        var webDav = new FailingWebDavBackupService { ListFailure = new HttpRequestException(rawFailure) };
        var viewModel = CreateViewModel(GetTempPath(), webDavBackupService: webDav);
        ConfigureWebDav(viewModel);

        await viewModel.TestWebDavConnectionCommand.ExecuteAsync(null);

        Assert.Equal(viewModel.L.Get("WebDavConnectionTestFailed"), viewModel.StatusMessage);
        Assert.DoesNotContain(rawFailure, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("private-user", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dav.example.com", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoteSyncFailureSecurity_loading_webdav_history_keeps_remote_path_out_of_status()
    {
        const string rawFailure = "PROPFIND /Monica/private-backups returned 500 for private-user";
        var webDav = new FailingWebDavBackupService { ListFailure = new HttpRequestException(rawFailure) };
        var viewModel = CreateViewModel(GetTempPath(), webDavBackupService: webDav);
        ConfigureWebDav(viewModel, "/Monica/private-backups");

        await viewModel.LoadWebDavBackupsCommand.ExecuteAsync(null);

        Assert.Equal(viewModel.L.Get("WebDavBackupHistoryFailed"), viewModel.StatusMessage);
        Assert.DoesNotContain(rawFailure, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("private-backups", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-user", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoteSyncFailureSecurity_creating_webdav_backup_keeps_upload_details_out_of_status()
    {
        const string rawFailure = "PUT https://dav.example.com/Monica/private-backup failed for private-user";
        var webDav = new FailingWebDavBackupService { UploadTextFailure = new HttpRequestException(rawFailure) };
        var viewModel = CreateViewModel(GetTempPath(), webDavBackupService: webDav);
        ConfigureWebDav(viewModel);
        viewModel.WebDavBackupEncryptionPassword = "backup password";

        await viewModel.CreateWebDavBackupCommand.ExecuteAsync(null);

        Assert.Equal(viewModel.L.Get("CreateWebDavBackupFailed"), viewModel.StatusMessage);
        Assert.DoesNotContain(rawFailure, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("private-backup", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-user", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoteSyncFailureSecurity_restoring_webdav_backup_keeps_download_details_out_of_status()
    {
        const string rawFailure = "GET /Monica/private-backup.json failed for private-user";
        var webDav = new FailingWebDavBackupService { DownloadTextFailure = new HttpRequestException(rawFailure) };
        var viewModel = CreateViewModel(
            GetTempPath(),
            webDavBackupService: webDav,
            confirmationDialogService: new ApprovingConfirmationDialogService());
        ConfigureWebDav(viewModel);
        var item = new WebDavBackupHistoryItem(
            "private-backup.json",
            "/Monica/private-backup.json",
            "2026/07/16 22:00",
            "1 KB",
            DateTimeOffset.UtcNow);

        await viewModel.RestoreWebDavBackupCommand.ExecuteAsync(item);

        Assert.Equal(viewModel.L.Get("RestoreWebDavBackupFailed"), viewModel.StatusMessage);
        Assert.DoesNotContain(rawFailure, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("private-backup", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-user", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoteSyncFailureSecurity_deleting_webdav_backup_keeps_remote_details_out_of_status()
    {
        const string rawFailure = "DELETE /Monica/private-backup.json denied for private-user";
        var webDav = new FailingWebDavBackupService { DeleteFailure = new HttpRequestException(rawFailure) };
        var viewModel = CreateViewModel(
            GetTempPath(),
            webDavBackupService: webDav,
            confirmationDialogService: new ApprovingConfirmationDialogService());
        ConfigureWebDav(viewModel);
        var item = new WebDavBackupHistoryItem(
            "private-backup.json",
            "/Monica/private-backup.json",
            "2026/07/16 22:00",
            "1 KB",
            DateTimeOffset.UtcNow);

        await viewModel.DeleteWebDavBackupCommand.ExecuteAsync(item);

        Assert.Equal(viewModel.L.Get("DeleteWebDavBackupFailed"), viewModel.StatusMessage);
        Assert.DoesNotContain(rawFailure, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("private-backup", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-user", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoteSyncFailureSecurity_webdav_mdbx_failure_keeps_raw_details_out_of_status_and_history()
    {
        const string rawFailure = "PUT https://dav.example.com/Monica/private-vault.mdbx failed for private-user";
        var webDav = new CapturingWebDavBackupService([])
        {
            UploadBinaryFailure = new HttpRequestException(rawFailure)
        };
        var viewModel = CreateViewModel(GetTempPath(), webDavBackupService: webDav);
        ConfigureWebDav(viewModel, "/Monica/private-vaults");

        await viewModel.CreateWebDavMdbxVaultCommand.ExecuteAsync(null);

        var item = Assert.Single(viewModel.MdbxDatabaseItems);
        Assert.Equal(viewModel.L.Get("MdbxOperationFailed"), viewModel.StatusMessage);
        Assert.Equal(viewModel.L.Get("MdbxWebDavSyncFailed"), item.LastSyncErrorText);
        Assert.DoesNotContain(rawFailure, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(rawFailure, item.LastSyncErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain("private-user", item.LastSyncErrorText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoteSyncFailureSecurity_webdav_mdbx_conflict_keeps_revision_details_out_of_history()
    {
        const string rawFailure = "ETag private-etag changed at https://dav.example.com/Monica/private-vault.mdbx";
        var webDav = new CapturingWebDavBackupService([])
        {
            UploadBinaryFailure = new RemoteFileConflictException(rawFailure)
        };
        var viewModel = CreateViewModel(GetTempPath(), webDavBackupService: webDav);
        ConfigureWebDav(viewModel, "/Monica/private-vaults");

        await viewModel.CreateWebDavMdbxVaultCommand.ExecuteAsync(null);

        var item = Assert.Single(viewModel.MdbxDatabaseItems);
        Assert.True(item.IsConflict);
        Assert.Equal(viewModel.L.Get("MdbxOperationFailed"), viewModel.StatusMessage);
        Assert.Equal(viewModel.L.Get("MdbxRemoteConflictDetected"), item.LastSyncErrorText);
        Assert.DoesNotContain(rawFailure, item.LastSyncErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain("private-etag", item.LastSyncErrorText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoteSyncFailureSecurity_connecting_onedrive_keeps_account_details_out_of_status()
    {
        const string rawFailure = "Microsoft Graph sign-in failed for private-user@example.com tenant private-tenant";
        var oneDrive = new RecordingOneDriveBackupService
        {
            SignInFailure = new HttpRequestException(rawFailure)
        };
        var viewModel = CreateViewModel(GetTempPath(), oneDriveBackupService: oneDrive);

        await viewModel.ConnectOneDriveCommand.ExecuteAsync(null);

        Assert.Equal(viewModel.L.Get("OneDriveConnectionFailed"), viewModel.StatusMessage);
        Assert.DoesNotContain(rawFailure, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("private-user", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-tenant", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoteSyncFailureSecurity_onedrive_mdbx_failure_keeps_account_and_path_out_of_history()
    {
        const string rawFailure = "PUT /Apps/Monica/private-vault.mdbx failed for private-user@example.com";
        var oneDrive = new RecordingOneDriveBackupService
        {
            UploadFailure = new HttpRequestException(rawFailure)
        };
        var viewModel = CreateViewModel(GetTempPath(), oneDriveBackupService: oneDrive);

        await viewModel.CreateOneDriveMdbxVaultCommand.ExecuteAsync(null);

        var item = Assert.Single(viewModel.MdbxDatabaseItems);
        Assert.Equal(viewModel.L.Get("MdbxOperationFailed"), viewModel.StatusMessage);
        Assert.Equal(viewModel.L.Get("MdbxOneDriveSyncFailed"), item.LastSyncErrorText);
        Assert.DoesNotContain(rawFailure, item.LastSyncErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain("private-user", item.LastSyncErrorText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-vault", item.LastSyncErrorText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoteSyncFailureSecurity_onedrive_conflict_does_not_persist_provider_details()
    {
        const string rawFailure = "Graph conflict for private-user@example.com drive private-drive item private-item";
        var oneDrive = new RecordingOneDriveBackupService
        {
            ConflictOnUpload = true,
            ConflictMessage = rawFailure
        };
        var viewModel = CreateViewModel(GetTempPath(), oneDriveBackupService: oneDrive);

        await viewModel.CreateOneDriveMdbxVaultCommand.ExecuteAsync(null);

        var database = Assert.Single(viewModel.MdbxDatabases);
        var item = Assert.Single(viewModel.MdbxDatabaseItems);
        Assert.Equal(SyncStatus.Conflict, database.LastSyncStatus);
        Assert.Equal(viewModel.L.Get("MdbxRemoteConflictDetected"), item.LastSyncErrorText);
        Assert.DoesNotContain(rawFailure, database.LastSyncError ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain("private-user", database.LastSyncError ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoteSyncFailureSecurity_onedrive_mdbx_missing_account_keeps_actionable_validation()
    {
        var oneDrive = new RecordingOneDriveBackupService();
        var viewModel = CreateViewModel(GetTempPath(), oneDriveBackupService: oneDrive);
        await viewModel.CreateOneDriveMdbxVaultCommand.ExecuteAsync(null);
        var item = Assert.Single(viewModel.MdbxDatabaseItems);
        await viewModel.DisconnectOneDriveCommand.ExecuteAsync(null);

        await viewModel.SyncMdbxDatabaseCommand.ExecuteAsync(item);

        Assert.Equal(viewModel.L.Get("MdbxOneDriveAccountRequired"), viewModel.StatusMessage);
        Assert.Contains("sign in", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.IsMdbxBusy);
    }

    [Fact]
    public async Task RemoteSyncFailureSecurity_legacy_mdbx_error_text_is_not_displayed()
    {
        const string rawFailure = "Legacy failure at https://private-user:secret@dav.example.com/private-vault.mdbx";
        var databasePath = TestTempPaths.CreateFilePath(".db");
        var factory = new SqliteConnectionFactory(databasePath);
        var repository = new MonicaRepository(factory, new DatabaseMigrator(factory));
        var webDav = new CapturingWebDavBackupService([]);
        var viewModel = CreateViewModel(
            GetTempPath(),
            webDavBackupService: webDav,
            repository: repository);
        ConfigureWebDav(viewModel);
        await viewModel.CreateWebDavMdbxVaultCommand.ExecuteAsync(null);
        var persisted = Assert.Single(await repository.GetMdbxDatabasesAsync());
        persisted.LastSyncStatus = SyncStatus.Failed;
        persisted.LastSyncError = rawFailure;
        await repository.SaveMdbxDatabaseAsync(persisted);

        await viewModel.RefreshMdbxVaultsCommand.ExecuteAsync(null);

        var item = Assert.Single(viewModel.MdbxDatabaseItems);
        Assert.Equal(viewModel.L.Get("MdbxWebDavSyncFailed"), item.LastSyncErrorText);
        Assert.DoesNotContain(rawFailure, item.LastSyncErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain("private-user", item.LastSyncErrorText, StringComparison.OrdinalIgnoreCase);
    }

}
