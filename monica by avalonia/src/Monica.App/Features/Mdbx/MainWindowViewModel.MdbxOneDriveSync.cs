using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private CancellationTokenSource? _oneDriveSignInCancellation;
    private string? _currentOneDriveAccountId;

    [RelayCommand]
    private async Task ConnectOneDriveAsync()
    {
        if (IsOneDriveBusy)
        {
            return;
        }

        IsOneDriveBusy = true;
        try
        {
            var account = await EnsureOneDriveAccountAsync();
            StatusMessage = _localization.Format("OneDriveConnectedFormat", account.DisplayName);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _localization.Get("OneDriveSignInCanceled");
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("OneDriveConnectionFailedFormat", ex.Message);
        }
        finally
        {
            IsOneDriveBusy = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectOneDriveAsync()
    {
        if (IsOneDriveBusy)
        {
            return;
        }

        IsOneDriveBusy = true;
        try
        {
            await _oneDriveBackupService.SignOutAsync(_currentOneDriveAccountId);
            await _oneDriveBackupService.ClearSensitiveCacheAsync();
            _currentOneDriveAccountId = null;
            HasOneDriveAccount = false;
            OneDriveAccountDisplayName = "";
            OneDriveEnabled = false;
            StatusMessage = _localization.Get("OneDriveDisconnected");
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("OneDriveConnectionFailedFormat", ex.Message);
        }
        finally
        {
            IsOneDriveBusy = false;
        }
    }

    [RelayCommand]
    private Task CopyOneDriveDeviceCodeAsync() =>
        string.IsNullOrWhiteSpace(OneDriveDeviceCode)
            ? Task.CompletedTask
            : _clipboardService.SetSensitiveTextAsync(OneDriveDeviceCode);

    [RelayCommand]
    private Task OpenOneDriveVerificationAsync() =>
        Uri.TryCreate(OneDriveVerificationUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? _externalLinkService.OpenAsync(uri)
            : Task.CompletedTask;

    [RelayCommand]
    private void CancelOneDriveSignIn() => _oneDriveSignInCancellation?.Cancel();

    private async Task<OneDriveAccountInfo> EnsureOneDriveAccountAsync(CancellationToken cancellationToken = default)
    {
        var account = await _oneDriveBackupService.GetCachedAccountAsync(cancellationToken: cancellationToken);
        if (account is null)
        {
            using var signInCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _oneDriveSignInCancellation = signInCancellation;
            try
            {
                var challenge = await _oneDriveBackupService.BeginSignInAsync(signInCancellation.Token);
                OneDriveDeviceCode = challenge.Prompt.UserCode;
                OneDriveVerificationUrl = challenge.Prompt.VerificationUri.AbsoluteUri;
                account = await challenge.Completion.WaitAsync(signInCancellation.Token);
            }
            finally
            {
                if (ReferenceEquals(_oneDriveSignInCancellation, signInCancellation))
                {
                    _oneDriveSignInCancellation = null;
                }
                OneDriveDeviceCode = "";
                OneDriveVerificationUrl = "";
            }
        }

        ApplyOneDriveAccount(account);
        return account;
    }

    private async Task EnsureBoundOneDriveAccountAsync(
        LocalMdbxDatabase database,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(database.RemoteAccountId))
        {
            throw new OneDriveAccountUnavailableException(
                "This legacy OneDrive source is not bound to a Microsoft account. Sign in and recreate the source.");
        }

        var account = await _oneDriveBackupService.GetCachedAccountAsync(database.RemoteAccountId, cancellationToken);
        if (account is null)
        {
            throw new OneDriveAccountUnavailableException(
                "The Microsoft account linked to this OneDrive source is no longer signed in.");
        }

        ApplyOneDriveAccount(account);
    }

    private void ApplyOneDriveAccount(OneDriveAccountInfo account)
    {
        HasOneDriveAccount = true;
        _currentOneDriveAccountId = account.AccountId;
        OneDriveAccountDisplayName = string.IsNullOrWhiteSpace(account.DisplayName)
            ? account.Username
            : account.DisplayName;
        OneDriveEnabled = true;
    }

    private async Task UploadOneDriveMdbxWorkingCopyAsync(
        LocalMdbxDatabase database,
        RemoteWriteCondition? writeCondition = null,
        CancellationToken cancellationToken = default)
    {
        var accountId = GetBoundOneDriveAccountId(database);
        var workingCopyPath = GetMdbxWorkingCopyPath(database);
        if (!File.Exists(workingCopyPath))
        {
            throw new InvalidOperationException(_localization.Get("MdbxWorkingCopyMissing"));
        }

        try
        {
            var condition = writeCondition ?? BuildOneDriveWriteCondition(database);
            await using var content = await _mdbxVaultService.OpenLocalStreamAsync(database, cancellationToken);
            content.Position = 0;
            var version = await _oneDriveBackupService.UploadBinaryConditionallyAsync(
                accountId,
                database.FilePath,
                content,
                condition,
                cancellationToken);
            await MarkOneDriveMdbxSyncedAsync(database, workingCopyPath, version);
        }
        catch (RemoteFileConflictException ex)
        {
            await MarkOneDriveMdbxSyncFailedAsync(database, SyncStatus.Conflict, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            await MarkOneDriveMdbxSyncFailedAsync(
                database,
                writeCondition is null ? SyncStatus.PendingUpload : SyncStatus.Conflict,
                ex.Message);
            throw;
        }
    }

    private async Task DownloadOneDriveMdbxWorkingCopyAsync(
        LocalMdbxDatabase database,
        SyncStatus failureStatus = SyncStatus.Failed,
        CancellationToken cancellationToken = default)
    {
        var accountId = GetBoundOneDriveAccountId(database);
        var workingCopyPath = GetMdbxWorkingCopyPath(database);
        var directory = Path.GetDirectoryName(workingCopyPath) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(directory);
        var incomingPath = Path.Combine(directory, $".{Path.GetFileName(workingCopyPath)}.{Guid.NewGuid():N}.download");
        try
        {
            RemoteFileVersion version;
            await using (var destination = new FileStream(
                incomingPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                version = await _oneDriveBackupService.DownloadBinaryVersionedAsync(
                    accountId,
                    database.FilePath,
                    destination,
                    cancellationToken);
                await destination.FlushAsync(cancellationToken);
                destination.Flush(flushToDisk: true);
            }

            await ValidateIncomingMdbxWorkingCopyAsync(database, incomingPath, cancellationToken);
            File.Move(incomingPath, workingCopyPath, overwrite: true);
            await MarkOneDriveMdbxSyncedAsync(database, workingCopyPath, version);
        }
        catch (Exception ex)
        {
            await MarkOneDriveMdbxSyncFailedAsync(database, failureStatus, ex.Message);
            throw;
        }
        finally
        {
            if (File.Exists(incomingPath))
            {
                File.Delete(incomingPath);
            }
        }
    }

    private async Task MarkOneDriveMdbxSyncedAsync(
        LocalMdbxDatabase database,
        string workingCopyPath,
        RemoteFileVersion version)
    {
        database.WorkingCopyPath = workingCopyPath;
        database.CacheCopyPath = workingCopyPath;
        database.IsOfflineAvailable = true;
        database.LastSyncedAt = DateTimeOffset.UtcNow;
        database.LastSyncStatus = SyncStatus.Synced;
        database.LastSyncError = null;
        database.RemoteETag = version.ETag;
        database.RemoteLastModifiedAt = version.LastModified;
        await SaveMdbxSyncStateAsync(database);
    }

    private Task MarkOneDriveMdbxSyncFailedAsync(LocalMdbxDatabase database, SyncStatus status, string error)
    {
        database.LastSyncStatus = status;
        database.LastSyncError = error;
        return SaveMdbxSyncStateAsync(database);
    }

    private static string GetBoundOneDriveAccountId(LocalMdbxDatabase database) =>
        string.IsNullOrWhiteSpace(database.RemoteAccountId)
            ? throw new OneDriveAccountUnavailableException("The OneDrive source has no bound Microsoft account.")
            : database.RemoteAccountId;

    private RemoteWriteCondition BuildOneDriveWriteCondition(LocalMdbxDatabase database)
    {
        if (!string.IsNullOrWhiteSpace(database.RemoteETag))
        {
            return RemoteWriteCondition.Match(new RemoteFileVersion(database.RemoteETag, database.RemoteLastModifiedAt, null));
        }

        if (database.LastSyncedAt is null)
        {
            return RemoteWriteCondition.CreateOnly;
        }

        throw new RemoteFileConflictException("The OneDrive revision is missing; refusing to overwrite the remote vault.");
    }

    private static string BuildOneDriveMdbxWorkingCopyPath(string accountId, string remotePath)
    {
        var identity = $"{accountId}\n{remotePath}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return BuildMdbxWorkingCopyPath($"onedrive-{hash[..16].ToLowerInvariant()}.mdbx");
    }
}
