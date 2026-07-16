using Monica.App.ViewModels;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed partial class AppSettingsTests
{
    [Fact]
    public async Task ViewModel_signs_in_binds_account_and_uploads_new_OneDrive_mdbx()
    {
        var oneDrive = new RecordingOneDriveBackupService();
        var viewModel = CreateViewModel(GetTempPath(), oneDriveBackupService: oneDrive);

        await viewModel.CreateOneDriveMdbxVaultCommand.ExecuteAsync(null);

        var database = Assert.Single(viewModel.MdbxDatabases);
        Assert.Equal(MdbxStorageLocation.RemoteOneDrive, database.StorageLocation);
        Assert.Equal(oneDrive.Account.AccountId, database.RemoteAccountId);
        Assert.Equal("\"one-v1\"", database.RemoteETag);
        Assert.Equal(SyncStatus.Synced, database.LastSyncStatus);
        Assert.True(viewModel.OneDriveEnabled);
        Assert.True(viewModel.HasOneDriveAccount);
        Assert.Equal(1, oneDrive.SignInCalls);
        Assert.True(Assert.Single(oneDrive.UploadConditions).RequireMissing);
        Assert.True(File.Exists(database.WorkingCopyPath));
    }

    [Fact]
    public async Task ViewModel_downloads_OneDrive_mdbx_with_bound_account_and_updates_version()
    {
        var oneDrive = new RecordingOneDriveBackupService();
        var viewModel = CreateViewModel(GetTempPath(), oneDriveBackupService: oneDrive);
        await viewModel.CreateOneDriveMdbxVaultCommand.ExecuteAsync(null);
        oneDrive.DownloadContent = await File.ReadAllBytesAsync(Assert.Single(viewModel.MdbxDatabases).WorkingCopyPath!);
        var item = Assert.Single(viewModel.MdbxDatabaseItems);

        await viewModel.SyncMdbxDatabaseCommand.ExecuteAsync(item);

        var database = Assert.Single(viewModel.MdbxDatabases);
        Assert.Equal(oneDrive.DownloadContent, await File.ReadAllBytesAsync(database.WorkingCopyPath!));
        Assert.Equal("\"one-v2\"", database.RemoteETag);
        Assert.Equal(oneDrive.Account.AccountId, Assert.Single(oneDrive.DownloadAccountIds));
        Assert.Equal(SyncStatus.Synced, database.LastSyncStatus);
    }

    [Fact]
    public async Task ViewModel_marks_OneDrive_create_collision_as_conflict_without_overwrite()
    {
        var oneDrive = new RecordingOneDriveBackupService { ConflictOnUpload = true };
        var viewModel = CreateViewModel(GetTempPath(), oneDriveBackupService: oneDrive);

        await viewModel.CreateOneDriveMdbxVaultCommand.ExecuteAsync(null);

        var database = Assert.Single(viewModel.MdbxDatabases);
        Assert.Equal(SyncStatus.Conflict, database.LastSyncStatus);
        Assert.True(File.Exists(database.WorkingCopyPath));
        Assert.True(viewModel.SelectedMdbxDatabaseItem?.IsConflict);
    }

    [Fact]
    public async Task ViewModel_resolves_OneDrive_conflict_by_uploading_against_current_revision()
    {
        var oneDrive = new RecordingOneDriveBackupService { ConflictOnUpload = true };
        var viewModel = CreateViewModel(
            GetTempPath(),
            oneDriveBackupService: oneDrive,
            confirmationDialogService: new ApprovingConfirmationDialogService());
        await viewModel.CreateOneDriveMdbxVaultCommand.ExecuteAsync(null);
        oneDrive.ConflictOnUpload = false;

        await viewModel.KeepLocalWebDavMdbxCommand.ExecuteAsync(Assert.Single(viewModel.MdbxDatabaseItems));

        var resolved = Assert.Single(viewModel.MdbxDatabases);
        Assert.Equal(SyncStatus.Synced, resolved.LastSyncStatus);
        Assert.Equal("\"one-v2\"", oneDrive.UploadConditions[^1].ExpectedVersion?.ETag);
    }

    [Fact]
    public async Task ViewModel_resolves_OneDrive_conflict_with_validated_remote_copy_and_local_recovery()
    {
        var oneDrive = new RecordingOneDriveBackupService { ConflictOnUpload = true };
        var viewModel = CreateViewModel(
            GetTempPath(),
            oneDriveBackupService: oneDrive,
            confirmationDialogService: new ApprovingConfirmationDialogService());
        await viewModel.CreateOneDriveMdbxVaultCommand.ExecuteAsync(null);
        var database = Assert.Single(viewModel.MdbxDatabases);
        var workingCopyPath = database.WorkingCopyPath!;
        var localBytes = await File.ReadAllBytesAsync(workingCopyPath);
        oneDrive.DownloadContent = localBytes;
        var recoveryPattern = $"{Path.GetFileNameWithoutExtension(workingCopyPath)}.local-conflict-*{Path.GetExtension(workingCopyPath)}";
        var existingRecoveryFiles = Directory.GetFiles(Path.GetDirectoryName(workingCopyPath)!, recoveryPattern).ToHashSet(StringComparer.OrdinalIgnoreCase);

        await viewModel.UseRemoteWebDavMdbxCommand.ExecuteAsync(Assert.Single(viewModel.MdbxDatabaseItems));

        Assert.Equal(SyncStatus.Synced, Assert.Single(viewModel.MdbxDatabases).LastSyncStatus);
        var recoveryFile = Assert.Single(
            Directory.GetFiles(Path.GetDirectoryName(workingCopyPath)!, recoveryPattern),
            path => !existingRecoveryFiles.Contains(path));
        Assert.Equal(localBytes, await File.ReadAllBytesAsync(recoveryFile));
    }

    private sealed class RecordingOneDriveBackupService : IOneDriveBackupService
    {
        public OneDriveAccountInfo Account { get; } = new("account-1", "Monica User", "user@example.com");
        public int SignInCalls { get; private set; }
        public bool ConflictOnUpload { get; set; }
        public string ConflictMessage { get; set; } = "remote changed";
        public Exception? SignInFailure { get; set; }
        public Exception? UploadFailure { get; set; }
        public List<RemoteWriteCondition> UploadConditions { get; } = [];
        public List<string> DownloadAccountIds { get; } = [];
        public byte[] DownloadContent { get; set; } = [];
        private bool _signedIn;

        public PlatformCapability GetCapability() => new("onedrive", "OneDrive", "Test", PlatformFeatureStatus.DesktopEquivalent);

        public Task<OneDriveAccountInfo?> GetCachedAccountAsync(string? accountId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<OneDriveAccountInfo?>(_signedIn && (accountId is null || accountId == Account.AccountId) ? Account : null);

        public Task<OneDriveSignInChallenge> BeginSignInAsync(CancellationToken cancellationToken = default)
        {
            SignInCalls++;
            if (SignInFailure is not null)
            {
                return Task.FromException<OneDriveSignInChallenge>(SignInFailure);
            }

            _signedIn = true;
            return Task.FromResult(new OneDriveSignInChallenge(
                new OneDriveDeviceCodePrompt(
                    "ABCD-EFGH",
                    new Uri("https://microsoft.com/devicelogin"),
                    DateTimeOffset.UtcNow.AddMinutes(10),
                    "Use code ABCD-EFGH"),
                Task.FromResult(Account)));
        }

        public Task SignOutAsync(string? accountId = null, CancellationToken cancellationToken = default)
        {
            _signedIn = false;
            return Task.CompletedTask;
        }

        public Task<RemoteFileVersion?> GetFileVersionAsync(string accountId, string remotePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<RemoteFileVersion?>(new("\"one-v2\"", DateTimeOffset.UtcNow, 4));

        public async Task<RemoteFileVersion> UploadBinaryConditionallyAsync(
            string accountId,
            string remotePath,
            Stream content,
            RemoteWriteCondition condition,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(Account.AccountId, accountId);
            UploadConditions.Add(condition);
            if (UploadFailure is not null)
            {
                throw UploadFailure;
            }

            if (ConflictOnUpload)
            {
                throw new RemoteFileConflictException(ConflictMessage);
            }

            _ = await ReadAllBytesAsync(content, cancellationToken);
            return new RemoteFileVersion("\"one-v1\"", DateTimeOffset.UtcNow, content.Length);
        }

        public async Task<RemoteFileVersion> DownloadBinaryVersionedAsync(
            string accountId,
            string remotePath,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            DownloadAccountIds.Add(accountId);
            await destination.WriteAsync(DownloadContent, cancellationToken);
            return new RemoteFileVersion("\"one-v2\"", DateTimeOffset.UtcNow, DownloadContent.Length);
        }

        public Task ClearSensitiveCacheAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
        {
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken);
            return memory.ToArray();
        }
    }
}
