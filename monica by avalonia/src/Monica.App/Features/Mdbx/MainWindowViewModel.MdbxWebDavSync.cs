using Monica.Core.Models;
using System.Security.Cryptography;
using System.Text;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task UploadWebDavMdbxWorkingCopyAsync(
        LocalMdbxDatabase database,
        WebDavProfile profile,
        CancellationToken cancellationToken = default)
    {
        var workingCopyPath = GetMdbxWorkingCopyPath(database);
        if (!File.Exists(workingCopyPath))
        {
            throw new InvalidOperationException(_localization.Get("MdbxWorkingCopyMissing"));
        }

        try
        {
            await using var content = await _mdbxVaultService.OpenLocalStreamAsync(database, cancellationToken);
            content.Position = 0;
            await _webDavBackupService.UploadBinaryAsync(
                profile,
                database.FilePath,
                content,
                cancellationToken);
            await MarkWebDavMdbxSyncedAsync(database, workingCopyPath);
        }
        catch (Exception ex)
        {
            await MarkWebDavMdbxSyncFailedAsync(database, SyncStatus.PendingUpload, ex.Message);
            throw;
        }
    }

    private async Task DownloadWebDavMdbxWorkingCopyAsync(
        LocalMdbxDatabase database,
        WebDavProfile profile,
        CancellationToken cancellationToken = default)
    {
        var workingCopyPath = GetMdbxWorkingCopyPath(database);
        var directory = Path.GetDirectoryName(workingCopyPath) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(directory);
        var incomingPath = Path.Combine(
            directory,
            $".{Path.GetFileName(workingCopyPath)}.{Guid.NewGuid():N}.download");

        try
        {
            await using (var destination = new FileStream(
                incomingPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await _webDavBackupService.DownloadBinaryAsync(
                    profile,
                    database.FilePath,
                    destination,
                    cancellationToken);
                await destination.FlushAsync(cancellationToken);
                destination.Flush(flushToDisk: true);
            }

            await ValidateIncomingMdbxWorkingCopyAsync(database, incomingPath, cancellationToken);
            File.Move(incomingPath, workingCopyPath, overwrite: true);
            await MarkWebDavMdbxSyncedAsync(database, workingCopyPath);
        }
        catch (Exception ex)
        {
            await MarkWebDavMdbxSyncFailedAsync(database, SyncStatus.Failed, ex.Message);
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

    private async Task ValidateIncomingMdbxWorkingCopyAsync(
        LocalMdbxDatabase database,
        string incomingPath,
        CancellationToken cancellationToken)
    {
        var validationMetadata = new LocalMdbxDatabase
        {
            FilePath = incomingPath,
            WorkingCopyPath = incomingPath,
            EncryptedPassword = database.EncryptedPassword,
            TigaMode = database.TigaMode,
            UnlockMethod = database.UnlockMethod
        };
        await using var stream = await _mdbxVaultService.OpenLocalStreamAsync(
            validationMetadata,
            cancellationToken);
    }

    private async Task MarkWebDavMdbxSyncedAsync(
        LocalMdbxDatabase database,
        string workingCopyPath)
    {
        database.WorkingCopyPath = workingCopyPath;
        database.CacheCopyPath = workingCopyPath;
        database.IsOfflineAvailable = true;
        database.LastSyncedAt = DateTimeOffset.UtcNow;
        database.LastSyncStatus = SyncStatus.Synced;
        database.LastSyncError = null;
        await SaveMdbxSyncStateAsync(database);
    }

    private async Task MarkWebDavMdbxSyncFailedAsync(
        LocalMdbxDatabase database,
        SyncStatus status,
        string error)
    {
        database.LastSyncStatus = status;
        database.LastSyncError = error;
        await SaveMdbxSyncStateAsync(database);
    }

    private async Task SaveMdbxSyncStateAsync(LocalMdbxDatabase database)
    {
        await _repository.SaveMdbxDatabaseAsync(database);
        await ReloadMdbxVaultStateAsync();
    }

    private static string GetMdbxWorkingCopyPath(LocalMdbxDatabase database)
    {
        var path = database.WorkingCopyPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("MDBX working copy path is missing.");
        }

        return Path.GetFullPath(path);
    }

    private static string BuildWebDavMdbxWorkingCopyPath(WebDavProfile profile, string remotePath)
    {
        var identity = $"{profile.BaseUri.AbsoluteUri.TrimEnd('/')}\n{remotePath}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return BuildMdbxWorkingCopyPath($"webdav-{hash[..16].ToLowerInvariant()}.mdbx");
    }
}
