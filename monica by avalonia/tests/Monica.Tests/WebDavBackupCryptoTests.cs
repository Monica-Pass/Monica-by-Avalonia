using System.Diagnostics;
using Monica.App.Services;

namespace Monica.Tests;

public sealed class WebDavBackupCryptoTests
{
    [Fact]
    public async Task Encrypt_and_decrypt_async_preserve_backup_payload()
    {
        var service = new WebDavBackupCryptoService();
        const string payload = "{\"format\":\"monica\",\"secret\":\"round-trip value\"}";

        var encrypted = await service.EncryptAsync(payload, "commercial backup password");
        var decrypted = await service.DecryptAsync(encrypted, "commercial backup password");

        Assert.Equal(payload, decrypted);
    }

    [Fact]
    public async Task Encrypt_async_returns_without_blocking_the_caller()
    {
        var service = new WebDavBackupCryptoService();
        var payload = new string('x', 8 * 1024 * 1024);

        var stopwatch = Stopwatch.StartNew();
        var encryption = service.EncryptAsync(payload, "commercial backup password");
        stopwatch.Stop();

        Assert.True(
            stopwatch.ElapsedMilliseconds < 50,
            $"Starting backup encryption blocked the caller for {stopwatch.ElapsedMilliseconds} ms.");
        var encrypted = await encryption;
        Assert.DoesNotContain(payload[..128], encrypted, StringComparison.Ordinal);
    }
}
