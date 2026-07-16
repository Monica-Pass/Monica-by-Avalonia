using Monica.App.Services;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed partial class AppSettingsTests
{
    [Fact]
    public async Task AtomicPersistence_partial_write_failure_preserves_destination_and_removes_temporary_file()
    {
        const string lastValidContent = "old-content";
        var path = GetTempPath();
        await File.WriteAllTextAsync(path, lastValidContent);

        await Assert.ThrowsAsync<IOException>(() => AtomicFileWriter.WriteAsync(
            path,
            async (stream, cancellationToken) =>
            {
                await stream.WriteAsync("partial-content"u8.ToArray(), cancellationToken);
                throw new IOException("Simulated interrupted write.");
            }));

        Assert.Equal(lastValidContent, await File.ReadAllTextAsync(path));
        Assert.Empty(GetAtomicTemporaryFiles(path));
    }

    [Fact]
    public async Task AtomicPersistence_cancelled_write_preserves_destination_and_removes_temporary_file()
    {
        const string lastValidContent = "old-content";
        var path = GetTempPath();
        await File.WriteAllTextAsync(path, lastValidContent);
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AtomicFileWriter.WriteAsync(
            path,
            async (stream, cancellationToken) =>
            {
                await stream.WriteAsync("partial-content"u8.ToArray(), cancellationToken);
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            },
            cancellation.Token));

        Assert.Equal(lastValidContent, await File.ReadAllTextAsync(path));
        Assert.Empty(GetAtomicTemporaryFiles(path));
    }

    [Fact]
    public async Task AtomicPersistence_success_replaces_destination_and_removes_temporary_file()
    {
        var path = GetTempPath();
        await File.WriteAllTextAsync(path, "old-content");

        await AtomicFileWriter.WriteAsync(
            path,
            (stream, cancellationToken) => stream.WriteAsync("new-content"u8.ToArray(), cancellationToken).AsTask());

        Assert.Equal("new-content", await File.ReadAllTextAsync(path));
        Assert.Empty(GetAtomicTemporaryFiles(path));
    }

    [Fact]
    public async Task AtomicPersistence_cancelled_serialization_preserves_last_valid_settings_file()
    {
        const string lastValidSettings = "{\"Language\":\"en-US\"}";
        var path = GetTempPath();
        await File.WriteAllTextAsync(path, lastValidSettings);
        using var cancellation = new CancellationTokenSource();
        var settings = new AppSettingsService(path, new CancellingSecretProtector(cancellation));
        settings.Current.WebDavPassword = "webdav-secret";
        settings.Current.WebDavBackupEncryptionPassword = "backup-secret";

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => settings.SaveAsync(cancellation.Token));

        Assert.Equal(lastValidSettings, await File.ReadAllTextAsync(path));
        Assert.Empty(GetAtomicTemporaryFiles(path));
    }

    private static string[] GetAtomicTemporaryFiles(string targetPath)
    {
        var directory = Path.GetDirectoryName(targetPath)!;
        var pattern = $".{Path.GetFileName(targetPath)}.*.tmp";
        return Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);
    }

    private sealed class CancellingSecretProtector(CancellationTokenSource cancellation) : ISecretProtector
    {
        private int _protectCallCount;

        public PlatformIntegrationCapability Capability { get; } = PlatformIntegrationService.Available(
            PlatformFeatureKeys.SecretProtection,
            "Test secret protection is available.");

        public Task<string> ProtectAsync(string plainText, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _protectCallCount) == 2)
            {
                cancellation.Cancel();
            }

            return Task.FromResult(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plainText)));
        }

        public Task<string> UnprotectAsync(string protectedText, CancellationToken cancellationToken = default) =>
            Task.FromResult(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(protectedText)));
    }
}
