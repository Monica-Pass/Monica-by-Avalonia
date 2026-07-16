using Monica.App.ViewModels;
using Monica.Core.Models;
using Monica.Data;
using Monica.Data.Repositories;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed partial class AppSettingsTests
{
    [Fact]
    public async Task ViewModel_preserves_local_webdav_mdbx_when_conditional_upload_conflicts()
    {
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
        var item = Assert.Single(viewModel.MdbxDatabaseItems);
        var workingCopyPath = item.Database.WorkingCopyPath!;
        var localBytes = await File.ReadAllBytesAsync(workingCopyPath);
        var initiallyUploadedBytes = webDav.UploadedBytes.ToArray();

        var persisted = Assert.Single(await repository.GetMdbxDatabasesAsync());
        persisted.LastSyncStatus = SyncStatus.PendingUpload;
        await repository.SaveMdbxDatabaseAsync(persisted);
        webDav.UploadBinaryFailure = new RemoteFileConflictException("remote version changed");
        webDav.DownloadBytes = [9, 8, 7, 6];

        await viewModel.SyncMdbxDatabaseCommand.ExecuteAsync(item);

        var current = Assert.Single(viewModel.MdbxDatabases);
        Assert.Equal(SyncStatus.Conflict, current.LastSyncStatus);
        Assert.Contains("remote version changed", current.LastSyncError, StringComparison.Ordinal);
        Assert.Equal(localBytes, await File.ReadAllBytesAsync(workingCopyPath));
        Assert.Equal(initiallyUploadedBytes, webDav.UploadedBytes);
        Assert.Empty(webDav.DownloadedBinaryPath);
        Assert.Equal("\"fixture-v1\"", webDav.LastWriteCondition?.ExpectedVersion?.ETag);

        var conflictItem = Assert.Single(viewModel.MdbxDatabaseItems);
        await viewModel.SyncMdbxDatabaseCommand.ExecuteAsync(conflictItem);
        Assert.Equal(2, webDav.UploadBinaryCallCount);
        Assert.Empty(webDav.DownloadedBinaryPath);
        Assert.Equal(viewModel.L.Get("MdbxWebDavConflictRequiresResolution"), viewModel.StatusMessage);
    }

    [Fact]
    public async Task ViewModel_uses_create_only_upload_and_preserves_existing_remote_vault()
    {
        var webDav = new CapturingWebDavBackupService([])
        {
            UploadBinaryFailure = new RemoteFileConflictException("remote vault already exists")
        };
        var viewModel = CreateViewModel(GetTempPath(), webDavBackupService: webDav);
        ConfigureWebDav(viewModel);

        await viewModel.CreateWebDavMdbxVaultCommand.ExecuteAsync(null);

        var database = Assert.Single(viewModel.MdbxDatabases);
        Assert.Equal(SyncStatus.Conflict, database.LastSyncStatus);
        Assert.True(File.Exists(database.WorkingCopyPath));
        Assert.True(webDav.LastWriteCondition?.RequireMissing);
        Assert.Empty(webDav.UploadedBytes);
        Assert.Null(database.LastSyncedAt);
        Assert.Null(database.RemoteETag);
    }

    [Fact]
    public async Task ViewModel_refuses_legacy_pending_upload_without_remote_revision()
    {
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
        var item = Assert.Single(viewModel.MdbxDatabaseItems);

        var persisted = Assert.Single(await repository.GetMdbxDatabasesAsync());
        persisted.RemoteETag = null;
        persisted.RemoteLastModifiedAt = null;
        persisted.LastSyncStatus = SyncStatus.PendingUpload;
        await repository.SaveMdbxDatabaseAsync(persisted);

        await viewModel.SyncMdbxDatabaseCommand.ExecuteAsync(item);

        var current = Assert.Single(viewModel.MdbxDatabases);
        Assert.Equal(SyncStatus.Conflict, current.LastSyncStatus);
        Assert.Contains(viewModel.L.Get("MdbxWebDavMissingRevision"), current.LastSyncError, StringComparison.Ordinal);
        Assert.Equal(1, webDav.UploadBinaryCallCount);
        Assert.Empty(webDav.DownloadedBinaryPath);
    }

    [Fact]
    public async Task ViewModel_resolves_webdav_conflict_by_conditionally_uploading_local_copy()
    {
        var databasePath = TestTempPaths.CreateFilePath(".db");
        var factory = new SqliteConnectionFactory(databasePath);
        var repository = new MonicaRepository(factory, new DatabaseMigrator(factory));
        var webDav = new CapturingWebDavBackupService([]);
        var viewModel = CreateViewModel(
            GetTempPath(),
            webDavBackupService: webDav,
            repository: repository,
            confirmationDialogService: new ApprovingConfirmationDialogService());
        ConfigureWebDav(viewModel, $"/Monica/{Guid.NewGuid():N}");
        await viewModel.CreateWebDavMdbxVaultCommand.ExecuteAsync(null);
        var item = Assert.Single(viewModel.MdbxDatabaseItems);
        var persisted = Assert.Single(await repository.GetMdbxDatabasesAsync());
        persisted.LastSyncStatus = SyncStatus.PendingUpload;
        await repository.SaveMdbxDatabaseAsync(persisted);
        webDav.UploadBinaryFailure = new RemoteFileConflictException("remote changed");
        await viewModel.SyncMdbxDatabaseCommand.ExecuteAsync(item);
        webDav.UploadBinaryFailure = null;
        webDav.RemoteVersion = new RemoteFileVersion(
            "\"fixture-v2\"",
            DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_100_000),
            null);

        await viewModel.KeepLocalWebDavMdbxCommand.ExecuteAsync(Assert.Single(viewModel.MdbxDatabaseItems));

        var resolved = Assert.Single(viewModel.MdbxDatabases);
        Assert.Equal(SyncStatus.Synced, resolved.LastSyncStatus);
        Assert.Equal("\"fixture-v2\"", resolved.RemoteETag);
        Assert.Equal("\"fixture-v2\"", webDav.LastWriteCondition?.ExpectedVersion?.ETag);
        Assert.Equal(3, webDav.UploadBinaryCallCount);
        Assert.Contains("upload", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ViewModel_resolves_webdav_conflict_with_remote_copy_and_preserves_local_backup()
    {
        var databasePath = TestTempPaths.CreateFilePath(".db");
        var factory = new SqliteConnectionFactory(databasePath);
        var repository = new MonicaRepository(factory, new DatabaseMigrator(factory));
        var webDav = new CapturingWebDavBackupService([]);
        var viewModel = CreateViewModel(
            GetTempPath(),
            webDavBackupService: webDav,
            repository: repository,
            confirmationDialogService: new ApprovingConfirmationDialogService());
        ConfigureWebDav(viewModel, $"/Monica/{Guid.NewGuid():N}");
        await viewModel.CreateWebDavMdbxVaultCommand.ExecuteAsync(null);
        var item = Assert.Single(viewModel.MdbxDatabaseItems);
        var workingCopyPath = item.Database.WorkingCopyPath!;
        var originalLocalBytes = await File.ReadAllBytesAsync(workingCopyPath);
        var persisted = Assert.Single(await repository.GetMdbxDatabasesAsync());
        persisted.LastSyncStatus = SyncStatus.PendingUpload;
        await repository.SaveMdbxDatabaseAsync(persisted);
        webDav.UploadBinaryFailure = new RemoteFileConflictException("remote changed");
        await viewModel.SyncMdbxDatabaseCommand.ExecuteAsync(item);
        webDav.UploadBinaryFailure = null;
        webDav.DownloadBytes = webDav.UploadedBytes.ToArray();
        webDav.RemoteVersion = new RemoteFileVersion(
            "\"fixture-v2\"",
            DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_100_000),
            null);

        await viewModel.UseRemoteWebDavMdbxCommand.ExecuteAsync(Assert.Single(viewModel.MdbxDatabaseItems));

        var resolved = Assert.Single(viewModel.MdbxDatabases);
        Assert.Equal(SyncStatus.Synced, resolved.LastSyncStatus);
        Assert.Equal("\"fixture-v2\"", resolved.RemoteETag);
        var recoveryFile = Assert.Single(Directory.GetFiles(
            Path.GetDirectoryName(workingCopyPath)!,
            $"{Path.GetFileNameWithoutExtension(workingCopyPath)}.local-conflict-*{Path.GetExtension(workingCopyPath)}"));
        Assert.Equal(originalLocalBytes, await File.ReadAllBytesAsync(recoveryFile));
        Assert.Contains(recoveryFile, viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
}
