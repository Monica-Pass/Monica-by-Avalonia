using Monica.Core.Models;
using Monica.Platform.Services;
using System.Security.Cryptography;
using System.Text;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task UploadWebDavMdbxWorkingCopyAsync(
        LocalMdbxDatabase database,
        WebDavProfile profile,
        RemoteWriteCondition? writeCondition = null,
        CancellationToken cancellationToken = default)
    {
        using var transferCancellation = CreateMdbxTransferCancellation(cancellationToken);
        cancellationToken = transferCancellation.Token;
        cancellationToken.ThrowIfCancellationRequested();
        var workingCopyPath = GetMdbxWorkingCopyPath(database);
        if (!File.Exists(workingCopyPath))
        {
            throw new InvalidOperationException(_localization.Get("MdbxWorkingCopyMissing"));
        }

        try
        {
            var condition = writeCondition ?? BuildWebDavWriteCondition(database);
            await using var content = await _mdbxVaultService.OpenSnapshotStreamAsync(database, cancellationToken);
            content.Position = 0;
            var version = await _webDavBackupService.UploadBinaryConditionallyAsync(
                profile,
                database.FilePath,
                content,
                condition,
                cancellationToken);
            await _mdbxVaultService.CommitSnapshotUploadAsync(
                database,
                content,
                (current, token) => CommitMdbxUploadedMetadataAsync(database, workingCopyPath, version, current, token),
                cancellationToken);
            await ReloadMdbxVaultStateAsync();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (MdbxMissingRemoteRevisionException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await MarkWebDavMdbxSyncFailedAsync(
                database,
                SyncStatus.Conflict,
                MdbxMissingRemoteRevisionFailureCode,
                cancellationToken);
            throw;
        }
        catch (RemoteFileConflictException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await MarkWebDavMdbxSyncFailedAsync(database, SyncStatus.Conflict, MdbxRemoteConflictFailureCode, cancellationToken);
            throw;
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var failureStatus = writeCondition is null
                ? SyncStatus.PendingUpload
                : SyncStatus.Conflict;
            await MarkWebDavMdbxSyncFailedAsync(database, failureStatus, MdbxWebDavSyncFailureCode, cancellationToken);
            throw;
        }
    }

    private async Task<string?> DownloadWebDavMdbxWorkingCopyAsync(
        LocalMdbxDatabase database,
        WebDavProfile profile,
        SyncStatus failureStatus = SyncStatus.Failed,
        CancellationToken cancellationToken = default)
    {
        using var transferCancellation = CreateMdbxTransferCancellation(cancellationToken);
        cancellationToken = transferCancellation.Token;
        cancellationToken.ThrowIfCancellationRequested();
        var workingCopyPath = GetMdbxWorkingCopyPath(database);
        var directory = Path.GetDirectoryName(workingCopyPath) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(directory);
        var incomingPath = Path.Combine(
            directory,
            $".{Path.GetFileName(workingCopyPath)}.{Guid.NewGuid():N}.download");

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
                version = await _webDavBackupService.DownloadBinaryVersionedAsync(
                    profile,
                    database.FilePath,
                    destination,
                    cancellationToken);
                await destination.FlushAsync(cancellationToken);
                destination.Flush(flushToDisk: true);
            }

            var result = await RestoreIncomingMdbxSnapshotAsync(database, incomingPath, workingCopyPath, version, cancellationToken);
            return result.RecoveryPath;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await MarkWebDavMdbxSyncFailedAsync(database, failureStatus, MdbxWebDavSyncFailureCode, cancellationToken);
            throw;
        }
        finally
        {
            DeleteMdbxIncomingArtifacts(incomingPath);
        }
    }

    private async Task MarkWebDavMdbxSyncFailedAsync(
        LocalMdbxDatabase database,
        SyncStatus status,
        string failureCode,
        CancellationToken cancellationToken)
    {
        await CommitMdbxSyncFailureMetadataAsync(database, status, failureCode, cancellationToken);
        await ReloadMdbxVaultStateAsync();
    }

    private RemoteWriteCondition BuildWebDavWriteCondition(LocalMdbxDatabase database)
    {
        var expected = new RemoteFileVersion(
            database.RemoteETag,
            database.RemoteLastModifiedAt,
            Length: null);
        if (expected.HasValidator)
        {
            return RemoteWriteCondition.Match(expected);
        }

        if (database.LastSyncedAt is null)
        {
            return RemoteWriteCondition.CreateOnly;
        }

        throw new MdbxMissingRemoteRevisionException();
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
