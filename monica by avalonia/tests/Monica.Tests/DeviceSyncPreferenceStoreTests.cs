using Monica.App.Services;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed class DeviceSyncPreferenceStoreTests
{
    /// <summary>
    /// Linux and macOS have no secret store adapter yet. Saving used to throw on every call there, so
    /// pairing, the sync interval and the direction could never be changed. The keys must still never
    /// reach disk; they live for the session instead.
    /// </summary>
    [Fact]
    public async Task Without_a_secret_store_preferences_last_the_session_and_never_reach_disk()
    {
        var path = Path.Combine(TestTempPaths.CreateDirectoryPath(), "peers.protected");
        var store = new DeviceSyncPreferenceStore(new UnavailableProtector(), path);
        Assert.False(store.PersistsAcrossRestarts);

        var first = await store.LoadAsync();
        first.SetInterval(300);
        first.Mode = DeviceSyncMode.SendOnly;
        await store.SaveAsync(first);

        var again = await store.LoadAsync();
        Assert.Equal(first.DeviceId, again.DeviceId);
        Assert.Equal(first.ReplicaKey, again.ReplicaKey);
        Assert.Equal(300, again.IntervalSeconds);
        Assert.Equal(DeviceSyncMode.SendOnly, again.Mode);
        Assert.False(File.Exists(path));
        Assert.False(Directory.Exists(Path.GetDirectoryName(path)) && Directory.EnumerateFiles(Path.GetDirectoryName(path)!).Any());
    }

    [Fact]
    public async Task With_a_secret_store_preferences_are_sealed_on_disk()
    {
        var path = Path.Combine(TestTempPaths.CreateDirectoryPath(), "peers.protected");
        var store = new DeviceSyncPreferenceStore(new MarkingProtector(), path);
        Assert.True(store.PersistsAcrossRestarts);

        var first = await store.LoadAsync();
        first.SetInterval(600);
        await store.SaveAsync(first);

        Assert.StartsWith("sealed:", await File.ReadAllTextAsync(path));
        var reopened = await new DeviceSyncPreferenceStore(new MarkingProtector(), path).LoadAsync();
        Assert.Equal(first.DeviceId, reopened.DeviceId);
        Assert.Equal(600, reopened.IntervalSeconds);
    }

    private sealed class UnavailableProtector : ISecretProtector
    {
        public PlatformIntegrationCapability Capability { get; } = PlatformIntegrationService.PlatformLimited(
            PlatformFeatureKeys.SecretProtection,
            "No keyring adapter.");

        public Task<string> ProtectAsync(string plainText, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No keyring adapter.");

        public Task<string> UnprotectAsync(string protectedText, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No keyring adapter.");
    }

    private sealed class MarkingProtector : ISecretProtector
    {
        public PlatformIntegrationCapability Capability { get; } = PlatformIntegrationService.Available(
            PlatformFeatureKeys.SecretProtection,
            "Fixture");

        public Task<string> ProtectAsync(string plainText, CancellationToken cancellationToken = default) =>
            Task.FromResult("sealed:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plainText)));

        public Task<string> UnprotectAsync(string protectedText, CancellationToken cancellationToken = default) =>
            Task.FromResult(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(protectedText["sealed:".Length..])));
    }
}
