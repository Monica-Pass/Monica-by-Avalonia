using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed partial class AppSettingsTests
{
    private sealed class FailingWebDavBackupService : IWebDavBackupService
    {
        public Exception? ListFailure { get; init; }
        public Exception? UploadTextFailure { get; init; }
        public Exception? DownloadTextFailure { get; init; }
        public Exception? DeleteFailure { get; init; }

        public string NormalizeRemotePath(string rootPath, string relativePath) => relativePath;

        public Task<IReadOnlyList<RemoteFileEntry>> ListAsync(
            WebDavProfile profile,
            string relativePath,
            CancellationToken cancellationToken = default) =>
            ListFailure is null
                ? Task.FromResult<IReadOnlyList<RemoteFileEntry>>([])
                : Task.FromException<IReadOnlyList<RemoteFileEntry>>(ListFailure);

        public Task UploadTextAsync(
            WebDavProfile profile,
            string relativePath,
            string content,
            CancellationToken cancellationToken = default) =>
            UploadTextFailure is null
                ? Task.CompletedTask
                : Task.FromException(UploadTextFailure);

        public Task<string> DownloadTextAsync(
            WebDavProfile profile,
            string relativePath,
            CancellationToken cancellationToken = default) =>
            DownloadTextFailure is null
                ? Task.FromResult("")
                : Task.FromException<string>(DownloadTextFailure);

        public Task DeleteAsync(
            WebDavProfile profile,
            string relativePath,
            CancellationToken cancellationToken = default) =>
            DeleteFailure is null
                ? Task.CompletedTask
                : Task.FromException(DeleteFailure);
    }
}
